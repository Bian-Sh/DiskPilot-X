using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using NSubstitute;
using DiskPilotX.App.ViewModels;
using DiskPilotX.App.Views;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Copilot;
using DiskPilotX.Core.Services.Settings;

namespace DiskPilotX.App.Tests;

/// <summary>
/// 回归防线：气泡入口卡的模板里用的是 StaticResource，而模板解析时只能看到
/// 「声明在它之前」的资源，后声明的样式/转换器一律找不到。历史上一连踩了两次
/// （BoolVis 转换器、QuietButton 按钮样式），表现都是渲染瞬间抛 XamlParseException
/// 直接崩进程。这里把入口卡真实实例化一遍，把这类问题挡在 CI 里。
/// </summary>
[Collection("聊天滚动控件")]
public sealed class SkillViewBubbleRenderingTests
{
    /// <summary>
    /// 气泡里只应该有入口卡：标题、摘要、规模统计，
    /// 数据区块（海报网格 / 表格 / 条形图）一律不内联渲染。
    /// </summary>
    [Fact]
    public void 气泡只渲染入口卡不内联数据区块() => StaHost.Run(() =>
    {
        using var host = CreateHost(out var panel, out var vm);
        var message = new ChatMessage
        {
            Sender = Core.Enums.ChatSender.Assistant,
            Text = "已整理出游戏占用情况。"
        };
        message.Views.Add(BuildSpec());
        vm.Messages.Add(message);

        Layout(panel);

        var texts = Visuals<TextBlock>(panel).Where(t => t.IsVisible).Select(t => t.Text).ToList();
        // 入口卡内容
        Assert.Contains("查看游戏入口标题", texts);
        Assert.Contains("10 个项目 · 合计 481.3 GB", texts);
        // 5 个区块；条目按 path/标题去重后 11 条（海报 10 + 条形图 1）
        Assert.Contains(texts, text => text.Contains("5 个区块") && text.Contains("11 项"));
        Assert.Contains(texts, text => text.Contains("可勾选 10 项"));
        // 区块数据不该出现在气泡里
        Assert.DoesNotContain("赛博朋克 2077", texts);
        Assert.DoesNotContain("D:\\Steam", texts);
        Assert.DoesNotContain("118.5 GB", texts);
        Assert.DoesNotContain("说明段落", texts);
    });

    [Fact]
    public void 入口卡带打开按钮() => StaHost.Run(() =>
    {
        using var host = CreateHost(out var panel, out var vm);
        var message = new ChatMessage { Sender = Core.Enums.ChatSender.Assistant };
        message.Views.Add(BuildSpec());
        vm.Messages.Add(message);
        Layout(panel);

        Assert.Contains(Visuals<Button>(panel), button => (button.Content as string) == "打开视图");
    });

    /// <summary>
    /// 同一份视图被重复投递（技能源码返回 view 之后模型又补了一次 render_view）时，
    /// 气泡里只能保留一张入口卡。
    /// </summary>
    [Fact]
    public void 重复投递同一视图时气泡只保留一张入口卡() => StaHost.Run(() =>
    {
        using var host = CreateHost(out var panel, out var vm);
        var message = new ChatMessage { Sender = Core.Enums.ChatSender.Assistant };
        vm.Messages.Add(message);

        var spec = BuildSpec();
        vm.AddView(spec);
        vm.AddView(spec with { Id = spec.Id });
        vm.AddView(spec with { Id = "another-id" });

        Assert.Single(message.Views);
    });

    /// <summary>
    /// 模型补发的 render_view 往往换了标题、换了区块类型，但条目还是同一批。
    /// 这种「讲同一件事」的视图也不能在气泡里叠第二张卡。
    /// </summary>
    [Fact]
    public void 模型补发的等价视图不再叠第二张入口卡() => StaHost.Run(() =>
    {
        using var host = CreateHost(out var panel, out var vm);
        var message = new ChatMessage { Sender = Core.Enums.ChatSender.Assistant };
        vm.Messages.Add(message);

        vm.AddView(BuildSpec());
        vm.AddView(new SkillUiSpec
        {
            Title = "在窗口中查看 Steam 库",
            EntryTitle = "在窗口中查看 Steam 库",
            Sections =
            [
                new SkillUiSection
                {
                    Kind = SkillUiSectionKind.Table,
                    Headers = ["游戏", "占用"],
                    Items = [.. Enumerable.Range(1, 10).Select(index => new SkillUiItem
                    {
                        Cells = [index == 1 ? "赛博朋克 2077" : $"游戏 {index:00}", "1 GB"],
                        Path = $"D:\\Steam\\steamapps\\common\\Game{index:00}"
                    })]
                }
            ]
        });

        Assert.Single(message.Views);
        Layout(panel);
        var texts = Visuals<TextBlock>(panel).Where(t => t.IsVisible).Select(t => t.Text).ToList();
        Assert.Contains("查看游戏入口标题", texts);
        Assert.DoesNotContain("在窗口中查看 Steam 库", texts);
    });

    private static IDisposable CreateHost(out ChatPanel panel, out ChatViewModel vm)
    {
        var settings = Substitute.For<ISettingsService>();
        settings.LoadSettings().Returns(new AppSettings());
        var router = Substitute.For<IAiSkillRouter>();
        router.GetSkillCatalog().Returns(Array.Empty<AiSkillCatalogItem>());
        var assistant = new DiskAssistantService(_ => throw new InvalidOperationException(), [], new AssistantWorkspace(), settings);
        var viewModel = new ChatViewModel(assistant, settings, router);

        // 主题里有一部分静态资源挂在应用级资源上，必须在构造面板之前备好；
        // Application 由 StaHost 统一持有，这里只补资源，不再新建。
        Application.Current.Resources["VP.AccentButton"] = new Style(typeof(Button));

        var view = new ChatPanel();
        view.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/DiskPilot-X;component/Themes/VisionProTheme.xaml", UriKind.Relative)
        });
        var source = new HwndSource(new HwndSourceParameters("技能视图渲染回归")
        {
            Width = 470, Height = 900, WindowStyle = unchecked((int)0x80000000)
        })
        { RootVisual = view };
        view.SetViewModel(viewModel);
        panel = view;
        vm = viewModel;
        return new Disposer(source.Dispose);
    }

    private sealed class Disposer(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }

    /// <summary>构造一份覆盖五种区块的视图契约，贴近技能源码实际产出的结构。</summary>
    private static SkillUiSpec BuildSpec() => new()
    {
        Title = "游戏占用盘点",
        Subtitle = "共 10 个已安装项目",
        EntryTitle = "查看游戏入口标题",
        EntrySummary = "10 个项目 · 合计 481.3 GB",
        Surface = SkillUiSurface.Both,
        Sections =
        [
            new SkillUiSection
            {
                Kind = SkillUiSectionKind.StatRow,
                Title = "概览",
                Items =
                [
                    new SkillUiItem { Title = "已安装", Value = "10", ValueLabel = "个项目" },
                    new SkillUiItem { Title = "占用合计", Value = "481.3 GB", ValueLabel = "全部库" }
                ]
            },
            new SkillUiSection
            {
                Kind = SkillUiSectionKind.PosterGrid,
                Title = "占用排行",
                Columns = 4,
                Items = [.. Enumerable.Range(1, 10).Select(index => new SkillUiItem
                {
                    Title = index == 1 ? "赛博朋克 2077" : $"游戏 {index:00}",
                    Value = index == 1 ? "118.5 GB" : $"{120 - index * 9}.0 GB",
                    Subtitle = "3 天前玩过",
                    AppId = (1000000 + index).ToString(),
                    Selectable = true,
                    Path = $"D:\\Steam\\steamapps\\common\\Game{index:00}",
                    SizeBytes = index == 1 ? 118_500_000_000 : (long)(120 - index * 9) * 1_000_000_000L
                })]
            },
            new SkillUiSection
            {
                Kind = SkillUiSectionKind.Table,
                Title = "全部明细",
                Headers = ["游戏", "所在库", "占用"],
                Items = [new SkillUiItem { Cells = ["赛博朋克 2077", "D:\\Steam", "118.5 GB"], Selectable = true }]
            },
            new SkillUiSection
            {
                Kind = SkillUiSectionKind.BarChart,
                Title = "Top 占用",
                Items = [new SkillUiItem { Title = "赛博朋克 2077", Value = "118.5 GB", Metric = 118.5 }]
            },
            new SkillUiSection
            {
                Kind = SkillUiSectionKind.Text,
                Items = [new SkillUiItem { Title = "说明", Subtitle = "说明段落" }]
            }
        ],
        Actions =
        [
            new SkillUiAction { Kind = "continue", Label = "回传选中项继续分析" },
            new SkillUiAction { Kind = "close", Label = "关闭" }
        ]
    };

    private static void Layout(ChatPanel panel)
    {
        for (var i = 0; i < 4; i++)
        {
            panel.Measure(new Size(470, 900));
            panel.Arrange(new Rect(0, 0, 470, 900));
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
