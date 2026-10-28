using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DiskPilotX.App.Controls;
using DiskPilotX.App.ViewModels;
using DiskPilotX.App.Views;
using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Treemap;

namespace DiskPilotX.App.Tests;

[Collection("聊天滚动控件")]
public sealed class FileActionRegressionTests
{
    [Fact]
    public void 长正文使用滚动容器且卡片高度不是固定值() => StaHost.Run(() =>
    {
        var host = 创建弹窗();
        _ = host.ShowAsync("长正文", string.Join("\n", Enumerable.Repeat("需要完整显示的正文", 150)), ModalMessageType.Warning,
            ModalButtonFlags.Positive | ModalButtonFlags.Negative);
        var card = (Border)host.FindName("ModalCard");
        Assert.True(double.IsNaN(card.Height));
        var root = (Grid)((ContentPresenter)host.FindName("ModalContentPresenter")).Content;
        Assert.Single(root.Children.OfType<ScrollViewer>());
        host.CloseCurrent(ModalResult.Negative);
    });

    [Fact]
    public void 删除结果保留节点布局和导航并且只扣减一次()
    {
        var root = new FileEntry { Path = @"D:\scan", IsDirectory = true };
        var file = new FileEntry { Path = @"D:\scan\a.txt", Parent = root, Size = 7 };
        root.Children.Add(file);
        file.RecalculateSize();
        var session = new ScanSession { RootEntry = root, TotalFiles = 1, TotalFolders = 1, TotalSize = 7 };
        var vm = new TreemapViewModel(new 布局());
        vm.SetRoot(root, session);
        var nodes = vm.Nodes;
        var result = new CleanupAction { Result = CleanupResult.Success, Recommendation = new() { TargetPath = file.Path } };
        vm.ApplyCleanupResults([result, result], session);
        Assert.Single(root.Children);
        Assert.Same(file, root.Children[0]);
        Assert.Same(nodes, vm.Nodes);
        Assert.True((bool?)typeof(FileEntry).GetProperty("IsDeleted")?.GetValue(file));
        Assert.Equal(0, session.TotalFiles);
        Assert.Equal(0, session.TotalSize);
        Assert.Equal(7, root.Size);
    }

    [Fact]
    public void 两个视图都提供删除回调() => StaHost.Run(() =>
    {
        Assert.NotNull(typeof(TreemapView).GetProperty("DeleteEntryAsync"));
        Assert.NotNull(typeof(TreeViewControl).GetProperty("DeleteEntryAsync"));
    });

    [Fact]
    public void 文件夹菜单不显示打开文件夹() => StaHost.Run(() =>
    {
        foreach (var key in new[] { "VP.Button", "VP.ButtonIcon" })
        {
            var style = new Style(typeof(Button));
            style.Seal();
            Application.Current.Resources[key] = style;
        }
        foreach (var type in new[] { typeof(TreemapView), typeof(TreeViewControl) })
        {
            var view = Activator.CreateInstance(type)!;
            var menu = (ContextMenu)type.GetMethod("BuildContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(view, [new FileEntry { Path = @"D:\folder", IsDirectory = true }])!;
            Assert.DoesNotContain(menu.Items.OfType<MenuItem>(), item => Equals(item.Header, "打开文件夹"));
        }
    });

    [Fact]
    public void 窗口变小时正文滚动且按钮仍在卡片范围内() => StaHost.Run(() =>
    {
        var host = 创建弹窗();
        _ = host.ShowConfirmationAsync("确认删除", string.Join("\n", Enumerable.Repeat("长路径与警示内容", 150)), "确认删除");
        host.Measure(new Size(600, 320));
        host.Arrange(new Rect(0, 0, 600, 320));
        host.UpdateLayout();
        var card = (Border)host.FindName("ModalCard");
        Assert.InRange(card.ActualHeight, 100, 272);
        var root = (Grid)((ContentPresenter)host.FindName("ModalContentPresenter")).Content;
        var viewer = Assert.Single(root.Children.OfType<ScrollViewer>());
        Assert.True(viewer.ExtentHeight > viewer.ViewportHeight);
        var buttons = Assert.Single(root.Children.OfType<StackPanel>());
        var position = buttons.TranslatePoint(new Point(), card);
        Assert.True(position.Y >= 0 && position.Y + buttons.ActualHeight <= card.ActualHeight);
        Assert.True(buttons.Children.OfType<Button>().Single(button => button.IsCancel).IsDefault);
        host.CloseCurrent(ModalResult.Negative);
    });

    [Fact]
    public void 新扫描清除标记但旧结果不能重复扣减统计()
    {
        var root = new FileEntry { Path = @"D:\scan", IsDirectory = true };
        var file = new FileEntry { Path = @"D:\scan\a.txt", Parent = root, Size = 7 };
        root.Children.Add(file);
        var session = new ScanSession { RootEntry = root, TotalFiles = 1, TotalFolders = 1, TotalSize = 7 };
        var vm = new TreemapViewModel(new 布局());
        vm.SetRoot(root, session);
        var action = new CleanupAction { Result = CleanupResult.Success, Recommendation = new() { TargetPath = file.Path } };
        vm.ApplyCleanupResults([action], session);
        Assert.True(file.IsDeleted);
        vm.ClearDeletionMarks();
        Assert.False(file.IsDeleted);
        vm.ApplyCleanupResults([action], session);
        Assert.Equal(0, session.TotalFiles);
        Assert.Equal(0, session.TotalSize);
    }

    [Fact]
    public void 失败不会标记且标记属性通知可绑定()
    {
        var root = new FileEntry { Path = @"D:\scan", IsDirectory = true };
        var file = new FileEntry { Path = @"D:\scan\a.txt", Parent = root, Size = 7 };
        root.Children.Add(file);
        var session = new ScanSession { RootEntry = root, TotalFiles = 1, TotalSize = 7 };
        var result = new CleanupAction { Result = CleanupResult.Failed, Recommendation = new() { TargetPath = file.Path } };
        Assert.False(DiskPilotX.App.Services.DeletedEntryState.Apply(session, [result]));
        Assert.False(file.IsDeleted);
        Assert.Equal(7, session.TotalSize);
        var notifications = 0;
        file.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(FileEntry.IsDeleted)) notifications++; };
        file.IsDeleted = true;
        file.IsDeleted = true;
        Assert.Equal(1, notifications);
        Assert.Throws<InvalidOperationException>(() => DiskPilotX.App.Services.FileEntryShellService.Open(file));
        Assert.Throws<InvalidOperationException>(() => DiskPilotX.App.Services.FileEntryShellService.ShowInExplorer(file));
    }

    [Fact]
    public void 确认后立即出现的错误弹窗不会被旧关闭动画隐藏() => StaHost.Run(() =>
    {
        var host = 创建弹窗();
        using var source = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("模态动画回归")
        { Width = 600, Height = 400, WindowStyle = unchecked((int)0x80000000) }) { RootVisual = host };
        var confirmation = host.ShowConfirmationAsync("确认删除", "待删除项目", "确认删除");
        _ = confirmation.ContinueWith(_ => host.ShowAsync("删除失败", "文件被占用", ModalMessageType.Warning, ModalButtonFlags.Positive),
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.FromCurrentSynchronizationContext());
        host.CloseCurrent(ModalResult.Positive);
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        Assert.Equal(Visibility.Visible, host.Visibility);
        Assert.NotNull(((ContentPresenter)host.FindName("ModalContentPresenter")).Content);
        host.CloseCurrent(ModalResult.Positive);
    });
    [Theory]
    [InlineData("模式")]
    [InlineData("会话")]
    [InlineData("扫描")]
    [InlineData("已删除")]
    [InlineData("保护规则")]
    [InlineData("设置改回")]
    [InlineData("未改变")]
    public async Task 复核暂停期间上下文改变会阻止执行删除(string change)
    {
        var session = new ScanSession();
        ScanSession? current = session;
        var entry = new FileEntry { Path = @"D:\scan\sample.txt" };
        var mode = DeletionMode.PermanentDelete;
        var scanning = false;
        long revision = 1;
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executed = false;
        var guard = typeof(DiskPilotX.App.MainWindow).GetMethod("IsFileDeletionApprovalCurrent", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(guard);
        var task = Execute();
        switch (change)
        {
            case "模式": mode = DeletionMode.MoveToRecycleBin; break;
            case "会话": current = new ScanSession(); break;
            case "扫描": scanning = true; break;
            case "已删除": entry.IsDeleted = true; break;
            case "保护规则": revision++; break;
            case "设置改回": revision += 2; break;
        }
        resume.SetResult();
        await task;
        Assert.Equal(change == "未改变", executed);

        async Task Execute()
        {
            await resume.Task;
            if ((bool)guard!.Invoke(null, [session, current, entry, scanning, DeletionMode.PermanentDelete, mode, 1L, revision])!)
                executed = true;
        }
    }

    [Fact]
    public void 聚合块不是独立目标不得删除父目录() => StaHost.Run(() =>
    {
        var style = new Style(typeof(Button)); style.Seal();
        Application.Current.Resources["VP.Button"] = style;
        var view = new TreemapView { DeleteEntryAsync = _ => Task.CompletedTask };
        var method = typeof(TreemapView).GetMethod("BuildNodeContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var folder = new FileEntry { Path = @"D:\scan", IsDirectory = true };
        var aggregate = new TreemapNode { Entry = folder, AggregatedCount = 5 };
        var menu = (ContextMenu)method!.Invoke(view, [aggregate])!;
        var deletion = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, DiskPilotX.App.Localization.L.Text("DeleteEntryMenu")));
        Assert.False(deletion.IsEnabled);
        Assert.NotNull(deletion.ToolTip);
        Assert.True(ToolTipService.GetShowOnDisabled(deletion));
    });

    private static AppModalHost 创建弹窗()
    {
        var host = new AppModalHost();
        foreach (var key in new[] { "VP.SurfaceBrush", "VP.TextPrimaryBrush", "VP.TextSecondaryBrush", "VP.WarningTextBrush" })
            host.Resources[key] = Brushes.Black;
        host.Resources["VP.AccentButton"] = new Style(typeof(Button));
        host.Resources["VP.Button"] = new Style(typeof(Button));
        return host;
    }

    private sealed class 布局 : ITreemapLayoutEngine
    {
        public List<TreemapNode> ComputeLayout(FileEntry root, float width, float height, int maxDepth = 8, float zoom = 1) => [];
    }
}
