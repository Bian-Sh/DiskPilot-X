using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using DiskPilotX.App.Controls;
using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Models;

namespace DiskPilotX.App.Tests;

[Collection("聊天滚动控件")]
public sealed class ModalContentOptionsTests
{
    [Fact]
    public void 默认正文保持左对齐且不显示图标() => StaHost.Run(() =>
    {
        var host = 创建宿主();
        _ = host.ShowAsync("提示", "原有正文", ModalMessageType.Info, ModalButtonFlags.Positive);
        var (_, panel, body) = 布局(host);
        Assert.Equal(TextAlignment.Left, body.TextAlignment);
        Assert.Single(panel.Children);
        Assert.Empty(后代(panel).Where(element => element.Name == "ModalBodyIcon"));
        host.CloseCurrent(ModalResult.Positive);
    });

    [Theory]
    [InlineData(TextAlignment.Left)]
    [InlineData(TextAlignment.Center)]
    [InlineData(TextAlignment.Right)]
    public void 正文对齐可配置且上下留白一致(TextAlignment alignment) => StaHost.Run(() =>
    {
        var host = 创建宿主();
        显示(host, alignment, null);
        var (scroll, panel, body) = 布局(host);
        Assert.Equal(alignment, body.TextAlignment);
        Assert.Single(panel.Children);
        验证居中(scroll, body, horizontal: true);
        host.CloseCurrent(ModalResult.Positive);
    });

    [Theory]
    [InlineData(ModalMessageType.Info, "i", "VP.AccentTextBrush")]
    [InlineData(ModalMessageType.Warning, "!", "VP.WarningTextBrush")]
    [InlineData(ModalMessageType.Error, "✕", "VP.DangerTextBrush")]
    public void 图标类型可独立指定且不改变标题消息级别(ModalMessageType iconType, string glyph, string brushKey) => StaHost.Run(() =>
    {
        var host = 创建宿主();
        显示(host, TextAlignment.Center, iconType);
        var (scroll, panel, _) = 布局(host);
        var group = Assert.IsType<Grid>(Assert.Single(panel.Children));
        var icon = Assert.IsType<Grid>(后代(group).Single(element => element.Name == "ModalBodyIcon"));
        var symbol = Assert.Single(icon.Children.OfType<TextBlock>());
        Assert.Equal(glyph, symbol.Text);
        Assert.Same(host.Resources[brushKey], symbol.Foreground);
        Assert.Equal(34, icon.Width);
        Assert.Equal(34, icon.Height);
        Assert.Equal(12, icon.Margin.Right);
        var header = Assert.IsType<Border>(内容(host).Children[0]);
        Assert.Same(host.Resources["VP.ModalNormalHeaderBrush"], header.Background);
        Assert.InRange(header.ActualHeight, 47, 49);
        验证居中(scroll, group, horizontal: true);
        Assert.True(group.ActualWidth < panel.ActualWidth - 56);
        host.CloseCurrent(ModalResult.Positive);
    });

    [Theory]
    [InlineData(TextAlignment.Left)]
    [InlineData(TextAlignment.Center)]
    public void 带图标的长正文有界换行并从顶部滚动(TextAlignment alignment) => StaHost.Run(() =>
    {
        var host = 创建宿主();
        显示(host, alignment, ModalMessageType.Warning,
            string.Join("\n", Enumerable.Repeat("这是一段需要换行和滚动的很长的确认正文。", 100)));
        var (scroll, panel, body) = 布局(host, 340, 320);
        Assert.True(scroll.ExtentHeight > scroll.ViewportHeight);
        Assert.Equal(0, scroll.HorizontalOffset);
        Assert.True(scroll.ExtentWidth <= scroll.ViewportWidth + 1);
        var group = Assert.IsType<Grid>(Assert.Single(panel.Children));
        Assert.InRange(group.TranslatePoint(new Point(), scroll).Y, 0, 18);
        var icon = 后代(group).Single(element => element.Name == "ModalBodyIcon");
        Assert.InRange(icon.TranslatePoint(new Point(), scroll).Y, 0, 18);
        Assert.True(body.ActualWidth > 0);
        host.CloseCurrent(ModalResult.Negative);
    });

    [Theory]
    [InlineData("ShowMessageAsync")]
    [InlineData("ShowConfirmationAsync")]
    [InlineData("ShowCleanupConfirmationAsync")]
    public void 各消息入口透传正文对齐和可选图标(string methodName) => StaHost.Run(() =>
    {
        var host = 创建宿主();
        var parameters = new Dictionary<string, object?>
        {
            ["message"] = "提示正文", ["title"] = "标题", ["content"] = "确认正文",
            ["buttons"] = MessageBoxButton.OK, ["image"] = MessageBoxImage.Warning,
            ["positiveText"] = "确认", ["mode"] = DeletionMode.MoveToRecycleBin,
            ["items"] = new List<CleanupRecommendation> { new() { TargetPath = @"D:\cache\Cache", Size = 1024 } }
        };
        _ = 调用(host, methodName, parameters, TextAlignment.Center, ModalMessageType.Info);
        var (_, panel, body) = 布局(host);
        Assert.Equal(TextAlignment.Center, body.TextAlignment);
        Assert.Single(后代(panel).Where(element => element.Name == "ModalBodyIcon"));
        if (methodName != "ShowMessageAsync")
        {
            var footer = Assert.Single(内容(host).Children.OfType<StackPanel>());
            Assert.True(footer.Children.OfType<Button>().Single(button => button.IsCancel).IsDefault);
        }
        host.CloseCurrent(ModalResult.Negative);
    });

    private static void 显示(AppModalHost host, TextAlignment alignment, ModalMessageType? iconType, string content = "确定要关闭应用吗？") =>
        _ = 调用(host, "ShowAsync", new Dictionary<string, object?>
        {
            ["title"] = "提示", ["content"] = content,
            ["messageType"] = ModalMessageType.Info, ["buttonFlags"] = ModalButtonFlags.Positive | ModalButtonFlags.Negative
        }, alignment, iconType);

    private static object? 调用(AppModalHost host, string methodName, Dictionary<string, object?> values,
        TextAlignment alignment, ModalMessageType? iconType)
    {
        var method = Assert.Single(typeof(AppModalHost).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(candidate => candidate.Name == methodName));
        var parameters = method.GetParameters();
        Assert.Contains(parameters, parameter => parameter.Name == "contentTextAlignment" && parameter.HasDefaultValue);
        Assert.Contains(parameters, parameter => parameter.Name == "contentIconType" && parameter.HasDefaultValue);
        values["contentTextAlignment"] = alignment;
        values["contentIconType"] = iconType;
        return method.Invoke(host, parameters.Select(parameter =>
            values.TryGetValue(parameter.Name!, out var value) ? value : parameter.DefaultValue).ToArray());
    }

    private static (ScrollViewer Scroll, Grid Panel, TextBlock Body) 布局(AppModalHost host, double width = 600, double height = 420)
    {
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        host.UpdateLayout();
        host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
        host.UpdateLayout();
        var scroll = Assert.Single(内容(host).Children.OfType<ScrollViewer>());
        var panel = Assert.IsType<Grid>(scroll.Content);
        return (scroll, panel, 后代(panel).OfType<TextBlock>().Single(text => text.Text != "i" && text.Text != "!" && text.Text != "✕"));
    }

    private static void 验证居中(ScrollViewer scroll, FrameworkElement element, bool horizontal)
    {
        scroll.ApplyTemplate();
        var viewport = (ScrollContentPresenter)scroll.Template.FindName("PART_ScrollContentPresenter", scroll);
        var point = element.TranslatePoint(new Point(), viewport);
        Assert.InRange(Math.Abs(point.Y - (viewport.ActualHeight - point.Y - element.ActualHeight)), 0, 1);
        if (horizontal)
            Assert.InRange(Math.Abs(point.X - (viewport.ActualWidth - point.X - element.ActualWidth)), 0, 1);
    }

    private static IEnumerable<FrameworkElement> 后代(Panel panel)
    {
        foreach (var child in panel.Children.OfType<FrameworkElement>())
        {
            yield return child;
            if (child is Panel nested)
                foreach (var element in 后代(nested)) yield return element;
        }
    }

    private static Grid 内容(AppModalHost host) => (Grid)((ContentPresenter)host.FindName("ModalContentPresenter")).Content;

    private static AppModalHost 创建宿主()
    {
        var host = new AppModalHost();
        host.Resources["VP.SurfaceBrush"] = Brushes.LightGray;
        host.Resources["VP.BackgroundAltBrush"] = Brushes.White;
        host.Resources["VP.TextSecondaryBrush"] = Brushes.Gray;
        host.Resources["VP.ModalNormalHeaderBrush"] = Brushes.DimGray;
        host.Resources["VP.ModalWarningHeaderBrush"] = Brushes.SaddleBrown;
        host.Resources["VP.ModalDangerHeaderBrush"] = Brushes.DarkRed;
        host.Resources["VP.AccentTextBrush"] = Brushes.Blue;
        host.Resources["VP.WarningTextBrush"] = Brushes.DarkOrange;
        host.Resources["VP.DangerTextBrush"] = Brushes.Red;
        host.Resources["VP.AccentButton"] = new Style(typeof(Button));
        host.Resources["VP.Button"] = new Style(typeof(Button));
        return host;
    }
}
