using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DiskPilotX.App.Controls;
using DiskPilotX.Core.Models;
using Path=System.Windows.Shapes.Path;
namespace DiskPilotX.App.Tests;

[Collection("聊天滚动控件")]
public sealed class SkillChartMotionTests
{
    private static DependencyProperty ProgressProperty
    {
        get {var metadata=typeof(SkillChartControl).GetField("RevealProgressProperty",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);Assert.NotNull(metadata);return Assert.IsType<DependencyProperty>(metadata!.GetValue(null));}
    }
    private static AnimationClock? Clock(SkillChartControl chart)=>(AnimationClock?)typeof(SkillChartControl).GetField("revealClock",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(chart);
    [Fact]
    public void 缓动使用CubicEaseOut且系统禁动不建立时钟() => StaHost.Run(()=>
    {
        var method=typeof(SkillChartControl).GetMethod("CreateMotion",BindingFlags.Static|BindingFlags.NonPublic);Assert.NotNull(method);
        var animation=Assert.IsType<DoubleAnimation>(method!.Invoke(null,[0d,1d,400,true]));
        var easing=Assert.IsType<CubicEase>(animation.EasingFunction);Assert.Equal(EasingMode.EaseOut,easing.EasingMode);Assert.Equal(.875,easing.Ease(.5),6);
        Assert.Equal(TimeSpan.FromMilliseconds(400),animation.Duration.TimeSpan);Assert.Null(method.Invoke(null,[0d,1d,400,false]));
    });
    [Theory]
    [InlineData(SkillUiSectionKind.BarChart)]
    [InlineData(SkillUiSectionKind.PieChart)]
    [InlineData(SkillUiSectionKind.DonutChart)]
    public void 载入仅更新图形并最终回到真实比例(SkillUiSectionKind kind) => StaHost.Run(()=>
    {
        using var fixture=new Fixture(kind);var chart=fixture.Chart;var dp=ProgressProperty;var clock=Clock(chart);
        if(SystemParameters.ClientAreaAnimation)
        {
            Assert.NotNull(clock);clock!.Controller!.SeekAlignedToLastTick(TimeSpan.FromMilliseconds(160),TimeSeekOrigin.BeginTime);
            var progress=Assert.IsType<double>(chart.GetValue(dp));Assert.InRange(progress,.1,.999);
            if(kind==SkillUiSectionKind.BarChart)Assert.Equal(progress,Assert.IsType<ScaleTransform>(Visuals<Border>(chart).First(b=>Equals(b.Tag,"SkillBarFill")).RenderTransform).ScaleX,6);
            else Assert.False(Visuals<Path>(chart).First(p=>Equals(p.Tag,"SkillChartSlice")).Data.IsEmpty());
            clock.Controller.SeekAlignedToLastTick(TimeSpan.FromMilliseconds(450),TimeSeekOrigin.BeginTime);
        }
        Assert.Equal(1d,chart.GetValue(dp));Assert.All(Visuals<TextBlock>(chart),t=>{Assert.Equal(1,t.Opacity);Assert.IsType<MatrixTransform>(t.RenderTransform);});Assert.Equal(1,chart.Opacity);
    });
    [Fact]
    public void 主题语言尺寸不重播且新数据才替换载入时钟() => StaHost.Run(()=>
    {
        using var fixture=new Fixture(SkillUiSectionKind.PieChart);var oldClock=Clock(fixture.Chart);var old=DiskPilotX.App.Localization.L.GetState().LanguagePreference;
        try
        {
            fixture.Layout(280);fixture.Chart.Resources["VP.AccentBrush"]=Brushes.Orange;
            DiskPilotX.App.Localization.L.SetLanguage(old=="en-US"?"zh-CN":"en-US");fixture.Layout(620);
            Assert.Same(oldClock,Clock(fixture.Chart));
            fixture.Chart.Section=new SkillUiSection { Kind=SkillUiSectionKind.PieChart,Items=[new() { Title="新数据",Metric=5 }] };fixture.Layout(620);
            if(SystemParameters.ClientAreaAnimation)Assert.NotSame(oldClock,Clock(fixture.Chart));
        }
        finally {DiskPilotX.App.Localization.L.SetLanguage(old);}
    });
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 扇区与图例悬停联动且移出复原并保持命中几何(bool legend) => StaHost.Run(()=>
    {
        using var fixture=new Fixture(SkillUiSectionKind.DonutChart);fixture.Chart.SetValue(ProgressProperty,1d);fixture.Chart.BeginAnimation(ProgressProperty,null);
        var slice=Visuals<Path>(fixture.Chart).First(p=>Equals(p.Tag,"SkillChartSlice"));
        var hit=Visuals<Path>(fixture.Chart).First(p=>Equals(p.Tag,"SkillChartHit"));
        var target=legend?Visuals<Grid>(fixture.Chart).First(g=>Equals(g.Tag,"SkillChartLegend")):(FrameworkElement)hit;
        var geometry=hit.Data;var bounds=fixture.Chart.DesiredSize;var color=Assert.IsType<SolidColorBrush>(slice.Fill).Color;
        target.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0) { RoutedEvent=Mouse.MouseEnterEvent });
        PumpUntil(()=>Math.Abs(((TranslateTransform)slice.RenderTransform).X)+Math.Abs(((TranslateTransform)slice.RenderTransform).Y)>3.95);
        var translate=Assert.IsType<TranslateTransform>(slice.RenderTransform);Assert.True(Math.Abs(translate.X)+Math.Abs(translate.Y)>3);
        Assert.NotEqual(color,Assert.IsType<SolidColorBrush>(slice.Fill).Color);Assert.Same(geometry,hit.Data);Assert.Equal(bounds,fixture.Chart.DesiredSize);
        target.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0) { RoutedEvent=Mouse.MouseLeaveEvent });
        PumpUntil(()=>Math.Abs(translate.X)+Math.Abs(translate.Y)<.000001 && ((SolidColorBrush)slice.Fill).Color==color);
        Assert.Equal(0,translate.X,6);Assert.Equal(0,translate.Y,6);Assert.Equal(color,Assert.IsType<SolidColorBrush>(slice.Fill).Color);
    });
    [Fact]
    public void 空闲后新载入不沿用旧合成tick而瞬间跳终值() => StaHost.Run(()=>
    {
        using var fixture=new Fixture(SkillUiSectionKind.BarChart);
        if(!SystemParameters.ClientAreaAnimation)return;
        Clock(fixture.Chart)!.Controller!.SeekAlignedToLastTick(TimeSpan.FromMilliseconds(450),TimeSeekOrigin.BeginTime);Thread.Sleep(550);
        fixture.Chart.Section=new SkillUiSection {Kind=SkillUiSectionKind.BarChart,Items=[new() {Title="空闲后数据",Metric=2}]};
        Pump(65);Assert.InRange((double)fixture.Chart.GetValue(ProgressProperty),0,.98);
    });
    [Fact]
    public void 已完成后换数据立即进入起点而非短暂保留旧终态() => StaHost.Run(()=>
    {
        using var fixture=new Fixture(SkillUiSectionKind.BarChart);
        if(!SystemParameters.ClientAreaAnimation)return;
        Clock(fixture.Chart)!.Controller!.SeekAlignedToLastTick(TimeSpan.FromMilliseconds(450),TimeSeekOrigin.BeginTime);
        fixture.Chart.Section=new SkillUiSection {Kind=SkillUiSectionKind.BarChart,Items=[new() {Title="新结果",Metric=3}]};
        var actual=(double)fixture.Chart.GetValue(ProgressProperty);var clock=Clock(fixture.Chart)!;
        Assert.True(actual==0,$"progress={actual}; base={fixture.Chart.GetAnimationBaseValue(ProgressProperty)}; state={clock.CurrentState}; time={clock.CurrentTime}; from={((DoubleAnimation)clock.Timeline).From}");
    });
    [Fact]
    public void 快速更换数据从当前载入进度接续而非闪回零() => StaHost.Run(()=>
    {
        using var fixture=new Fixture(SkillUiSectionKind.PieChart);
        if(!SystemParameters.ClientAreaAnimation)return;
        var clock=Clock(fixture.Chart)!;clock.Controller!.SeekAlignedToLastTick(TimeSpan.FromMilliseconds(140),TimeSeekOrigin.BeginTime);
        var before=(double)fixture.Chart.GetValue(ProgressProperty);
        fixture.Chart.Section=new SkillUiSection { Kind=SkillUiSectionKind.PieChart,Items=[new() {Title="快速替换",Metric=9},new() {Metric=2}] };
        Clock(fixture.Chart)!.Controller!.SeekAlignedToLastTick(TimeSpan.Zero,TimeSeekOrigin.BeginTime);
        Assert.Equal(before,(double)fixture.Chart.GetValue(ProgressProperty),4);
    });    [Fact]
    public void 条形悬停只提亮且快速进入离开最终复原() => StaHost.Run(()=>
    {
        using var fixture=new Fixture(SkillUiSectionKind.BarChart);fixture.Chart.SetValue(ProgressProperty,1d);fixture.Chart.BeginAnimation(ProgressProperty,null);
        var fill=Visuals<Border>(fixture.Chart).First(b=>Equals(b.Tag,"SkillBarFill"));
        var row=Visuals<Grid>(fixture.Chart).First(g=>ReferenceEquals(g.DataContext,fill.DataContext));
        var normal=Assert.IsType<SolidColorBrush>(fill.Background).Color;var width=fill.ActualWidth;
        for(var i=0;i<4;i++)
        {
            row.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0) {RoutedEvent=Mouse.MouseEnterEvent});Pump(25);
            row.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0) {RoutedEvent=Mouse.MouseLeaveEvent});Pump(25);
        }
        PumpUntil(()=>((SolidColorBrush)fill.Background).Color==normal);Assert.Equal(normal,Assert.IsType<SolidColorBrush>(fill.Background).Color);Assert.Equal(width,fill.ActualWidth);Assert.Equal(1,Assert.IsType<ScaleTransform>(fill.RenderTransform).ScaleX);
    });
    [Fact]
    public void 禁动结束路径清除所有时钟并保持终值() => StaHost.Run(()=>
    {
        using var fixture=new Fixture(SkillUiSectionKind.DonutChart);
        typeof(SkillChartControl).GetMethod("FinishMotion",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(fixture.Chart,null);
        Assert.Null(Clock(fixture.Chart));Assert.Equal(1d,fixture.Chart.GetValue(ProgressProperty));
        Assert.All(Visuals<Path>(fixture.Chart).Where(p=>Equals(p.Tag,"SkillChartHit")),p=>Assert.True(p.IsHitTestVisible));
    });    [Fact]
    public void 卸载立即清除载入与悬停动画() => StaHost.Run(()=>
    {
        using var fixture=new Fixture(SkillUiSectionKind.PieChart);_ = ProgressProperty;
        fixture.Chart.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        Assert.Equal(1d,fixture.Chart.GetValue(ProgressProperty));Assert.Null(Clock(fixture.Chart));
        Assert.All(Visuals<Path>(fixture.Chart).Where(p=>Equals(p.Tag,"SkillChartSlice")),p=>Assert.False(Assert.IsType<TranslateTransform>(p.RenderTransform).HasAnimatedProperties));
    });
    private static void PumpUntil(Func<bool> ready)
    {
        // 非激活WPF宿主的合成tick不保证墙钟准点；等待实际终值，时长/easing单独精确检查。
        var limit=DateTime.UtcNow.AddSeconds(3);
        while(!ready() && DateTime.UtcNow<limit)Pump(10);
        Assert.True(ready(),"WPF动效未在时限内抵达目标状态。");
    }
    private static void Pump(int milliseconds){var limit=DateTime.UtcNow.AddMilliseconds(milliseconds);while(DateTime.UtcNow<limit){Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);Thread.Sleep(5);}}
    private sealed class Fixture:IDisposable
    {
        private readonly ResourceDictionary previous;private readonly HwndSource host;public SkillChartControl Chart {get;}
        public Fixture(SkillUiSectionKind kind){previous=Application.Current.Resources;Application.Current.Resources=new ResourceDictionary { MergedDictionaries={new ResourceDictionary { Source=new Uri("/DiskPilot-X;component/Themes/VisionProTheme.xaml",UriKind.Relative) }} };Chart=new SkillChartControl { Section=new SkillUiSection { Kind=kind,Items=[new() {Title="甲",Metric=8,Value="8 GB"},new() {Title="乙",Metric=4,Value="4 GB"}] } };host=new HwndSource(new HwndSourceParameters("图表动效回归") {Width=640,Height=500}) {RootVisual=Chart};Layout(620);}
        public void Layout(double width){Chart.Measure(new Size(width,500));Chart.Arrange(new Rect(0,0,width,Chart.DesiredSize.Height));Chart.UpdateLayout();Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);}
        public void Dispose(){host.Dispose();Application.Current.Resources=previous;}
    }
    private static IEnumerable<T> Visuals<T>(DependencyObject root) where T:DependencyObject {for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T value)yield return value;foreach(var nested in Visuals<T>(child))yield return nested;}}
}
