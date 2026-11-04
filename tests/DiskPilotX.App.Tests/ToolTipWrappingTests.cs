using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DiskPilotX.App.Tests;

/// <summary>
/// 回归防线：隐式 ToolTip 样式曾缺少换行模板，默认模板把字符串内容渲染成不换行的单行
/// TextBlock，超过 MaxWidth=360 的长路径与提示文案被硬裁切（无省略号、不换行）。
/// 这里验证字符串内容走换行 TextBlock 完整显示，非字符串内容仍交给 ContentPresenter。
/// </summary>
[Collection("聊天滚动控件")]
public sealed class ToolTipWrappingTests
{
    [Fact]
    public void 长路径提示换行完整显示() => StaHost.Run(() =>
    {
        EnsureTheme();
        const string path = @"D:\AppData\Visual Studio\Projects\DiskPilot-X\.planning\skill-creator-unity-decouple-20261027\fixtures\only-moved-assets\Assets\Editor\SomeVeryLongSceneNameThatGoesOn.xaml";
        var tip = new ToolTip { Content = path };
        Layout(tip);

        var text = (TextBlock)tip.Template.FindName("TipText", tip);
        var presenter = (ContentPresenter)tip.Template.FindName("TipContent", tip);
        Assert.Equal(Visibility.Visible, text.Visibility);
        Assert.Equal(Visibility.Collapsed, presenter.Visibility);
        Assert.Equal(path, text.Text);
        Assert.True(text.ActualWidth <= 338, $"单行宽度 {text.ActualWidth} 超出 360 宽预算，仍会被裁切");
        Assert.True(text.ActualHeight > 24, $"高度 {text.ActualHeight} 说明文本没有换行");
    });

    [Fact]
    public void 非字符串内容仍走ContentPresenter() => StaHost.Run(() =>
    {
        EnsureTheme();
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "第一行" });
        panel.Children.Add(new TextBlock { Text = "第二行" });
        var tip = new ToolTip { Content = panel };
        Layout(tip);

        var text = (TextBlock)tip.Template.FindName("TipText", tip);
        var presenter = (ContentPresenter)tip.Template.FindName("TipContent", tip);
        Assert.Equal(Visibility.Collapsed, text.Visibility);
        Assert.Equal(Visibility.Visible, presenter.Visibility);
    });

    private static void EnsureTheme()
    {
        var source = new Uri("/DiskPilot-X;component/Themes/VisionProTheme.xaml", UriKind.Relative);
        var resources = Application.Current.Resources;
        var loaded = resources.MergedDictionaries
            .Any(dictionary => dictionary.Source is not null &&
                               dictionary.Source.OriginalString.EndsWith("VisionProTheme.xaml", StringComparison.OrdinalIgnoreCase));
        if (loaded) return;
        resources.MergedDictionaries.Add(new ResourceDictionary { Source = source });
    }

    private static void Layout(ToolTip tip)
    {
        tip.ApplyTemplate();
        for (var i = 0; i < 3; i++)
        {
            tip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            tip.Arrange(new Rect(0, 0, tip.DesiredSize.Width, tip.DesiredSize.Height));
            tip.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        }
    }
}
