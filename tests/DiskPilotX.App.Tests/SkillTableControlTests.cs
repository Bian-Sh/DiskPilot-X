using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using DiskPilotX.App.Views;
using DiskPilotX.Core.Models;
namespace DiskPilotX.App.Tests;

[Collection("聊天滚动控件")]
public sealed class SkillTableControlTests
{
    [Fact]
    public void 表头三态与反选仅操作本表可选行() => StaHost.Run(() =>
    {
        var a=new SkillUiItem { Title="项目甲", Cells=["甲","E:\\","1 GB"], Selectable=true, SizeBytes=1024 };
        var b=new SkillUiItem { Title="项目乙", Cells=["乙","E:\\","2 GB"], Selectable=true };
        var disabled=new SkillUiItem { Title="不可选", Cells=["丙","E:\\","3 GB"], Selected=true };
        var section=new SkillUiSection { Kind=SkillUiSectionKind.Table, Headers=["项目","磁盘","容量"], Items=[a,b,disabled] };
        var type=typeof(SkillWindow).Assembly.GetType("DiskPilotX.App.Controls.SkillTableControl");
        Assert.NotNull(type);
        var control=Assert.IsAssignableFrom<FrameworkElement>(Activator.CreateInstance(type!));
        type!.GetProperty("Section")!.SetValue(control, section);
        control.Measure(new Size(900,600)); control.Arrange(new Rect(0,0,900,600)); control.UpdateLayout();
        var header=Assert.IsType<CheckBox>(control.FindName("SelectAllCheckBox"));
        Assert.Equal(false, header.IsChecked);
        a.Selected=true;
        Assert.Null(header.IsChecked);
        // UIA Toggle 与鼠标都更新 IsChecked，不能仅监听 Click。
        header.IsChecked=false;
        Assert.True(a.Selected && b.Selected && disabled.Selected);
        Assert.Equal(true, header.IsChecked);
        Assert.IsType<Button>(control.FindName("InvertButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.False(a.Selected || b.Selected);
        Assert.True(disabled.Selected);
        Assert.Equal(false, header.IsChecked);
    });

    [Fact]
    public void 无可选行时隐藏选择工具且短列不等宽平分() => StaHost.Run(() =>
    {
        var type=typeof(SkillWindow).Assembly.GetType("DiskPilotX.App.Controls.SkillTableControl");
        Assert.NotNull(type);
        var control=Assert.IsAssignableFrom<FrameworkElement>(Activator.CreateInstance(type!));
        type!.GetProperty("Section")!.SetValue(control,new SkillUiSection { Kind=SkillUiSectionKind.Table, Headers=["项目","磁盘","详情"], Items=[new SkillUiItem { Cells=["项目甲","E:\\","Library / Temp / obj / Logs"] }] });
        control.Measure(new Size(900,600)); control.Arrange(new Rect(0,0,900,600)); control.UpdateLayout();
        var grid=Assert.IsType<DataGrid>(control.FindName("Table"));
        Assert.Equal(3,grid.Columns.Count);
        Assert.True(grid.Columns[1].Width.IsAuto);
        Assert.True(grid.Columns[2].Width.IsStar);
        Assert.True(grid.Columns[1].ActualWidth < grid.Columns[2].ActualWidth);
        Assert.Equal(Visibility.Collapsed, Assert.IsType<Button>(control.FindName("InvertButton")).Visibility);
    });

    [Fact]
    public void 技能标题不再额外绘制悬空横线() => StaHost.Run(() =>
    {
        var window=new SkillWindow(new SkillUiSpec { Title="盘点" });
        try { Assert.DoesNotContain(Descendants(window).OfType<Border>(), b => b.Height==4 && b.Width==42); }
        finally { window.Close(); }
    });

    [Fact]
    public void 首次加载和语言切换均翻译列头且保留选择() => StaHost.Run(() =>
    {
        var old=DiskPilotX.App.Localization.L.CurrentLanguageName;
        DiskPilotX.App.Localization.L.SetLanguage("zh-CN");
        var item=new SkillUiItem { Title="项目甲", Cells=["{loc column}"], Selectable=true, Selected=true };
        var section=new SkillUiSection { Kind=SkillUiSectionKind.Table, TableColumns=[new SkillUiTableColumn { Header="{loc column}", Width="*" }], Items=[item] };
        var spec=new SkillUiSpec { Sections=[section], Localizations=new() { ["zh-CN"]=new() { ["column"]="项目" }, ["en-US"]=new() { ["column"]="Project" } } };
        using var vm=new DiskPilotX.App.ViewModels.SkillWindowViewModel(spec);
        var control=new DiskPilotX.App.Controls.SkillTableControl { Section=section };
        var window=new Window { Content=control, DataContext=vm };
        try
        {
            control.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            var grid=(DataGrid)control.FindName("Table");
            Assert.Equal("项目",Assert.IsType<TextBlock>(grid.Columns[1].Header).Text);
            DiskPilotX.App.Localization.L.SetLanguage("en-US");
            Assert.Equal("Project",Assert.IsType<TextBlock>(grid.Columns[1].Header).Text);
            Assert.Single(vm.SelectedItems);
            Assert.Contains("项目甲",vm.BuildSelectionPayload());
        }
        finally { control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); window.Close(); DiskPilotX.App.Localization.L.SetLanguage(old); }
    });

    [Fact]
    public void 多张表的反选互不影响且空表安全() => StaHost.Run(() =>
    {
        var first=new SkillUiItem { Title="甲", Selectable=true, Selected=true, Cells=["甲"] };
        var second=new SkillUiItem { Title="乙", Selectable=true, Selected=true, Cells=["乙"] };
        var section=new SkillUiSection { Kind=SkillUiSectionKind.Table, Headers=["项目"], Items=[first] };
        var other=new SkillUiSection { Kind=SkillUiSectionKind.Table, Headers=["项目"], Items=[second] };
        using var vm=new DiskPilotX.App.ViewModels.SkillWindowViewModel(new SkillUiSpec { Sections=[section,other] });
        var control=new DiskPilotX.App.Controls.SkillTableControl { Section=section };
        Assert.IsType<Button>(control.FindName("InvertButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.False(first.Selected); Assert.True(second.Selected); Assert.Single(vm.SelectedItems);
        control.Section=new SkillUiSection { Kind=SkillUiSectionKind.Table };
        Assert.False(Assert.IsType<CheckBox>(control.FindName("SelectAllCheckBox")).IsEnabled);
        Assert.Equal(Visibility.Collapsed,Assert.IsType<Button>(control.FindName("InvertButton")).Visibility);
        Assert.Equal(Visibility.Visible,Assert.IsType<TextBlock>(control.FindName("EmptyHint")).Visibility);
    });

    [Fact]
    public void 表头采用轻量主题叠色且短首列自动收紧() => StaHost.Run(() =>
    {
        var control=new DiskPilotX.App.Controls.SkillTableControl { Section=new SkillUiSection { Kind=SkillUiSectionKind.Table,
            Headers=["磁盘","项目数","详情"],Items=[new SkillUiItem { Cells=["E:/","12","详细项目占用"] }] } };
        var header=Assert.IsType<SolidColorBrush>(control.Resources["TableHeaderBrush"]);
        Assert.InRange(header.Opacity,0.02,0.08);
        control.Measure(new Size(900,600)); control.Arrange(new Rect(0,0,900,600)); control.UpdateLayout();
        var grid=(DataGrid)control.FindName("Table");
        Assert.Same(control.Resources["TableDividerBrush"],grid.HorizontalGridLinesBrush);
        Assert.InRange(grid.HorizontalGridLinesBrush.Opacity,0.02,0.1);
        Assert.True(Assert.IsType<DataGrid>(control.FindName("Table")).Columns[0].Width.IsAuto);
    });

    [Fact]
    public void 显式列宽在缩放后保持配置且勾选行带淡强调底色() => StaHost.Run(() =>
    {
        var item=new SkillUiItem { Title="项目",Cells=["项目","E:/","1 GB","详细占用"],Selectable=true,Selected=true };
        var control=new DiskPilotX.App.Controls.SkillTableControl { Section=new SkillUiSection { Kind=SkillUiSectionKind.Table,
            TableColumns=[new() { Header="项目",Width="*",MinWidth=140 },new() { Header="磁盘",Width="auto",MaxWidth=90 },new() { Header="容量",Width="100",Align="right" },new() { Header="详情",Width="2*",Wrap=true }],Items=[item] } };
        control.Resources["VP.Accent"]=Colors.Blue;
        foreach(var width in new[] { 900,1280,740,1600,1280 })
        {
            control.Measure(new Size(width,600));control.Arrange(new Rect(0,0,width,600));control.UpdateLayout();
            var grid=(DataGrid)control.FindName("Table");
            Assert.Equal(100,grid.Columns[3].ActualWidth);
            Assert.True(grid.Columns[1].Width.IsStar);
            var row=Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromIndex(0));
            Assert.Same(control.Resources["TableSelectionBrush"],row.Background);
        }
    });
    [Fact]
    public void 旧表格在真实窗口最大化还原后仍分配剩余列宽() => StaHost.Run(() =>
    {
        var section=new SkillUiSection { Kind=SkillUiSectionKind.Table,Headers=["磁盘","项目数","缓存合计","占用最大项目"],
            Items=[new SkillUiItem { Cells=["E:/","12","27.3 GB","VideoPlayer-UnityXR（8.7 GB）"] }] };
        var window=new SkillWindow(new SkillUiSpec { Title="旧表格缩放验收",Sections=[section] }) { Width=740,ShowActivated=false };
        try
        {
            window.Show();Settle();
            var table=Visuals(window).OfType<DataGrid>().Single();
            var before=table.Columns[^1].ActualWidth;
            Assert.True(before>400,$"初始详情列宽度不足：{before}");
            window.WindowState=WindowState.Maximized;Settle();
            window.WindowState=WindowState.Normal;Settle();
            Assert.True(table.Columns[^1].ActualWidth>=before-4,$"最大化前 {before}，还原后 {table.Columns[^1].ActualWidth}");
        }
        finally { window.Close(); }
        void Settle()
        {
            window.UpdateLayout();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
        }
    });

    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
        {var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in Visuals(child))yield return nested;}
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        { yield return child; foreach(var nested in Descendants(child)) yield return nested; }
    }
}
