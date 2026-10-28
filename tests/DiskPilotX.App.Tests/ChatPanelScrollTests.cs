using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Threading;
using NSubstitute;
using DiskPilotX.App.ViewModels;
using DiskPilotX.App.Views;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Copilot;
using DiskPilotX.Core.Services.Settings;

namespace DiskPilotX.App.Tests;

[Collection("聊天滚动控件")]
public sealed class ChatPanelScrollTests
{
    [Fact]
    public void 回到最新根据真实滚动位置显示并保留阅读位置() => StaHost.Run(() =>
    {
        var settings = Substitute.For<ISettingsService>();
        var saved = new AppSettings();
        settings.LoadSettings().Returns(saved);
        var router = Substitute.For<IAiSkillRouter>();
        router.GetSkillCatalog().Returns(Array.Empty<AiSkillCatalogItem>());
        var vm = new ChatViewModel(new DiskAssistantService(_ => throw new InvalidOperationException(), [], new AssistantWorkspace(), settings), settings, router);
        var panel = new ChatPanel();
        panel.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/DiskPilot-X;component/Themes/VisionProTheme.xaml", UriKind.Relative) });
        using var source = new HwndSource(new HwndSourceParameters("聊天滚动回归")
        {
            Width = 360, Height = 400, WindowStyle = unchecked((int)0x80000000)
        }) { RootVisual = panel };
        panel.SetViewModel(vm);
        var message = new ChatMessage { Sender = DiskPilotX.Core.Enums.ChatSender.Assistant, Text = "已选定 D 盘，正在扫描。" };
        var process = new ChatSegment(ChatSegmentKind.Thinking) { Text = "检查扫描路径。" };
        message.Segments.Add(process);
        vm.Messages.Add(message);
        Layout(panel);
        var root = Visuals<StackPanel>(panel).Single(element => element.Name == "MessageRoot");
        Assert.NotNull(root.Background);
        var copy = Visuals<Button>(root).Single(button => button.Width == 28 && button.Height == 26);
        Assert.Equal(HorizontalAlignment.Left, ((FrameworkElement)VisualTreeHelper.GetParent(copy)).HorizontalAlignment);
        message.ModelId = "实际模型-缓存验收";
        message.Usage = new(1234, 56, 1290, 1000, 0, 12, 1234);
        Layout(panel);
        var badge = Visuals<Border>(root).Single(element => element.Name == "UsageBadge");
        Assert.Equal(Visibility.Visible, badge.Visibility);
        Assert.Equal("1.3K tokens", Assert.Single(Visuals<TextBlock>(badge)).Text);
        var modelId = Visuals<TextBlock>(root).Single(text => text.Name == "MessageModelId");
        Assert.Equal(message.ModelId, modelId.Text);
        Assert.True(modelId.TransformToAncestor(root).Transform(new Point()).X < badge.TransformToAncestor(root).Transform(new Point()).X);
        Assert.True(badge.ActualWidth < 120, "默认用量胶囊不应占满消息宽度。");
        var metadata = Visuals<StackPanel>(root).Single(element => element.Name == "MessageMetadata");
        Assert.Equal(0, metadata.Opacity);
        var usageTip = Assert.IsType<ToolTip>(badge.ToolTip);
        usageTip.PlacementTarget = badge;
        usageTip.IsOpen = true;
        Layout(panel);
        Assert.Same(message, usageTip.DataContext);
        var tipText = Visuals<TextBlock>(usageTip).Select(text => text.Text).ToArray();
        Assert.Contains("已报告用量", tipText);
        Assert.Contains("缓存读取", tipText);
        Assert.Contains("缓存命中率", tipText);
        Assert.DoesNotContain(tipText, text => text.Contains("命中率 ="));
        Assert.Contains((1000d / 1234).ToString("P1"), tipText);
        Assert.Contains(1234.ToString("N0"), tipText);
        Assert.Contains(1290.ToString("N0"), tipText);
        Assert.InRange(usageTip.ActualWidth, 230, 260);
        usageTip.IsOpen = false;
        Layout(panel);
        // AI 底部仅有完成时间与复制按钮；创建时间和执行时长不混入底部时间。
        var footerTime = Assert.Single(Visuals<TextBlock>(metadata));
        Assert.Equal(Visibility.Collapsed, footerTime.Visibility);
        message.Timestamp = new DateTime(2026, 10, 26, 9, 0, 0);
        message.MarkCompletedAt(new DateTime(2026, 10, 26, 9, 2, 30));
        Layout(panel);
        Assert.Equal("09:02", footerTime.Text);
        Assert.Equal(Visibility.Visible, footerTime.Visibility);
        Assert.Equal(new DateTime(2026, 10, 26, 9, 2, 30), footerTime.ToolTip);
        // 操作栏隐藏时，其区域和上方间隙仍须命中同一消息根节点，保证移入后不会丢失 hover。
        var copyOrigin = copy.TransformToAncestor(root).Transform(new Point());
        Assert.Same(root, root.InputHitTest(new Point(copyOrigin.X + copy.ActualWidth / 2, copyOrigin.Y + copy.ActualHeight / 2)));
        Assert.Same(root, root.InputHitTest(new Point(copyOrigin.X + copy.ActualWidth / 2, copyOrigin.Y - 1)));
        var userMessage = new ChatMessage { Sender = DiskPilotX.Core.Enums.ChatSender.User, Text = "用户消息复制回归" };
        vm.Messages.Add(userMessage);
        Layout(panel);
        var userRoot = Visuals<StackPanel>(panel).Single(element => element.Name == "MessageRoot" && ReferenceEquals(element.DataContext, userMessage));
        var userCopy = Visuals<Button>(userRoot).Single(button => button.Width == 28 && button.Height == 26);
        Assert.Equal(HorizontalAlignment.Right, ((FrameworkElement)VisualTreeHelper.GetParent(userCopy)).HorizontalAlignment);
        var userCopyOrigin = userCopy.TransformToAncestor(userRoot).Transform(new Point());
        Assert.Same(userRoot, userRoot.InputHitTest(new Point(userCopyOrigin.X + userCopy.ActualWidth / 2, userCopyOrigin.Y + userCopy.ActualHeight / 2)));
        Assert.Same(userRoot, userRoot.InputHitTest(new Point(userCopyOrigin.X + userCopy.ActualWidth / 2, userCopyOrigin.Y - 1)));
        vm.Messages.Remove(userMessage);
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
        var nested = new ScrollViewer { Height = 60, Content = new Border { Height = 400 } };
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
        // 折叠到全部可见后再次增长，须恢复跟随最新消息。
        expander.IsExpanded = true;
        process.Text = string.Join("\n", Enumerable.Repeat("折叠后恢复的扫描进度。", 90));
        Layout(panel);
        Assert.True(scroll.ScrollableHeight > 24);
        Assert.InRange(scroll.ScrollableHeight - scroll.VerticalOffset, 0, 1);
        Assert.Equal(Visibility.Collapsed, jump.Visibility);
        验证问答展示(panel, vm);
        验证问答替换输入区(panel, vm);
    });

    private static void 验证问答展示(ChatPanel panel, ChatViewModel vm)
    {
        vm.Messages.Clear();
        var message = new ChatMessage { Sender = DiskPilotX.Core.Enums.ChatSender.Assistant };
        var segment = new ChatSegment(ChatSegmentKind.Tool)
        {
            ToolName = "ask_user", Status = ChatSegmentStatus.Completed,
            Detail = "参数：\n" + """{"title":"确认","question":"保留哪些内容？","fields":[{"id":"scope","label":"保留哪些内容？","type":"text"}]}""",
            Text = """{"cancelled":false,"values":{"scope":"保留用户资料"},"custom_inputs":{}}"""
        };
        message.Segments.Add(segment);
        vm.Messages.Add(message);
        Layout(panel);
        var expander = Visuals<Expander>(panel).Single(e => ReferenceEquals(e.DataContext, segment) && e.Visibility == Visibility.Visible);
        expander.IsExpanded = true;
        Layout(panel);
        Assert.Contains(Visuals<TextBlock>(expander), t => t.Text == "保留哪些内容？" && t.IsVisible);
        Assert.Contains(Visuals<TextBlock>(expander), t => t.Text == "保留用户资料" && t.IsVisible);
        Assert.DoesNotContain(Visuals<TextBox>(expander), t => t.IsVisible);
        Assert.DoesNotContain(Visuals<TextBlock>(expander), t => t.Text == "ask_user" && t.IsVisible);
        // 普通工具依然可查看原有 JSON 结果。
        segment.ToolName = "scan_path";
        Layout(panel);
        Assert.Contains(Visuals<TextBox>(expander), t => t.IsVisible);
    }

    private static void 验证问答替换输入区(ChatPanel panel, ChatViewModel vm)
    {
        var input = (Border)panel.FindName("InputCard");
        var send = (Button)panel.FindName("SendButton");
        var before = send.TransformToAncestor(panel).Transform(new Point());
        var inputBottom = input.TransformToAncestor(panel).Transform(new Point(0, input.ActualHeight)).Y;
        var waiting = vm.AskUserAsync(System.Text.Json.Nodes.JsonNode.Parse("""{"title":"确认","question":"请填写保留范围","fields":[{"id":"scope","label":"保留范围","type":"text","is_required":true,"default_text":"用户资料"}]}"""), CancellationToken.None);
        vm.IsOperationRunning = true;
        Layout(panel);
        var card = Assert.Single(Visuals<AskUserCard>(panel));
        Assert.Equal(Visibility.Collapsed, input.Visibility);
        Assert.Same(send, panel.FindName("SendButton"));
        Assert.Equal(before, send.TransformToAncestor(panel).Transform(new Point()));
        Assert.Equal(inputBottom, card.TransformToAncestor(panel).Transform(new Point(0, card.ActualHeight)).Y, 1);
        var next = (Button)card.FindName("NextButton");
        Assert.Equal(send.ActualHeight, next.ActualHeight);
        Assert.Equal(send.TransformToAncestor(panel).Transform(new Point()).Y, next.TransformToAncestor(panel).Transform(new Point()).Y, 1);
        Assert.Equal("提交回答", send.ToolTip);
        var glyphs = Visuals<System.Windows.Shapes.Path>(send).ToArray();
        Assert.Equal(Visibility.Visible, glyphs[0].Visibility);
        Assert.Equal(Visibility.Collapsed, glyphs[1].Visibility);
        send.Command.Execute(null);
        for (var i = 0; i < 100 && !waiting.IsCompleted; i++) { Layout(panel); Thread.Sleep(5); }
        Assert.True(waiting.IsCompletedSuccessfully);
        Layout(panel);
        Assert.Null(vm.PendingAskUser);
        Assert.Equal(Visibility.Visible, input.Visibility);
        vm.IsOperationRunning = false;
    }

    private static void Layout(ChatPanel panel)
    {
        for (var i = 0; i < 4; i++)
        {
            panel.Measure(new Size(360, 400));
            panel.Arrange(new Rect(0, 0, 360, 400));
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

}

[CollectionDefinition("聊天滚动控件", DisableParallelization = true)]
public sealed class ChatPanelScrollCollection { }
