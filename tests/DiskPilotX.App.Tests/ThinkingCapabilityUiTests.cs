using System.Runtime.CompilerServices;
using System.Windows.Interop;
using System.Windows.Input;
using DiskPilotX.App.Views;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using DiskPilotX.App.Services;
using DiskPilotX.App.ViewModels;
using DiskPilotX.App.Views.SettingsSections;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Copilot;
using DiskPilotX.Core.Services.Llm;
using DiskPilotX.Core.Services.Settings;
using NSubstitute;
using TinyHarness;

namespace DiskPilotX.App.Tests;

[Collection("聊天滚动控件")]
public class ThinkingCapabilityUiTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void 滑条捕获前跳转拖动及键盘命令同步等级文字和保存(int interaction) => StaHost.Run(() =>
    {
        var saved = new AppSettings { ApiFormat = "openai-responses", ChatModelName = "模型甲", DiscoveredModels = ["模型甲", "模型乙"] };
        var settings = Settings(saved); var chat = Chat(settings);
        Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/DiskPilot-X;component/Themes/VisionProTheme.xaml", UriKind.Relative) });
        var panel = new ChatPanel();
        panel.SetViewModel(chat);
        var slider = (Slider)panel.FindName("ThinkingSlider");
        ((Panel)slider.Parent).Children.Remove(slider);
        slider.DataContext = chat;
        var host = new StackPanel(); host.Children.Add(panel); host.Children.Add(slider);
        using var source = new HwndSource(new HwndSourceParameters("思考滑条回归") { Width = 400, Height = 600 }) { RootVisual = host };
        host.Measure(new Size(400,600)); host.Arrange(new Rect(0,0,400,600)); host.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => {}, DispatcherPriority.ApplicationIdle);
        Assert.True(panel.IsLoaded); Assert.False(slider.IsMouseCaptureWithin); Assert.False(slider.IsKeyboardFocusWithin);
        Assert.Equal(ThinkingLevel.Default, chat.SelectedThinkingLevel);
        if (interaction == 1) Slider.IncreaseSmall.Execute(null, slider);
        else if (interaction == 2)
        {
            var track = (Track)slider.Template.FindName("PART_Track", slider);
            track.Thumb.RaiseEvent(new DragDeltaEventArgs((track.ActualWidth - track.Thumb.ActualWidth) * 5 / 7, 0) { RoutedEvent = Thumb.DragDeltaEvent });
        }
        else slider.SetCurrentValue(Slider.ValueProperty, 5.0); // 轨道跳转在取得鼠标捕获之前即改变数值。
        var expected = interaction == 1 ? ThinkingLevel.Off : ThinkingLevel.High;
        Assert.Equal(expected, chat.SelectedThinkingLevel);
        Assert.Equal(AiOptions.ThinkingLevels().Single(option => option.Value == expected).Label, chat.ThinkingPickerLabel);
        Assert.Equal(expected, ModelEndpointConfig.ResolveThinkingLevel(saved));
        Assert.Equal(expected, Chat(settings).SelectedThinkingLevel);
        settings.Received().SaveSettings(saved);
        Assert.Equal(expected, ModelEndpointConfig.FromSettings(saved, "测试密钥", "模型甲").ThinkingLevel);
        saved.ModelThinkingConfigurations[ThinkingCapabilities.GetProfileKey(saved, "模型乙")] = new ModelThinkingSettings { SupportedLevels = ["default", "low"], SelectedLevel = "default" };
        chat.SelectedChatModel = "模型乙";
        Dispatcher.CurrentDispatcher.Invoke(() => {}, DispatcherPriority.ApplicationIdle);
        Assert.Equal(ThinkingLevel.Default, chat.SelectedThinkingLevel);
        Assert.Equal(0, slider.Value);
        Assert.Equal("default", saved.ModelThinkingConfigurations[ThinkingCapabilities.GetProfileKey(saved, "模型乙")].SelectedLevel);
        chat.SelectedChatModel = "模型甲";
        Assert.Equal(expected, chat.SelectedThinkingLevel);
    });

    [Fact]
    public void 设置复选能力过滤对话稀疏档位且切换模型各自恢复()
    {
        var saved = new AppSettings { ApiFormat = "openai-responses", ChatModelName = "模型甲", DiscoveredModels = ["模型甲", "模型乙"], ThinkingLevel = "high" };
        var settings = Settings(saved);
        var vm = new SettingsViewModel(settings, new ModelConnectionTester(new ModelClientFactory(new ModelHttpClientFactory())), new ThemeManager(settings));
        var chat = Chat(settings);
        vm.SettingsChanged += chat.RefreshApiKeyStatus;
        Assert.Equal(8, vm.SupportedThinkingLevelOptions.Count);
        Assert.All(vm.SupportedThinkingLevelOptions, option => Assert.True(option.IsSelected));
        foreach (var option in vm.SupportedThinkingLevelOptions.Where(option => option.Value is not ThinkingLevel.Low and not ThinkingLevel.High)) option.IsSelected = false;
        Assert.Equal(new[] { ThinkingLevel.Low, ThinkingLevel.High }, chat.ThinkingLevelOptions.Select(option => option.Value));
        Assert.Equal(1, chat.ThinkingLevelMaximumIndex);
        chat.SetThinkingLevelFromSlider(0);
        Assert.Equal(ThinkingLevel.Low, chat.SelectedThinkingLevel);
        chat.SetThinkingLevelFromSlider(1);
        Assert.Equal(ThinkingLevel.High, chat.SelectedThinkingLevel);
        vm.SupportedThinkingLevelOptions.Single(option => option.Value == ThinkingLevel.High).IsSelected = false;
        Assert.Equal(ThinkingLevel.Low, chat.SelectedThinkingLevel);
        Assert.False(chat.IsThinkingLevelAdjustable);
        chat.SelectedChatModel = "模型乙";
        Assert.Equal(8, chat.ThinkingLevelOptions.Count);
        chat.SelectedThinkingLevel = ThinkingLevel.XHigh;
        chat.SelectedChatModel = "模型甲";
        Assert.Equal(ThinkingLevel.Low, chat.SelectedThinkingLevel);
        vm.ThinkingConfigurationModel = "模型乙";
        Assert.Equal("模型甲", saved.ChatModelName);
        Assert.All(vm.SupportedThinkingLevelOptions, option => Assert.True(option.IsSelected));
        vm.Save();
        Assert.Equal("high", saved.ThinkingLevel);
        Assert.Equal(ThinkingLevel.Low, ModelEndpointConfig.ResolveThinkingLevel(saved));
        chat.SelectedChatModel = "模型乙";
        Assert.Equal(ThinkingLevel.XHigh, chat.SelectedThinkingLevel);
    }

    [Fact]
    public void 协议和调节方式决定候选且不能取消最后一项()
    {
        var saved = new AppSettings { ChatModelName = "模型甲", DiscoveredModels = ["模型甲"] };
        var settings = Settings(saved);
        var vm = new SettingsViewModel(settings, new ModelConnectionTester(new ModelClientFactory(new ModelHttpClientFactory())), new ThemeManager(settings));
        Assert.Equal(5, vm.SupportedThinkingLevelOptions.Count);
        Assert.DoesNotContain(vm.SupportedThinkingLevelOptions, item => item.Value == ThinkingLevel.Minimal);
        vm.SelectedThinkingControlMode = ThinkingControlMode.Effort;
        Assert.Equal(7, vm.SupportedThinkingLevelOptions.Count);
        Assert.All(vm.SupportedThinkingLevelOptions, item => Assert.True(item.IsSelected));
        Assert.Contains(vm.SupportedThinkingLevelOptions, item => item.Value == ThinkingLevel.Max);
        vm.SelectedApiFormat = LlmApiFormat.OpenAiChatCompletions;
        Assert.Equal(8, vm.SupportedThinkingLevelOptions.Count);
        foreach (var item in vm.SupportedThinkingLevelOptions) item.IsSelected = false;
        Assert.Single(vm.SupportedThinkingLevelOptions.Where(item => item.IsSelected));
    }

    [Fact]
    public void 实际设置控件使用复选绑定并按模型切换() => StaHost.Run(() =>
    {
        var saved = new AppSettings { ApiFormat = "openai-responses", ChatModelName = "模型甲", DiscoveredModels = ["模型甲", "模型乙"] };
        var settings = Settings(saved);
        var vm = new SettingsViewModel(settings, new ModelConnectionTester(new ModelClientFactory(new ModelHttpClientFactory())), new ThemeManager(settings));
        var section = new ApiSettingsSection { DataContext = vm };
        section.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/DiskPilot-X;component/Themes/VisionProTheme.xaml", UriKind.Relative) });
        var popup = (Popup)section.FindName("ModelThinkingPopup");
        var editor = popup.Child;
        popup.Child = null;
        section.Content = editor;
        section.Measure(new Size(850, 1500)); section.Arrange(new Rect(0, 0, 850, 1500)); section.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        var flags = (ItemsControl)section.FindName("SupportedThinkingLevels");
        var checks = Visuals<CheckBox>(flags).ToArray();
        Assert.Equal((byte)255, Assert.IsType<SolidColorBrush>(((Border)section.Content).Background).Color.A);
        Assert.Equal(8, checks.Length);
        Assert.All(checks, item => Assert.True(item.IsChecked));
        checks.Single(item => ((SupportedThinkingLevelOption)item.DataContext).Value == ThinkingLevel.Minimal).IsChecked = false;
        Assert.DoesNotContain(ThinkingLevel.Minimal, ThinkingCapabilities.GetSupportedLevels(saved));
        vm.ThinkingConfigurationModel = "模型乙";
        Assert.Equal("模型乙", vm.ThinkingConfigurationModel);
        Assert.Contains(ThinkingLevel.Minimal, ThinkingCapabilities.GetSupportedLevels(new AppSettings { ApiFormat = saved.ApiFormat, ChatModelName = "模型乙", ModelThinkingConfigurations = saved.ModelThinkingConfigurations }));
    });

    private static ISettingsService Settings(AppSettings saved)
    {
        var settings = Substitute.For<ISettingsService>(); settings.LoadSettings().Returns(saved); settings.GetApiKey(saved).Returns("测试密钥"); return settings;
    }
    private static ChatViewModel Chat(ISettingsService settings)
    {
        var router = Substitute.For<IAiSkillRouter>(); router.GetSkillCatalog().Returns(Array.Empty<AiSkillCatalogItem>());
        return new ChatViewModel(new DiskAssistantService(_ => new Client(), [], new AssistantWorkspace(), settings), settings, router);
    }
    private sealed class Client : IModelClient
    {
        public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request, [EnumeratorCancellation] CancellationToken token)
        { yield return new TextDeltaEvent("完成"); await Task.Yield(); }
    }
    private static IEnumerable<T> Visuals<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); if (child is T match) yield return match;
            foreach (var nested in Visuals<T>(child)) yield return nested;
        }
    }
}
