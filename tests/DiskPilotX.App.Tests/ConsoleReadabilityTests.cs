using System.Text.Json;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Llm;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DiskPilotX.App.Logging;
using DiskPilotX.App.Services;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Models.Theme;
using DiskPilotX.Core.Services.Settings;
using NSubstitute;
using Serilog.Events;

namespace DiskPilotX.App.Tests;

[Collection("聊天滚动控件")]
public sealed class ConsoleReadabilityTests
{
    [Theory]
    [InlineData(false, false, 0.6)]
    [InlineData(true, false, 0.6)]
    [InlineData(false, true, 0.0)]
    [InlineData(false, true, 0.6)]
    [InlineData(false, true, 1.0)]
    [InlineData(true, true, 0.0)]
    [InlineData(true, true, 0.6)]
    [InlineData(true, true, 1.0)]
    public void 各模式按主题参考底色增强文字且不添加背景遮色(bool light, bool glass, double opacity) => Isolated(() =>
    {
        var profile = (light ? ThemeProfile.VisionProLight : ThemeProfile.VisionProDark).Clone();
        profile.GlassEnabled = glass;
        profile.GlassOpacity = opacity;
        var manager = CreateManager(profile);
        Apply(manager, profile, glass);
        VerifyContrast(glass ? 7 : 4.5);
        Assert.False(Application.Current.Resources.Contains("VP.LogBackdropBrush"));
        Assert.Equal(opacity, manager.CurrentProfile.GlassOpacity);
        AssertHue(LogEventLevel.Information, color => color.G > color.R && color.G > color.B);
        AssertHue(LogEventLevel.Warning, color => color.R > color.G && color.G > color.B);
        AssertHue(LogEventLevel.Error, color => color.R > color.G && color.R > color.B);
    });

    [Theory]
    [InlineData("#FF777777")]
    [InlineData("#FF003344")]
    [InlineData("#FFF4DDE8")]
    [InlineData("#FF65717D")]
    [InlineData("#FF4B555E")]
    public void 自定义背景不能改变日志色板或让语义色变成黑白(string background) => Isolated(() =>
    {
        var profile = ThemeProfile.VisionProDark.Clone();
        profile.GlassEnabled = true;
        var manager = CreateManager(profile);
        Apply(manager, profile, true);
        var expected = CaptureEntries();
        var changed = profile.Clone();
        changed.Name = "Custom";
        changed.BackgroundColor = background;
        changed.AutoTextContrast = true;
        Apply(manager, changed, true);
        Assert.Equal(expected, CaptureEntries());
        AssertHue(LogEventLevel.Warning, color => color.R > color.G && color.G > color.B);
        AssertHue(LogEventLevel.Error, color => color.R > color.G && color.R > color.B);
    });

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void 实际背景拾色与自动对比开关不改变日志且重载后保留(bool light, bool glass) => Isolated(() =>
    {
        var profile = (light ? ThemeProfile.VisionProLight : ThemeProfile.VisionProDark).Clone();
        profile.GlassEnabled = glass;
        var saved = new AppSettings { ThemeProfile = profile };
        var settings = Substitute.For<ISettingsService>();
        settings.LoadSettings().Returns(saved);
        var manager = new ThemeManager(settings);
        manager.ApplyTheme(profile);
        using var clients = new ModelHttpClientFactory();
        var vm = new SettingsViewModel(settings, new ModelConnectionTester(new ModelClientFactory(clients)), manager);
        Apply(manager, manager.CurrentProfile, glass);
        var expected = CaptureEntries();
        foreach (var background in new[] { "#FF65717D", "#FF4B555E", "#FFF2F2F2", "#FF112233" })
        {
            vm.ThemeBackgroundColor = ThemeManager.ParseColor(background);
            foreach (var automatic in new[] { false, true })
            {
                vm.AutoTextContrast = automatic;
                Apply(manager, manager.CurrentProfile, glass);
                Assert.Equal(expected, CaptureEntries());
            }
        }
        saved.ThemeProfile = JsonSerializer.Deserialize<ThemeProfile>(JsonSerializer.Serialize(manager.CurrentProfile));
        var reloaded = new ThemeManager(settings);
        reloaded.Initialize();
        Apply(reloaded, reloaded.CurrentProfile, glass);
        Assert.Equal(expected, CaptureEntries());
    });

    private static string[] CaptureEntries() => Enum.GetValues<LogEventLevel>().Select(level =>
    {
        var entry = new UiLogEntry(DateTimeOffset.Now, level, "颜色独立验证", null);
        return $"{level}:{ColorOf(entry.LevelBrush)}:{ColorOf(entry.MessageBrush)}:{ColorOf(entry.TimeBrush)}";
    }).ToArray();

    [Fact]
    public void 改变界面强调色和语义色不会串改日志色系() => Isolated(() =>
    {
        var profile = ThemeProfile.VisionProDark.Clone();
        var manager = CreateManager(profile);
        manager.ApplyTheme(profile);
        var entries = Enum.GetValues<LogEventLevel>().Select(level => new UiLogEntry(DateTimeOffset.Now, level, "语义独立验证", null)).ToArray();
        var expected = entries.Select(entry => ColorOf(entry.LevelBrush)).ToArray();
        var changed = profile.Clone();
        changed.AccentColor = changed.SuccessColor = changed.WarningColor = changed.DangerColor = "#FFCC33FF";
        manager.ApplyTheme(changed);
        Assert.Equal(expected, entries.Select(entry => ColorOf(entry.LevelBrush)).ToArray());
    });

    [Fact]
    public void 切换深浅主题时已显示日志的画刷原位更新() => Isolated(() =>
    {
        var manager = CreateManager(ThemeProfile.VisionProDark.Clone());
        manager.ApplyTheme(ThemeProfile.VisionProDark.Clone());
        var entry = new UiLogEntry(DateTimeOffset.Now, LogEventLevel.Warning, "旧日志", "异常详情");
        var levelBrush = entry.LevelBrush;
        var messageBrush = entry.MessageBrush;
        var timeBrush = entry.TimeBrush;
        var oldColor = ColorOf(messageBrush);
        manager.ApplyTheme(ThemeProfile.VisionProLight.Clone());
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        Assert.Same(levelBrush, entry.LevelBrush);
        Assert.Same(messageBrush, entry.MessageBrush);
        Assert.Same(timeBrush, entry.TimeBrush);
        Assert.NotEqual(oldColor, ColorOf(messageBrush));
        VerifyContrast();
    });

    [Fact]
    public void 深色普通模式保留原有警告和错误配色() => Isolated(() =>
    {
        var manager = CreateManager(ThemeProfile.VisionProDark.Clone());
        manager.ApplyTheme(ThemeProfile.VisionProDark.Clone());
        var warning = new UiLogEntry(DateTimeOffset.Now, LogEventLevel.Warning, "警告", null);
        var error = new UiLogEntry(DateTimeOffset.Now, LogEventLevel.Error, "错误", null);
        Assert.Equal("#FFE0AF68", ColorOf(warning.LevelBrush).ToString());
        Assert.Equal("#FFFFC777", ColorOf(warning.MessageBrush).ToString());
        Assert.Equal("#FFF7768E", ColorOf(error.LevelBrush).ToString());
        Assert.Equal("#FFFF9EAB", ColorOf(error.MessageBrush).ToString());
    });

    [Fact]
    public void 面板阴影裁切排除内部而保留外侧()
    {
        var method = typeof(DiskPilotX.App.MainWindow).GetMethod("CreateBottomPanelShadowClip", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var clip = Assert.IsAssignableFrom<Geometry>(method!.Invoke(null, new object[] { new Size(800, 300) }));
        Assert.False(clip.FillContains(new Point(400, 150)));
        Assert.False(clip.FillContains(new Point(1, 1)));
        Assert.False(clip.FillContains(new Point(799, 299)));
        Assert.True(clip.FillContains(new Point(-4, 150)));
        Assert.True(clip.FillContains(new Point(400, 304)));
    }

    private static void VerifyContrast(double target = 4.5)
    {
        // 玻璃不再添加遮色，参考主题底色校正；实际桌面背景变化不能用单一颜色保证。
        var tint = ResourceColor("VP.BackgroundBrush");
        var background = Over(ResourceColor("VP.SurfaceBrush"), Color.FromRgb(tint.R, tint.G, tint.B));
        var available = Math.Max(Contrast(Colors.Black, background), Contrast(Colors.White, background));
        foreach (var level in Enum.GetValues<LogEventLevel>())
        {
            var entry = new UiLogEntry(DateTimeOffset.Now, level, "正文对比度", "详情");
            foreach (var brush in new[] { entry.LevelBrush, entry.MessageBrush, entry.TimeBrush })
            {
                var color = ColorOf(brush);
                var ratio = Contrast(color, background);
                Assert.True(ratio + 1e-6 >= Math.Min(target, available), $"{level} 的 {color} 对参考背景 {background} 只有 {ratio:F3}:1。");
            }
        }
    }

    private static void AssertHue(LogEventLevel level, Func<Color, bool> predicate) =>
        Assert.True(predicate(ColorOf(new UiLogEntry(DateTimeOffset.Now, level, "色系验证", null).LevelBrush)));

    private static ThemeManager CreateManager(ThemeProfile profile)
    {
        var settings = Substitute.For<ISettingsService>();
        settings.LoadSettings().Returns(new AppSettings { ThemeProfile = profile });
        return new ThemeManager(settings);
    }

    private static void Apply(ThemeManager manager, ThemeProfile profile, bool glass)
    {
        manager.ApplyTheme(profile);
        if (!glass) return;
        // 无窗口单元测试仅模拟材质可用状态，不调用原生窗口或依赖桌面背景。
        typeof(ThemeManager).GetField("_glassBackdropActive", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(manager, true);
        typeof(ThemeManager).GetMethod("ApplyToResources", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(manager, null);
    }

    private static Color ResourceColor(string key) => ColorOf(Assert.IsAssignableFrom<Brush>(Application.Current.Resources[key]));
    private static Color ColorOf(Brush brush) => Assert.IsType<SolidColorBrush>(brush).Color;
    private static Color Over(Color foreground, Color background)
    {
        var alpha = foreground.A / 255.0;
        return Color.FromRgb((byte)Math.Round(foreground.R * alpha + background.R * (1 - alpha)),
            (byte)Math.Round(foreground.G * alpha + background.G * (1 - alpha)),
            (byte)Math.Round(foreground.B * alpha + background.B * (1 - alpha)));
    }
    private static double Contrast(Color a, Color b)
    {
        static double L(Color color)
        {
            static double C(byte channel) { var value = channel / 255.0; return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4); }
            return C(color.R) * 0.2126 + C(color.G) * 0.7152 + C(color.B) * 0.0722;
        }
        return (Math.Max(L(a), L(b)) + 0.05) / (Math.Min(L(a), L(b)) + 0.05);
    }
    private static void Isolated(Action action) => StaHost.Run(() =>
    {
        var previous = Application.Current.Resources;
        Application.Current.Resources = new ResourceDictionary();
        try { action(); }
        finally { Application.Current.Resources = previous; }
    });
}
