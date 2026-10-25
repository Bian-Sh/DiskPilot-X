using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TinyHarness;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceMonger.App.Localization;
using SpaceMonger.Core.Enums;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Assistant;
using SpaceMonger.Core.Services.Copilot;
using SpaceMonger.Core.Services.Llm;
using SpaceMonger.Core.Services.Settings;
using Message = SpaceMonger.Core.Models.ChatMessage;

namespace SpaceMonger.App.ViewModels;

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
    public string SendButtonText => IsOperationRunning ? L.Text("CopilotStop") : L.Text("SendButton");
    public event Action? ClearConsoleRequested;

    public ChatViewModel(DiskAssistantService assistant, ISettingsService settingsService, IAiSkillRouter skillRouter, ILogger<ChatViewModel>? logger = null)
    {
        this.assistant = assistant; _settingsService = settingsService; _skillRouter = skillRouter;
        _logger = logger ?? NullLogger<ChatViewModel>.Instance;
        RefreshSkills();
        FilteredSkillMentionSuggestions = new(SkillMentionSuggestions);
        Messages.CollectionChanged += (_, _) => HasMessages = Messages.Count > 0;
        assistant.Activity += activity => OnUi(() => ApplyActivity(activity));
        assistant.RunFinished += () => OnUi(RefreshConversationCommands);
        L.LanguageChanged += RefreshSlashCommandSuggestions;
        L.LanguageChanged += () => OnPropertyChanged(nameof(ThinkingLevelOptions));
        L.LanguageChanged += () => OnPropertyChanged(nameof(ThinkingPickerLabel));
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
            SelectedChatModel = settings.ChatModelName;
            SelectedThinkingLevel = ModelEndpointConfig.ResolveThinkingLevel(settings);
            OnPropertyChanged(nameof(ThinkingLevelOptions));
        }
        finally { _isLoadingSettings = false; }
        OnPropertyChanged(nameof(ThinkingPickerLabel));
        ModelDisplayName = IsOperationRunning && assistant.ActiveModel is { } active
            ? active
            : ModelEndpointResolver.ResolveModel(settings.ChatModelName, settings.AnthropicBaseUrl, false);
    }

    partial void OnSelectedChatModelChanged(string? value)
    {
        if (_isLoadingSettings) return;
        PersistSetting(settings =>
        {
            settings.ChatModelName = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            ModelDisplayName = ModelEndpointResolver.ResolveModel(settings.ChatModelName, settings.AnthropicBaseUrl, false);
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
        if (assistant.IsRunning) { assistant.CancelCurrentRun(); return; }
        if (HasPendingInteractionCard || PendingAskUser is not null) { ErrorMessage="请先完成或取消当前选择。"; return; }
        if (string.IsNullOrWhiteSpace(InputText)) return;
        var text = InputText.Trim();
        if (TryExecuteSlashCommand(text)) { InputText = null; return; }
        RefreshApiKeyStatus();
        if (!IsApiKeyConfigured) { ErrorMessage = "请先在设置中配置模型 API Key。"; return; }
        InputText = null; ErrorMessage = null;
        Messages.Add(new Message { Sender = ChatSender.User, Text = text, Timestamp = DateTime.Now, LinkedEntry = LinkedEntry, LinkedRecommendation = LinkedRecommendation });
        SaveCurrentConversation();
        var settings = _settingsService.LoadSettings();
        var routing = _skillRouter.Route(text, LinkedEntry, _currentViewRoot, false);
        try
        {
            var endpoint = ModelEndpointConfig.FromSettings(settings, _settingsService.GetApiKey(settings)!,
                ModelEndpointResolver.ResolveModel(settings.ChatModelName, settings.AnthropicBaseUrl, false),
                _settingsService.GetProxyPassword(settings));
            await assistant.RunAsync(text, new(endpoint, settings.Language, routing.Skills), null, CancellationToken.None);
        }
        catch (OperationCanceledException) { StatusText = L.Text("CopilotStopped"); }
        catch (Exception ex)
        {
            _logger.LogWarning("Copilot 运行未完成 {ExceptionType}", ex.GetType().Name);
            ErrorMessage = "当前任务未完成，请检查模型配置或稍后重试。";
        }
        finally { OnUi(() => { IsOperationRunning = false; PendingInteractionCard = null; RefreshSkills(); SaveCurrentConversation(); RefreshConversationHistory(); }); }
    }
    private void ApplyActivity(AgentEvent activity)
    {
        if (activity.Kind == AgentEventKind.SessionStarted)
        {
            displayMessage = new Message { Sender = ChatSender.Assistant, IsStreaming = true, Timestamp = DateTime.Now };
            Messages.Add(displayMessage); ModelDisplayName = assistant.ActiveModel ?? ModelDisplayName; IsOperationRunning = true; ErrorMessage = null; StatusText = L.Text("CopilotWorking");
        }
        displayMessage?.ApplyActivity(activity);
        if (activity.Kind == AgentEventKind.ToolCallStarted) StatusText = "正在执行 · " + activity.ToolName;
        if (activity.Kind is AgentEventKind.TaskCompleted or AgentEventKind.TaskFailed or AgentEventKind.TaskCancelled)
        {
            StatusText = activity.Kind switch { AgentEventKind.TaskCompleted => L.Text("CopilotCompleted"), AgentEventKind.TaskCancelled => L.Text("CopilotStopped"), _ => L.Text("CopilotFailed") };
            if(activity.Kind == AgentEventKind.TaskFailed) ErrorMessage = activity.Detail ?? "任务未完成，请稍后重试。";
            IsOperationRunning = false; PendingInteractionCard = null;
            displayMessage?.MarkCompletedAt(DateTime.Now); displayMessage = null;
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
