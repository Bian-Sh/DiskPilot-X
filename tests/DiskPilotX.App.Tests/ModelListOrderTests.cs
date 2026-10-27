using System.Net;
using System.Net.Http;
using NSubstitute;
using DiskPilotX.App.Services;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Copilot;
using DiskPilotX.Core.Services.Llm;
using DiskPilotX.Core.Services.Settings;

namespace DiskPilotX.App.Tests;

public sealed class ModelListOrderTests
{
    [Fact]
    public async Task 刷新保留手动顺序且重新发现和新模型追加末尾()
    {
        var saved = new AppSettings { DiscoveredModels = ["model-c", "model-a", "已下线"], ChatModelName = "model-c" };
        using var clients = Clients("model-a", "model-b", "model-c", "model-d");
        var (vm, settings) = Create(saved, clients);
        await vm.RefreshModelsCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "model-c", "model-a", "model-b", "model-d" }, vm.ModelCandidates);
        Assert.Equal(vm.ModelCandidates, saved.DiscoveredModels);
        Assert.Equal("model-c", saved.ChatModelName);
        settings.Received().SaveSettings(saved);
    }

    [Fact]
    public void 移动模型即时保存并通知聊天同步()
    {
        var saved = new AppSettings { DiscoveredModels = ["a", "b", "c"], ChatModelName = "a" };
        using var clients = Clients();
        var (vm, settings) = Create(saved, clients);
        var notices = 0; vm.SettingsChanged += () => notices++;
        Assert.True(vm.MoveModel("c", 0));
        Assert.Equal(new[] { "c", "a", "b" }, vm.ModelCandidates);
        Assert.Equal(vm.ModelCandidates, saved.DiscoveredModels);
        Assert.Equal(1, notices);
        Assert.Equal("a", saved.ChatModelName);
        vm.LoadSettings();
        Assert.Equal(new[] { "c", "a", "b" }, vm.ModelCandidates);
        settings.Received(1).SaveSettings(saved);
    }

    [Fact]
    public void 无效移动或移除不改列表也不保存()
    {
        var saved = new AppSettings { DiscoveredModels = ["a", "b"] };
        using var clients = Clients(); var (vm, settings) = Create(saved, clients);
        Assert.False(vm.MoveModel("missing", 0));
        Assert.False(vm.MoveModel("a", -1));
        Assert.False(vm.MoveModel("a", 2));
        Assert.False(vm.MoveModel("a", 0));
        vm.RemoveModelCommand.Execute("missing");
        Assert.Equal(new[] { "a", "b" }, saved.DiscoveredModels);
        settings.DidNotReceive().SaveSettings(Arg.Any<AppSettings>());
    }

    [Fact]
    public void 删除当前模型后清空选择但不擅自切换其他模型()
    {
        var saved = new AppSettings { DiscoveredModels = ["a", "b"], ChatModelName = "a" };
        using var clients = Clients(); var (vm, settings) = Create(saved, clients);
        vm.RemoveModelCommand.Execute("a");
        Assert.Equal("b", Assert.Single(vm.ModelCandidates));
        Assert.Null(saved.ChatModelName); Assert.Null(vm.ChatModelName);
        Assert.Equal(vm.ModelCandidates, saved.DiscoveredModels);
        settings.Received(1).SaveSettings(saved);
    }

    [Fact]
    public async Task 移除不是永久屏蔽再次刷新追加尾部()
    {
        var saved = new AppSettings { DiscoveredModels = ["a", "b", "c"], ChatModelName = "b" };
        using var clients = Clients("a", "b", "c", "d"); var (vm, _) = Create(saved, clients);
        vm.RemoveModelCommand.Execute("a");
        Assert.Equal(new[] { "b", "c" }, saved.DiscoveredModels);
        await vm.RefreshModelsCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "b", "c", "a", "d" }, saved.DiscoveredModels);
        Assert.Equal("b", saved.ChatModelName);
    }

    [Fact]
    public void 排序删除经现有通知同步到聊天列表()
    {
        var saved = new AppSettings { DiscoveredModels = ["a", "b", "c"], ChatModelName = "b" };
        using var clients = Clients(); var (vm, settings) = Create(saved, clients);
        var chat = new ChatViewModel(new DiskAssistantService(_ => throw new InvalidOperationException("不应调用模型"), [], new AssistantWorkspace(), settings), settings, Substitute.For<IAiSkillRouter>());
        vm.SettingsChanged += chat.RefreshApiKeyStatus;
        vm.MoveModel("c", 0);
        Assert.Equal(new[] { "c", "a", "b" }, chat.ModelCandidates);
        Assert.Equal("b", chat.SelectedChatModel);
        vm.RemoveModelCommand.Execute("b");
        Assert.Equal(new[] { "c", "a" }, chat.ModelCandidates);
        Assert.Null(chat.SelectedChatModel);
    }

    [Fact]
    public void 刷新时不允许排序或移除避免覆盖目录()
    {
        using var clients = Clients(); var (vm, settings) = Create(new() { DiscoveredModels = ["a", "b"] }, clients);
        vm.IsRefreshingModels = true;
        Assert.False(vm.MoveModel("a", 1)); vm.RemoveModelCommand.Execute("a");
        Assert.Equal(new[] { "a", "b" }, vm.ModelCandidates);
        settings.DidNotReceive().SaveSettings(Arg.Any<AppSettings>());
    }
    private static (SettingsViewModel Vm, ISettingsService Settings) Create(AppSettings saved, ModelHttpClientFactory clients)
    {
        saved.EncryptedApiKey ??= [1];
        var settings = Substitute.For<ISettingsService>(); settings.LoadSettings().Returns(saved); settings.GetApiKey(saved).Returns("test-key");
        return (new SettingsViewModel(settings, new ModelConnectionTester(new ModelClientFactory(clients)), new ThemeManager(settings), new ModelCatalogService(clients)), settings);
    }

    private static ModelHttpClientFactory Clients(params string[] ids) => new(() => new Handler(ids));
    private sealed class Handler(string[] ids) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new { data = ids.Select(id => new { id }) })) });
    }
}
