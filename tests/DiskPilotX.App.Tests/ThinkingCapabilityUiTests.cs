using System.Runtime.CompilerServices;
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
