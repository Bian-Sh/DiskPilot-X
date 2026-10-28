using System.Net;
using System.Net.Http;
using NSubstitute;
using DiskPilotX.App.Localization;
using DiskPilotX.App.Services;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Llm;
using DiskPilotX.Core.Services.Settings;

namespace DiskPilotX.App.Tests;

public sealed class ModelConfigurationEventsTests
{
    [Fact]
    public async Task 逐字符编辑不请求完成编辑才加载且不要求验证密钥()
    {
        var calls = 0;
        using var clients = Clients((_, _) => { calls++; return Task.FromResult(Models("model-a")); });
        var vm = Create(clients);
        vm.ApiKey = "first"; vm.ApiKey = "second"; vm.ModelsUrl = "https://catalog.example/list";
        await Task.Delay(260);
        Assert.Equal(0, calls); Assert.False(vm.IsApiKeyValid);
        await vm.CompleteModelConfigurationAsync();
        Assert.Equal(1, calls); Assert.Equal("model-a", Assert.Single(vm.ModelCandidates));
    }

    [Theory]
    [InlineData(null, "key", null)]
    [InlineData("https://relay.example", "", null)]
    [InlineData("not-a-url", "key", null)]
    [InlineData("file:///C:/models", "key", null)]
    [InlineData("https://relay.example", "key", "/models")]
    public async Task 配置不完整或地址非法不请求也不弹窗(string? url, string key, string? modelsUrl)
    {
        var calls = 0; var popups = 0;
        using var clients = Clients((_, _) => { calls++; return Task.FromResult(Models("unexpected")); });
        var vm = Create(clients); vm.AnthropicBaseUrl = url; vm.ApiKey = key; vm.ModelsUrl = modelsUrl;
        vm.ShowModelDiscoveryFailureAsync = _ => { popups++; return Task.CompletedTask; };
        await vm.CompleteModelConfigurationAsync();
        Assert.Equal(0, calls); Assert.Equal(0, popups); Assert.False(string.IsNullOrWhiteSpace(vm.ModelRefreshMessage));
    }

    [Fact]
    public async Task Enter失焦和验证的相邻请求合并成功后不重复()
    {
        var calls = 0;
        using var clients = Clients((_, _) => { calls++; return Task.FromResult(Models("model-a")); });
        var vm = Create(clients);
        await Task.WhenAll(vm.CompleteModelConfigurationAsync(), vm.CompleteModelConfigurationAsync(), vm.CompleteModelConfigurationAsync(force: true));
        Assert.Equal(1, calls);
        await vm.CompleteModelConfigurationAsync();
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task 自定义URL完成编辑生效清空后改用自动候选()
    {
        var urls = new List<string>();
        using var clients = Clients((request, _) => { urls.Add(request.RequestUri!.AbsoluteUri); return Task.FromResult(Models("model-a")); });
        var vm = Create(clients); vm.ModelsUrl = "https://catalog.example/custom";
        await vm.CompleteModelConfigurationAsync();
        vm.ModelsUrl = null; await vm.CompleteModelConfigurationAsync();
        Assert.Equal(new[] { "https://catalog.example/custom", "https://relay.example/v1/models" }, urls);
    }

    [Fact]
    public async Task 自动失败保留列表并提示代理URL且不弹窗随后可重试()
    {
        var calls = 0; var popups = 0;
        using var clients = Clients((_, _) => Task.FromResult(++calls == 1 ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : Models("恢复模型")));
        var vm = Create(clients, new() { DiscoveredModels = ["原模型"] });
        vm.ShowModelDiscoveryFailureAsync = _ => { popups++; return Task.CompletedTask; };
        await vm.CompleteModelConfigurationAsync();
        Assert.Equal("原模型", Assert.Single(vm.ModelCandidates)); Assert.Equal(0, popups);
        Assert.Equal(L.Text("SettingsModelAutoFetchFailed"), vm.ModelRefreshMessage);
        await vm.CompleteModelConfigurationAsync(force: true);
        Assert.Equal("恢复模型", Assert.Single(vm.ModelCandidates)); Assert.Equal(2, calls);
    }

    [Fact]
    public async Task 旧配置的迟到成功或失败不覆盖新配置结果()
    {
        foreach (var oldStatus in new[] { HttpStatusCode.OK, HttpStatusCode.Unauthorized })
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var oldResponse = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var clients = Clients((request, _) =>
            {
                if (request.RequestUri!.Host == "old.example") { entered.TrySetResult(); return oldResponse.Task; }
                return Task.FromResult(Models("新模型"));
            });
            var vm = Create(clients); vm.AnthropicBaseUrl = "https://old.example";
            var oldTask = vm.CompleteModelConfigurationAsync();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            vm.AnthropicBaseUrl = "https://new.example";
            await vm.CompleteModelConfigurationAsync();
            var message = vm.ModelRefreshMessage;
            oldResponse.SetResult(oldStatus == HttpStatusCode.OK ? Models("旧模型") : new(oldStatus));
            await oldTask;
            Assert.Equal("新模型", Assert.Single(vm.ModelCandidates));
            Assert.Equal(message, vm.ModelRefreshMessage); Assert.False(vm.IsRefreshingModels);
        }
    }

    [Fact]
    public async Task 输入新配置但还没完成时旧请求也不能更新列表()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldResponse = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var clients = Clients((_, _) => { entered.TrySetResult(); return oldResponse.Task; });
        var vm = Create(clients, new() { DiscoveredModels = ["原模型"] });
        var task = vm.CompleteModelConfigurationAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        vm.ApiKey = "正在编辑的新密钥";
        oldResponse.SetResult(Models("不应出现")); await task;
        Assert.Equal("原模型", Assert.Single(vm.ModelCandidates)); Assert.False(vm.IsRefreshingModels);
    }

    [Fact]
    public async Task 初始化仅在已有完整配置且列表为空时请求()
    {
        var calls = 0;
        using var clients = Clients((_, _) => { calls++; return Task.FromResult(Models("model-a")); });
        var vm = Create(clients, new() { DiscoveredModels = ["已有模型"] });
        await vm.EnsureModelCatalogAsync(); Assert.Equal(0, calls);
        vm.ModelCandidates.Clear(); await vm.EnsureModelCatalogAsync(); Assert.Equal(1, calls);
        await vm.EnsureModelCatalogAsync(); Assert.Equal(1, calls);
    }

    [Fact]
    public async Task 自定义代理未完成不请求完成后自动恢复()
    {
        var calls = 0;
        using var clients = Clients((_, _) => { calls++; return Task.FromResult(Models("代理模型")); });
        var vm = Create(clients); vm.SelectedProxyKind = ProxyKind.Custom; vm.ProxyHost = ""; vm.ProxyPortText = "";
        await vm.CompleteModelConfigurationAsync(); Assert.Equal(0, calls);
        vm.ProxyHost = "127.0.0.1"; vm.ProxyPortText = "not-a-port";
        await vm.CompleteModelConfigurationAsync(); Assert.Equal(0, calls);
        vm.ProxyPortText = "7890"; await vm.CompleteModelConfigurationAsync();
        Assert.Equal(1, calls); Assert.Equal("代理模型", Assert.Single(vm.ModelCandidates));
    }

    [Fact]
    public async Task 验证按钮即使对话验证失败也能获取模型且无需先选模型()
    {
        var catalogCalls = 0;
        using var clients = Clients((request, _) =>
        {
            if (request.Method == HttpMethod.Get) { catalogCalls++; return Task.FromResult(Models("首次模型")); }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        });
        var vm = Create(clients);
        await vm.ValidateCommand.ExecuteAsync(null);
        Assert.Equal(1, catalogCalls); Assert.Equal("首次模型", Assert.Single(vm.ModelCandidates));
        Assert.False(vm.IsApiKeyValid);
    }

    [Fact]
    public async Task 曾经成功的配置随后失败不能被成功缓存阻止再次重试()
    {
        var calls = 0;
        using var clients = Clients((_, _) => Task.FromResult(++calls == 2 ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : Models("model-a")));
        var vm = Create(clients);
        await vm.CompleteModelConfigurationAsync();
        await vm.CompleteModelConfigurationAsync(force: true);
        Assert.Equal(L.Text("SettingsModelAutoFetchFailed"), vm.ModelRefreshMessage);
        await vm.CompleteModelConfigurationAsync();
        Assert.Equal(L.Text("SettingsModelAutoFetchFailed"), vm.ModelRefreshMessage);
        await Task.Delay(550);
        await vm.CompleteModelConfigurationAsync();
        Assert.Equal(3, calls);
        Assert.Equal("model-a", Assert.Single(vm.ModelCandidates));
    }
    [Fact]
    public async Task 内部手动请求与自动提交重叠时最终加载标记正确结束()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var clients = Clients((_, _) => { Interlocked.Increment(ref calls); entered.TrySetResult(); return response.Task; });
        var vm = Create(clients);
        var manual = vm.RefreshModelsCommand.ExecuteAsync(null);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var automatic = vm.CompleteModelConfigurationAsync(force: true);
        response.SetResult(Models("共用模型"));
        await Task.WhenAll(manual, automatic);
        Assert.Equal(1, calls);
        Assert.False(vm.IsRefreshingModels);
    }
    [Fact]
    public async Task 思考等级和聊天选中项变化不触发重复目录请求()
    {
        var calls = 0;
        using var clients = Clients((_, _) => { calls++; return Task.FromResult(Models("model-a")); });
        var vm = Create(clients);
        await vm.CompleteModelConfigurationAsync();
        vm.SelectedThinkingLevel = ThinkingLevel.High; vm.ChatModelName = "model-a";
        await vm.CompleteModelConfigurationAsync(); Assert.Equal(1, calls);
    }
    private static SettingsViewModel Create(ModelHttpClientFactory clients, AppSettings? saved = null)
    {
        saved ??= new(); saved.EncryptedApiKey = [1]; saved.AnthropicBaseUrl = "https://relay.example"; saved.ApiFormat = "openai-responses";
        var settings = Substitute.For<ISettingsService>(); settings.LoadSettings().Returns(saved); settings.GetApiKey(saved).Returns("test-key");
        return new(settings, new ModelConnectionTester(new ModelClientFactory(clients)), new ThemeManager(settings), new ModelCatalogService(clients));
    }
    private static HttpResponseMessage Models(string id) => new(HttpStatusCode.OK) { Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new { data = new[] { new { id } } })) };
    private static ModelHttpClientFactory Clients(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) => new(() => new Handler(respond));
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => respond(request, token);
    }
}
