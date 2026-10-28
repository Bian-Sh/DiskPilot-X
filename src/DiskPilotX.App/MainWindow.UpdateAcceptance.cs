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
        if (action == "check-real") vm.CheckForUpdateCommand.Execute(null);
        else if (action == "download-real") vm.DownloadCommand.Execute(null);
        else if (action is "display-current" or "display-checking" or "display-available" or "display-downloading" or "display-ready" or "display-error")
        {
            // 显示验收仅改变内存状态，不下载或启动任何安装程序。
            vm.IsChecking = false;
            vm.IsDownloading = false;
            vm.IsDownloadComplete = false;
            vm.HasError = false;
            vm.LatestVersion = action is "display-current" or "display-checking" or "display-error" ? vm.CurrentVersion : "2.0.0";
            vm.UpdateAvailable = action is "display-available" or "display-downloading" or "display-ready";
            vm.MsiDownloadUrl = null;
            vm.CheckCompleted = true;
            vm.IsChecking = action == "display-checking";
            vm.DownloadProgress = action == "display-downloading" ? 64 : 0;
            vm.IsDownloading = action == "display-downloading";
            vm.IsDownloadComplete = action == "display-ready";
            vm.ErrorText = Localization.L.Text("UpdateCheckFailed");
            vm.HasError = action == "display-error";
            vm.UpdateStatusText();
        }
        else if (action is "seed" or "seed-scroll")
        {
            vm.LatestVersion = "9.9.9";
            vm.UpdateAvailable = true;
            vm.ReleaseNotes = "# 更新体验预览\n\n" + string.Join("\n\n", Enumerable.Range(1, action == "seed-scroll" ? 80 : 32).Select(i => $"## 改进 {i}\n- 更新日志支持滚动预览，外部空白不会关闭。"));
            vm.PublishedAt = "2026-10-28";
            vm.MsiDownloadUrl = null;
            vm.Releases.Clear();
            vm.Releases.Add(new UpdateRelease { Version="9.9.9", ReleaseNotes=vm.ReleaseNotes, PublishedAt=vm.PublishedAt, IsLatest=true });
            vm.Releases.Add(new UpdateRelease { Version=vm.CurrentVersion, ReleaseNotes="# 当前版本\n\n只读验收夹具。", IsCurrent=true });
            // 长内容只用于显式验收：左右两侧都必须真正产生可滚动范围。
            if (action == "seed-scroll")
                for (var i = 1; i <= 48; i++)
                    vm.Releases.Add(new UpdateRelease { Version = $"8.0.{i}", ReleaseNotes = vm.ReleaseNotes, PublishedAt = "2026-10-28" });
            vm.SelectedRelease = vm.Releases[0];
            vm.HistoryLoaded = true;
            vm.HistoryHasError = false;
        }
        else if (action is "settings-top" or "settings-middle" or "settings-bottom")
        {
            var (outer, left, right) = GetUpdateSettingsScrolls();
            var section = (FrameworkElement)SettingsPage.FindName("UpdateSectionControl");
            var pane = (FrameworkElement)section.FindName("ReleaseListPane");
            outer.ScrollToVerticalOffset(outer.VerticalOffset + pane.TransformToAncestor(outer).Transform(new Point()).Y - 20);
            foreach (var inner in new[] { left, right })
                inner?.ScrollToVerticalOffset(action == "settings-top" ? 0 : inner.ScrollableHeight * (action == "settings-middle" ? 0.5 : 1));
            outer.UpdateLayout();
        }
        else if (action is "settings-left-up" or "settings-left-down" or "settings-right-up" or "settings-right-down")
        {
            var (outer, left, right) = GetUpdateSettingsScrolls();
            var inner = action.Contains("-left-") ? left : right;
            var target = inner?.Content as UIElement ?? throw new InvalidOperationException("请先打开设置并注入长内容夹具。");
            var delta = action.EndsWith("-up") ? 120 : -120;
            // 从真实列表/文档内容触发路由事件，不直接调用滚轮处理器或设置预期结果。
            var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
            target.RaiseEvent(wheel);
            if (!wheel.Handled)
                target.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = Mouse.MouseWheelEvent });
            outer.UpdateLayout();
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
        var (outer, left, right) = GetUpdateSettingsScrolls();
        return new { BottomVersionText = VersionDisplay.Text, _updateViewModel?.StatusText, _updateViewModel?.IsUpdateEntryVisible,
            _updateViewModel?.CurrentVersion, _updateViewModel?.LatestVersion, _updateViewModel?.IsChecking,
            _updateViewModel?.IsDownloading, _updateViewModel?.DownloadProgress, _updateViewModel?.IsDownloadComplete,
            _updateViewModel?.HasError, _updateViewModel?.MsiDownloadUrl,
            PopupOpen=popup.IsOpen, PopupHeight=content.ActualHeight,
            EntryHint=_updateViewModel?.UpdateEntryHint,
            PopupButtons=CountButtons(content),
            ScrollableHeight=scroll?.ScrollableHeight, ScrollOffset=scroll?.VerticalOffset,
            SettingsScroll = new { OuterOffset = outer.VerticalOffset, OuterRange = outer.ScrollableHeight,
                LeftOffset = left?.VerticalOffset, LeftRange = left?.ScrollableHeight,
                RightOffset = right?.VerticalOffset, RightRange = right?.ScrollableHeight,
                ReleaseCount = _updateViewModel?.Releases.Count },
            SharedSettings=ReferenceEquals(SettingsPage.FindName("UpdateSectionControl") is FrameworkElement section ? section.DataContext : null, _updateViewModel) };
    }
    private (ScrollViewer Outer, ScrollViewer? Left, ScrollViewer? Right) GetUpdateSettingsScrolls()
    {
        var section = (FrameworkElement)SettingsPage.FindName("UpdateSectionControl");
        return ((ScrollViewer)SettingsPage.FindName("SettingsScrollViewer"),
            FindScroll((DependencyObject)section.FindName("ReleaseList")),
            FindScroll((DependencyObject)section.FindName("ReleaseNotesViewer")));
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
