using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using DiskPilotX.App.Controls;
using DiskPilotX.App.Diagnostics;
using DiskPilotX.Core.Models;

namespace DiskPilotX.App;
public partial class MainWindow
{
    private SkillUiSpec? chartLibrarySpec;
    /// <summary>只读图表夹具：通过真实JSON解析进入共享聊天/窗口渲染，不扫描用户数据。</summary>
    internal object AcceptanceChartLibrary(string? mode)
    {
        if(!AcceptanceAutomationServer.IsEnabled() || _chatViewModel is null)throw new InvalidOperationException("验收入口未启用。");
        if(mode is "motion-data" or "motion-hover-in" or "motion-hover-out" or "motion-bar-in" or "motion-bar-out")
        {
            var window=Application.Current.Windows.OfType<Views.SkillWindow>().LastOrDefault(w=>w.IsVisible&&w.Title=="宿主图表组件库")??throw new InvalidOperationException("图表夹具未打开。");
            var charts=Walk(window).OfType<SkillChartControl>().Where(c=>c.IsVisible).ToArray();
            if(mode=="motion-data")
                foreach(var chart in charts)
                    chart.Section=chart.Section! with { Items=chart.Section!.Items.Select((item,index)=>new SkillUiItem {Title=item.Title,Metric=index==0?6:index==1?3:1,Value=index==0?"6 GB":index==1?"3 GB":"1 GB"}).ToArray() };
            else
            {
                var bar=mode.StartsWith("motion-bar",StringComparison.Ordinal);
                var chart=charts.First(c=>c.Section!.Kind==(bar?SkillUiSectionKind.BarChart:SkillUiSectionKind.DonutChart));
                var target=bar?(FrameworkElement)Walk(chart).OfType<Grid>().First(g=>g.DataContext is SkillUiItem)
                    :Walk(chart).OfType<Grid>().First(g=>Equals(g.Tag,"SkillChartLegend"));
                target.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount) {RoutedEvent=mode.EndsWith("-in",StringComparison.Ordinal)?Mouse.MouseEnterEvent:Mouse.MouseLeaveEvent});
            }
        }
        else if(mode=="view")
        {
            if(chartLibrarySpec is null)throw new InvalidOperationException("请先建立图表夹具。");
            _chatViewModel.OpenSkillWindow(chartLibrarySpec);
        }
        else if(mode=="chat")
        {
            using var json=System.Text.Json.JsonDocument.Parse("""
            {"title":"宿主图表组件库","subtitle":"只读UI示例，不是当前磁盘扫描结果","entry_title":"查看图表组件示例","localizations":{"zh-CN":{"pie":"空间构成 · 饼图","donut":"缓存构成 · 环形图","a":"Library","b":"Temp","c":"Logs"},"en-US":{"pie":"Space composition · Pie","donut":"Cache composition · Donut","a":"Library","b":"Temp","c":"Logs"}},"sections":[
             {"kind":"pie_chart","title":"{loc pie}","subtitle":"8 / 3 / 1 GB","items":[{"title":"{loc a}","metric":8,"value":"8 GB"},{"title":"{loc b}","metric":3,"value":"3 GB"},{"title":"{loc c}","metric":1,"value":"1 GB"}]},
             {"kind":"donut_chart","title":"{loc donut}","subtitle":"合计单位：GB","items":[{"title":"{loc a}","metric":8,"value":"8 GB"},{"title":"{loc b}","metric":3,"value":"3 GB"},{"title":"{loc c}","metric":1,"value":"1 GB"}]},
             {"kind":"bar_chart","title":"占用比较 · 条形图","items":[{"title":"{loc a}","metric":8,"value":"8 GB"},{"title":"{loc b}","metric":3,"value":"3 GB"},{"title":"{loc c}","metric":1,"value":"1 GB"}]}],"actions":[{"kind":"close","label":"关闭"}]}
            """);
            chartLibrarySpec=SkillUiSpecContract.Parse(json.RootElement);_chatViewModel.AddView(chartLibrarySpec);
            ChatPanelViewport.Visibility=Visibility.Visible;ClearChatPanelSlideTransform();ChatPanelContainer.Width=double.NaN;ChatPanelContainer.HorizontalAlignment=HorizontalAlignment.Stretch;ChatPanelColumn.Width=new GridLength(620);
        }
        return AcceptanceChartLibraryState();
    }
    internal object AcceptanceChartLibraryState()
    {
        if(!AcceptanceAutomationServer.IsEnabled())throw new InvalidOperationException("验收入口未启用。");
        UpdateLayout();
        object Charts(DependencyObject root)=>Walk(root).OfType<SkillChartControl>().Where(c=>c.IsVisible).Select(c=>new
        {
            kind=c.Section?.Kind.ToString(),width=c.ActualWidth,height=c.ActualHeight,motion=c.MotionState,
            texts=Walk(c).OfType<TextBlock>().Select(t=>t.Text).ToArray(),
            slices=Walk(c).OfType<System.Windows.Shapes.Path>().Where(p=>Equals(p.Tag,"SkillChartSlice")).Select(p=>new { hint=p.ToolTip?.ToString(),color=p.Fill?.ToString(),bounds=p.Data.Bounds.ToString(),offset=p.RenderTransform is TranslateTransform shift?new {x=shift.X,y=shift.Y}:null }).ToArray()
        ,bars=Walk(c).OfType<Border>().Where(b=>Equals(b.Tag,"SkillBarFill")).Select(b=>new {width=b.ActualWidth,color=b.Background?.ToString(),scale=b.RenderTransform is ScaleTransform scale?scale.ScaleX:1}).ToArray()
        }).ToArray();
        return new { mainHwnd=new WindowInteropHelper(this).Handle.ToInt64(),mainActive=IsActive,foregroundHwnd=GlassForegroundWindow().ToInt64(),chat=Charts(ChatPanelContainer),windows=Application.Current.Windows.OfType<Window>().Where(w=>w.IsVisible && w is Views.SkillWindow).Select(w=>new { hwnd=new WindowInteropHelper(w).Handle.ToInt64(),w.Title,isActive=w.IsActive,charts=Charts(w) }).ToArray() };
    }
    /// <summary>只向已打开表格发送原生滚轮路由，返回前后偏移，不直接更改滚动位置。</summary>
    internal object AcceptanceTableWheel(string? direction)
    {
        if(!AcceptanceAutomationServer.IsEnabled())throw new InvalidOperationException("验收入口未启用。");
        if(localizationFixture is not Views.SkillWindow window)throw new InvalidOperationException("表格夹具未打开。");
        window.UpdateLayout();var table=Walk(window).OfType<SkillTableControl>().Last(control=>control.Section?.IsTable==true);var grid=(DataGrid)table.FindName("Table");var inner=NestedScrollWheel.FindScroll(grid)!;
        var outer=Walk(window).OfType<ScrollViewer>().First();var before=inner.VerticalOffset;var outside=outer.VerticalOffset;var delta=direction=="up"?120:-120;
        var args=new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,delta) { RoutedEvent=Mouse.PreviewMouseWheelEvent };grid.RaiseEvent(args);
        if(!args.Handled)inner.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,delta) { RoutedEvent=Mouse.MouseWheelEvent });
        window.UpdateLayout();return new { innerBefore=before,innerAfter=inner.VerticalOffset,innerMax=inner.ScrollableHeight,outerBefore=outside,outerAfter=outer.VerticalOffset,outerMax=outer.ScrollableHeight,handled=args.Handled };
    }
}
