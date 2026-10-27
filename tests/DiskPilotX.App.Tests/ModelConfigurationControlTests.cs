using System.Net;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using NSubstitute;
using DiskPilotX.App.Services;
using DiskPilotX.App.ViewModels;
using DiskPilotX.App.Views.SettingsSections;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Llm;
using DiskPilotX.Core.Services.Settings;

namespace DiskPilotX.App.Tests;

[Collection("聊天滚动控件")]
public sealed class ModelConfigurationControlTests
{
    [Fact]
    public void 实际文本控件通过失焦和Enter提交且不会逐字符请求() => RunSta(() =>
    {
        var calls = 0;
        var saved = new AppSettings { DiscoveredModels = ["已有模型"], ApiFormat = "openai-responses" };
        var settings = Substitute.For<ISettingsService>(); settings.LoadSettings().Returns(saved);
        using var clients = new ModelHttpClientFactory(() => new Handler(() => Interlocked.Increment(ref calls)));
        var vm = new SettingsViewModel(settings, new ModelConnectionTester(new ModelClientFactory(clients)), new ThemeManager(settings), new ModelCatalogService(clients));
        var section = new ApiSettingsSection { DataContext = vm };
        section.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/DiskPilot-X;component/Themes/VisionProTheme.xaml", UriKind.Relative) });
        var fields = new[] { "BaseUrlInput", "ApiKeyInput", "ModelsUrlInput" }.Select(name => (TextBox)section.FindName(name)).ToArray();
        var content = new StackPanel();
        foreach (var field in fields)
        {
            if (field.Parent is Panel panel) panel.Children.Remove(field);
            content.Children.Add(field);
        }
        section.Content = content;
        using var source = new HwndSource(new HwndSourceParameters("配置完成事件回归") { Width = 570, Height = 260, WindowStyle = unchecked((int)0x80000000) }) { RootVisual = section };
        section.Measure(new Size(570, 260)); section.Arrange(new Rect(0, 0, 570, 260)); section.UpdateLayout();
        PumpUntil(() => section.IsLoaded);
        fields[0].Text = "https://relay.example";
        fields[1].Text = "test-key-a";
        fields[1].Text = "test-key-b";
        PumpFor(TimeSpan.FromMilliseconds(260));
        Assert.Equal(0, calls);
        Dispatcher.CurrentDispatcher.Invoke(() => fields[1].RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, Environment.TickCount, fields[1], fields[2])
        { RoutedEvent = Keyboard.LostKeyboardFocusEvent }));
        PumpUntil(() => vm.ModelCandidates.Contains("自动模型") && !vm.IsRefreshingModels);
        Assert.Equal(1, calls);
        fields[2].Text = "https://catalog.example/models";
        Dispatcher.CurrentDispatcher.Invoke(() => fields[2].RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Enter)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent }));
        Dispatcher.CurrentDispatcher.Invoke(() => fields[2].RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, Environment.TickCount, fields[2], fields[0])
        { RoutedEvent = Keyboard.LostKeyboardFocusEvent }));
        PumpUntil(() => calls == 2 && !vm.IsRefreshingModels);
        Assert.Equal(2, calls);
        Assert.Equal("https://catalog.example/models", saved.ModelsUrl);
        var help = (TextBlock)section.FindName("ModelsUrlHelp");
        Assert.Equal(Cursors.Help, help.Cursor); Assert.NotNull(help.ToolTip);
    });

    private static void PumpUntil(Func<bool> ready)
    {
        var limit = DateTime.UtcNow.AddSeconds(5);
        while (!ready() && DateTime.UtcNow < limit)
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); Thread.Sleep(5);
        }
        Assert.True(ready(), "完成编辑事件未在时限内取得模型。");
    }
    private static void PumpFor(TimeSpan time)
    {
        var limit = DateTime.UtcNow + time;
        while (DateTime.UtcNow < limit) { Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); Thread.Sleep(5); }
    }
    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "配置控件回归未及时完成。");
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
    private sealed class Handler(Action called) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            called();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"data\":[{\"id\":\"自动模型\"}]}") });
        }
    }
}