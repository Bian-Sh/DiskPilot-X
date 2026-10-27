using System.Text.Json;
using System.Collections.ObjectModel;
using TinyHarness;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Globalization;
using System.Text.RegularExpressions;
using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Services.Copilot;

namespace DiskPilotX.Core.Models;

public class ChatMessage : INotifyPropertyChanged
{
    private static readonly Regex ThinkingTitleRegex = new(@"^\s*\*\*([^*\r\n]+)\*\*(?:\r?\n\r?\n|\s*$)", RegexOptions.Compiled);

    private string _text = string.Empty;
    private string _thinking = string.Empty;
    private bool _isError;
    private bool _isStreaming;
    private string _operationStatusText = string.Empty;
    private string _operationResultText = string.Empty;
    private string _completedAtText = string.Empty;
    private bool _isThinkingExpanded;

    public ObservableCollection<ChatSegment> Segments { get; } = new();
    public ObservableCollection<SkillTaskCard> SkillTasks { get; } = new();

    /// <summary>气泡内渲染的按需视图（紧凑模式）。可在窗口中打开完整视图。</summary>
    public ObservableCollection<SkillUiSpec> Views { get; } = new();

    private ChatSegment? _activeSegment;
    private bool _hasStepDeltas;
    private int _stepStartIndex;
    private bool _isProcessExpanded = true;
    private DateTime _timestamp = DateTime.Now;
    private TimeSpan _elapsed;
    private ModelTokenUsage? _usage;
    private readonly HashSet<string> _usageMessageIds = new(StringComparer.Ordinal);

    public ModelTokenUsage? Usage
    {
        get => _usage;
        set { if (_usage == value) return; _usage = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUsage)); OnPropertyChanged(nameof(UsageText)); OnPropertyChanged(nameof(UsageSummary)); OnPropertyChanged(nameof(UsageDetails)); }
    }
    public bool HasUsage => Usage is not null;
    public string UsageSummary => Usage?.TotalTokens is { } total
        ? (total >= 1_000_000 ? $"{total / 1_000_000d:0.#}M" : total >= 1_000 ? $"{total / 1_000d:0.#}K" : total.ToString("N0")) + " tokens"
        : "Token 用量";
    public IReadOnlyList<KeyValuePair<string, string>> UsageDetails
    {
        get
        {
            var rows = new List<KeyValuePair<string, string>>();
            if (Usage is not { } usage) return rows;
            void Add(string label, long? count) { if (count.HasValue) rows.Add(new(label, count.Value.ToString("N0"))); }
            Add("缓存读取", usage.CachedInputTokens); Add("缓存写入", usage.CacheCreationTokens);
            Add("输入", usage.InputTokens); Add("输出", usage.OutputTokens); Add("推理", usage.ReasoningTokens); Add("合计", usage.TotalTokens);
            return rows;
        }
    }
    public string UsageText => Usage is null ? string.Empty
        : "已报告 Token · " + string.Join(" · ", UsageDetails.Select(row => $"{row.Key} {row.Value}"));

    public ChatMessage() => Segments.CollectionChanged += (_, _) => RefreshSegmentBindings();

    public IEnumerable<ChatSegment> ProcessSegments => Segments.Where(segment => !segment.IsFinal);
    public IEnumerable<ChatSegment> FinalSegments => Segments.Where(segment => segment.IsFinal);
    public bool HasProcess => ProcessSegments.Any() || IsStreaming;
    public bool HasFinalContent => FinalSegments.Any();
    public string CopyText => HasFinalContent ? string.Concat(FinalSegments.Select(segment => segment.Text)) : Text;
    public DateTime? CompletedAt { get; private set; }
    public ChatSegmentStatus ProcessStatus { get; private set; } = ChatSegmentStatus.Running;
    public TimeSpan Elapsed => _elapsed;
    public bool HasDuration => Sender == ChatSender.Assistant && (IsStreaming || CompletedAt.HasValue);
    public string DisplayTime => Timestamp.ToString("HH:mm", CultureInfo.CurrentCulture);
    public string FullTime => Timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
    public string DurationText => _elapsed.TotalHours >= 1 ? $"{(int)_elapsed.TotalHours}h{_elapsed.Minutes:00}m"
        : _elapsed.TotalMinutes >= 1 ? $"{(int)_elapsed.TotalMinutes}m{_elapsed.Seconds:00}s"
        : $"{(int)_elapsed.TotalSeconds}s";
    public bool IsProcessExpanded
    {
        get => _isProcessExpanded;
        set { if (_isProcessExpanded == value) return; _isProcessExpanded = value; OnPropertyChanged(); }
    }

    private void RefreshSegmentBindings()
    {
        OnPropertyChanged(nameof(ProcessSegments));
        OnPropertyChanged(nameof(FinalSegments));
        OnPropertyChanged(nameof(HasProcess));
        OnPropertyChanged(nameof(HasFinalContent));
        OnPropertyChanged(nameof(CopyText));
    }

    public void RefreshElapsed(DateTime? now = null)
    {
        var duration = (CompletedAt ?? now ?? DateTime.Now) - Timestamp;
        _elapsed = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        OnPropertyChanged(nameof(Elapsed));
        OnPropertyChanged(nameof(DurationText));
    }

    private void MarkFinalContent()
    {
        var lastTool = Segments.ToList().FindLastIndex(segment => segment.IsTool);
        foreach (var segment in Segments.Skip(Math.Max(_stepStartIndex, lastTool + 1)).Where(segment => segment.IsText))
            segment.IsFinal = true;
        RefreshSegmentBindings();
    }

    public void RestoreCompletion(DateTime? completedAt, ChatSegmentStatus status)
    {
        ProcessStatus = status;
        if (completedAt.HasValue) MarkCompletedAt(completedAt.Value);
        if (status == ChatSegmentStatus.Completed && !HasFinalContent)
        {
            if (Segments.Count == 0 && HasText && Sender == ChatSender.Assistant)
                Segments.Add(new ChatSegment(ChatSegmentKind.Text) { Text = Text, Status = status });
            MarkFinalContent();
        }
        IsProcessExpanded = false;
        OnPropertyChanged(nameof(ProcessStatus));
        RefreshSegmentBindings();
    }

    private void FinishProcess(ChatSegmentStatus status, DateTime completedAt)
    {
        ProcessStatus = status;
        OnPropertyChanged(nameof(ProcessStatus));
        if (CompletedAt.HasValue) return;
        IsStreaming = false;
        MarkCompletedAt(completedAt);
        if (status == ChatSegmentStatus.Completed && HasFinalContent)
        {
            IsProcessExpanded = false;
            foreach (var segment in Segments) segment.IsExpanded = false;
        }
    }

    /// <summary>将真实 Agent 事件投影为有序内容；工具结果仅回填对应调用。</summary>
    public void ApplyActivity(AgentEvent activity)
    {
        switch (activity.Kind)
        {
            case AgentEventKind.SessionStarted:
                Timestamp = activity.Timestamp.LocalDateTime;
                IsStreaming = true;
                RefreshElapsed(Timestamp);
                break;
            case AgentEventKind.StepStarted:
                CompleteActiveSegment();
                _stepStartIndex = Segments.Count;
                _hasStepDeltas = false;
                break;
            case AgentEventKind.TextDelta:
            case AgentEventKind.ReasoningDelta:
                if (string.IsNullOrEmpty(activity.Text)) break;
                _hasStepDeltas = true;
                IsStreaming = true;
                AppendContent(activity.Kind == AgentEventKind.TextDelta
                    ? ChatSegmentKind.Text : ChatSegmentKind.Thinking, activity.Text, activity.IsSummary);
                break;
            case AgentEventKind.ToolCallStarted:
                CompleteActiveSegment();
                IsStreaming = true;
                GetToolSegment(activity.ToolCallId, activity.ToolName);
                break;
            case AgentEventKind.MessageCompleted when activity.Message?.Role == ChatRole.Tool:
                var result = activity.Message;
                var resultSegment = GetToolSegment(result.ToolCallId ?? activity.ToolCallId, result.ToolName ?? activity.ToolName);
                resultSegment.Text = result.Content ?? string.Empty;
                try { using var json = JsonDocument.Parse(result.Content ?? ""); if(json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("cancelled",out var cancelled) && cancelled.ValueKind == JsonValueKind.True) resultSegment.Status = ChatSegmentStatus.Cancelled; } catch(JsonException) { }
                break;
            case AgentEventKind.MessageCompleted when activity.Message?.Role == ChatRole.Assistant:
                if (activity.Message.Usage is { } usage && _usageMessageIds.Add(activity.Message.Id))
                    Usage = Usage is null ? usage : Usage.Add(usage);
                // 完整消息是流式内容的快照；仅在没有增量时用于补齐内容。
                if (!_hasStepDeltas)
                {
                    var snapshot = activity.Message;
                    if (snapshot.Blocks.Count > 0)
                    {
                        foreach (var block in snapshot.Blocks)
                        {
                            if (block.Kind == ChatContentKind.ToolCall && block.ToolCall is { } call)
                            {
                                CompleteActiveSegment();
                                GetToolSegment(call.Id, call.Name);
                            }
                            else if (!string.IsNullOrEmpty(block.Text))
                            {
                                AppendContent(block.Kind == ChatContentKind.Thinking
                                    ? ChatSegmentKind.Thinking : ChatSegmentKind.Text, block.Text, block.IsSummary);
                            }
                        }
                    }
                    else if (!string.IsNullOrEmpty(snapshot.Content))
                        AppendContent(ChatSegmentKind.Text, snapshot.Content);
                    _hasStepDeltas = true;
                }
                foreach(var call in activity.Message!.ToolCalls)
                    GetToolSegment(call.Id, call.Name).Detail = "参数：\n" + ChatSegment.FormatJson(ToolCallProjection.EnsureSafe(call).ArgumentsJson);
                CompleteActiveSegment();
                if (activity.Message.ToolCalls.Count == 0 && !activity.Message.Blocks.Any(block => block.Kind == ChatContentKind.ToolCall)
                    && Segments.Skip(_stepStartIndex).Any(segment => segment.IsText && segment.HasText))
                {
                    MarkFinalContent();
                    FinishProcess(ChatSegmentStatus.Completed, activity.Timestamp.LocalDateTime);
                }
                break;
            case AgentEventKind.ToolApprovalRequested:
                GetToolSegment(activity.ToolCallId, activity.ToolName).Status = ChatSegmentStatus.WaitingForUser;
                break;
            case AgentEventKind.ToolCallCompleted:
                var tool = GetToolSegment(activity.ToolCallId, activity.ToolName);
                if (!string.IsNullOrWhiteSpace(activity.Detail)) tool.Detail = string.IsNullOrWhiteSpace(tool.Detail) ? activity.Detail : tool.Detail + "\n" + activity.Detail;
                if(tool.Status != ChatSegmentStatus.Cancelled) tool.Status = activity.Success == false ? ChatSegmentStatus.Failed : ChatSegmentStatus.Completed;
                break;
            case AgentEventKind.TaskCompleted:
            case AgentEventKind.TaskFailed:
            case AgentEventKind.TaskCancelled:
                var status = activity.Kind switch
                {
                    AgentEventKind.TaskFailed => ChatSegmentStatus.Failed,
                    AgentEventKind.TaskCancelled => ChatSegmentStatus.Cancelled,
                    _ => ChatSegmentStatus.Completed
                };
                foreach (var segment in Segments.Where(segment =>
                    segment.Status is ChatSegmentStatus.Running or ChatSegmentStatus.WaitingForUser))
                {
                    segment.Status = status;
                    if (segment.IsTool && !string.IsNullOrEmpty(activity.Detail)) segment.Detail = activity.Detail;
                }
                _activeSegment = null;
                if (status == ChatSegmentStatus.Completed && !HasFinalContent) MarkFinalContent();
                IsError = activity.Kind == AgentEventKind.TaskFailed;
                if (IsError) OperationResultText = activity.Detail ?? string.Empty;
                FinishProcess(status, activity.Timestamp.LocalDateTime);
                break;
        }
    }

    private void AppendContent(ChatSegmentKind kind, string text, bool isSummary = false)
    {
        if (_activeSegment is null || _activeSegment.Kind != kind || _activeSegment.IsSummary != isSummary)
        {
            CompleteActiveSegment();
            _activeSegment = new ChatSegment(kind) { IsSummary = isSummary, IsExpanded = kind == ChatSegmentKind.Thinking };
            Segments.Add(_activeSegment);
        }
        _activeSegment.Text += text;
        if (kind == ChatSegmentKind.Text) Text += text;
        else Thinking += text;
    }

    private void CompleteActiveSegment()
    {
        if (_activeSegment is not null) _activeSegment.Status = ChatSegmentStatus.Completed;
        _activeSegment = null;
    }

    private ChatSegment GetToolSegment(string? callId, string? name)
    {
        var segment = string.IsNullOrEmpty(callId) ? null : Segments.FirstOrDefault(item => item.IsTool && item.ToolCallId == callId);
        if (segment is null)
        {
            segment = new ChatSegment(ChatSegmentKind.Tool) { ToolCallId = callId, ToolName = name };
            Segments.Add(segment);
        }
        else if (!string.IsNullOrEmpty(name)) segment.ToolName = name;
        return segment;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; set; } = Guid.NewGuid();

    public ChatSender Sender { get; set; }

    public string Text
    {
        get => _text;
        set { _text = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasText)); }
    }

    /// <summary>
    /// The thinking/reasoning content from the AI model.
    /// </summary>
    public string Thinking
    {
        get => _thinking;
        set
        {
            _thinking = value;
            if (_isStreaming && !string.IsNullOrWhiteSpace(_thinking))
            {
                _isThinkingExpanded = true;
                OnPropertyChanged(nameof(IsThinkingExpanded));
                OnPropertyChanged(nameof(IsThinkingCollapsed));
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasThinking));
            OnPropertyChanged(nameof(ThinkingTitle));
            OnPropertyChanged(nameof(ThinkingBody));
            OnPropertyChanged(nameof(ThinkingPreview));
            OnPropertyChanged(nameof(HasThinkingBody));
        }
    }

    public DateTime Timestamp
    {
        get => _timestamp;
        set { _timestamp = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayTime)); OnPropertyChanged(nameof(FullTime)); }
    }

    public string OperationStatusText
    {
        get => _operationStatusText;
        set { _operationStatusText = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasOperationStatus)); }
    }

    public string OperationResultText
    {
        get => _operationResultText;
        set { _operationResultText = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasOperationResult)); }
    }

    public string CompletedAtText
    {
        get => _completedAtText;
        set { _completedAtText = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasCompletedAtText)); }
    }
    public FileEntry? LinkedEntry { get; set; }

    public CleanupRecommendation? LinkedRecommendation { get; set; }

    public AiInteractionCard? InteractionCard { get; set; }

    public bool IsError
    {
        get => _isError;
        set { _isError = value; OnPropertyChanged(); }
    }

    public bool IsStreaming
    {
        get => _isStreaming;
        set
        {
            _isStreaming = value;
            IsThinkingExpanded = value && HasThinking;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasDuration));
            OnPropertyChanged(nameof(HasProcess));
        }
    }

    /// <summary>
    /// Whether the thinking section is expanded (visible).
    /// </summary>
    public bool IsThinkingExpanded
    {
        get => _isThinkingExpanded;
        set { _isThinkingExpanded = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsThinkingCollapsed)); }
    }

    public bool HasText => !string.IsNullOrWhiteSpace(Text);
    public bool HasThinking => !string.IsNullOrWhiteSpace(Thinking);
    public bool IsThinkingCollapsed => !IsThinkingExpanded;
    public string? ThinkingTitle => ParseThinking().Title;
    public string ThinkingBody => ParseThinking().Body;
    public bool HasThinkingBody => !string.IsNullOrWhiteSpace(ThinkingBody);
    public string ThinkingPreview
    {
        get
        {
            var parsed = ParseThinking();
            var preview = string.IsNullOrWhiteSpace(parsed.Title) ? FirstThinkingLine(parsed.Body) : parsed.Title;
            const int maxLength = 96;
            return preview.Length <= maxLength ? preview : preview[..maxLength].TrimEnd() + "…";
        }
    }
    public bool HasOperationStatus => !string.IsNullOrWhiteSpace(OperationStatusText);
    public bool HasOperationResult => !string.IsNullOrWhiteSpace(OperationResultText);
    public bool HasCompletedAtText => !string.IsNullOrWhiteSpace(CompletedAtText);

    public void MarkCompletedAt(DateTime completedAt)
    {
        if (CompletedAt.HasValue) return;
        CompletedAt = completedAt;
        CompletedAtText = completedAt.ToString("HH:mm", CultureInfo.CurrentCulture);
        RefreshElapsed();
        OnPropertyChanged(nameof(CompletedAt));
        OnPropertyChanged(nameof(HasDuration));
    }
    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private (string? Title, string Body) ParseThinking()
    {
        var content = Thinking.Replace("[REDACTED]", string.Empty).Trim();
        if (string.IsNullOrEmpty(content))
        {
            return (null, string.Empty);
        }

        var match = ThinkingTitleRegex.Match(content);
        if (!match.Success)
        {
            return (null, content);
        }

        return (match.Groups[1].Value.Trim(), content[match.Length..].TrimEnd());
    }

    private static string FirstThinkingLine(string text)
    {
        using var reader = new StringReader(text.Trim());
        return reader.ReadLine()?.Trim() ?? string.Empty;
    }
}




