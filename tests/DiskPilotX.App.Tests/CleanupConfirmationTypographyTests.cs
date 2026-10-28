using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using DiskPilotX.App.Controls;
using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Models;

namespace DiskPilotX.App.Tests;

[Collection("聊天滚动控件")]
public sealed class CleanupConfirmationTypographyTests
{
    [Fact]
    public void 正文左对齐并独占滚动区域而非图标文字两列() => StaHost.Run(() =>
    {
        var host = 创建宿主();
        _ = host.ShowConfirmationAsync("确认清理", "连续正文", "确认清理");
        var root = 内容(host);
        var scroll = Assert.Single(root.Children.OfType<ScrollViewer>());
        var body = 正文控件(scroll);
        Assert.Equal(TextAlignment.Left, body.TextAlignment);
        Assert.Equal(HorizontalAlignment.Stretch, body.HorizontalAlignment);
        var header = Assert.IsType<Border>(root.Children[0]);
        var title = Assert.Single(后代<TextBlock>(header).Where(text => text.Text == "确认清理"));
        Assert.Equal(TextAlignment.Center, title.TextAlignment);
        Assert.DoesNotContain(后代<FrameworkElement>(scroll), element => element is System.Windows.Shapes.Ellipse);
        host.CloseCurrent(ModalResult.Negative);
    });

    [Theory]
    [InlineData(DeletionMode.MoveToRecycleBin)]
    [InlineData(DeletionMode.PermanentDelete)]
    public void 单项连续富文本仅强调名称数量大小和方式且名称按字面呈现(DeletionMode mode) => StaHost.Run(() =>
    {
        var host = 创建宿主();
        var target = new CleanupRecommendation { TargetPath = @"D:\cache\Cache **[用户资料]**", Size = 282800000 };
        打开清理确认(host, [target], mode);
        var body = 正文控件(Assert.Single(内容(host).Children.OfType<ScrollViewer>()));
        Assert.DoesNotContain('\n', 正文(body));
        Assert.DoesNotContain("D:\\cache", 正文(body));
        Assert.Contains("Cache **[用户资料]**", 正文(body));
        Assert.Equal(1, 正文(body).Split("1 个项目").Length - 1);
        var highlights = body.Inlines.OfType<Run>().Where(run => run.FontWeight == FontWeights.SemiBold).Select(run => run.Text).ToArray();
        Assert.Contains("Cache **[用户资料]**", highlights);
        Assert.Contains("1 个项目", highlights);
        Assert.Contains(mode == DeletionMode.PermanentDelete ? "永久删除" : "移入回收站", highlights);
        Assert.Equal(4, highlights.Length);
        Assert.Equal(mode == DeletionMode.PermanentDelete, 正文(body).Contains("无法恢复"));
        var cancel = 内容(host).Children.OfType<StackPanel>().SelectMany(panel => panel.Children.OfType<Button>()).Single(button => button.IsCancel);
        Assert.True(cancel.IsDefault);
        host.CloseCurrent(ModalResult.Negative);
    });

    [Fact]
    public void 多项只展示总数总量并且长名称可以滚动而按钮常驻() => StaHost.Run(() =>
    {
        var host = 创建宿主();
        var items = Enumerable.Range(0, 10).Select(index => new CleanupRecommendation
            { TargetPath = $@"D:\cache\秘密路径{index}", Size = 1024 }).ToList();
        打开清理确认(host, items, DeletionMode.MoveToRecycleBin);
        var body = 正文控件(Assert.Single(内容(host).Children.OfType<ScrollViewer>()));
        Assert.Contains("10 个项目", 正文(body));
        Assert.Contains("10.0 KB", 正文(body));
        Assert.DoesNotContain("秘密路径", 正文(body));
        Assert.DoesNotContain('\n', 正文(body));
        host.CloseCurrent(ModalResult.Negative);

        打开清理确认(host, [new() { TargetPath = @"D:\cache\" + new string('长', 900), Size = 1024 }], DeletionMode.PermanentDelete);
        host.Measure(new Size(500, 320));
        host.Arrange(new Rect(0, 0, 500, 320));
        host.UpdateLayout();
        var scroll = Assert.Single(内容(host).Children.OfType<ScrollViewer>());
        Assert.True(scroll.ExtentHeight > scroll.ViewportHeight);
        var buttons = Assert.Single(内容(host).Children.OfType<StackPanel>());
        var card = (Border)host.FindName("ModalCard");
        var point = buttons.TranslatePoint(new Point(), card);
        Assert.True(point.Y >= 0 && point.Y + buttons.ActualHeight <= card.ActualHeight);
        host.CloseCurrent(ModalResult.Negative);
    });

    private static void 打开清理确认(AppModalHost host, List<CleanupRecommendation> items, DeletionMode mode)
    {
        _ = host.ShowCleanupConfirmationAsync("确认清理", items, mode, "确认清理");
    }

    private static TextBlock 正文控件(ScrollViewer scroll) => Assert.Single(Assert.IsType<Grid>(scroll.Content).Children.OfType<TextBlock>());

    private static string 正文(TextBlock body) => new TextRange(body.ContentStart, body.ContentEnd).Text;

    private static Grid 内容(AppModalHost host) => (Grid)((ContentPresenter)host.FindName("ModalContentPresenter")).Content;

    private static IEnumerable<T> 后代<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T typed) yield return typed;
            foreach (var descendant in 后代<T>(child)) yield return descendant;
        }
    }

    private static AppModalHost 创建宿主()
    {
        var host = new AppModalHost();
        foreach (var key in new[] { "VP.SurfaceBrush", "VP.TextPrimaryBrush", "VP.TextSecondaryBrush", "VP.WarningTextBrush" })
            host.Resources[key] = Brushes.Black;
        host.Resources["VP.AccentButton"] = new Style(typeof(Button));
        host.Resources["VP.Button"] = new Style(typeof(Button));
        return host;
    }
}
