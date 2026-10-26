using System.Windows;
using System.Windows.Controls;
using DiskPilotX.App.Controls;

namespace DiskPilotX.App;

public partial class MainWindow
{
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
