using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TinyHarness;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using DiskPilotX.App.Localization;
using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Copilot;
using DiskPilotX.Core.Services.Llm;
using DiskPilotX.Core.Services.Settings;
using Message = DiskPilotX.Core.Models.ChatMessage;

namespace DiskPilotX.App.ViewModels;

public partial class ChatViewModel : ObservableObject
{
    private readonly DiskAssistantService assistant;
    private readonly ISettingsService _settingsService;
    private readonly IAiSkillRouter _skillRouter;
    private readonly ILogger<ChatViewModel> _logger;
    private Message? displayMessage;
    private ScanSession? _currentSession;
    private FileEntry? _currentViewRoot;
    private TaskCompletionSource<bool>? pendingDecision;
    private bool _isLoadingSettings;
    [ObservableProperty] private ObservableCollection<Message> _messages = [];
    [ObservableProperty] private string? _inputText;
    [ObservableProperty] private bool _hasMessages;
    [ObservableProperty] private bool _isOperationRunning;
    [ObservableProperty] private bool _isApiKeyConfigured;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string _statusText = L.Text("CopilotReady");
    [ObservableProperty] private string _modelDisplayName = "";
    [ObservableProperty] private FileEntry? _linkedEntry;
    [ObservableProperty] private CleanupRecommendation? _linkedRecommendation;
    [ObservableProperty] private string? _linkedItemPath;
    [ObservableProperty] private AiInteractionCard? _pendingInteractionCard;
    [ObservableProperty] private bool _isUserInputRequested;
    [ObservableProperty] private bool _isSlashCommandMenuOpen;
    [ObservableProperty] private bool _isSkillMentionMenuOpen;
    [ObservableProperty] private ObservableCollection<ChatCommandSuggestion> _slashCommandSuggestions = new(BuildSlashCommandSuggestions());
    [ObservableProperty] private ObservableCollection<ChatSkillSuggestion> _skillMentionSuggestions = [];
    [ObservableProperty] private ObservableCollection<ChatSkillSuggestion> _filteredSkillMentionSuggestions = [];
    [ObservableProperty] private ChatCommandSuggestion? _selectedSlashCommandSuggestion;
    [ObservableProperty] private ChatSkillSuggestion? _selectedSkillMentionSuggestion;
    [ObservableProperty] private ObservableCollection<string> _modelCandidates = [];
    [ObservableProperty] private string? _selectedChatModel;
    [ObservableProperty] private string? _cliIdentityDisplayName;
    [ObservableProperty] private string? _cliIdentityToolTip;
    [ObservableProperty] private ThinkingLevel _selectedThinkingLevel = ThinkingLevel.Off;
    public IReadOnlyList<ThinkingLevelOption> ThinkingLevelOptions => AiOptions.ThinkingLevels();
    /// <summary>合并选择器上的思考等级文案，如「最高」。</summary>
    public string ThinkingPickerLabel => CurrentThinkingLevelLabel;
    private string CurrentThinkingLevelLabel => ThinkingLevelOptions.FirstOrDefault(option => option.Value == SelectedThinkingLevel)?.Label ?? "";
    /// <summary>思考等级滑条绑定（0=关闭 … 4=高），随 SelectedThinkingLevel 双向同步。</summary>
    public int SelectedThinkingLevelIndex => (int)SelectedThinkingLevel;
    public bool IsChatAvailable => true;
    public bool IsSending => IsOperationRunning;
    public bool HasPendingInteractionCard => PendingInteractionCard is not null;
    public bool IsCompletionMenuOpen => IsSlashCommandMenuOpen || IsSkillMentionMenuOpen;
    public string SendButtonText => IsAskingUser ? L.Text("CopilotSubmitAnswers") : IsOperationRunning ? L.Text("CopilotStop") : L.Text("SendButton");
    public event Action? ClearConsoleRequested;

    public ChatViewModel(DiskAssistantService assistant, ISettingsService settingsService, IAiSkillRouter skillRouter, ILogger<ChatViewModel>? logger = null)
    {
        this.assistant = assistant; _settingsService = settingsService; _skillRouter = skillRouter;
        _logger = logger ?? NullLogger<ChatViewModel>.Instance;
        RefreshSkills();
        FilteredSkillMentionSuggestions = new(SkillMentionSuggestions);
        Messages.CollectionChanged += (_, _) => { HasMessages = Messages.Count > 0; OnPropertyChanged(nameof(HeaderText)); };
        activityBuffer = new ChatActivityBuffer(ApplyActivity, Application.Current?.Dispatcher);
        assistant.Activity += activityBuffer.Enqueue;
        assistant.RunFinished += () => OnUi(() => { activityBuffer.Flush(); RefreshConversationCommands(); });
        L.LanguageChanged += RefreshSlashCommandSuggestions;
        L.LanguageChanged += RefreshSkills;
        L.LanguageChanged += () => OnPropertyChanged(nameof(HeaderText));
        L.LanguageChanged += () => OnPropertyChanged(nameof(ThinkingLevelOptions));
        L.LanguageChanged += () => OnPropertyChanged(nameof(ThinkingPickerLabel));
        CliIdentityService.VersionChanged += _ => OnUi(() => RefreshCliIdentityDisplay(_settingsService.LoadSettings()));
        RefreshApiKeyStatus();
        RefreshConversationHistory();
    }
    public void SetContext(ScanSession session, FileEntry viewRoot) { _currentSession = session; _currentViewRoot = viewRoot; RefreshApiKeyStatus(); }
    public void UpdateViewRoot(FileEntry root) => _currentViewRoot = root;
    public void RefreshApiKeyStatus()
    {
        var settings = _settingsService.LoadSettings();
        IsApiKeyConfigured = !string.IsNullOrWhiteSpace(_settingsService.GetApiKey(settings));
        _isLoadingSettings = true;
        try
        {
            ModelCandidates = new ObservableCollection<string>(settings.DiscoveredModels.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase));
            CliIdentityService.RestoreVersions(settings.CliIdentityVersions);
            SelectedChatModel = ModelCandidates.FirstOrDefault(id => string.Equals(id, settings.ChatModelName?.Trim(), StringComparison.OrdinalIgnoreCase));
            RefreshCliIdentityDisplay(settings);
            SelectedThinkingLevel = ModelEndpointConfig.ResolveThinkingLevel(settings);
            OnPropertyChanged(nameof(ThinkingLevelOptions));
        }
        finally { _isLoadingSettings = false; }
        OnPropertyChanged(nameof(ThinkingPickerLabel));
        ModelDisplayName = IsOperationRunning && assistant.ActiveModel is { } active
            ? active
            : SelectedChatModel ?? L.Text("CopilotChooseModel");
    }

    private void RefreshCliIdentityDisplay(AppSettings settings)
    {
        var identity = LlmContracts.ParseCliIdentity(settings.CliIdentity);
        CliIdentityDisplayName = identity == CliIdentityKind.None ? null
            : $"{AiOptions.CliIdentities().First(option => option.Value == identity).Label} · {CliIdentityService.GetVersion(identity)}";
        CliIdentityToolTip = identity == CliIdentityKind.None ? null
            : string.Join("\n", CliIdentityService.BuildHeaders(identity).Select(header => $"{header.Key}: {header.Value}"));
    }

    partial void OnSelectedChatModelChanged(string? value)
    {
        if (_isLoadingSettings) return;
        PersistSetting(settings =>
        {
            settings.ChatModelName = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            ModelDisplayName = ModelCandidates.Contains(value ?? "") ? value! : L.Text("CopilotChooseModel");
        });
    }

    partial void OnSelectedThinkingLevelChanged(ThinkingLevel value)
    {
        if (_isLoadingSettings) return;
        PersistSetting(settings => settings.ThinkingLevel = LlmContracts.FormatThinkingLevel(value));
        OnPropertyChanged(nameof(ThinkingPickerLabel));
        OnPropertyChanged(nameof(SelectedThinkingLevelIndex));
    }

    /// <summary>滑条写入入口：滑条 Value 双向绑定到 SelectedThinkingLevel。</summary>
    public void SetThinkingLevelFromSlider(int index) => SelectedThinkingLevel = (ThinkingLevel)Math.Clamp(index, 0, 4);

    private void PersistSetting(Action<AppSettings> update)
    {
        var settings = _settingsService.LoadSettings();
        update(settings);
        _settingsService.SaveSettings(settings);
    }
    partial void OnLinkedEntryChanged(FileEntry? value) => LinkedItemPath = value?.Path ?? LinkedRecommendation?.TargetPath;
    partial void OnLinkedRecommendationChanged(CleanupRecommendation? value) => LinkedItemPath = value?.TargetPath ?? LinkedEntry?.Path;
    partial void OnPendingInteractionCardChanged(AiInteractionCard? value) => OnPropertyChanged(nameof(HasPendingInteractionCard));
    partial void OnIsOperationRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(SendButtonText)); OnPropertyChanged(nameof(IsSending));
        RefreshConversationCommands();
        if (value) IsHistoryOpen = false;
        else RefreshApiKeyStatus();
    }
    partial void OnIsSlashCommandMenuOpenChanged(bool value) => OnPropertyChanged(nameof(IsCompletionMenuOpen));
    partial void OnIsSkillMentionMenuOpenChanged(bool value) => OnPropertyChanged(nameof(IsCompletionMenuOpen));
    partial void OnInputTextChanged(string? value)
    {
        IsSlashCommandMenuOpen = IsSlashCommandPrompt(value); IsSkillMentionMenuOpen = IsSkillMentionPrompt(value);
        FilteredSkillMentionSuggestions = new(BuildFilteredSkillMentionSuggestions(value));
        SelectedSlashCommandSuggestion = IsSlashCommandMenuOpen ? SlashCommandSuggestions.FirstOrDefault() : null;
        SelectedSkillMentionSuggestion = IsSkillMentionMenuOpen ? FilteredSkillMentionSuggestions.FirstOrDefault() : null;
    }
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SubmitOrStopAsync()
    {
        // 提问期间复用发送按钮提交答案，优先于模型停止逻辑。
        if (PendingAskUser is { } decision) { decision.TryCompleteSubmission(); return; }
        if (assistant.IsRunning) { assistant.CancelCurrentRun(); return; }
        if (HasPendingInteractionCard) { ErrorMessage="请先完成或取消当前选择。"; return; }
        if (string.IsNullOrWhiteSpace(InputText)) return;
        var text = InputText.Trim();
        if (TryExecuteSlashCommand(text)) { InputText = null; return; }
        RefreshApiKeyStatus();
        if (!IsApiKeyConfigured) { ErrorMessage = "请先在设置中配置模型 API Key。"; return; }
        InputText = null; ErrorMessage = null;
        var settings = _settingsService.LoadSettings();
        var routing = _skillRouter.Route(text, LinkedEntry, _currentViewRoot, false);
        var endpoint = BuildChatEndpoint(settings);
        await RunAssistantAsync(
            new Message { Sender = ChatSender.User, Text = text, Timestamp = DateTime.Now, LinkedEntry = LinkedEntry, LinkedRecommendation = LinkedRecommendation },
            new(endpoint, settings.Language, routing.Skills));
    }

    /// <summary>应用功能（如「分析」按钮）代发的对话消息：显式溯源展示，走当前聊天模型；
    /// 分析证据仍由 AI 通过 get_analysis_evidence 工具读取，消息本身不携带扫描数据。</summary>
    public async Task<AssistantRunResult?> SendAppAnalysisAsync(string text, string? focusPath)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (assistant.IsRunning) { ErrorMessage = "已有 Copilot 任务正在运行，请完成或停止后继续。"; return null; }
        if (HasPendingInteractionCard || PendingAskUser is not null) { ErrorMessage = "请先完成或取消当前选择。"; return null; }
        RefreshApiKeyStatus();
        if (!IsApiKeyConfigured) { ErrorMessage = "请先在设置中配置模型 API Key。"; return null; }
        ErrorMessage = null;
        var settings = _settingsService.LoadSettings();
        var endpoint = BuildChatEndpoint(settings);
        // 不走技能路由：应用代发的文本不参与 @提及与创建者意图匹配，保持请求语义可控。
        return await RunAssistantAsync(
            new Message { Sender = ChatSender.System, Text = text, Timestamp = DateTime.Now, LinkedEntry = LinkedEntry, LinkedRecommendation = LinkedRecommendation },
            new(endpoint, settings.Language, Skills: [], FocusPath: focusPath, IsAnalysis: true, InConversationSession: true));
    }

    private ModelEndpointConfig BuildChatEndpoint(AppSettings settings) =>
        ModelEndpointConfig.FromSettings(settings, _settingsService.GetApiKey(settings)!,
            ModelEndpointResolver.ResolveModel(settings.ChatModelName, settings.AnthropicBaseUrl),
            _settingsService.GetProxyPassword(settings));

    /// <summary>把一条消息加入对话并驱动一次助手运行；用户输入与应用代发的分析共用该通道。</summary>
    private async Task<AssistantRunResult?> RunAssistantAsync(Message message, AssistantRunOptions options)
    {
        Messages.Add(message);
        SaveCurrentConversation();
        var conversationId = CurrentConversationId;
        try
        {
            var result = await assistant.RunAsync(message.Text, options with { MessageId = message.Id.ToString() }, null, CancellationToken.None);
            if (result.State == AgentSessionState.Completed && !string.IsNullOrWhiteSpace(result.Text))
                OnUi(() => { if (CurrentConversationId == conversationId) StartConversationTitle(options, message.Text, result.Text); });
            if (result.State == AgentSessionState.Completed && CurrentConversationId == conversationId)
            {
                try
                {
                    var draft = await assistant.SuggestShortcutAsync(CancellationToken.None);
                    if (draft is not null) OnUi(() => {
                        ruleCreationScanId = _currentSession?.Id;
                        PendingRuleCreationPrompt = draft;
                    });
                }
                catch (Exception exception) { _logger.LogDebug("检查快捷入口建议未完成 {ExceptionType}", exception.GetType().Name); }
            }
            return result;
        }
        catch (OperationCanceledException) { StatusText = L.Text("CopilotStopped"); }
        catch (Exception ex)
        {
            _logger.LogWarning("Copilot 运行未完成 {ExceptionType}", ex.GetType().Name);
            ErrorMessage = "当前任务未完成，请检查模型配置或稍后重试。";
        }
        finally { OnUi(() => { activityBuffer.Flush(); IsOperationRunning = false; PendingInteractionCard = null; RefreshSkills(); SaveCurrentConversation(); RefreshConversationHistory(); }); }
        return null;
    }
    private readonly ChatActivityBuffer activityBuffer;

    private void ApplyActivity(AgentEvent activity)
    {
        if (activity.Kind == AgentEventKind.SessionStarted)
        {
            displayMessage = new Message { Sender = ChatSender.Assistant, IsStreaming = true, Timestamp = activity.Timestamp.LocalDateTime, ModelId = assistant.ActiveModel };
            Messages.Add(displayMessage); ModelDisplayName = assistant.ActiveModel ?? ModelDisplayName; IsOperationRunning = true; ErrorMessage = null; StatusText = L.Text("CopilotWorking");
        }
        displayMessage?.ApplyActivity(activity);
        if (activity.Kind == AgentEventKind.MessageCompleted && activity.Message is { Role: ChatRole.Tool, ToolName: "web_search" } searchResult)
        {
            try
            {
                using var result = System.Text.Json.JsonDocument.Parse(searchResult.Content ?? "");
                if (result.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                    && result.RootElement.TryGetProperty("user_notice", out var notice) && notice.ValueKind == System.Text.Json.JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(notice.GetString())) ErrorMessage = notice.GetString();
            }
            catch (System.Text.Json.JsonException) { }
        }
        if (activity.Kind == AgentEventKind.ToolCallStarted) StatusText = "正在执行 · " + activity.ToolName;
        if (activity.Kind is AgentEventKind.TaskCompleted or AgentEventKind.TaskFailed or AgentEventKind.TaskCancelled)
        {
            StatusText = activity.Kind switch { AgentEventKind.TaskCompleted => L.Text("CopilotCompleted"), AgentEventKind.TaskCancelled => L.Text("CopilotStopped"), _ => L.Text("CopilotFailed") };
            if(activity.Kind == AgentEventKind.TaskFailed) ErrorMessage = activity.Detail ?? "任务未完成，请稍后重试。";
            IsOperationRunning = false; PendingInteractionCard = null;
            displayMessage = null;
        }
        if (activity.Kind == AgentEventKind.MessageCompleted) SaveCurrentConversation();
    }
    public async Task<bool> ConfirmAsync(string title, string description, string? impact, CancellationToken ct, bool allowNotes = false)
    {
        var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await OnUiAsync(() =>
        {
            pendingDecision?.TrySetResult(false); pendingDecision = decision; IsUserInputRequested = allowNotes;
            PendingInteractionCard = new AiInteractionCard { Title = title, Description = description, Impact = impact,
                ConfirmText = Localized("Continue","继续"), CancelText = Localized("Cancel","取消"), Action = new(AiActionKind.None), UserNotes = allowNotes ? "" : null };
            StatusText = L.Text("CopilotWaiting");
        });
        try { return await decision.Task.WaitAsync(ct); }
        finally { await OnUiAsync(() => { if (ReferenceEquals(pendingDecision, decision)) { pendingDecision = null; PendingInteractionCard = null; IsUserInputRequested = false; StatusText = IsOperationRunning ? L.Text("CopilotWorking") : L.Text("CopilotReady"); } }); }
    }
    public string? LastUserNotes { get; private set; }
    [RelayCommand] private void ConfirmInteraction() { LastUserNotes = PendingInteractionCard?.UserNotes; pendingDecision?.TrySetResult(true); }
    [RelayCommand] private void CancelInteraction() => pendingDecision?.TrySetResult(false);
    [RelayCommand] private void UnlinkItem() { LinkedEntry = null; LinkedRecommendation = null; LinkedItemPath = null; }
    internal static string Localized(string english, string chinese) => L.CurrentLanguageName.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? english : chinese;
    private static void OnUi(Action action) { var dispatcher = Application.Current?.Dispatcher; if (dispatcher is null || dispatcher.CheckAccess()) action(); else dispatcher.Invoke(action); }
    private static Task OnUiAsync(Action action) { var dispatcher = Application.Current?.Dispatcher; if (dispatcher is null || dispatcher.CheckAccess()) { action(); return Task.CompletedTask; } return dispatcher.InvokeAsync(action).Task; }
}
