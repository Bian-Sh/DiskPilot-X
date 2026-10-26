using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using NSubstitute;
using SpaceMonger.App.ViewModels;
using SpaceMonger.App.Views;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Assistant;
using SpaceMonger.Core.Services.Copilot;
using SpaceMonger.Core.Services.Settings;

namespace SpaceMonger.App.Tests;

[Collection("聊天滚动控件")]
public sealed class ChatPanelScrollTests
{
    [Fact]
    public void 回到最新根据真实滚动位置显示并保留阅读位置() => RunSta(() =>
    {
        var settings = Substitute.For<ISettingsService>();
        var saved = new AppSettings();
        settings.LoadSettings().Returns(saved);
        var router = Substitute.For<IAiSkillRouter>();
        router.GetSkillCatalog().Returns(Array.Empty<AiSkillCatalogItem>());
        var vm = new ChatViewModel(new DiskAssistantService(_ => throw new InvalidOperationException(), [], new AssistantWorkspace(), settings), settings, router);
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.Resources["VP.AccentButton"] = new Style(typeof(Button));
        var panel = new ChatPanel();
        panel.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/SpaceMonger.App;component/Themes/VisionProTheme.xaml", UriKind.Relative) });
        panel.SetViewModel(vm);
        var message = new ChatMessage { Sender = SpaceMonger.Core.Enums.ChatSender.Assistant, Text = "已选定 D 盘，正在扫描。" };
        var process = new ChatSegment(ChatSegmentKind.Thinking) { Text = "检查扫描路径。" };
        message.Segments.Add(process);
        vm.Messages.Add(message);
        Layout(panel);
        var scroll = (ScrollViewer)panel.FindName("MessagesScrollViewer");
        var jump = (Button)panel.FindName("JumpButton");
        var expander = Visuals<Expander>(panel).Single(e => ReferenceEquals(e.DataContext, process) && e.Visibility == Visibility.Visible);
        Assert.Equal(0, scroll.ScrollableHeight);
        expander.IsExpanded = true;
        Layout(panel);
        Assert.Equal(Visibility.Collapsed, jump.Visibility);
        expander.IsExpanded = false;
        Layout(panel);
        Assert.Equal(Visibility.Collapsed, jump.Visibility);

        // 展开短详情后，流式内容增长到超过一屏，仍应自动跟随。
        expander.IsExpanded = true;
        Layout(panel);
        process.Text = string.Join("\n", Enumerable.Repeat("新的扫描进度。", 70));
        Layout(panel);
        Assert.True(scroll.ScrollableHeight > 24);
        Assert.InRange(scroll.ScrollableHeight - scroll.VerticalOffset, 0, 1);
        Assert.Equal(Visibility.Collapsed, jump.Visibility);
        process.Text = "新的短进度。";
        expander.IsExpanded = false;
        Layout(panel);
        // 流式追加超过一屏时，仍应继续跟随到底。
        process.Text = string.Join("\n", Enumerable.Repeat("正在检查目录和清理规则。", 90));
        expander.IsExpanded = true;
        Layout(panel);
        jump.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Layout(panel);
        Assert.True(scroll.ScrollableHeight > 24);
        Assert.InRange(scroll.ScrollableHeight - scroll.VerticalOffset, 0, 1);
        Assert.Equal(Visibility.Collapsed, jump.Visibility);

        // 手动上翻后追加内容，保留阅读位置并提供返回按钮。
        scroll.ScrollToTop();
        Layout(panel);
        Assert.Equal(Visibility.Visible, jump.Visibility);
        var offset = scroll.VerticalOffset;
        process.Text += "\n新的扫描进度。";
        Layout(panel);
        Assert.Equal(offset, scroll.VerticalOffset);
        Assert.Equal(Visibility.Visible, jump.Visibility);

        // 嵌套详情中的滚动事件不能让外层聊天误以为已经到底。
        var body = (Border)expander.Content;
        var originalContent = body.Child;
        body.Child = null;
        var details = new StackPanel();
        details.Children.Add(originalContent);
        var nested = new ScrollViewer { Height = 60, Content = new Border { Height = 300 } };
        details.Children.Add(nested);
        body.Child = details;
        Layout(panel);
        nested.ScrollToVerticalOffset(20);
        Layout(panel);
        Assert.Equal(offset, scroll.VerticalOffset);
        Assert.Equal(Visibility.Visible, jump.Visibility);
        // 详情折叠后，全部内容重新可见，不应残留返回按钮。
        expander.IsExpanded = false;
        Layout(panel);
        Assert.Equal(0, scroll.ScrollableHeight);
        Assert.Equal(Visibility.Collapsed, jump.Visibility);
        process.Text = "新的短进度。";
        Layout(panel);
        Assert.Equal(Visibility.Collapsed, jump.Visibility);
        application.Shutdown();
    });

    private static void Layout(ChatPanel panel)
    {
        for (var i = 0; i < 4; i++)
        {
            panel.Measure(new Size(360, 730));
            panel.Arrange(new Rect(0, 0, 360, 730));
            panel.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        }
    }

    private static IEnumerable<T> Visuals<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var nested in Visuals<T>(child)) yield return nested;
        }
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "聊天滚动测试未在时限内完成。");
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}

[CollectionDefinition("聊天滚动控件", DisableParallelization = true)]
public sealed class ChatPanelScrollCollection { }
