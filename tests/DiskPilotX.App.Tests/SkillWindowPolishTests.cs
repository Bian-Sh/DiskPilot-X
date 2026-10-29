using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DiskPilotX.App.Controls;
using DiskPilotX.App.Views;
using DiskPilotX.Core.Models;
namespace DiskPilotX.App.Tests;

[Collection("聊天滚动控件")]
public sealed class SkillWindowPolishTests
{
    [Fact]
    public void 非透明原生窗口不能以透明底层合成圆角() => StaHost.Run(() =>
    {
        var window=new SkillWindow(new SkillUiSpec { Title="角部验收" });
        window.Resources["VP.BackgroundAltBrush"]=Brushes.White;
        try
        {
            Assert.False(window.AllowsTransparency);
            Assert.Equal(255,Assert.IsType<SolidColorBrush>(window.Background).Color.A);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void 动态窗口隐藏重绘品牌但保留系统标题并上移主标题() => StaHost.Run(() =>
    {
        var window=new SkillWindow(new SkillUiSpec { Title="唯一主标题" });
        try
        {
            var visibility=typeof(WindowTitleBar).GetProperty("BrandingVisibility");
            Assert.NotNull(visibility);
            var frame=(FrameworkElement)window.Content;
            frame.Measure(new Size(1000,780));frame.Arrange(new Rect(0,0,1000,780));frame.UpdateLayout();
            var titleBar=Find<WindowTitleBar>(frame).Single();
            Assert.Equal(Visibility.Collapsed,visibility!.GetValue(titleBar));
            Assert.Equal("唯一主标题",window.Title);
            var heading=Find<TextBlock>(frame).First(t=>t.Text=="唯一主标题" && t.FontSize==22);
            Assert.True(heading.TranslatePoint(new Point(),frame).Y<36,"主标题仍被重复的标题栏空行推低。");
        }
        finally { window.Close(); }
    });
    private static IEnumerable<T> Find<T>(DependencyObject root) where T:DependencyObject
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
        {var child=VisualTreeHelper.GetChild(root,i);if(child is T found)yield return found;foreach(var nested in Find<T>(child))yield return nested;}
    }
}
