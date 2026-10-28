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
        if (e.Handled || e.Delta == 0) return;
        var inner = FindScroll(ReferenceEquals(sender, ReleaseListPane) ? ReleaseList : ReleaseNotesViewer);
        var atBoundary = inner is null || inner.ScrollableHeight <= 0 ||
            (e.Delta > 0 ? inner.VerticalOffset <= 0 : inner.VerticalOffset >= inner.ScrollableHeight);
        var target = inner;
        if (atBoundary)
        {
            // 只有到达当前方向的边界，才把滚轮交给历史区域外的设置页。
            target = null;
            for (var parent = VisualTreeHelper.GetParent((DependencyObject)sender); parent is not null; parent = VisualTreeHelper.GetParent(parent))
                if (parent is ScrollViewer outer) { target = outer; break; }
        }
        if (target is null) return;
        e.Handled = true;
        // 重新发送冒泡事件，保留列表逻辑滚动和正文像素滚动的原生步长，避免双重滚动。
        target.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            { RoutedEvent = Mouse.MouseWheelEvent, Source = target });
    }

    private static ScrollViewer? FindScroll(DependencyObject root)
    {
        if (root is ScrollViewer scroll) return scroll;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindScroll(VisualTreeHelper.GetChild(root, i)) is { } child) return child;
        return null;
    }

    private void Release_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        if (e.Uri is { IsAbsoluteUri: true, Scheme: "https" })
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
