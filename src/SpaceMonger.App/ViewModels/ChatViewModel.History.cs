using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SpaceMonger.App.Localization;
using SpaceMonger.Core.Services.Assistant;

namespace SpaceMonger.App.ViewModels;

public partial class ChatViewModel
{
    [ObservableProperty] private ObservableCollection<ConversationSummary> _conversationHistory = [];
    [ObservableProperty] private bool _isHistoryOpen;
    public string? CurrentConversationId => assistant.CurrentConversationId;
    public bool HasConversationHistory => ConversationHistory.Count > 0;
    public bool CanChangeConversation => !IsOperationRunning && !assistant.IsRunning;

    private void RefreshConversationCommands()
    {
        OnPropertyChanged(nameof(CanChangeConversation));
        ClearConversationCommand.NotifyCanExecuteChanged();
        ToggleHistoryCommand.NotifyCanExecuteChanged();
        LoadConversationCommand.NotifyCanExecuteChanged();
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
