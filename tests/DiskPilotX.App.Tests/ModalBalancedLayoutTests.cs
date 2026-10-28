using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using DiskPilotX.App.Controls;
using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Models;

namespace DiskPilotX.App.Tests;

[Collection("聊天滚动控件")]
public sealed class ModalBalancedLayoutTests
{
    [Theory]
    [InlineData(400)]
    [InlineData(600)]
    public void 外框沿原始比例伸展但标题和按钮底边保持固定(double ownerWidth) => StaHost.Run(() =>
    {
        var host = 创建宿主();
        _ = host.ShowAsync("常规提示", "短正文", ModalMessageType.Info, ModalButtonFlags.Positive);
        布局(host, ownerWidth, 420);
        var (card, root, scroll, body, footer) = 元素(host);
        Assert.InRange(Math.Abs(card.ActualHeight - card.ActualWidth * 180 / 386), 0, 2);
        Assert.InRange(((Border)root.Children[0]).ActualHeight, 47, 49);
        Assert.Equal(16, footer.Margin.Bottom);
        var button = Assert.Single(footer.Children.OfType<Button>());
        var origin = button.TranslatePoint(new Point(), card);
        Assert.InRange(card.ActualHeight - origin.Y - button.ActualHeight, 16, 18);
        验证等距(scroll, body);
        host.CloseCurrent(ModalResult.Positive);
    });

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public void 清理正文上下间隙相等且默认取消(int count) => StaHost.Run(() =>
    {
        var host = 创建宿主();
        _ = host.ShowCleanupConfirmationAsync("确认清理", Enumerable.Range(0, count).Select(index => new CleanupRecommendation
            { TargetPath = $@"D:\cache\Cache{index}", Size = 282800000 }).ToList(), DeletionMode.MoveToRecycleBin, "确认清理");
        布局(host, 600, 420);
        var (_, root, scroll, body, footer) = 元素(host);
        验证等距(scroll, body);
        Assert.True(footer.Children.OfType<Button>().Single(button => button.IsCancel).IsDefault);
        var title = Assert.IsType<TextBlock>(((Border)root.Children[0]).Child);
        Assert.Same(Brushes.White, title.Foreground);
        host.CloseCurrent(ModalResult.Negative);
    });

    [Fact]
    public void 长正文从顶部可读且只滚动中间区域() => StaHost.Run(() =>
    {
        var host = 创建宿主();
        _ = host.ShowConfirmationAsync("确认清理", string.Join("\n", Enumerable.Repeat("长正文从这里开始", 100)), "确认清理");
        布局(host, 600, 320);
        var (card, _, scroll, body, footer) = 元素(host);
        Assert.True(scroll.ExtentHeight > scroll.ViewportHeight);
        Assert.InRange(body.TranslatePoint(new Point(), scroll).Y, 0, 18);
        Assert.InRange(card.ActualHeight, 150, 272);
        var point = footer.TranslatePoint(new Point(), card);
        Assert.True(point.Y + footer.ActualHeight <= card.ActualHeight);
        host.CloseCurrent(ModalResult.Negative);
    });

    [Fact]
    public void 自定义内容弹窗不被消息弹窗的比例和宽度覆盖() => StaHost.Run(() =>
    {
        var host = 创建宿主();
        _ = host.ShowAsync("消息", "内容", ModalMessageType.Info, ModalButtonFlags.Positive);
        布局(host, 600, 420);
        host.CloseCurrent(ModalResult.Positive);
        _ = host.ShowContentAsync(new Border { Width = 200, Height = 100 });
        布局(host, 600, 420);
        var card = (Border)host.FindName("ModalCard");
        Assert.True(double.IsNaN(card.Width));
        Assert.Equal(0, card.MinHeight);
        Assert.InRange(card.ActualWidth, 200, 250);
        host.CloseCurrent(null);
    });

    private static void 验证等距(ScrollViewer scroll, TextBlock body)
    {
        scroll.ApplyTemplate();
        var presenter = (ScrollContentPresenter)scroll.Template.FindName("PART_ScrollContentPresenter", scroll);
        var point = body.TranslatePoint(new Point(), presenter);
        var above = point.Y;
        var below = presenter.ActualHeight - point.Y - body.ActualHeight;
        Assert.InRange(Math.Abs(above - below), 0, 1);
        Assert.True(above >= 12);
    }

    private static void 布局(AppModalHost host, double width, double height)
    {
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        host.UpdateLayout();
        host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
        host.UpdateLayout();
    }

    private static (Border Card, Grid Root, ScrollViewer Scroll, TextBlock Body, StackPanel Footer) 元素(AppModalHost host)
    {
        var root = (Grid)((ContentPresenter)host.FindName("ModalContentPresenter")).Content;
        var scroll = Assert.Single(root.Children.OfType<ScrollViewer>());
        var body = scroll.Content is TextBlock text ? text : Assert.Single(((Grid)scroll.Content).Children.OfType<TextBlock>());
        return ((Border)host.FindName("ModalCard"), root, scroll, body, Assert.Single(root.Children.OfType<StackPanel>()));
    }

    private static AppModalHost 创建宿主()
    {
        var host = new AppModalHost();
        foreach (var key in new[] { "VP.SurfaceBrush", "VP.BackgroundAltBrush" }) host.Resources[key] = Brushes.White;
        host.Resources["VP.TextPrimaryBrush"] = Brushes.Black;
        host.Resources["VP.TextSecondaryBrush"] = Brushes.Gray;
        host.Resources["VP.ModalNormalHeaderBrush"] = Brushes.DimGray;
        host.Resources["VP.ModalWarningHeaderBrush"] = Brushes.SaddleBrown;
        host.Resources["VP.DangerBrush"] = Brushes.DarkRed;
        host.Resources["VP.AccentButton"] = new Style(typeof(Button));
        host.Resources["VP.Button"] = new Style(typeof(Button));
        return host;
    }
}
