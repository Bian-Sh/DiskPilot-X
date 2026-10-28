using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DiskPilotX.App.Views;
using DiskPilotX.Core.Models;

namespace DiskPilotX.App.Tests;

[Collection("聊天滚动控件")]
public sealed class ThinkingVirtualizationTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void 十万字思考只创建少量可见容器且不丢失全文() => StaHost.Run(() =>
    {
        var text = string.Concat(Enumerable.Repeat("核对中文缓存路径与代理对😀", 10000));
        var segment = new ChatSegment(ChatSegmentKind.Thinking) { Text = text, IsExpanded = true };
        var list = new ThinkingVirtualizedView { Segment = segment };
        using var source = new HwndSource(new HwndSourceParameters("虚拟化回归")
            { Width = 320, Height = 200, WindowStyle = unchecked((int)0x80000000) }) { RootVisual = list };
        Pump(list);
        Assert.True(list.Items.Count > 100);
        var realized = Visuals<ListBoxItem>(list).Count();
        output.WriteLine($"原始字符数={text.Length}，文本片段={list.Items.Count}，可见容器={realized}");
        Assert.InRange(realized, 1, 60);
        Assert.All(list.Items.Cast<object>(), item => Assert.InRange(item.ToString()!.Length, 1, 258));
        Assert.Equal(text, string.Concat(list.Items.Cast<object>().Select(item => item.ToString())));
        list.ScrollIntoView(list.Items[0]);
        Pump(list);
        var scroll = Visuals<ScrollViewer>(list).Single();
        scroll.ScrollToTop();
        Pump(list);
        var offset = scroll.VerticalOffset;
        segment.Text += "追加内容";
        Pump(list);
        Assert.Equal(offset, scroll.VerticalOffset, 1);
        Assert.Equal(text + "追加内容", string.Concat(list.Items.Cast<object>().Select(item => item.ToString())));
        segment.IsExpanded = false;
        Pump(list);
        Assert.Empty(list.Items);
    });

    [Fact]
    public void 折叠预览有界且展开后隐藏() => StaHost.Run(() =>
    {
        var segment = new ChatSegment(ChatSegmentKind.Thinking) { Text = new string('思', 100000) + "最新增量" };
        var preview = new ThinkingPreviewText { Segment = segment };
        using var source = new HwndSource(new HwndSourceParameters("预览回归")
            { Width = 200, Height = 30, WindowStyle = unchecked((int)0x80000000) }) { RootVisual = preview };
        Pump(preview);
        var text = Assert.Single(Visuals<TextBlock>(preview));
        Assert.InRange(text.Text.Length, 1, 162);
        Assert.EndsWith("最新增量", text.Text);
        segment.Text += "第一段新增";
        Pump(preview);
        Assert.Equal("第一段新增", text.Text);
        Assert.Same(text, preview.Child);
        Assert.True(text.RenderTransform.Value.IsIdentity);
        Pump(preview);
        Assert.True(text.RenderTransform.Value.IsIdentity);
        segment.IsExpanded = true;
        Pump(preview);
        Assert.Equal(Visibility.Collapsed, preview.Visibility);
        segment.IsExpanded = false;
        segment.Text += "再次推送";
        Pump(preview);
        Assert.Equal(Visibility.Visible, preview.Visibility);
        Assert.Equal("再次推送", text.Text);
        segment.Status = ChatSegmentStatus.Completed;
        Pump(preview);
        Assert.Equal(Visibility.Collapsed, preview.Visibility);
    });

    [Fact]
    public void 首次折叠不创建正文且换行代理对和替换文本不丢失() => StaHost.Run(() =>
    {
        var segment = new ChatSegment(ChatSegmentKind.Thinking) { Text = new string('甲', 255) + "😀\r\n\n尾段" };
        var list = new ThinkingVirtualizedView { Segment = segment };
        using var source = new HwndSource(new HwndSourceParameters("片段完整性回归")
            { Width = 320, Height = 200, WindowStyle = unchecked((int)0x80000000) }) { RootVisual = list };
        Pump(list);
        Assert.Empty(list.Items);
        Assert.Empty(Visuals<ListBoxItem>(list));
        segment.IsExpanded = true;
        Pump(list);
        Assert.Equal(segment.Text, string.Concat(list.Items.Cast<object>()));
        Assert.All(list.Items.Cast<object>(), item => Assert.False(char.IsHighSurrogate(item.ToString()![^1])));
        segment.Text += "\n再次追加😀";
        Pump(list);
        Assert.Equal(segment.Text, string.Concat(list.Items.Cast<object>()));
        segment.Text = "替换后的内容";
        Pump(list);
        Assert.Single(list.Items);
        Assert.Equal(segment.Text, list.Items[0].ToString());
        segment.Text = string.Empty;
        Pump(list);
        Assert.Empty(list.Items);
    });

    [Fact]
    public void 内层底部跟随上翻与重新展开保留位置() => StaHost.Run(() =>
    {
        var segment = new ChatSegment(ChatSegmentKind.Thinking)
            { Text = string.Concat(Enumerable.Repeat("逐项核对缓存路径\n", 1000)), IsExpanded = true };
        var list = new ThinkingVirtualizedView { Segment = segment };
        using var source = new HwndSource(new HwndSourceParameters("跟随生命周期回归")
            { Width = 320, Height = 200, WindowStyle = unchecked((int)0x80000000) }) { RootVisual = list };
        Pump(list);
        var scroll = Visuals<ScrollViewer>(list).Single();
        Assert.InRange(scroll.ScrollableHeight - scroll.VerticalOffset, 0, 1);
        segment.Text += "新增末行\n";
        Pump(list);
        Assert.InRange(scroll.ScrollableHeight - scroll.VerticalOffset, 0, 1);
        scroll.ScrollToVerticalOffset(100);
        Pump(list);
        var offset = scroll.VerticalOffset;
        segment.Text += "不会抢走阅读位置\n";
        Pump(list);
        Assert.Equal(offset, scroll.VerticalOffset, 1);
        segment.IsExpanded = false;
        Pump(list);
        segment.Text += "折叠期间仍推送\n";
        segment.IsExpanded = true;
        Pump(list);
        Assert.Equal(offset, scroll.VerticalOffset, 1);
        source.RootVisual = null;
        Pump(list);
        Assert.Empty(list.Items);
    });

    private static void Pump(FrameworkElement element)
    {
        var until = DateTime.UtcNow.AddMilliseconds(130);
        do
        {
            element.Measure(new Size(320, 200));
            element.Arrange(new Rect(0, 0, 320, 200));
            element.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Thread.Sleep(5);
        } while (DateTime.UtcNow < until);
    }

    private static IEnumerable<T> Visuals<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Visuals<T>(child)) yield return nested;
        }
    }
}
