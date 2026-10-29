using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DiskPilotX.App.Controls;
using DiskPilotX.App.Views;
using DiskPilotX.Core.Models;

namespace DiskPilotX.App.Tests;

[Collection("聊天滚动控件")]
public sealed class WindowTitleBarAlignmentTests
{
    [Theory]
    [InlineData("宿主布局")]
    [InlineData("结果窗")]
    [InlineData("任务窗")]
    public void 窗口按钮与标题栏顶边对齐而不垂直居中(string kind) => StaHost.Run(() =>
    {
        var previous=Application.Current.Resources;
        Application.Current.Resources=new ResourceDictionary
        {
            MergedDictionaries={new ResourceDictionary { Source=new Uri("/DiskPilot-X;component/Themes/VisionProTheme.xaml",UriKind.Relative) }}
        };
        Window? window=null;
        try
        {
            FrameworkElement root;
            if(kind=="宿主布局")
            {
                var grid=new Grid();grid.RowDefinitions.Add(new RowDefinition { Height=new GridLength(36) });
                grid.RowDefinitions.Add(new RowDefinition());grid.Children.Add(new WindowTitleBar());root=grid;
            }
            else
            {
                window=kind=="结果窗"
                    ? new SkillWindow(new SkillUiSpec { Title="顶边对齐" })
                    : new SkillTaskWindow("顶边对齐",null,null,(_,_,_)=>Task.FromResult<JsonNode>(new JsonObject()));
                root=(FrameworkElement)window.Content;
            }
            root.Measure(new Size(1000,780));root.Arrange(new Rect(0,0,1000,780));root.UpdateLayout();
            var bar=Assert.Single(Visuals<WindowTitleBar>(root));
            foreach(var name in new[]{"MinimizeButton","MaximizeButton","CloseButton"})
            {
                var button=Assert.IsType<Button>(bar.FindName(name));
                Assert.Equal(32,button.ActualHeight);
                Assert.Equal(0,button.TranslatePoint(new Point(),bar).Y,6);
                Assert.Equal(0,button.TranslatePoint(new Point(),root).Y,6);
                Assert.Equal(46,button.ActualWidth);
            }
        }
        finally { window?.Close();Application.Current.Resources=previous; }
    });

    private static IEnumerable<T> Visuals<T>(DependencyObject parent) where T:DependencyObject
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {
            var child=VisualTreeHelper.GetChild(parent,i);
            if(child is T match)yield return match;
            foreach(var nested in Visuals<T>(child))yield return nested;
        }
    }
}
