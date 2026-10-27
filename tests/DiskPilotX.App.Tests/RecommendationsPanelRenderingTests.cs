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
