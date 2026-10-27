using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DiskPilotX.App.Converters;
using DiskPilotX.App.Views;
using DiskPilotX.Core.Models;

namespace DiskPilotX.App.Tests;

/// <summary>
/// 条目行内动作（Steam 游戏卡的「卸载」）的回归防线：
/// ① 共享模板能被真实渲染出来（资源漏声明会在布局阶段直接炸进程）；
/// ② 点击一定冒泡到宿主回调，带回被点击的那一条；
/// ③ 卸载必须是危险色按钮，不能和普通动作混淆；
/// ④ 底栏的视图动作按钮不会误触发条目动作。
/// </summary>
[Collection("聊天滚动控件")]
public sealed class SkillItemActionTests
{
    [Fact]
    public void 卡片上的卸载按钮能把条目冒泡到宿主() => StaHost.Run(() =>
    {
        EnsureResources();
        var spec = BuildSpec();
        SkillUiItem? requestedItem = null;
        SkillUiItemAction? requestedAction = null;

        var window = new SkillWindow(spec);
        try
        {
            window.ItemActionRequested += (source, item, action) =>
            {
                requestedItem = item;
                requestedAction = action;
            };

            var probe = new Button { DataContext = spec.Sections[0].Items[0], Content = "卸载" };
            Attach(window, probe);
            probe.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            Assert.NotNull(requestedItem);
            Assert.Equal("Short Circuit VR", requestedItem!.Title);
            Assert.NotNull(requestedAction);
            Assert.True(requestedAction!.IsUninstall);
            Assert.Equal("970800", requestedAction.Target);
            Assert.True(requestedAction.ConfirmRequired);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 底栏视图动作不会误触发条目动作() => StaHost.Run(() =>
    {
        EnsureResources();
        var invocations = 0;
        var window = new SkillWindow(BuildSpec());
        try
        {
            window.ItemActionRequested += (source, item, action) => invocations++;

            // 底栏按钮的数据上下文是 SkillUiAction，不能走进条目动作分支
            var footer = new Button { DataContext = new SkillUiAction { Kind = "close", Label = "关闭" } };
            Attach(window, footer);
            footer.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            Assert.Equal(0, invocations);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 海报卡能真实渲染出卸载按钮() => StaHost.Run(() =>
    {
        EnsureResources();
        var section = BuildSpec().Sections[0];

        var buttons = Render<Button>(section, button => Label(button) == "卸载");

        Assert.Single(buttons);
        Assert.Contains("Short Circuit VR", Render<TextBlock>(section, _ => true).Select(text => text.Text));
    });

    [Fact]
    public void 表格行尾也渲染出卸载按钮() => StaHost.Run(() =>
    {
        EnsureResources();
        var section = BuildTableSpec().Sections[0];

        // 两行明细各一个卸载按钮
        Assert.Equal(2, Render<Button>(section, button => Label(button) == "卸载").Count);
    });

    [Fact]
    public void 没有动作的条目不渲染按钮() => StaHost.Run(() =>
    {
        EnsureResources();
        var section = new SkillUiSection
        {
            Kind = SkillUiSectionKind.PosterGrid,
            Items = [new SkillUiItem { Title = "无动作", AppId = "1" }]
        };

        Assert.Empty(Render<Button>(section, button => Label(button) == "卸载"));
    });

    [Fact]
    public void 卸载按钮取危险色样式而非强调色()
    {
        Assert.Equal("VP.DangerButton", ItemActionStyleConverter.DangerKey);
        StaHost.Run(() =>
        {
            var converter = new ItemActionStyleConverter();
            var resources = Application.Current.Resources;
            resources[ItemActionStyleConverter.NormalKey] = new Style(typeof(Button));
            var danger = new Style(typeof(Button));
            resources[ItemActionStyleConverter.DangerKey] = danger;

            Assert.Same(danger, converter.Convert(true, typeof(Style), null, CultureInfo.InvariantCulture));
            Assert.Same(resources[ItemActionStyleConverter.NormalKey], converter.Convert(false, typeof(Style), null, CultureInfo.InvariantCulture));
        });
    }

    /// <summary>把探针按钮挂进窗口内容，让它处在路由事件的冒泡链上。</summary>
    private static void Attach(SkillWindow window, Button probe)
    {
        var frame = (Border)window.Content;
        var grid = (Grid)frame.Child!;
        grid.Children.Add(probe);
    }

    /// <summary>把区块真实渲染一遍；模板里的资源缺失会在布局阶段直接抛。</summary>
    private static IReadOnlyList<T> Render<T>(SkillUiSection section, Func<T, bool> filter) where T : DependencyObject
    {
        var view = new ContentControl { Content = section };
        var source = new HwndSource(new HwndSourceParameters("技能条目动作渲染回归")
        {
            Width = 1000, Height = 780, WindowStyle = unchecked((int)0x80000000)
        })
        { RootVisual = view };

        try
        {
            Layout(view);
            // 可见性必须在宿主还在时判定：IsVisible 依赖真实句柄。
            return Items<T>(view).Where(filter).Where(item => item is not UIElement element || element.IsVisible).ToList();
        }
        finally
        {
            source.Dispose();
        }
    }

    private static string Label(Button button) => button.Content as string ?? string.Empty;

    private static void Layout(ContentControl host)
    {
        for (var i = 0; i < 4; i++)
        {
            host.Measure(new Size(1000, 780));
            host.Arrange(new Rect(0, 0, 1000, 780));
            host.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        }
    }

    private static IEnumerable<T> Items<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var nested in Items<T>(child)) yield return nested;
        }
    }

    /// <summary>隐式模板在应用级资源里解析，因此主题与技能模板都要挂到应用资源。</summary>
    private static void EnsureResources()
    {
        var resources = Application.Current.Resources;
        Merge(resources, "/DiskPilot-X;component/Themes/VisionProTheme.xaml");
        Merge(resources, "/DiskPilot-X;component/Views/SkillUiTemplates.xaml");
    }

    private static void Merge(ResourceDictionary resources, string uri)
    {
        var already = resources.MergedDictionaries
            .Any(dictionary => dictionary.Source is not null &&
                               string.Equals(dictionary.Source.OriginalString, uri, StringComparison.OrdinalIgnoreCase));
        if (already) return;
        resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(uri, UriKind.Relative) });
    }

    private static SkillUiSpec BuildSpec() => new()
    {
        Title = "Steam 游戏库盘点",
        Surface = SkillUiSurface.Entry,
        Sections =
        [
            new SkillUiSection
            {
                Kind = SkillUiSectionKind.PosterGrid,
                Columns = 4,
                Items =
                [
                    new SkillUiItem
                    {
                        Title = "Short Circuit VR",
                        Value = "696.9 MB",
                        AppId = "970800",
                        Path = @"G:\SteamLibrary\steamapps\common\Short Circuit VR",
                        SizeBytes = 730_800_000,
                        Action = new SkillUiItemAction { Kind = "uninstall", Label = "卸载", Target = "970800" }
                    }
                ]
            }
        ],
        Actions = [new SkillUiAction { Kind = "close", Label = "关闭" }]
    };

    private static SkillUiSpec BuildTableSpec() => new()
    {
        Title = "明细",
        Sections =
        [
            new SkillUiSection
            {
                Kind = SkillUiSectionKind.Table,
                Headers = ["游戏", "占用"],
                Items =
                [
                    new SkillUiItem { Cells = ["A", "1 GB"], AppId = "1", Action = new SkillUiItemAction { Kind = "uninstall", Label = "卸载", Target = "1" } },
                    new SkillUiItem { Cells = ["B", "2 GB"], AppId = "2", Action = new SkillUiItemAction { Kind = "uninstall", Label = "卸载", Target = "2" } }
                ]
            }
        ]
    };
}
