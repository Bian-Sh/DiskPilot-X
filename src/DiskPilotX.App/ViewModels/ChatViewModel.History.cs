using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using DiskPilotX.App.Localization;
using DiskPilotX.Core.Services.Assistant;

namespace DiskPilotX.App.ViewModels;

public partial class ChatViewModel
{
    [ObservableProperty] private ObservableCollection<ConversationSummary> _conversationHistory = [];
    [ObservableProperty] private bool _isHistoryOpen;
    public string? CurrentConversationId => assistant.CurrentConversationId;
    public string HeaderText => assistant.ConversationTitle
        ?? (Messages.FirstOrDefault()?.Text is { Length: > 0 } text
            ? string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            : L.Text("CopilotReady"));
    private CancellationTokenSource? titleCancellation;
    private Task titleGenerationTask = Task.CompletedTask;

    private void StartConversationTitle(AssistantRunOptions options, string userText, string assistantText)
    {
        if (assistant.ConversationTitle is not null || titleCancellation is not null || CurrentConversationId is not { } id) return;
        var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        titleCancellation = cancellation;
        titleGenerationTask = GenerateAsync();
        async Task GenerateAsync()
        {
            try
            {
                var title = await assistant.GenerateConversationTitleAsync(options, userText, assistantText, cancellation.Token);
                await OnUiAsync(() =>
                {
                    if (cancellation.IsCancellationRequested || !ReferenceEquals(titleCancellation, cancellation)
                        || !assistant.SetConversationTitle(id, title)) return;
                    OnPropertyChanged(nameof(HeaderText));
                    SaveCurrentConversation();
                    RefreshConversationHistory();
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                _logger.LogDebug("对话标题生成未完成 {ExceptionType}", exception.GetType().Name);
            }
            finally { cancellation.Dispose(); }
        }
    }

    // 未连接确认界面时一律拒绝删除，避免调用命令绕过用户确认。
    public Func<ConversationSummary, Task<bool>>? ConfirmConversationDeletion { get; set; }

    [RelayCommand(CanExecute = nameof(CanChangeConversation))]
    private async Task DeleteConversationAsync(ConversationSummary? summary)
    {
        if (!CanChangeConversation || summary is null || ConfirmConversationDeletion is not { } confirm) return;
        if (!await confirm(summary) || !CanChangeConversation) return;
        try
        {
            var isCurrent = summary.Id == CurrentConversationId;
            assistant.DeleteConversation(summary.Id);
            if (isCurrent)
            {
                Messages.Clear();
                ResetConversationUi();
            }
            RefreshConversationHistory();
        }
        catch (Exception exception) when (ConversationStore.IsStorageError(exception))
        {
            _logger.LogWarning("历史对话删除失败 {ExceptionType}", exception.GetType().Name);
            ErrorMessage = L.Text("CopilotHistoryDeleteFailed");
        }
    }

    public bool HasConversationHistory => ConversationHistory.Count > 0;
    public bool CanChangeConversation => !IsOperationRunning && !assistant.IsRunning;

    private void RefreshConversationCommands()
    {
        OnPropertyChanged(nameof(CanChangeConversation));
        ClearConversationCommand.NotifyCanExecuteChanged();
        ToggleHistoryCommand.NotifyCanExecuteChanged();
        LoadConversationCommand.NotifyCanExecuteChanged();
        DeleteConversationCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanChangeConversation))]
    private void ToggleHistory()
    {
        if (!CanChangeConversation) return;
        RefreshConversationHistory();
        IsHistoryOpen = !IsHistoryOpen;
    }

    private void RefreshConversationHistory()
    {
        try
        {
            ConversationHistory = new(assistant.GetConversations().Select(summary => summary with { IsCurrent = summary.Id == CurrentConversationId }));
            OnPropertyChanged(nameof(HasConversationHistory));
            OnPropertyChanged(nameof(CurrentConversationId));
        }
        catch (Exception exception) when (ConversationStore.IsStorageError(exception))
        {
            _logger.LogWarning("历史对话列表读取失败 {ExceptionType}", exception.GetType().Name);
            ErrorMessage = L.Text("CopilotHistoryLoadFailed");
        }
    }

    private bool SaveCurrentConversation()
    {
        try
        {
            assistant.SaveConversation(Messages);
            OnPropertyChanged(nameof(CurrentConversationId));
            return true;
        }
        catch (Exception exception) when (ConversationStore.IsStorageError(exception))
        {
            _logger.LogWarning("历史对话保存失败 {ExceptionType}", exception.GetType().Name);
            ErrorMessage = L.Text("CopilotHistorySaveFailed");
            return false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanChangeConversation))]
    private void LoadConversation(ConversationSummary? summary)
    {
        if (!CanChangeConversation || summary is null) return;
        try
        {
            // 先验证目标，失败时保留当前对话，也不覆盖损坏的目标文件。
            var snapshot = assistant.ReadConversation(summary.Id);
            if (!SaveCurrentConversation()) return;
            var restored = assistant.RestoreConversation(snapshot);
            Messages.Clear();
            foreach (var message in restored) Messages.Add(message);
            ResetConversationUi();
            RefreshConversationHistory();
        }
        catch (Exception exception) when (ConversationStore.IsStorageError(exception))
        {
            _logger.LogWarning("历史对话加载失败 {ExceptionType}", exception.GetType().Name);
            ErrorMessage = L.Text("CopilotHistoryLoadFailed");
        }
    }

    private void ResetConversationUi()
    {
        if (titleCancellation is { } cancellation)
        {
            titleCancellation = null;
            try { cancellation.Cancel(); } catch (ObjectDisposedException) { }
        }
        OnPropertyChanged(nameof(HeaderText));
        displayMessage = null;
        InputText = null;
        PendingInteractionCard = null;
        pendingDecision?.TrySetResult(false);
        pendingDecision = null;
        IsUserInputRequested = false;
        LastUserNotes = null;
        UnlinkItem();
        ErrorMessage = null;
        StatusText = L.Text("CopilotReady");
        IsHistoryOpen = false;
    }
}
