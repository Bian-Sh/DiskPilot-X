using System.Windows;
using System.Windows.Controls;
using DiskPilotX.App.Controls;
using DiskPilotX.App.Localization;
using DiskPilotX.Core.Services.Assistant;

namespace DiskPilotX.App;

public partial class MainWindow
{
    private async Task<bool> ConfirmConversationDeletionAsync(ConversationSummary summary)
    {
        if (_chatViewModel is not null) _chatViewModel.IsHistoryOpen = false;
        var result = await ModalHost.ShowConfirmationAsync(L.Text("CopilotDeleteConversation"),
            string.Format(System.Globalization.CultureInfo.CurrentCulture, L.Text("CopilotDeleteConversationPrompt"), summary.Title),
            L.Text("CopilotDeleteConversationConfirm"));
        return result == ModalResult.Positive;
    }

    private Task<MessageBoxResult> ShowAppMessageAsync(string message, string title, MessageBoxButton buttons, MessageBoxImage image)
    {
        return ModalHost.ShowMessageAsync(message, title, buttons, image);
    }

    private Task<int> ShowAppModalAsync(string title, string content, ModalMessageType messageType, ModalButtonFlags buttonFlags)
    {
        return ModalHost.ShowAsync(title, content, messageType, buttonFlags);
    }

    private Task<bool?> ShowAppContentAsync(FrameworkElement content, double maxWidth = 620, double maxHeight = 700)
    {
        return ModalHost.ShowContentAsync(content, maxWidth, maxHeight);
    }
}
