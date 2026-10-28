using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shell;
using DiskPilotX.App.Converters;
using DiskPilotX.App.Controls;
using DiskPilotX.App.Views;
using DiskPilotX.Core.Models;

namespace DiskPilotX.App.Tests;

/// <summary>
/// 动态窗口必须「长在宿主身上」：不能出现系统自带标题栏，窗口按钮、圆角裁切、磨砂材质
/// 全部复用宿主主窗口那一套。这里把外壳契约钉死，避免以后有人把 WindowStyle 改回默认。
/// </summary>
[Collection("聊天滚动控件")]
public sealed class SkillWindowChromeTests
{
    [Fact]
    public void 动态窗口复用宿主标题栏而不是系统标题栏() => StaHost.Run(() =>
    {
        var window = new SkillWindow(BuildSpec());
        try
        {
            Assert.Equal(WindowStyle.None, window.WindowStyle);
            // 与主窗口一致：靠 DWM 圆角 + DWM 材质，不开 AllowsTransparency（否则磨砂失真）
            Assert.False(window.AllowsTransparency);
            Assert.NotNull(WindowChrome.GetWindowChrome(window));

            var titleBar = Descendants(window).OfType<WindowTitleBar>().FirstOrDefault();
            Assert.NotNull(titleBar);
            // 任务窗隐藏设置/收起入口，但最小化、最大化、关闭沿用宿主实现
            Assert.Equal(Visibility.Collapsed, titleBar!.ApplicationToolsVisibility);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 动作按钮样式直接取宿主通用样式键()
    {
        // 样式键就是宿主主题里的通用按钮与强调按钮，改名会让动态窗口立刻失样式。
        Assert.Equal("VP.AccentButton", ActionButtonStyleConverter.PrimaryKey);
        Assert.Equal("VP.Button", ActionButtonStyleConverter.NormalKey);

        StaHost.Run(() =>
        {
            var converter = new ActionButtonStyleConverter();
            var resources = Application.Current.Resources;
            resources[ActionButtonStyleConverter.NormalKey] = new Style(typeof(Button));
            resources[ActionButtonStyleConverter.PrimaryKey] = new Style(typeof(Button));

            Assert.Same(resources[ActionButtonStyleConverter.PrimaryKey], converter.Convert(true, typeof(Style), null, CultureInfo.InvariantCulture));
            Assert.Same(resources[ActionButtonStyleConverter.NormalKey], converter.Convert(false, typeof(Style), null, CultureInfo.InvariantCulture));
            // 未知值一律回落到普通按钮，不会出现无样式裸按钮
            Assert.Same(resources[ActionButtonStyleConverter.NormalKey], converter.Convert(null, typeof(Style), null, CultureInfo.InvariantCulture));
        });
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private static SkillUiSpec BuildSpec() => new()
    {
        Title = "Steam 游戏库盘点",
        Subtitle = "共 10 个已安装项目",
        Surface = SkillUiSurface.Window,
        Sections =
        [
            new SkillUiSection
            {
                Kind = SkillUiSectionKind.PosterGrid,
                Columns = 4,
                Items = [new SkillUiItem { Title = "赛博朋克 2077", Value = "118.5 GB", AppId = "1091500", Selectable = true }]
            }
        ],
        Actions =
        [
            new SkillUiAction { Kind = "continue", Label = "回传选中项继续分析" },
            new SkillUiAction { Kind = "close", Label = "关闭" }
        ]
    };
}
