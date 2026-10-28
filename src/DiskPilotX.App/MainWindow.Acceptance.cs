using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Data;
using Microsoft.Extensions.DependencyInjection;
using DiskPilotX.App.Diagnostics;
using DiskPilotX.App.Helpers;
using DiskPilotX.App.Localization;
using DiskPilotX.App.Converters;
using DiskPilotX.App.ViewModels;
using DiskPilotX.App.Views;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Cleanup;

namespace DiskPilotX.App;

public partial class MainWindow
{
    /// <summary>验收注入滚轮缩放（程序化事件拿不到指定坐标，走与 OnMouseWheel 等价的内部路径）。</summary>
    internal object AcceptanceTreemapWheel(int? x, int? y, int? delta)
    {
        var control = (Controls.TreemapControl)TreemapView.FindName("Treemap");
        control.RaiseWheelAt(x ?? 0, y ?? 0, delta ?? 0);
        return GetAcceptanceState();
    }

    internal object AcceptanceTreemapPointer(int? x, int? y, string? action)
    {
        var control = (Controls.TreemapControl)TreemapView.FindName("Treemap");
        var position = new Point(x ?? -1, y ?? -1);
        switch (action)
        {
            case "move": control.HandlePointerMove(position); break;
            case "left_down": control.HandleLeftPress(position, 1, false); break;
            case "left_up": control.HandleLeftRelease(position); break;
            case "middle_click":
                control.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Middle)
                    { RoutedEvent = Mouse.MouseDownEvent });
                break;
            case "leave": control.HandleHover(new Point(-1, -1)); break;
            case "click": control.HandleClick(position, 1, false); break;
            case "ctrl_click": control.HandleClick(position, 1, true); break;
            case "double_click": control.HandleClick(position, 2, false); break;
            case "nodes": break;
            default: throw new InvalidOperationException("不支持的树状图验收动作。");
        }
        return new
        {
            ProcessId = Environment.ProcessId,
            CurrentRootPath = _treemapViewModel?.CurrentRoot?.Path,
            SelectedPath = _treemapViewModel?.SelectedNode?.Entry.Path,
            MultiSelectedPaths = control.MultiSelection.Select(node => node.Entry.Path).ToArray(),
            HoveredPath = _treemapViewModel?.HoveredNode?.Entry.Path,
            Width = control.ActualWidth,
            Height = control.ActualHeight,
            Viewport = new { control.Viewport.Zoom, PanX = control.Viewport.X, PanY = control.Viewport.Y },
            TypeColors = _treemapViewModel?.TypeColors,
            RenderStats = new { control.RenderedNodeCount, control.TitleClipNodeCount, control.RenderedLabelCount },
            Nodes = action == "nodes" ? control.Nodes?.Select(n => new
            {
                n.Entry.Name, n.Entry.Path, n.Entry.IsDirectory, n.Entry.IsDeleted,
                n.X, n.Y, n.Width, n.Height, n.Depth, n.Label, n.SizeLabel, n.ColorHex, n.ContentColorHex, n.AggregatedCount, n.AggregatedSize,
                ScreenX = n.X * control.Viewport.Zoom + control.Viewport.X,
                ScreenY = n.Y * control.Viewport.Zoom + control.Viewport.Y,
                ScreenWidth = n.Width * control.Viewport.Zoom, ScreenHeight = n.Height * control.Viewport.Zoom
            }).ToArray() : null,
        };
    }

    private System.Text.Json.Nodes.JsonNode? acceptanceDecision;
    internal object AcceptanceAskUser(string? json)
    {
        if(_chatViewModel is null || !AcceptanceAutomationServer.IsEnabled()) throw new InvalidOperationException("验收入口未启用。");
        var args=System.Text.Json.Nodes.JsonNode.Parse(json??"{}");
        acceptanceDecision=null;
        _ = Observe();
        async Task Observe() { try { acceptanceDecision=await _chatViewModel.AskUserAsync(args,CancellationToken.None); } catch(Exception ex) { acceptanceDecision=new System.Text.Json.Nodes.JsonObject { ["error"]=ex.Message }; } }
        return new { Started=true };
    }
    internal object AcceptanceShowSkill(string? id)
    {
        if(!AcceptanceAutomationServer.IsEnabled()) throw new InvalidOperationException("验收入口未启用。");
        return ShowSkillPanel(new System.Text.Json.Nodes.JsonObject { ["id"]=id });
    }
    /// <summary>只在验收模式打开真实菜单，确认与删除仍必须经可见 UI 操作。</summary>
    internal object AcceptanceFileContext(string? path, string? view)
    {
        if (!AcceptanceAutomationServer.IsEnabled() || DataContext is not MainViewModel main || main.CurrentSession is not { } session)
            throw new InvalidOperationException("验收入口未启用或尚无扫描。");
        var entry = new DiskPilotX.Core.Services.FileTree.FileTreeQueryService().FindByPath(session, path ?? "")
            ?? throw new InvalidOperationException("路径不在当前扫描内。");
        var parts = (view ?? "treemap").Split(':', 2);
        var tree = parts[0] == "tree";
        if (tree) TreeViewTabBtn.IsChecked = true;
        else TreemapTabBtn.IsChecked = true;
        var menu = tree ? TreeViewControl.BuildContextMenu(entry) : TreemapView.BuildContextMenu(entry);
        menu.PlacementTarget = tree ? (UIElement)TreeViewControl : TreemapView;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Relative;
        menu.HorizontalOffset = 140;
        menu.VerticalOffset = 60;
        var items = menu.Items.OfType<MenuItem>().ToArray();
        if (parts.Length == 2)
        {
            var item = items.Single(candidate => Equals(candidate.Header, parts[1]));
            if (!item.IsEnabled) throw new InvalidOperationException("该菜单项当前不可用。");
            // 后台画布坐标被遮挡时复用真实菜单事件；此入口不确认删除。
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        }
        else menu.IsOpen = true;
        return new { entry.Path, entry.IsDirectory, entry.IsDeleted,
            Items = items.Select(item => new { Header = item.Header?.ToString(), item.IsEnabled }).ToArray() };
    }

    private object? GetModalLayoutSnapshot()
    {
        if (ModalHost.Visibility != Visibility.Visible || ModalHost.FindName("ModalCard") is not Border card
            || ModalHost.FindName("ModalContentPresenter") is not ContentPresenter { Content: Grid root }) return null;
        var header = root.Children.OfType<Border>().FirstOrDefault();
        var title = header?.Child as TextBlock;
        var scroll = root.Children.OfType<ScrollViewer>().FirstOrDefault();
        var body = (scroll?.Content as Grid)?.Children.OfType<TextBlock>().FirstOrDefault();
        var footer = root.Children.OfType<StackPanel>().FirstOrDefault();
        var viewport = scroll?.Template?.FindName("PART_ScrollContentPresenter", scroll) as FrameworkElement;
        if (header is null || title is null || scroll is null || body is null || footer is null
            || viewport is null || !body.IsArrangeValid || viewport.ActualHeight <= 0) return null;
        var textOrigin = body.TranslatePoint(new Point(), viewport);
        var footerOrigin = footer.TranslatePoint(new Point(), card);
        return new
        {
            Width = card.ActualWidth, Height = card.ActualHeight, HeaderHeight = header.ActualHeight,
            WindowWidth = ActualWidth, WindowHeight = ActualHeight,
            TitleFontSize = title.FontSize, TitleColor = (title.Foreground as SolidColorBrush)?.Color.ToString(),
            HeaderColor = (header.Background as SolidColorBrush)?.Color.ToString(),
            BodyHeight = body.ActualHeight, ViewportHeight = viewport.ActualHeight,
            GapAbove = textOrigin.Y, GapBelow = viewport.ActualHeight - textOrigin.Y - body.ActualHeight,
            Overflow = scroll.ExtentHeight > scroll.ViewportHeight,
            ButtonBottomGap = card.ActualHeight - footerOrigin.Y - footer.ActualHeight
        };
    }

    internal object GetAcceptanceState()
    {
        var mainVm = DataContext as MainViewModel;
        var currentRoot = _treemapViewModel?.CurrentRoot;
        return new
        {
            ModalLayout = GetModalLayoutSnapshot(),
            SelectedPath = mainVm?.SelectedPath,
            CurrentRootPath = currentRoot?.Path,
            IsScanning = mainVm?.IsScanning ?? false,
            CurrentSessionTargetPath = mainVm?.CurrentSession?.TargetPath,
            IsExternalRoot = currentRoot is not null && _treemapViewModel?.ScanRoot is not null && !ReferenceEquals(currentRoot, _treemapViewModel.ScanRoot) && FindEntryByPathInTree(_treemapViewModel.ScanRoot, currentRoot.Path) is null,
            BreadcrumbMode = BreadcrumbBar.Visibility == Visibility.Visible ? "breadcrumb" : "edit",
            PathEditText = PathEditTextBox.Text,
            CanGoBack = _treemapViewModel?.CanGoBack ?? false,
            CanGoForward = _treemapViewModel?.CanGoForward ?? false,
            CanGoUp = _treemapViewModel?.CanGoUp ?? false,
            BreadcrumbText = string.Join("", BreadcrumbBar.Children.OfType<ContentControl>().Select(c => c.Content?.ToString()).Where(s => !string.IsNullOrEmpty(s))),
            TreeView = TreeViewControl.GetAcceptanceState(),
            SkillInteraction = new { AskUser = _chatViewModel?.PendingAskUser?.Title, Field = _chatViewModel?.PendingAskUser?.CurrentFieldIndex, Answer = acceptanceDecision, Rules = _chatViewModel?.CleanupRules.Select(r=>r.Title).ToArray() },
            RecommendationsVisible = RecommendationsPanel.Visibility == Visibility.Visible,
            ConsoleVisible = ConsoleFrame.Visibility == Visibility.Visible,
            Copilot = new { Busy = _chatViewModel?.IsOperationRunning, Status = _chatViewModel?.StatusText, Model = _chatViewModel?.ModelDisplayName, Width = ChatPanelColumn.ActualWidth, Pending = _chatViewModel?.PendingInteractionCard?.Title, MessageCount = _chatViewModel?.Messages.Count, HistoryOpen = _chatViewModel?.IsHistoryOpen, HistoryCount = _chatViewModel?.ConversationHistory.Count, ConversationId = _chatViewModel?.CurrentConversationId, LastMessage = _chatViewModel?.Messages.LastOrDefault()?.Text, SegmentCount = _chatViewModel?.Messages.LastOrDefault()?.Segments.Count },
            Recommendations = _recommendationsViewModel?.Recommendations.Select(r => new { r.Id, r.TargetPath, r.Size, r.IsAccepted }).ToArray(),
            ScanTotals = new { Files = mainVm?.CurrentSession?.TotalFiles, Folders = mainVm?.CurrentSession?.TotalFolders, Bytes = mainVm?.CurrentSession?.TotalSize },

        };
    }

    internal object AcceptanceCapture(string? outputPath,string? captureTarget=null)
    {
        var settingsPath = DiskPilotX.Core.AppCompatibility.GetEnvironmentVariable("DISKPILOT_X_ACCEPTANCE_SETTINGS");
        if (string.IsNullOrWhiteSpace(settingsPath) || string.IsNullOrWhiteSpace(outputPath)) throw new InvalidOperationException("验收截图需要独立设置目录。");
        var allowedRoot = Path.GetFullPath(Path.GetDirectoryName(settingsPath)!).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(outputPath);
        if (!target.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase) || !target.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("截图只能写入本次验收目录。");
        UpdateLayout(); var sourceWindow = captureTarget=="task" ? skillPanels.Values.Select(p=>p.Window).FirstOrDefault(w=>w?.IsVisible==true) ?? throw new InvalidOperationException("没有可见任务面板。") : (Window)this; sourceWindow.UpdateLayout(); var visual = captureTarget == "update" ? (FrameworkElement)TitleBar.FindName("UpdatePopupContent") : (FrameworkElement)sourceWindow.Content;
        var image = new System.Windows.Media.Imaging.RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(visual.ActualWidth)), Math.Max(1, (int)Math.Ceiling(visual.ActualHeight)), 96, 96, PixelFormats.Pbgra32);
        image.Render(visual); var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
        using var file = File.Create(target); encoder.Save(file); return new { Path = target, Width = image.PixelWidth, Height = image.PixelHeight };
    }
    internal object AcceptanceSetCopilotWidth(int width)
    {
        ChatPanelColumn.Width = new GridLength(Math.Clamp(width, 300, 700)); UpdateLayout(); return GetAcceptanceState();
    }
    internal object AcceptanceSetTheme(string? name)
    {
        if (_settingsViewModel is null) throw new InvalidOperationException("设置尚未初始化。");
        var preset = name == "light" ? DiskPilotX.Core.Models.Theme.ThemeProfile.VisionProLight : DiskPilotX.Core.Models.Theme.ThemeProfile.VisionProDark;
        _settingsViewModel.ApplyThemePresetCommand.Execute(preset);
        return GetAcceptanceState();
    }
    internal void AcceptanceNavigateToPath(string path)
    {
        NavigateToPathOrSelect(path);
    }

    internal void AcceptanceNavigateBack()
    {
        _treemapViewModel?.NavigateBack();
    }

    internal void AcceptanceNavigateForward()
    {
        _treemapViewModel?.NavigateForward();
    }

    internal void AcceptanceNavigateUp()
    {
        _treemapViewModel?.NavigateToParent();
    }

    internal void AcceptanceSwitchToEditMode()
    {
        SwitchToEditMode();
    }

    internal void AcceptanceBlurAddressBar()
    {
        SwitchToBreadcrumbMode();
        Keyboard.ClearFocus();
    }
}
