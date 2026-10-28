using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DiskPilotX.App.Services;
using DiskPilotX.App.ViewModels;
using DiskPilotX.App.Views.SettingsSections;
using NSubstitute;

namespace DiskPilotX.App.Tests;

[Collection("聊天滚动控件")]
public sealed class UpdateSettingsScrollTests
{
    [Theory]
    [InlineData(true, 120)]
    [InlineData(true, -120)]
    [InlineData(false, 120)]
    [InlineData(false, -120)]
    public void 长内容到达当前方向边界后滚动整个设置页(bool left, int delta) => StaHost.Run(() =>
    {
        using var fixture = new ScrollFixture(longContent: true);
        var inner = fixture.Inner(left);
        Assert.True(inner.ScrollableHeight > 0);
        inner.ScrollToVerticalOffset(delta > 0 ? 0 : inner.ScrollableHeight);
        fixture.Layout();
        var before = inner.VerticalOffset;
        var outerBefore = fixture.Outer.VerticalOffset;
        fixture.Wheel((UIElement)inner.Content, delta);
        Assert.Equal(before, inner.VerticalOffset);
        Assert.True(delta > 0 ? fixture.Outer.VerticalOffset < outerBefore : fixture.Outer.VerticalOffset > outerBefore);
    });

    [Theory]
    [InlineData(true, 120)]
    [InlineData(true, -120)]
    [InlineData(false, 120)]
    [InlineData(false, -120)]
    public void 长内容中间位置只滚内部不滚设置页(bool left, int delta) => StaHost.Run(() =>
    {
        using var fixture = new ScrollFixture(longContent: true);
        var inner = fixture.Inner(left);
        Assert.True(inner.ScrollableHeight > 0);
        inner.ScrollToVerticalOffset(inner.ScrollableHeight / 2);
        fixture.Layout();
        var before = inner.VerticalOffset;
        var outerBefore = fixture.Outer.VerticalOffset;
        fixture.Wheel((UIElement)inner.Content, delta);
        Assert.True(delta > 0 ? inner.VerticalOffset < before : inner.VerticalOffset > before);
        Assert.Equal(outerBefore, fixture.Outer.VerticalOffset);
    });

    [Theory]
    [InlineData(true, 120)]
    [InlineData(true, -120)]
    [InlineData(false, 120)]
    [InlineData(false, -120)]
    public void 内容无需内部滚动时直接滚设置页(bool left, int delta) => StaHost.Run(() =>
    {
        using var fixture = new ScrollFixture(longContent: false);
        var inner = fixture.Inner(left);
        Assert.Equal(0, inner.ScrollableHeight);
        var before = fixture.Outer.VerticalOffset;
        fixture.Wheel((UIElement)inner.Content, delta);
        Assert.True(delta > 0 ? fixture.Outer.VerticalOffset < before : fixture.Outer.VerticalOffset > before);
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void 左右栏标题及空白处沿用相同内部滚动规则(bool left) => StaHost.Run(() =>
    {
        using var fixture = new ScrollFixture(longContent: true);
        var inner = fixture.Inner(left);
        inner.ScrollToVerticalOffset(inner.ScrollableHeight / 2);
        fixture.Layout();
        var before = inner.VerticalOffset;
        var outerBefore = fixture.Outer.VerticalOffset;
        var source = left ? (UIElement)fixture.List.Parent : (UIElement)fixture.Document.Parent;
        fixture.Wheel(source, -120);
        Assert.True(inner.VerticalOffset > before);
        Assert.Equal(outerBefore, fixture.Outer.VerticalOffset);
    });

    private sealed class ScrollFixture : IDisposable
    {
        private readonly HwndSource source;
        public ScrollViewer Outer { get; }
        public ListBox List { get; }
        public FlowDocumentScrollViewer Document { get; }

        public ScrollFixture(bool longContent)
        {
            var service = Substitute.For<IUpdateService>();
            service.GetCurrentVersion().Returns("1.0.0");
            var vm = new UpdateViewModel(service) { HistoryLoaded = true };
            var notes = longContent ? string.Join("\n\n", Enumerable.Range(1, 80).Select(i => $"## 验收段落 {i}\n用于确认右侧正文确实需要滚动，而不是只测空白区域。")) : "一行短日志";
            for (var i = 0; i < (longContent ? 48 : 1); i++)
                vm.Releases.Add(new UpdateRelease { Version = $"1.0.{i}", ReleaseNotes = notes, PublishedAt = "2026-10-28" });
            vm.SelectedRelease = vm.Releases[0];
            var section = new UpdateSettingsSection { DataContext = vm };
            var content = new StackPanel();
            content.Children.Add(new Border { Height = 500 });
            content.Children.Add(section);
            content.Children.Add(new Border { Height = 900 });
            Outer = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Width = 800, Height = 400 };
            source = new HwndSource(new HwndSourceParameters("发布日志嵌套滚轮测试") { Width = 800, Height = 400 }) { RootVisual = Outer };
            Layout();
            List = Visuals<ListBox>(section).Single();
            Document = Visuals<FlowDocumentScrollViewer>(section).Single();
            Outer.ScrollToVerticalOffset(400);
            Layout();
        }

        public ScrollViewer Inner(bool left) => Visuals<ScrollViewer>(left ? List : Document).First();

        public void Layout()
        {
            Outer.Measure(new Size(800, 400));
            Outer.Arrange(new Rect(0, 0, 800, 400));
            Outer.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        }

        public void Wheel(UIElement target, int delta)
        {
            // 模拟 WPF 原生的预览阶段和未被处理时的冒泡阶段，不直接调用滚动方法。
            var e = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
            target.RaiseEvent(e);
            if (!e.Handled)
                target.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = Mouse.MouseWheelEvent });
            Layout();
        }

        public void Dispose() => source.Dispose();
    }

    private static IEnumerable<T> Visuals<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Visuals<T>(child)) yield return descendant;
        }
    }
}
