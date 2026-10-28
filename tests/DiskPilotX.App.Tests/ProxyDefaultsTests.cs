using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using DiskPilotX.App.Services;
using DiskPilotX.App.ViewModels;
using DiskPilotX.App.Views.SettingsSections;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Llm;
using DiskPilotX.Core.Services.Settings;
using NSubstitute;

namespace DiskPilotX.App.Tests;

[Collection("聊天滚动控件")]
public sealed class ProxyDefaultsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AI切换到自定义时空地址自动补齐(string? host) => StaHost.Run(() =>
    {
        var saved = new AppSettings { ProxyHost = host };
        var (vm, _) = CreateAi(saved);
        vm.SelectedProxyKind = ProxyKind.Custom;
        Assert.Equal("127.0.0.1", vm.ProxyHost);
        Assert.Equal("127.0.0.1", saved.ProxyHost);
    });

    [Fact]
    public void AI加载自定义空地址会补齐但已有地址不覆盖() => StaHost.Run(() =>
    {
        var saved = new AppSettings { ProxyMode = "custom", ProxyHost = " " };
        var (vm, _) = CreateAi(saved);
        Assert.Equal("127.0.0.1", vm.ProxyHost);
        vm.ProxyHost = "10.2.3.4"; vm.SelectedProxyKind = ProxyKind.System; vm.SelectedProxyKind = ProxyKind.Custom;
        Assert.Equal("10.2.3.4", vm.ProxyHost);
    });

    [Fact]
    public void AI输入过程中允许清空但提交时补齐默认地址() => StaHost.Run(() =>
    {
        var (vm, _) = CreateAi(new AppSettings { ProxyMode = "custom", ProxyHost = "10.2.3.4" });
        vm.ProxyHost = "";
        Assert.Equal("", vm.ProxyHost);
        Assert.True(vm.CompleteModelConfigurationAsync().IsCompletedSuccessfully);
        Assert.Equal("127.0.0.1", vm.ProxyHost);
    });

    [Fact]
    public void 更新代理真实控件独立保存并默认本地地址且不改AI凭据() => StaHost.Run(() =>
    {
        var saved = JsonSerializer.Deserialize<AppSettings>("{\"ProxyMode\":\"custom\",\"ProxyHost\":\"ai.example\",\"ProxyPort\":7001,\"ProxyUsername\":\"AI用户\",\"EncryptedProxyPassword\":\"AQI=\",\"UpdateProxyMode\":\"system\"}")!;
        var settings = Substitute.For<ISettingsService>(); settings.LoadSettings().Returns(saved);
        settings.EncryptProxyPassword("更新口令").Returns([8, 9]);
        var vm = new UpdateViewModel(Substitute.For<IUpdateService>(), settings) { HistoryLoaded = true };
        var section = new UpdateSettingsSection { DataContext = vm };
        using var source = new HwndSource(new HwndSourceParameters("独立更新代理控件测试") { Width = 650, Height = 1000, WindowStyle = unchecked((int)0x80000000) }) { RootVisual = section };
        section.Measure(new Size(650, 1000)); section.Arrange(new Rect(0, 0, 650, 1000)); section.UpdateLayout(); Pump();
        var mode = Assert.IsType<ComboBox>(section.FindName("UpdateProxyModeInput"));
        mode.SelectedValue = ProxyKind.Custom; Pump();
        var host = Assert.IsType<TextBox>(section.FindName("UpdateProxyHostInput"));
        Assert.Equal("127.0.0.1", host.Text);
        var port = Assert.IsType<TextBox>(section.FindName("UpdateProxyPortInput")); port.Text = "8002";
        var username = Assert.IsType<TextBox>(section.FindName("UpdateProxyUsernameInput")); username.Text = "更新用户";
        var password = Assert.IsType<PasswordBox>(section.FindName("UpdateProxyPasswordBox")); password.Password = "更新口令"; Pump();
        var json = JsonSerializer.SerializeToElement(saved);
        Assert.Equal("custom", json.GetProperty("UpdateProxyMode").GetString());
        Assert.Equal("127.0.0.1", json.GetProperty("UpdateProxyHost").GetString());
        Assert.Equal(8002, json.GetProperty("UpdateProxyPort").GetInt32());
        Assert.Equal("更新用户", json.GetProperty("UpdateProxyUsername").GetString());
        Assert.Equal(new byte[] { 8, 9 }, json.GetProperty("EncryptedUpdateProxyPassword").GetBytesFromBase64());
        Assert.DoesNotContain("更新口令", JsonSerializer.Serialize(saved));
        Assert.Equal("ai.example", saved.ProxyHost); Assert.Equal(7001, saved.ProxyPort); Assert.Equal("AI用户", saved.ProxyUsername); Assert.Equal(new byte[] { 1, 2 }, saved.EncryptedProxyPassword);
        host.Text = ""; Pump(); Assert.Equal("", host.Text);
        host.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, source, 0, System.Windows.Input.Key.Enter) { RoutedEvent = UIElement.PreviewKeyDownEvent }); Pump();
        Assert.Equal("127.0.0.1", host.Text);
        host.Text = "10.9.8.7"; mode.SelectedValue = ProxyKind.Direct; Pump(); Assert.False(host.IsVisible);
        mode.SelectedValue = ProxyKind.Custom; Pump(); Assert.Equal("10.9.8.7", host.Text);
        password.Password = ""; Pump(); Assert.Equal(JsonValueKind.Null, JsonSerializer.SerializeToElement(saved).GetProperty("EncryptedUpdateProxyPassword").ValueKind);
    });

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 更新加载已有独立自定义空地址会补齐且不填端口(string? host)
    {
        var saved = new AppSettings { UpdateProxyMode = "custom", UpdateProxyHost = host, ProxyHost = "ai.example" };
        var settings = Substitute.For<ISettingsService>(); settings.LoadSettings().Returns(saved);
        var vm = new UpdateViewModel(Substitute.For<IUpdateService>(), settings);
        Assert.Equal("127.0.0.1", vm.ProxyHost);
        Assert.Equal("127.0.0.1", saved.UpdateProxyHost);
        Assert.Null(vm.ProxyPortText); Assert.Null(saved.UpdateProxyPort);
        Assert.Equal("ai.example", saved.ProxyHost);
    }

    [Theory]
    [InlineData("system")]
    [InlineData("direct")]
    public void 非自定义的更新模式不补入隐藏地址(string mode)
    {
        var saved = new AppSettings { UpdateProxyMode = mode };
        var settings = Substitute.For<ISettingsService>(); settings.LoadSettings().Returns(saved);
        var vm = new UpdateViewModel(Substitute.For<IUpdateService>(), settings);
        Assert.Null(vm.ProxyHost); Assert.Null(saved.UpdateProxyHost);
        Assert.False(vm.IsCustomProxyConfigurable);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void 代理下拉切换语言后保留选中模式且没有绑定错误(bool update) => StaHost.Run(() =>
    {
        var original = DiskPilotX.App.Localization.L.GetState().LanguagePreference;
        try
        {
            DiskPilotX.App.Localization.L.SetLanguage("zh-CN");
            var saved = new AppSettings { ProxyMode = "custom", ProxyHost = "127.0.0.1", ProxyPort = 8123,
                UpdateProxyMode = "custom", UpdateProxyHost = "127.0.0.1", UpdateProxyPort = 9002 };
            var settings = Substitute.For<ISettingsService>(); settings.LoadSettings().Returns(saved);
            object vm = update ? new UpdateViewModel(Substitute.For<IUpdateService>(), settings) : CreateAi(saved).Vm;
            var mode = new ComboBox { DataContext = vm, DisplayMemberPath = "Label", SelectedValuePath = "Value" };
            mode.SetBinding(ItemsControl.ItemsSourceProperty, new System.Windows.Data.Binding("ProxyKindOptions"));
            mode.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedValueProperty,
                new System.Windows.Data.Binding("SelectedProxyKind") { Mode = System.Windows.Data.BindingMode.TwoWay });
            using var source = new HwndSource(new HwndSourceParameters("代理语言切换测试") { Width = 300, Height = 50, WindowStyle = unchecked((int)0x80000000) }) { RootVisual = mode };
            mode.Measure(new Size(300, 50)); mode.Arrange(new Rect(0, 0, 300, 50)); mode.UpdateLayout(); Pump();
            Assert.Equal(ProxyKind.Custom, mode.SelectedValue);
            DiskPilotX.App.Localization.L.SetLanguage("en-US"); Pump();
            Assert.Equal(ProxyKind.Custom, mode.SelectedValue);
            Assert.False(Validation.GetHasError(mode));
            Assert.Equal("custom", saved.ProxyMode); Assert.Equal("custom", saved.UpdateProxyMode);
        }
        finally { DiskPilotX.App.Localization.L.SetLanguage(original); }
    });

    private static (SettingsViewModel Vm, ISettingsService Settings) CreateAi(AppSettings saved)
    {
        var settings = Substitute.For<ISettingsService>(); settings.LoadSettings().Returns(saved);
        var clients = new ModelHttpClientFactory();
        return (new SettingsViewModel(settings, new ModelConnectionTester(new ModelClientFactory(clients)), new ThemeManager(settings)), settings);
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
}
