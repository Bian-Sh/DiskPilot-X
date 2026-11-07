using System.Reflection;
using NSubstitute;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Enums;
using DiskPilotX.App.Controls;
using DiskPilotX.Core.Services.Scanning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DiskPilotX.App.Tests;

[Collection("聊天滚动控件")]
public sealed class BusyOperationToastTests
{
    [Theory]
    [InlineData(true, false, false, "AI 正在查看文件，暂时不能删除，避免文件变化影响判断。等本轮回复结束后再试。")]
    [InlineData(false, true, false, "正在扫描文件，暂时不能删除，避免扫描结果不完整。等扫描结束后再试。")]
    [InlineData(false, false, true, "正在清理文件，暂时不能再次删除，避免重复操作。等本轮清理结束后再试。")]
    [InlineData(true, true, true, "正在清理文件，暂时不能再次删除，避免重复操作。等本轮清理结束后再试。")]
    public void 删除忙碌原因明确且清理优先(bool ai, bool scanning, bool cleaning, string expected)
    {
        Assert.Equal(expected, Message(ai, scanning, cleaning));
    }

    [Fact]
    public void 空闲删除不显示忙碌提示() => Assert.Null(Message(false, false, false));

    [Fact]
    public void 提示不阻断模态确认且重复触发续时后自动隐藏()
    {
        RunSta(() =>
        {
            var window = new MainWindow();
            try
            {
                var toast = window.FindName("OperationToast") as Border;
                var text = window.FindName("OperationToastText") as TextBlock;
                var modal = window.FindName("ModalHost") as AppModalHost;
                Assert.NotNull(toast);
                Assert.NotNull(text);
                Assert.NotNull(modal);
                Assert.False(toast.IsHitTestVisible);
                Assert.True(Panel.GetZIndex(toast) < Panel.GetZIndex(modal));
                // 仅显示现有确认 UI，不批准，也不调用任何删除服务。
                var confirmation = modal.ShowCleanupConfirmationAsync("确认删除",
                    [new CleanupRecommendation { TargetPath = @"D:\不存在的安全测试文件", Size = 1024 }],
                    DeletionMode.MoveToRecycleBin, "确认删除");
                Assert.False(confirmation.IsCompleted);
                var show = typeof(MainWindow).GetMethod("ShowOperationToast", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(show);
                show.Invoke(window, ["首次提示"]);
                Assert.Equal(Visibility.Visible, toast.Visibility);
                Pump(TimeSpan.FromSeconds(2));
                show.Invoke(window, ["续时提示"]);
                Assert.Equal("续时提示", text.Text);
                Pump(TimeSpan.FromSeconds(3.3));
                Assert.Equal(Visibility.Visible, toast.Visibility);
                Pump(TimeSpan.FromSeconds(2));
                Assert.Equal(Visibility.Collapsed, toast.Visibility);
                Assert.False(confirmation.IsCompleted);
                modal.CloseCurrent(ModalResult.Negative);
            }
            finally { Close(window); }
        });
    }

    [Theory]
    [InlineData("AI")]
    [InlineData("扫描")]
    [InlineData("清理")]
    public void 共享删除入口忙碌时只提示不删除也不打开确认(string reason)
    {
        RunSta(() =>
        {
            var main = new MainViewModel(Substitute.For<IFileScanner>())
            {
                IsCopilotBusy = reason == "AI",
                IsScanning = reason == "扫描"
            };
            var window = new MainWindow { DataContext = main };
            try
            {
                typeof(MainWindow).GetField("_cleanupRunning", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, reason == "清理");
                var modal = (UIElement)window.FindName("ModalHost");
                var before = modal.Visibility;
                var entry = new FileEntry { Path = @"D:\不存在的安全测试文件", IsDeleted = true };
                var delete = typeof(MainWindow).GetMethod("DeleteEntryAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
                Assert.True(((Task)delete.Invoke(window, [entry])!).IsCompletedSuccessfully, "忙碌删除不得进入异步确认或执行");
                var toast = window.FindName("OperationToast") as Border;
                Assert.NotNull(toast);
                Assert.Equal(Visibility.Visible, toast.Visibility);
                Assert.Contains(reason == "AI" ? "AI 正在查看文件" : "正在" + reason + "文件", ((TextBlock)window.FindName("OperationToastText")).Text);
                Assert.Equal(before, modal.Visibility);
                Assert.Null(main.CurrentSession);
            }
            finally { Close(window); }
        });
    }

    private static void Close(MainWindow window)
    {
        typeof(MainWindow).GetField("_closeConfirmed", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
        window.Close();
    }

    private static string? Message(bool ai, bool scanning, bool cleaning)
    {
        var method = typeof(MainWindow).GetMethod("GetFileDeletionBusyMessage", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (string?)method.Invoke(null, [ai, scanning, cleaning]);
    }

    private static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void RunSta(Action action) => StaHost.Run(() =>
    {
        // 复用项目共享 STA 宿主，加载真实主题并恢复资源，避免影响其他 UI 测试。
        var previous = Application.Current.Resources;
        Application.Current.Resources = new ResourceDictionary
        {
            MergedDictionaries = { new ResourceDictionary { Source = new Uri("/DiskPilot-X;component/Themes/VisionProTheme.xaml", UriKind.Relative) } }
        };
        try { action(); }
        finally { Application.Current.Resources = previous; }
    });
}
