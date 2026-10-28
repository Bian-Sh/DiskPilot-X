using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Media;
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
public sealed class ApiKeyVisibilityTests
{
    private const string FixtureKey = "dp-test-only-not-a-real-api-key";

    [Fact]
    public void 默认使用原生星号密钥框且明文不可见() => StaHost.Run(() =>
    {
        using var f = new Fixture();
        Assert.Equal(Visibility.Collapsed, f.Plain.Visibility);
        Assert.Equal(Visibility.Visible, f.Mask.Visibility);
        Assert.Equal('*', f.Mask.PasswordChar);
        Assert.Equal(FixtureKey, f.Mask.Password);
        Assert.False(f.Eye.IsChecked ?? false);
        Assert.True(UIElementAutomationPeer.CreatePeerForElement(f.Mask)!.IsPassword());
    });

    [Fact]
    public void 眼睛切换只改变显示不保存不失效密钥也不发请求() => StaHost.Run(() =>
    {
        using var f = new Fixture();
        f.Eye.IsChecked = true; f.Pump();
        Assert.Equal(Visibility.Visible, f.Plain.Visibility); Assert.Equal(Visibility.Collapsed, f.Mask.Visibility);
        Assert.Equal(FixtureKey, f.Plain.Text);
        f.Eye.IsChecked = false; f.Pump();
        Assert.Equal(Visibility.Collapsed, f.Plain.Visibility); Assert.Equal(Visibility.Visible, f.Mask.Visibility);
        Assert.Equal(FixtureKey, f.Vm.ApiKey); Assert.True(f.Vm.IsApiKeyValid);
        f.Settings.DidNotReceive().SaveSettings(Arg.Any<AppSettings>());
        Assert.Equal(0, f.Requests);
    });

    [Fact]
    public void 隐藏编辑保存真实值且切换不重复保存() => StaHost.Run(() =>
    {
        using var f = new Fixture();
        f.Mask.Password = "dp-new-test-value"; f.Pump();
        Assert.Equal("dp-new-test-value", f.Vm.ApiKey);
        Assert.Equal("dp-new-test-value", Encoding.UTF8.GetString(f.Saved.EncryptedApiKey!));
        f.Eye.IsChecked = true; f.Pump(); Assert.Equal("dp-new-test-value", f.Plain.Text);
        f.Eye.IsChecked = false; f.Pump();
        f.Settings.Received(1).SaveSettings(f.Saved); Assert.Equal(0, f.Requests);
    });

    [Theory]
    [InlineData("dp-visible-edit-test")]
    [InlineData("")]
    public void 明文编辑后切回掩码保留真实值包括清空(string value) => StaHost.Run(() =>
    {
        using var f = new Fixture();
        f.Eye.IsChecked = true; f.Pump(); f.Plain.Text = value; f.Pump();
        Assert.Equal(value, f.Vm.ApiKey);
        f.Eye.IsChecked = false; f.Pump(); Assert.Equal(value, f.Mask.Password);
        if (value.Length == 0) Assert.Null(f.Saved.EncryptedApiKey);
        else Assert.Equal(value, Encoding.UTF8.GetString(f.Saved.EncryptedApiKey!));
        f.Settings.Received(1).SaveSettings(f.Saved); Assert.Equal(0, f.Requests);
    });

    [Fact]
    public void 重新打开设置恢复隐藏但不改变密钥() => StaHost.Run(() =>
    {
        using var f = new Fixture(); f.Eye.IsChecked = true; f.Pump();
        f.Section.Visibility = Visibility.Collapsed; f.Pump();
        f.Section.Visibility = Visibility.Visible; f.Pump();
        Assert.False(f.Eye.IsChecked ?? false); Assert.Equal(Visibility.Visible, f.Mask.Visibility);
        Assert.Equal(Visibility.Collapsed, f.Plain.Visibility); Assert.Equal(FixtureKey, f.Vm.ApiKey);
        f.Settings.DidNotReceive().SaveSettings(Arg.Any<AppSettings>()); Assert.Equal(0, f.Requests);
    });

    [Fact]
    public void 外部密钥变化同步掩码但不触发二次写入() => StaHost.Run(() =>
    {
        using var f = new Fixture(); f.Vm.ApiKey = "dp-external-test-value"; f.Pump();
        Assert.Equal("dp-external-test-value", f.Mask.Password);
        f.Settings.Received(1).SaveSettings(f.Saved); Assert.Equal(0, f.Requests);
    });

    [Fact]
    public void 移动焦点到眼睛以及切换不会提交模型配置() => StaHost.Run(() =>
    {
        using var f = new Fixture();
        f.Mask.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, Environment.TickCount, f.Mask, f.Eye) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
        f.Eye.IsChecked = true; f.Pump();
        f.Plain.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, Environment.TickCount, f.Plain, f.Eye) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
        f.Eye.IsChecked = false; f.Pump();
        Assert.Equal(0, f.Requests); f.Settings.DidNotReceive().SaveSettings(Arg.Any<AppSettings>());
    });

    [Fact]
    public void 掩码框正常Enter仍能提交模型配置且不把星号当成密钥() => StaHost.Run(() =>
    {
        using var f = new Fixture(); f.Mask.Password = "dp-enter-test-value"; f.Pump();
        var e = new KeyEventArgs(Keyboard.PrimaryDevice, f.Source, Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        f.Mask.RaiseEvent(e);
        var limit = DateTime.UtcNow.AddSeconds(5);
        while (f.Requests == 0 && DateTime.UtcNow < limit) { f.Pump(); Thread.Sleep(5); }
        Assert.True(e.Handled); Assert.Equal(1, f.Requests); Assert.Equal("dp-enter-test-value", f.Vm.ApiKey);
    });

    [Fact]
    public void 眼睛在输入框右侧内部且预留文字空间() => StaHost.Run(() =>
    {
        using var f = new Fixture();
        var bounds = f.Eye.TransformToAncestor(f.Plain.Parent as Visual ?? throw new InvalidOperationException()).TransformBounds(new Rect(f.Eye.RenderSize));
        Assert.InRange(bounds.Right, f.Mask.ActualWidth - 10, f.Mask.ActualWidth);
        Assert.True(f.Mask.Padding.Right >= f.Eye.ActualWidth + 6);
    });

    [Fact]
    public void 空密钥保持空值不会生成星号占位或保存假密钥() => StaHost.Run(() =>
    {
        using var f = new Fixture(stored: false);
        Assert.Null(f.Vm.ApiKey); Assert.Equal("", f.Mask.Password);
        f.Eye.IsChecked = true; f.Pump(); Assert.Equal("", f.Plain.Text);
        f.Eye.IsChecked = false; f.Pump(); Assert.Null(f.Vm.ApiKey);
        Assert.Null(f.Saved.EncryptedApiKey); f.Settings.DidNotReceive().SaveSettings(Arg.Any<AppSettings>());
    });

    [Fact]
    public void 更换配置后隐藏并解绑旧密钥来源() => StaHost.Run(() =>
    {
        using var f = new Fixture(); f.Eye.IsChecked = true; f.Pump();
        var saved = new AppSettings { EncryptedApiKey = [2], DiscoveredModels = ["缓存模型"] };
        var settings = Substitute.For<ISettingsService>(); settings.LoadSettings().Returns(saved); settings.GetApiKey(saved).Returns("dp-second-fixture-key");
        using var clients = new ModelHttpClientFactory();
        var next = new SettingsViewModel(settings, new ModelConnectionTester(new ModelClientFactory(clients)), new ThemeManager(settings));
        settings.ClearReceivedCalls();
        f.Section.DataContext = next; f.Pump();
        Assert.False(f.Eye.IsChecked ?? false); Assert.Equal("dp-second-fixture-key", f.Mask.Password);
        f.Vm.ApiKey = "dp-old-source-value"; f.Pump();
        Assert.Equal("dp-second-fixture-key", f.Mask.Password); Assert.Equal("dp-second-fixture-key", next.ApiKey);
        settings.DidNotReceive().SaveSettings(Arg.Any<AppSettings>());
    });

    [Fact]
    public void 眼睛提示与可访问名称按显示状态和语言同步变化() => StaHost.Run(() =>
    {
        var original = DiskPilotX.App.Localization.L.GetState().LanguagePreference;
        try
        {
            using var f = new Fixture();
            DiskPilotX.App.Localization.L.SetLanguage("en-US"); f.Pump();
            Assert.Equal("Show API key", f.Eye.ToolTip);
            f.Eye.IsChecked = true; f.Pump();
            Assert.Equal("Hide API key", f.Eye.ToolTip);
            Assert.Equal("Hide API key", System.Windows.Automation.AutomationProperties.GetName(f.Eye));
            DiskPilotX.App.Localization.L.SetLanguage("zh-CN"); f.Pump(); Assert.Equal("隐藏 API Key", f.Eye.ToolTip);
            f.Eye.IsChecked = false; f.Pump(); Assert.Equal("显示 API Key", f.Eye.ToolTip);
            f.Settings.DidNotReceive().SaveSettings(Arg.Any<AppSettings>()); Assert.Equal(0, f.Requests);
        }
        finally { DiskPilotX.App.Localization.L.SetLanguage(original); }
    });

    private sealed class Fixture : IDisposable
    {
        public readonly AppSettings Saved = new() { EncryptedApiKey = [1], IsApiKeyValid = true, AnthropicBaseUrl = "https://fixture.invalid", ApiFormat = "openai-responses", DiscoveredModels = ["已保存模型"] };
        public ISettingsService Settings { get; }
        public SettingsViewModel Vm { get; }
        public ApiSettingsSection Section { get; }
        public TextBox Plain { get; }
        public PasswordBox Mask { get; }
        public ToggleButton Eye { get; }
        public HwndSource Source { get; }
        public int Requests;
        private readonly ModelHttpClientFactory clients;

        public Fixture(bool stored = true)
        {
            if (!stored) { Saved.EncryptedApiKey = null; Saved.IsApiKeyValid = false; }
            Settings = Substitute.For<ISettingsService>(); Settings.LoadSettings().Returns(Saved); Settings.GetApiKey(Saved).Returns(FixtureKey);
            Settings.EncryptApiKey(Arg.Any<string>()).Returns(c => Encoding.UTF8.GetBytes(c.Arg<string>()));
            clients = new ModelHttpClientFactory(() => new Handler(() => Interlocked.Increment(ref Requests)));
            Vm = new SettingsViewModel(Settings, new ModelConnectionTester(new ModelClientFactory(clients)), new ThemeManager(Settings), new ModelCatalogService(clients));
            Section = new ApiSettingsSection { DataContext = Vm };
            Section.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/DiskPilot-X;component/Themes/VisionProTheme.xaml", UriKind.Relative) });
            Plain = (TextBox)Section.FindName("ApiKeyInput");
            Mask = Assert.IsType<PasswordBox>(Section.FindName("ApiKeyMaskedInput"));
            Eye = Assert.IsType<ToggleButton>(Section.FindName("ApiKeyVisibilityToggle"));
            var editor = (Grid)Plain.Parent; ((Panel)editor.Parent).Children.Remove(editor); Section.Content = editor;
            Source = new HwndSource(new HwndSourceParameters("虚构 API Key 显示测试") { Width = 500, Height = 90, WindowStyle = unchecked((int)0x80000000) }) { RootVisual = Section };
            Section.Measure(new Size(500, 90)); Section.Arrange(new Rect(0, 0, 500, 90)); Section.UpdateLayout(); Pump();
            Settings.ClearReceivedCalls();
        }
        public void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        public void Dispose() { Source.Dispose(); clients.Dispose(); }
    }
    private sealed class Handler(Action called) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            called(); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"data\":[{\"id\":\"测试模型\"}]}") });
        }
    }
}
