using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DiskPilotX.App.Diagnostics;
using DiskPilotX.App.Services;

namespace DiskPilotX.App;

public partial class MainWindow
{
    internal object AcceptanceUpdateFixture(string? action)
    {
        if (!AcceptanceAutomationServer.IsEnabled() || _updateViewModel is null)
            throw new InvalidOperationException("更新验收入口未启用。");
        var vm = _updateViewModel;
        var button = (Button)TitleBar.FindName("UpdateButton");
        if (action == "seed")
        {
            vm.LatestVersion = "9.9.9";
            vm.UpdateAvailable = true;
            vm.ReleaseNotes = "# 更新体验预览\n\n" + string.Join("\n\n", Enumerable.Range(1, 32).Select(i => $"## 改进 {i}\n- 更新日志支持滚动预览，外部空白不会关闭。"));
            vm.PublishedAt = "2026-10-28";
            vm.MsiDownloadUrl = null;
            vm.Releases.Clear();
            vm.Releases.Add(new UpdateRelease { Version="9.9.9", ReleaseNotes=vm.ReleaseNotes, PublishedAt=vm.PublishedAt, IsLatest=true });
            vm.Releases.Add(new UpdateRelease { Version=vm.CurrentVersion, ReleaseNotes="# 当前版本\n\n只读验收夹具。", IsCurrent=true });
            vm.SelectedRelease = vm.Releases[0];
            vm.HistoryLoaded = true;
            vm.HistoryHasError = false;
        }
        else if (action == "preview")
            button.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent=Mouse.MouseEnterEvent });
        else if (action == "leave")
            button.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent=Mouse.MouseLeaveEvent });
        else if (action == "blank")
            TitleBar.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent=Mouse.PreviewMouseMoveEvent });
        else if (action == "interactive")
            ((Button)TitleBar.FindName("SettingsButton")).RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent=Mouse.PreviewMouseMoveEvent });
        else if (action == "inside")
            ((Border)TitleBar.FindName("UpdatePopupContent")).RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent=Mouse.PreviewMouseMoveEvent });
        else if (action is "scroll" or "wheel-up")
        {
            var scroll = (ScrollViewer)TitleBar.FindName("UpdateReleaseScroll");
            // 从真实文档正文所在控件触发滚轮路由，不直接调用滚动方法。
            var document = (FlowDocumentScrollViewer)scroll.Content;
            document.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, action == "scroll" ? -120 : 120)
                { RoutedEvent=Mouse.PreviewMouseWheelEvent });
            scroll.UpdateLayout();
        }
        return AcceptanceUpdateState();
    }

    internal object AcceptanceUpdateState()
    {
        var popup = (Popup)TitleBar.FindName("UpdateReleasePopup");
        var content = (Border)TitleBar.FindName("UpdatePopupContent");
        var scroll = FindScroll(content);
        return new { _updateViewModel?.StatusText, _updateViewModel?.IsUpdateEntryVisible,
            PopupOpen=popup.IsOpen, PopupHeight=content.ActualHeight,
            EntryHint=_updateViewModel?.UpdateEntryHint,
            PopupButtons=CountButtons(content),
            ScrollableHeight=scroll?.ScrollableHeight, ScrollOffset=scroll?.VerticalOffset,
            SharedSettings=ReferenceEquals(SettingsPage.FindName("UpdateSectionControl") is FrameworkElement section ? section.DataContext : null, _updateViewModel) };
    }
    private static int CountButtons(DependencyObject root)
    {
        var count = root is Button ? 1 : 0;
        for (var i=0; i<VisualTreeHelper.GetChildrenCount(root); i++) count += CountButtons(VisualTreeHelper.GetChild(root,i));
        return count;
    }
    private static ScrollViewer? FindScroll(DependencyObject root)
    {
        if (root is ScrollViewer scroll) return scroll;
        for (var i=0; i<VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindScroll(VisualTreeHelper.GetChild(root,i)) is { } child) return child;
        return null;
    }
}
