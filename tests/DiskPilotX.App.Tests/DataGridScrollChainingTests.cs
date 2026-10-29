using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
namespace DiskPilotX.App.Tests;

[Collection("聊天滚动控件")]
public sealed class DataGridScrollChainingTests
{
    [Theory]
    [InlineData(0,-120)]
    [InlineData(1,-120)]
    [InlineData(80,-120)]
    [InlineData(80,120)]
    public void 无内容短表及当前方向边界只滚外层(int rows,int delta) => StaHost.Run(()=>
    {
        using var fixture=new Fixture(rows);
        if(delta<0)fixture.Inner.ScrollToBottom();else fixture.Inner.ScrollToTop();
        fixture.Layout();var inside=fixture.Inner.VerticalOffset;var outside=fixture.Outer.VerticalOffset;
        fixture.Wheel(delta);
        Assert.Equal(inside,fixture.Inner.VerticalOffset);
        Assert.True(delta<0 ? fixture.Outer.VerticalOffset>outside : fixture.Outer.VerticalOffset<outside,"表格边界吞掉滚轮，外层未滚动。");
    });
    [Theory]
    [InlineData(-120)]
    [InlineData(120)]
    public void 长表中段只滚自身且保持选择与横向偏移(int delta) => StaHost.Run(()=>
    {
        using var fixture=new Fixture(80);fixture.Inner.ScrollToVerticalOffset(fixture.Inner.ScrollableHeight/2);fixture.Layout();
        var inside=fixture.Inner.VerticalOffset;var outside=fixture.Outer.VerticalOffset;var horizontal=fixture.Inner.HorizontalOffset;
        fixture.Table.SelectedIndex=3;fixture.Wheel(delta);
        Assert.True(delta<0 ? fixture.Inner.VerticalOffset>inside : fixture.Inner.VerticalOffset<inside);
        Assert.Equal(outside,fixture.Outer.VerticalOffset);Assert.Equal(horizontal,fixture.Inner.HorizontalOffset);Assert.Equal(3,fixture.Table.SelectedIndex);
    });
    [Fact]
    public void 任务表格使用宿主显式样式也能转交滚轮() => StaHost.Run(()=>
    {
        using var fixture=new Fixture(1);fixture.Table.SetResourceReference(FrameworkElement.StyleProperty,"VP.DataGrid");fixture.Layout();
        var before=fixture.Outer.VerticalOffset;fixture.Wheel(-120);Assert.True(fixture.Outer.VerticalOffset>before);
    });
    private sealed class Fixture:IDisposable
    {
        private readonly ResourceDictionary previous;
        private readonly HwndSource source;
        public DataGrid Table {get;}
        public ScrollViewer Inner {get;}
        public ScrollViewer Outer {get;}
        public Fixture(int count)
        {
            previous=Application.Current.Resources;Application.Current.Resources=new ResourceDictionary { MergedDictionaries={new ResourceDictionary { Source=new Uri("/DiskPilot-X;component/Themes/VisionProTheme.xaml",UriKind.Relative) }} };
            Table=new DataGrid { Width=650,Height=220,AutoGenerateColumns=false,CanUserAddRows=false,ItemsSource=Enumerable.Range(1,count).Select(i=>new Row($"项目 {i}")).ToArray() };
            Table.Columns.Add(new DataGridTextColumn { Binding=new System.Windows.Data.Binding("Title"),Width=600 });
            var panel=new StackPanel();panel.Children.Add(new Border { Height=500 });panel.Children.Add(Table);panel.Children.Add(new Border { Height=900 });
            Outer=new ScrollViewer { Content=panel,Width=700,Height=400,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
            source=new HwndSource(new HwndSourceParameters("通用表格滚轮验收") { Width=700,Height=400 }) { RootVisual=Outer };
            Layout();Inner=Visuals<ScrollViewer>(Table).First();Outer.ScrollToVerticalOffset(400);Layout();
        }
        public void Layout(){Outer.Measure(new Size(700,400));Outer.Arrange(new Rect(0,0,700,400));Outer.UpdateLayout();Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);}
        public void Wheel(int delta){var e=new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,delta) { RoutedEvent=Mouse.PreviewMouseWheelEvent };Table.RaiseEvent(e);if(!e.Handled)Inner.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,delta) { RoutedEvent=Mouse.MouseWheelEvent });Layout();}
        public void Dispose(){source.Dispose();Application.Current.Resources=previous;}
    }
    private sealed record Row(string Title);
    private static IEnumerable<T> Visuals<T>(DependencyObject parent) where T:DependencyObject
    {for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);if(child is T match)yield return match;foreach(var nested in Visuals<T>(child))yield return nested;}}
}
