using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DiskPilotX.App.Controls;
using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Models;

namespace DiskPilotX.App.Tests;

[Collection("聊天滚动控件")]
public sealed class ModalHeaderAppearanceTests
{
    [Theory]
    [InlineData(ModalMessageType.Info, "VP.ModalNormalHeaderBrush")]
    [InlineData(ModalMessageType.Warning, "VP.ModalWarningHeaderBrush")]
    [InlineData(ModalMessageType.Error, "VP.ModalDangerHeaderBrush")]
    public void 风险只改变不透明标题底色且标题居中并移除图标(ModalMessageType type, string key) => StaHost.Run(() =>
    {
        var host = 创建宿主();
        _ = host.ShowAsync("提示标题", "左对齐正文", type, ModalButtonFlags.Positive);
        验证标题(host, key);
        host.CloseCurrent(ModalResult.Positive);
    });

    [Theory]
    [InlineData(DeletionMode.MoveToRecycleBin, "VP.ModalWarningHeaderBrush")]
    [InlineData(DeletionMode.PermanentDelete, "VP.ModalDangerHeaderBrush")]
    public void 清理确认按删除方式选择警告或严重底色(DeletionMode mode, string key) => StaHost.Run(() =>
    {
        var host = 创建宿主();
        _ = host.ShowCleanupConfirmationAsync("确认删除", new List<CleanupRecommendation>
            { new() { TargetPath = @"D:\cache\Cache", Size = 1024 } }, mode, "确认删除");
        验证标题(host, key);
        host.CloseCurrent(ModalResult.Negative);
    });

    [Fact]
    public void 切换主文字与常规资源不改变固定警示底色() => StaHost.Run(() =>
    {
        var host = 创建宿主();
        using var source = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("标题配色回归")
            { Width = 600, Height = 340, WindowStyle = unchecked((int)0x80000000) }) { RootVisual = host };
        _ = host.ShowAsync("警告", "正文", ModalMessageType.Warning, ModalButtonFlags.Positive);
        验证标题(host, "VP.ModalWarningHeaderBrush");
        var (header, title) = 标题(host);
        host.Resources["VP.TextPrimaryBrush"] = Brushes.White;
        var warning = host.Resources["VP.ModalWarningHeaderBrush"];
        host.Resources["VP.ModalNormalHeaderBrush"] = Brushes.DarkGray;
        host.UpdateLayout();
        Assert.Same(Brushes.White, title.Foreground);
        Assert.Same(warning, header.Background);
        Assert.Equal((byte)255, Assert.IsType<SolidColorBrush>(header.Background).Color.A);
        host.CloseCurrent(ModalResult.Positive);
    });

    [Fact]
    public void 清理标题沿用关闭应用弹窗的字号字重与字体() => StaHost.Run(() =>
    {
        var host = 创建宿主();
        _ = host.ShowAsync("关闭应用", "确定关闭？", ModalMessageType.Warning,
            ModalButtonFlags.Positive | ModalButtonFlags.Negative);
        var (_, closeTitle) = 标题(host);
        var size = closeTitle.FontSize;
        var weight = closeTitle.FontWeight;
        var family = closeTitle.FontFamily;
        host.CloseCurrent(ModalResult.Negative);
        _ = host.ShowCleanupConfirmationAsync("确认清理", new List<CleanupRecommendation>
            { new() { TargetPath = @"D:\cache\Cache", Size = 1024 } }, DeletionMode.MoveToRecycleBin, "确认清理");
        var (_, cleanupTitle) = 标题(host);
        Assert.Equal(20, size);
        Assert.Equal(size, cleanupTitle.FontSize);
        Assert.Equal(weight, cleanupTitle.FontWeight);
        Assert.Equal(family, cleanupTitle.FontFamily);
        host.CloseCurrent(ModalResult.Negative);
    });

    private static void 验证标题(AppModalHost host, string key)
    {
        host.Measure(new Size(600, 340));
        host.Arrange(new Rect(0, 0, 600, 340));
        host.UpdateLayout();
        var (header, title) = 标题(host);
        Assert.Same(host.Resources[key], header.Background);
        Assert.Equal((byte)255, Assert.IsType<SolidColorBrush>(header.Background).Color.A);
        Assert.Same(Brushes.White, title.Foreground);
        Assert.Equal(TextAlignment.Center, title.TextAlignment);
        Assert.InRange(header.ActualHeight, 47, 49);
        var card = (Border)host.FindName("ModalCard");
        Assert.Equal((byte)255, Assert.IsType<SolidColorBrush>(card.Background).Color.A);
        if (key == "VP.ModalNormalHeaderBrush")
            Assert.NotEqual(((SolidColorBrush)card.Background).Color, ((SolidColorBrush)header.Background).Color);
        var origin = title.TranslatePoint(new Point(), card);
        Assert.InRange(Math.Abs(origin.X + title.ActualWidth / 2 - card.ActualWidth / 2), 0, 0.5);
        var root = 内容(host);
        var body = Assert.Single(Assert.IsType<Grid>(Assert.Single(root.Children.OfType<ScrollViewer>()).Content).Children.OfType<TextBlock>());
        Assert.Equal(TextAlignment.Left, body.TextAlignment);
    }

    private static (Border Header, TextBlock Title) 标题(AppModalHost host)
    {
        var header = Assert.IsType<Border>(内容(host).Children[0]);
        return (header, Assert.IsType<TextBlock>(header.Child));
    }

    private static Grid 内容(AppModalHost host) => (Grid)((ContentPresenter)host.FindName("ModalContentPresenter")).Content;

    private static AppModalHost 创建宿主()
    {
        var host = new AppModalHost();
        host.Resources["VP.SurfaceBrush"] = Brushes.LightGray;
        host.Resources["VP.BackgroundAltBrush"] = Brushes.White;
        host.Resources["VP.ModalNormalHeaderBrush"] = Brushes.DimGray;
        host.Resources["VP.TextPrimaryBrush"] = Brushes.Black;
        host.Resources["VP.TextSecondaryBrush"] = Brushes.Gray;
        host.Resources["VP.ModalWarningHeaderBrush"] = new SolidColorBrush(Color.FromRgb(134, 113, 79));
        host.Resources["VP.DangerBrush"] = new SolidColorBrush(Color.FromRgb(255, 69, 58));
        host.Resources["VP.ModalDangerHeaderBrush"] = new SolidColorBrush(Color.FromRgb(187, 33, 36));
        host.Resources["VP.AccentButton"] = new Style(typeof(Button));
        host.Resources["VP.Button"] = new Style(typeof(Button));
        host.Resources["VP.WarningTextBrush"] = Brushes.DarkOrange;
        host.Resources["VP.DangerTextBrush"] = Brushes.Red;
        host.Resources["VP.AccentTextBrush"] = Brushes.Blue;
        return host;
    }
}
