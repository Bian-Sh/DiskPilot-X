using System.Text.Json;
using DiskPilotX.App.Services;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Models.Theme;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Llm;
using DiskPilotX.Core.Services.Settings;
using NSubstitute;

namespace DiskPilotX.App.Tests;

[Collection("CLI 指纹状态")]
public sealed class UnifiedFrostedThemeTests
{
    [Fact]
    public void 旧版磨砂配置保留开关颜色和透明度但不再保存材质与模糊参数()
    {
        var profile = JsonSerializer.Deserialize<ThemeProfile>("""
            {"GlassEnabled":true,"BackgroundColor":"#FF123456","GlassBackdropType":1,"BlurRadius":60,"GlassOpacity":0.1}
            """)!;
        Assert.True(profile.GlassEnabled);
        Assert.Equal("#FF123456", profile.BackgroundColor);
        var saved = JsonSerializer.Serialize(profile);
        Assert.DoesNotContain("GlassBackdropType", saved);
        Assert.DoesNotContain("BlurRadius", saved);
        Assert.Equal(0.1, JsonDocument.Parse(saved).RootElement.GetProperty("GlassOpacity").GetDouble());
    }

    [Fact]
    public void 没有可用原生窗口时磨砂回退为不透明背景()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.LoadSettings().Returns(new AppSettings());
        var manager = new ThemeManager(settings);
        manager.ApplyTheme(new ThemeProfile { GlassEnabled = true });
        Assert.Equal(1.0, manager.GetGlassOpacity());
    }

    [Fact]
    public void 切换磨砂只改变材质而保留完整配色()
    {
        var profile = ThemeProfile.VisionProLight.Clone();
        var saved = new AppSettings { ThemeProfile = profile };
        var settings = Substitute.For<ISettingsService>();
        settings.LoadSettings().Returns(saved);
        var manager = new ThemeManager(settings);
        manager.Initialize();
        using var clients = new ModelHttpClientFactory();
        var vm = new SettingsViewModel(settings, new ModelConnectionTester(new ModelClientFactory(clients)), manager);
        vm.GlassEnabled = true;
        var expected = profile.Clone();
        expected.GlassEnabled = true;
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(manager.CurrentProfile));
        Assert.False(profile.GlassEnabled);
        vm.SaveCommand.Execute(null);
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(manager.CurrentProfile));
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(saved.ThemeProfile));
    }

    [Fact]
    public void 透明度即时保存且跨配色重载与磨砂开关保留()
    {
        var profile = ThemeProfile.VisionProDark.Clone();
        profile.GlassEnabled = true;
        var saved = new AppSettings { ThemeProfile = profile };
        var settings = Substitute.For<ISettingsService>();
        settings.LoadSettings().Returns(saved);
        var manager = new ThemeManager(settings);
        manager.Initialize();
        using var clients = new ModelHttpClientFactory();
        var vm = new SettingsViewModel(settings, new ModelConnectionTester(new ModelClientFactory(clients)), manager);
        Assert.Equal(60, vm.GlassOpacityPercent, 8);
        vm.GlassOpacityPercent = 75;
        var expected = profile.Clone();
        expected.GlassOpacity = 0.75;
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(manager.CurrentProfile));
        Assert.Equal(0.6, profile.GlassOpacity);
        Assert.Equal(0.75, saved.ThemeProfile!.GlassOpacity);
        vm.ApplyThemePresetCommand.Execute(ThemeProfile.VisionProLight);
        vm.LoadSettings();
        Assert.Equal(75, vm.GlassOpacityPercent, 8);
        Assert.Equal(0.75, manager.CurrentProfile.GlassOpacity);
        vm.GlassEnabled = false;
        Assert.Equal(1.0, manager.GetGlassOpacity());
        vm.GlassEnabled = true;
        vm.SaveCommand.Execute(null);
        Assert.Equal(0.75, saved.ThemeProfile!.GlassOpacity);
        Assert.Equal(0.6, ThemeProfile.VisionProLight.GlassOpacity);
    }

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(25, 0.25)]
    [InlineData(100, 1.0)]
    public void 滑杆零值通透而满值不透明(double sliderValue, double expectedOpacity)
    {
        var saved = new AppSettings { ThemeProfile = new ThemeProfile { GlassEnabled = true } };
        var settings = Substitute.For<ISettingsService>();
        settings.LoadSettings().Returns(saved);
        var manager = new ThemeManager(settings);
        manager.Initialize();
        using var clients = new ModelHttpClientFactory();
        var vm = new SettingsViewModel(settings, new ModelConnectionTester(new ModelClientFactory(clients)), manager);
        vm.GlassOpacityPercent = sliderValue;
        Assert.Equal(expectedOpacity, manager.CurrentProfile.GlassOpacity);
        Assert.Equal(expectedOpacity, saved.ThemeProfile!.GlassOpacity);
        vm.LoadSettings();
        Assert.Equal(sliderValue, vm.GlassOpacityPercent, 8);
    }

    [Theory]
    [InlineData(-0.5, 0.0)]
    [InlineData(1.5, 1.0)]
    [InlineData(double.NaN, 0.6)]
    [InlineData(double.PositiveInfinity, 0.6)]
    public void 透明度配置修正越界和无效值(double input, double expected)
    {
        var profile = new ThemeProfile { GlassOpacity = input };
        Assert.Equal(expected, profile.GlassOpacity);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void 切换深浅配色保留磨砂开关且不修改内置预设(bool enabled)
    {
        var saved = new AppSettings { ThemeProfile = new ThemeProfile { GlassEnabled = enabled } };
        var settings = Substitute.For<ISettingsService>();
        settings.LoadSettings().Returns(saved);
        var manager = new ThemeManager(settings);
        manager.Initialize();
        using var clients = new ModelHttpClientFactory();
        var vm = new SettingsViewModel(settings, new ModelConnectionTester(new ModelClientFactory(clients)), manager);
        foreach (var preset in new[] { ThemeProfile.VisionProLight, ThemeProfile.VisionProDark })
        {
            vm.ApplyThemePresetCommand.Execute(preset);
            Assert.Equal(enabled, vm.GlassEnabled);
            Assert.Equal(enabled, saved.ThemeProfile!.GlassEnabled);
            Assert.NotSame(preset, manager.CurrentProfile);
            Assert.False(preset.GlassEnabled);
        }
    }
}
