using System.Net;
using System.Net.Http;
using NSubstitute;
using SpaceMonger.App.Services;
using SpaceMonger.App.ViewModels;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Assistant;
using SpaceMonger.Core.Services.Llm;
using SpaceMonger.Core.Services.Settings;

namespace SpaceMonger.App.Tests;

[Collection("CLI 指纹状态")]
public sealed class SettingsModelListTests
{
    [Fact] public async Task 模型URL持久化并用于刷新且不改对话模型()
    {
        var saved=new AppSettings { EncryptedApiKey=[1], ChatModelName="原模型" };
        var settings=Substitute.For<ISettingsService>(); settings.LoadSettings().Returns(saved); settings.GetApiKey(saved).Returns("key");
        using var clients=new ModelHttpClientFactory(()=>new Handler(request=> { Assert.Equal("https://catalog.example/models",request.RequestUri!.AbsoluteUri); return new(HttpStatusCode.OK){Content=new StringContent("{\"data\":[{\"id\":\"model-b\"},{\"id\":\"model-a\"}]}")}; }));
        var vm=new SettingsViewModel(settings,new ModelConnectionTester(new ModelClientFactory(clients)),new ThemeManager(settings),new ModelCatalogService(clients));
        vm.ModelsUrl=" https://catalog.example/models "; await vm.RefreshModelsCommand.ExecuteAsync(null);
        Assert.Equal("https://catalog.example/models",saved.ModelsUrl); Assert.Equal(new[]{"model-a","model-b"},vm.ModelCandidates); Assert.Equal(vm.ModelCandidates,saved.DiscoveredModels); Assert.Equal("原模型",saved.ChatModelName);
    }
    [Fact] public async Task 获取失败保留已有模型列表且允许再次刷新()
    {
        var saved=new AppSettings { EncryptedApiKey=[1], DiscoveredModels=["model-a"] }; var settings=Substitute.For<ISettingsService>(); settings.LoadSettings().Returns(saved); settings.GetApiKey(saved).Returns("key");
        using var clients=new ModelHttpClientFactory(()=>new Handler(_=>new(HttpStatusCode.BadGateway)));
        var vm=new SettingsViewModel(settings,new ModelConnectionTester(new ModelClientFactory(clients)),new ThemeManager(settings),new ModelCatalogService(clients));
        await vm.RefreshModelsCommand.ExecuteAsync(null); await vm.RefreshModelsCommand.ExecuteAsync(null);
        Assert.Equal("model-a",Assert.Single(vm.ModelCandidates)); Assert.Equal("model-a",Assert.Single(saved.DiscoveredModels)); Assert.False(vm.IsRefreshingModels);
    }
    [Fact] public async Task 刷新其他身份不改变当前选择且Grok明确保留内置指纹()
    {
        var saved=new AppSettings {CliIdentity="codex"}; var settings=Substitute.For<ISettingsService>(); settings.LoadSettings().Returns(saved);
        using var clients=new ModelHttpClientFactory(); var vm=new SettingsViewModel(settings,new ModelConnectionTester(new ModelClientFactory(clients)),new ThemeManager(settings));
        var grok=vm.CliIdentityOptions.Single(option=>option.Value==CliIdentityKind.Grok);
        await vm.RefreshCliIdentityCommand.ExecuteAsync(grok);
        Assert.Equal(CliIdentityKind.Codex,vm.SelectedCliIdentity); Assert.Equal("codex",saved.CliIdentity); Assert.Equal(SpaceMonger.App.Localization.L.Text("SettingsCliIdentityBuiltIn"),grok.Status); Assert.False(grok.IsRefreshing);
    }
    [Fact] public async Task 后台指纹变化同步更新设置选项和当前摘要()
    {
        var saved=new AppSettings {CliIdentity="codex"};var settings=Substitute.For<ISettingsService>();settings.LoadSettings().Returns(saved);
        using var clients=new ModelHttpClientFactory();var vm=new SettingsViewModel(settings,new ModelConnectionTester(new ModelClientFactory(clients)),new ThemeManager(settings));
        var option=vm.CliIdentityOptions.Single(item=>item.Value==CliIdentityKind.Codex);
        var notifications=new List<string?>();option.PropertyChanged+=(_,e)=>notifications.Add(e.PropertyName);
        var next=$"{Version.Parse(CliIdentityService.GetVersion(CliIdentityKind.Codex)).Major+1}.2.3";
        using var http=new HttpClient(new Handler(_=>new(HttpStatusCode.OK){Content=new StringContent(System.Text.Json.JsonSerializer.Serialize(new{tag_name=$"rust-v{next}"}))}));
        Assert.True(await CliIdentityService.RefreshVersionAsync(CliIdentityKind.Codex,http));
        Assert.Contains(nameof(CliIdentityOption.DisplayLabel),notifications);
        Assert.Contains(next,option.DisplayLabel);Assert.Contains(next,vm.CliIdentitySummary);
    }
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> respond):HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>Task.FromResult(respond(request)); }
}
