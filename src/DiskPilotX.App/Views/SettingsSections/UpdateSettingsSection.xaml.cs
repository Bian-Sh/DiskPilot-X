using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using System.Windows.Input;
using System.Windows.Media;
using DiskPilotX.App.ViewModels;

namespace DiskPilotX.App.Views.SettingsSections;

public partial class UpdateSettingsSection : UserControl
{
    public UpdateSettingsSection() => InitializeComponent();
    private void Section_Loaded(object sender, RoutedEventArgs e)
    {
        if (IsVisible && DataContext is UpdateViewModel vm) vm.LoadHistoryCommand.Execute(null);
    }
    private void Section_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible && DataContext is UpdateViewModel vm) vm.LoadHistoryCommand.Execute(null);
    }
    private void ProxyEdit_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (DataContext is UpdateViewModel vm) vm.CommitProxySettings();
    }
    private void ProxyEdit_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not UpdateViewModel vm) return;
        e.Handled = true;
        vm.CommitProxySettings();
    }
    private void UpdateProxyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is UpdateViewModel vm) vm.ProxyPassword = ((PasswordBox)sender).Password;
    }

    private void HistoryPane_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var inner=Controls.NestedScrollWheel.FindScroll(ReferenceEquals(sender,ReleaseListPane) ? ReleaseList : ReleaseNotesViewer);
        Controls.NestedScrollWheel.Forward((DependencyObject)sender,inner,e);
    }

    private void Release_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        if (e.Uri is { IsAbsoluteUri: true, Scheme: "https" })
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
