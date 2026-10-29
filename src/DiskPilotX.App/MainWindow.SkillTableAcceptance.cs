using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using DiskPilotX.App.Controls;
using DiskPilotX.App.Diagnostics;
using DiskPilotX.App.ViewModels;
using DiskPilotX.App.Views;
using DiskPilotX.Core.Models;
namespace DiskPilotX.App;

public partial class MainWindow
{
    /// <summary>仅验收模式可用的固定展示数据，不扫描或改写用户文件。</summary>
    internal object AcceptanceSkillTable(string? mode)
    {
        if(!AcceptanceAutomationServer.IsEnabled()) throw new InvalidOperationException("验收入口未启用。");
        if(mode is "narrow" or "wide" or "maximize" or "restore" && localizationFixture is SkillWindow current)
        {
            current.WindowState=mode=="maximize" ? WindowState.Maximized : WindowState.Normal;
            if(mode=="narrow") current.Width=740;
            if(mode=="wide") current.Width=1280;
            current.UpdateLayout();
            return AcceptanceSkillTableState();
        }
        var names=new[] { "VideoPlayer-UnityXR", "VideoPlayer-UnityXR_codeX_build", "UVRPN-Input-System", "XRWorldSpaceUIToolkit", "UnityPomodoro", "UnityClassroom", "crest", "Unity.Mvvm.Calc", "UnityVRPN", "MGS.Machinery", "Unity-Rendering-Sample", "Unity-Interaction-Demo" };
        var sizes=new[] { 8.7,7.1,2.1,2.0,1.9,1.7,1.1,1.1,0.95,0.3,0.18,0.12 };
        var items=names.Select((name,i)=>new SkillUiItem { Title=name, Path="E:/Unity/"+name, SizeBytes=(long)(sizes[i]*1024*1024*1024),
            Value=sizes[i].ToString("0.0")+" GB", Selectable=true,
            Cells=[name,"E:/",i%2==0?"2022.3.62f1":"2023.2.19f1",sizes[i].ToString("0.0")+" GB",$"Library {sizes[i]:0.0} GB · Temp 1.0 KB · obj 9.7 MB · Logs 276.1 MB"] }).ToArray();
        if(mode=="empty") items=[];
        var detail=new SkillUiSection { Kind=SkillUiSectionKind.Table, Title="项目明细", Subtitle="只读 UI 验收数据，不代表当前磁盘扫描结果；可勾选后回传。",
            TableColumns=[new() { Header="项目", Width="1.3*", MinWidth=170 },new() { Header="所在盘", Width="auto", MaxWidth=88 },
                new() { Header="Unity 版本", Width="auto", MaxWidth=160 },new() { Header="缓存合计", Width="100", Align="right" },
                new() { Header="缓存构成", Width="2*", MinWidth=200, Wrap=true }], Items=items };
        var spec=new SkillUiSpec { Title="Unity 项目缓存盘点", Subtitle="内置技能表格 · 自适应列宽 · 安全勾选回传",
            Sections=[new SkillUiSection { Kind=SkillUiSectionKind.StatRow, Items=[new() { Title="Unity 项目", Value=items.Length.ToString(), ValueLabel="本次展示的项目" },new() { Title="缓存合计",Value="27.3 GB",ValueLabel="仅为界面验收样例" },new() { Title="占用最大",Value="8.7 GB",ValueLabel="VideoPlayer-UnityXR" },new() { Title="覆盖磁盘",Value="1",ValueLabel="项目所在盘" }] },
                new SkillUiSection { Kind=SkillUiSectionKind.Table, Title="按磁盘分布", Headers=["磁盘","项目数","缓存合计","占用最大项目"],
                    Items=[new SkillUiItem { Cells=["E:/",items.Length.ToString(),"27.3 GB","VideoPlayer-UnityXR（8.7 GB）"] }] },detail],
            Actions=[new() { Kind="continue",Label="把勾选的项目交回分析" },new() { Kind="close",Label="关闭" }] };
        localizationFixture?.Close();
        localizationFixture=new SkillWindow(spec) { Owner=this };
        localizationFixture.Show();
        return AcceptanceSkillTableState();
    }

    internal object AcceptanceSkillTableState()
    {
        if(!AcceptanceAutomationServer.IsEnabled()) throw new InvalidOperationException("验收入口未启用。");
        if(localizationFixture is not SkillWindow window) return new { open=false };
        window.UpdateLayout();
        var model=(SkillWindowViewModel)window.DataContext;
        var tables=Walk(window).OfType<SkillTableControl>().Where(t=>t.Section?.IsTable==true).Select(t=>
        {
            var grid=(DataGrid)t.FindName("Table");
            return new { count=t.Section!.Items.Count, selected=t.Section.Items.Count(i=>i.Selectable&&i.Selected),
                widths=grid.Columns.Select(c=>c.ActualWidth).ToArray(),
                configured=t.Section.TableColumns.Select(c=>new { c.Width,c.MinWidth,c.MaxWidth }).ToArray(),
                actual=grid.Columns.Select(c=>new { width=c.Width.ToString(),c.MinWidth,c.MaxWidth }).ToArray(),
                header_state=((CheckBox)t.FindName("SelectAllCheckBox")).IsChecked };
        }).ToArray();
        return new { open=true, hwnd=new WindowInteropHelper(window).Handle.ToInt64(),window.Title,window.Width,window.Height,
            state=window.WindowState.ToString(),corner=((Border)window.Content).CornerRadius.TopLeft,
            selected=model.SelectedItems.Count,summary=model.SelectionSummary,payload=model.BuildSelectionPayload(),tables };
    }
    private static IEnumerable<DependencyObject> Walk(DependencyObject parent)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        { var child=VisualTreeHelper.GetChild(parent,i);yield return child;foreach(var nested in Walk(child))yield return nested; }
    }
}
