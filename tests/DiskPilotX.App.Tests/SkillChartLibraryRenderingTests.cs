using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Path=System.Windows.Shapes.Path;
using System.Windows.Threading;
using DiskPilotX.Core.Models;
namespace DiskPilotX.App.Tests;

[Collection("聊天滚动控件")]
public sealed class SkillChartLibraryRenderingTests
{
    [Theory]
    [InlineData("PieChart")]
    [InlineData("DonutChart")]
    public void 饼环共享矢量比例且单项全圆无缺口(string kind) => StaHost.Run(()=>
    {
        var section=new SkillUiSection { Kind=Enum.Parse<SkillUiSectionKind>(kind),Items=[new() { Title="缓存",Metric=8,Value="8 GB" }] };
        using var fixture=new Fixture(section);
        var slice=Assert.Single(Visuals<Path>(fixture.Chart).Where(p=>Equals(p.Tag,"SkillChartSlice")));
        Assert.NotNull(slice.Data);Assert.True(slice.Data.FillContains(new Point(150,88)));
        Assert.Equal(kind=="PieChart",slice.Data.FillContains(new Point(88,88)));
        Assert.Contains("100",slice.ToolTip?.ToString());
    });
    [Fact]
    public void 极大值比例稳定且非法数据不生成扇区() => StaHost.Run(()=>
    {
        using var fixture=new Fixture(new SkillUiSection { Kind=Enum.Parse<SkillUiSectionKind>("DonutChart"),Items=[new() { Title="甲",Metric=double.MaxValue },new() { Title="乙",Metric=double.MaxValue },new() { Metric=double.NaN },new() { Metric=-1 },new() { Metric=double.PositiveInfinity }] });
        var slices=Visuals<Path>(fixture.Chart).Where(p=>Equals(p.Tag,"SkillChartSlice")).ToArray();Assert.Equal(2,slices.Length);
        Assert.All(slices,p=>{Assert.Contains("50",p.ToolTip?.ToString());Assert.False(p.Data.Bounds.IsEmpty);});
        Assert.DoesNotContain(Visuals<TextBlock>(fixture.Chart),t=>t.Text.Contains("Infinity")||t.Text.Contains("NaN")||t.Text.Contains('∞'));
        fixture.Layout(280);Assert.True(fixture.Chart.DesiredSize.Height>200,"窄窗口应把图例放到下方而非挤出窗口。");
    });
    [Theory]
    [InlineData("BarChart")]
    [InlineData("PieChart")]
    [InlineData("DonutChart")]
    public void 全无效值使用明确空态而非伪造图形(string kind) => StaHost.Run(()=>
    {
        using var fixture=new Fixture(new SkillUiSection { Kind=Enum.Parse<SkillUiSectionKind>(kind),Items=[new() { Metric=0 },new() { Metric=-9 },new() { Metric=double.NaN }] });
        Assert.Empty(Visuals<Path>(fixture.Chart));Assert.DoesNotContain(Visuals<Border>(fixture.Chart),b=>Equals(b.Tag,"SkillBarFill"));
        Assert.Contains(Visuals<TextBlock>(fixture.Chart),t=>Equals(t.Tag,"SkillChartEmpty")&&!string.IsNullOrWhiteSpace(t.Text));
    });
    [Fact]
    public void 图表实时继承主题颜色与技能词典而不重建声明() => StaHost.Run(()=>
    {
        var old=DiskPilotX.App.Localization.L.GetState().LanguagePreference;
        try
        {
            DiskPilotX.App.Localization.L.SetLanguage("zh-CN");
            var section=new SkillUiSection { Kind=SkillUiSectionKind.PieChart,Items=[new() { Title="{loc category}",Metric=2 },new() { Title="Temp",Metric=1 }] };
            using var fixture=new Fixture(section);
            DiskPilotX.App.Localization.SkillTextExtension.SetCatalog(fixture.Chart,new Dictionary<string,Dictionary<string,string>> { ["zh-CN"]=new() { ["category"]="缓存" },["en-US"]=new() { ["category"]="Cache" } });
            fixture.Layout(620);Assert.Contains(Visuals<TextBlock>(fixture.Chart),t=>t.Text=="缓存");
            fixture.Chart.Resources["VP.AccentBrush"]=new SolidColorBrush(Colors.OrangeRed);fixture.Layout(620);
            var first=Visuals<Path>(fixture.Chart).First(p=>Equals(p.Tag,"SkillChartSlice"));var color=Assert.IsType<SolidColorBrush>(first.Fill).Color;
            var accentProperty=(DependencyProperty)fixture.Chart.GetType().GetField("AccentProperty",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.GetValue(null)!;
            Assert.True(color==Colors.OrangeRed,$"fill={color}; resolved={fixture.Chart.TryFindResource("VP.AccentBrush")}; dp={fixture.Chart.GetValue(accentProperty)}; loaded={fixture.Chart.IsLoaded}");
            DiskPilotX.App.Localization.L.SetLanguage("en-US");fixture.Layout(620);Assert.Contains(Visuals<TextBlock>(fixture.Chart),t=>t.Text=="Cache");
            ((SolidColorBrush)fixture.Chart.Resources["VP.AccentBrush"]).Color=Colors.LimeGreen;fixture.Layout(620);
            Assert.Equal(Colors.LimeGreen,Assert.IsType<SolidColorBrush>(Visuals<Path>(fixture.Chart).First(p=>Equals(p.Tag,"SkillChartSlice")).Fill).Color);
            Assert.Same(section,fixture.Chart.GetType().GetProperty("Section")!.GetValue(fixture.Chart));Assert.Equal(1,fixture.Chart.Opacity);
        }
        finally {DiskPilotX.App.Localization.L.SetLanguage(old);}
    });

    private sealed class Fixture:IDisposable
    {
        private readonly ResourceDictionary previous;private readonly HwndSource host;
        public FrameworkElement Chart {get;}
        public Fixture(SkillUiSection section)
        {
            previous=Application.Current.Resources;Application.Current.Resources=new ResourceDictionary { MergedDictionaries={new ResourceDictionary { Source=new Uri("/DiskPilot-X;component/Themes/VisionProTheme.xaml",UriKind.Relative) }} };
            var type=typeof(DiskPilotX.App.Controls.SkillTableControl).Assembly.GetType("DiskPilotX.App.Controls.SkillChartControl");Assert.NotNull(type);
            Chart=Assert.IsAssignableFrom<FrameworkElement>(Activator.CreateInstance(type!));type!.GetProperty("Section")!.SetValue(Chart,section);
            host=new HwndSource(new HwndSourceParameters("共享图表渲染") { Width=640,Height=700 }) { RootVisual=Chart };Layout(620);
            // 本组检查最终几何；载入中间态由SkillChartMotionTests单独验证。
            var progress=(DependencyProperty)type!.GetField("RevealProgressProperty",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.GetValue(null)!;
            Chart.SetValue(progress,1d);Chart.BeginAnimation(progress,null);
        }
        public void Layout(double width){Chart.Measure(new Size(width,700));Chart.Arrange(new Rect(0,0,width,Chart.DesiredSize.Height));Chart.UpdateLayout();Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);Chart.Measure(new Size(width,700));Chart.Arrange(new Rect(0,0,width,Chart.DesiredSize.Height));Chart.UpdateLayout();}
        public void Dispose(){host.Dispose();Application.Current.Resources=previous;}
    }
    private static IEnumerable<T> Visuals<T>(DependencyObject parent) where T:DependencyObject
    {for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);if(child is T match)yield return match;foreach(var nested in Visuals<T>(child))yield return nested;}}
}
