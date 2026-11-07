using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using DiskPilotX.App.ViewModels;
using DiskPilotX.App.Views;
using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Models;

namespace DiskPilotX.App.Tests;

/// <summary>
/// 回归防线：多范围合并发布之后，「推荐清理」面板的条目模板会多出来源范围分组表头
/// 和来源徽章，两者都用 StaticResource 找 NullVis 转换器。转换器忘了在该文件的资源里
/// 声明时，模板解析会抛 XamlParseException 并在 Measure 阶段直接崩进程（线上真出过一次：
/// 选中两个游戏回传候选后，应用当场重启、整段对话丢失）。
/// 这里把面板连同分组后的条目真实渲染一遍，把这类问题挡在 CI 里。
/// </summary>
[Collection("聊天滚动控件")]
public sealed class RecommendationsPanelRenderingTests
{
    [Fact]
    public void 多范围候选渲染时分组表头与来源徽章可见() => StaHost.Run(() =>
    {
        using var host = CreateHost(out var panel, out var vm);
        vm.SetExternalRecommendations(
        [
            new CleanupRecommendation
            {
                Id = "scope-a-1",
                TargetPath = @"G:\SteamLibrary\steamapps\common\WOG",
                Size = 188_700_000,
                Category = RecommendationCategory.Other,
                SafetyRating = SafetyRating.Safe,
                SourceScope = "World of Guns: Gun Disassembly"
            },
            new CleanupRecommendation
            {
                Id = "scope-b-1",
                TargetPath = @"G:\SteamLibrary\steamapps\common\Short Circuit VR",
                Size = 730_800_000,
                Category = RecommendationCategory.Other,
                SafetyRating = SafetyRating.ReviewFirst,
                SourceScope = "Short Circuit VR"
            }
        ]);

        Layout(panel);

        var ordered = vm.FilteredRecommendations;
        Assert.Equal(2, ordered.Count);
        Assert.Equal("World of Guns: Gun Disassembly", ordered[0].ScopeHeader);
        Assert.Equal("Short Circuit VR", ordered[1].ScopeHeader);

        var texts = Visuals<TextBlock>(panel).Where(text => text.IsVisible).Select(text => text.Text).ToList();
        Assert.Contains("World of Guns: Gun Disassembly", texts);
        Assert.Contains("Short Circuit VR", texts);
    });

    [Fact]
    public void 无来源标记时不渲染分组表头() => StaHost.Run(() =>
    {
        using var host = CreateHost(out var panel, out var vm);
        vm.SetExternalRecommendations(
        [
            new CleanupRecommendation
            {
                Id = "solo",
                TargetPath = @"C:\Temp\a.tmp",
                Size = 1024,
                Category = RecommendationCategory.TemporaryFiles,
                SafetyRating = SafetyRating.Safe
            }
        ]);

        Layout(panel);

        Assert.Equal(string.Empty, vm.FilteredRecommendations[0].ScopeHeader);
        var texts = Visuals<TextBlock>(panel).Where(text => text.IsVisible).Select(text => text.Text).ToList();
        Assert.DoesNotContain("来源表头", texts);
    });

    [Fact]
    public void 自定义空态纯文本可换行滚动且不生成状态横条() => StaHost.Run(() =>
    {
        using var host = CreateHost(out var panel, out var vm);
        const string title = "<Button>只是纯文本，不执行</Button>";
        vm.SetEmptyStatePresentation(title, string.Join("\n", Enumerable.Repeat("空间提示：先看证据。", 100)));
        Layout(panel);
        Assert.Null(panel.FindName("AnalysisStatus"));
        var header = Assert.Single(Visuals<TextBlock>(panel).Where(text => text.Text == title));
        Assert.Equal(TextWrapping.Wrap, header.TextWrapping);
        var scroll = Assert.IsType<ScrollViewer>(panel.FindName("EmptyStateScroll"));
        Assert.True(scroll.ScrollableHeight > 0);
        Assert.Empty(vm.Recommendations);
        Assert.Null(vm.AnalysisTask);
    });
    private static IDisposable CreateHost(out RecommendationsPanel panel, out RecommendationsViewModel vm)
    {
        // 面板里有位于<UserControl>根上的 StaticResource（VP.ListViewItem 等），在
        // InitializeComponent 阶段就要能解析到，所以主题必须挂到应用级资源上再构造控件。
        EnsureTheme();

        var viewModel = new RecommendationsViewModel();
        var view = new RecommendationsPanel();
        var source = new HwndSource(new HwndSourceParameters("推荐清理面板渲染回归")
        {
            Width = 900, Height = 700, WindowStyle = unchecked((int)0x80000000)
        })
        { RootVisual = view };
        view.DataContext = viewModel;
        panel = view;
        vm = viewModel;
        return new Disposer(source.Dispose);
    }

    [Fact]
    public void 底栏保留清理而清空只在右键菜单() => StaHost.Run(() =>
    {
        using var host = CreateHost(out var panel, out var vm);
        vm.SetExternalRecommendations([new CleanupRecommendation { Id="1", TargetPath=@"D:\缓存\样本", Size=100, IsAccepted=true }]);
        Layout(panel);
        Assert.DoesNotContain(Visuals<Button>(panel), button => button.Name == "ClearRecommendationsButton");
        Assert.Contains(Visuals<Button>(panel), button => button.Name == "CleanupButton"
            && Equals(button.Content,DiskPilotX.App.Localization.L.Text("CleanUpButton")));
        var list=Visuals<ListView>(panel).Single(item=>item.Name=="RecommendationsList");
        Assert.NotNull(list.ContextMenu);
        Assert.DoesNotContain(list.ContextMenu.Items.OfType<MenuItem>(),item=>item.Name=="CleanupRecommendationsMenu");
        Assert.Contains(list.ContextMenu.Items.OfType<MenuItem>(),item=>item.Name=="ClearRecommendationsMenu");
        var row=Visuals<Border>(panel).Single(border=>border.ContextMenu?.Items.OfType<MenuItem>().Any(item=>item.Name=="RemoveRecommendationMenu")==true);
        Assert.DoesNotContain(row.ContextMenu.Items.OfType<MenuItem>(),item=>item.Name=="CleanupRecommendationsMenu");
        Assert.Contains(row.ContextMenu.Items.OfType<MenuItem>(),item=>item.Name=="CopyRecommendationPathMenu");
        Assert.Contains(row.ContextMenu.Items.OfType<MenuItem>(),item=>item.Name=="ClearRecommendationsMenu");
    });

    [Fact]
    public void 底栏清理仍只请求原批准入口而不直接删除或清空() => StaHost.Run(() =>
    {
        using var host=CreateHost(out var panel,out var vm);
        vm.SetExternalRecommendations([new CleanupRecommendation {Id="1",TargetPath=@"D:\本任务菜单夹具",IsAccepted=true}]);
        Layout(panel);var requested=0;panel.CleanupRequested+=()=>requested++;
        var button=Visuals<Button>(panel).Single(item=>item.Name=="CleanupButton");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1,requested);Assert.Single(vm.Recommendations);
    });

    [Fact]
    public void 失败后空白区域右键清空不删除真实夹具文件() => StaHost.Run(() =>
    {
        var directory=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"DiskPilot-X-menu-test-"+Guid.NewGuid().ToString("N"));
        var file=System.IO.Path.Combine(directory,"必须保留.txt");System.IO.Directory.CreateDirectory(directory);System.IO.File.WriteAllText(file,"只清建议，不删文件");
        try
        {
            using var host=CreateHost(out var panel,out var vm);
            vm.SetExternalRecommendations([new CleanupRecommendation {Id="1",TargetPath=file}]);
            vm.SetAnalysisTask(new(Guid.NewGuid(),Guid.NewGuid(),directory,DiskPilotX.Core.Services.Analysis.AnalysisTaskStatus.Failed,
                DiskPilotX.Core.Services.Analysis.AnalysisOutcome.Unknown,0,[],new Dictionary<string,string>()));
            Layout(panel);var menu=Visuals<ListView>(panel).Single(item=>item.Name=="RecommendationsList").ContextMenu;Assert.NotNull(menu);
            menu.Items.OfType<MenuItem>().Single(item=>item.Name=="ClearRecommendationsMenu").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Empty(vm.Recommendations);Assert.True(System.IO.File.Exists(file));
        }
        finally{System.IO.File.Delete(file);System.IO.Directory.Delete(directory);}
    });
    [Fact]
    public void 底栏和右键菜单都随语言模块切换() => StaHost.Run(() =>
    {
        var previous=DiskPilotX.App.Localization.L.CurrentLanguageName;
        try
        {
            using var host=CreateHost(out var panel,out var vm);
            vm.SetExternalRecommendations([new CleanupRecommendation {Id="1",TargetPath=@"D:\本任务语言夹具",IsAccepted=true}]);
            Layout(panel);
            var button=Visuals<Button>(panel).Single(item=>item.Name=="CleanupButton");
            var list=Visuals<ListView>(panel).Single(item=>item.Name=="RecommendationsList");
            var row=Visuals<Border>(panel).Single(border=>border.ContextMenu?.Items.OfType<MenuItem>().Any(item=>item.Name=="RemoveRecommendationMenu")==true);
            // 菜单首次打开时由现有多语言绑定接管条目模板初值。
            list.ContextMenu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
            row.ContextMenu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
            foreach(var language in new[]{"en","zh-CN","zh-TW","en"})
            {
                DiskPilotX.App.Localization.L.SetLanguage(language);Layout(panel);
                Assert.Equal(DiskPilotX.App.Localization.L.Text("CleanUpButton"),button.Content);
                Assert.Equal(DiskPilotX.App.Localization.L.Text("CleanUpToolTip"),button.ToolTip);
                Assert.Equal(DiskPilotX.App.Localization.L.Text("CleanUpButton"),System.Windows.Automation.AutomationProperties.GetName(button));
                foreach(var menu in new[]{list.ContextMenu,row.ContextMenu})
                {
                    Assert.Equal(DiskPilotX.App.Localization.L.Text("ClearRecommendations"),menu.Items.OfType<MenuItem>().Single(item=>item.Name=="ClearRecommendationsMenu").Header);
                }
                Assert.Equal(DiskPilotX.App.Localization.L.Text("CopyPathMenu"),row.ContextMenu.Items.OfType<MenuItem>().Single(item=>item.Name=="CopyRecommendationPathMenu").Header);
                Assert.Equal(DiskPilotX.App.Localization.L.Text("RemoveRecommendation"),row.ContextMenu.Items.OfType<MenuItem>().Single(item=>item.Name=="RemoveRecommendationMenu").Header);
            }
        }
        finally{DiskPilotX.App.Localization.L.SetLanguage(previous);}
    });

    [Fact]
    public void 三种语言都包含菜单文案而非回退英文()
    {
        var directory=new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while(directory is not null && !System.IO.Directory.Exists(System.IO.Path.Combine(directory.FullName,"src/DiskPilotX.App/Localization")))directory=directory.Parent;
        Assert.NotNull(directory);
        foreach(var file in new[]{"Strings.resx","Strings.zh-CN.resx","Strings.zh-TW.resx"})
        {
            var document=System.Xml.Linq.XDocument.Load(System.IO.Path.Combine(directory.FullName,"src/DiskPilotX.App/Localization",file));
            foreach(var key in new[]{"CleanUpButton","AnalyzeButton","CleanUpToolTip","ClearRecommendations","ClearRecommendationsToolTip","RemoveRecommendation","RemoveRecommendationToolTip"})
                Assert.Contains(document.Root!.Elements("data"),element=>(string?)element.Attribute("name")==key && !string.IsNullOrWhiteSpace(element.Element("value")?.Value));
        }
    }

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

    private sealed class Disposer(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }

    private static void Layout(RecommendationsPanel panel)
    {
        for (var i = 0; i < 4; i++)
        {
            panel.Measure(new Size(900, 700));
            panel.Arrange(new Rect(0, 0, 900, 700));
            panel.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        }
    }

    private static IEnumerable<T> Visuals<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var nested in Visuals<T>(child)) yield return nested;
        }
    }
}
