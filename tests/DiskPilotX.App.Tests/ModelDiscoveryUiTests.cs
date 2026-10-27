using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Serilog;
using Serilog.Extensions.Logging;
using DiskPilotX.App.Logging;
using DiskPilotX.App.Services;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Llm;
using DiskPilotX.Core.Services.Settings;

namespace DiskPilotX.App.Tests;

public sealed class ModelDiscoveryUiTests
{
    [Fact]
    public async Task 候选耗尽弹窗提示自定义连接并保留原列表()
    {
        var saved = new AppSettings { EncryptedApiKey = [1], DiscoveredModels = ["原模型"] };
        var settings = Substitute.For<ISettingsService>();
        settings.LoadSettings().Returns(saved);
        settings.GetApiKey(saved).Returns("测试密钥");
        using var clients = new ModelHttpClientFactory(() => new Handler(_ => new(HttpStatusCode.NotFound)));
        var vm = new SettingsViewModel(settings, new ModelConnectionTester(new ModelClientFactory(clients)), new ThemeManager(settings), new ModelCatalogService(clients));
        var messages = new List<string>();
        vm.ShowModelDiscoveryFailureAsync = message => { messages.Add(message); return Task.CompletedTask; };
        await vm.RefreshModelsCommand.ExecuteAsync(null);
        Assert.Contains("URL", Assert.Single(messages));
        Assert.Equal("原模型", Assert.Single(vm.ModelCandidates));
        Assert.False(vm.IsRefreshingModels);
    }

    [Fact]
    public async Task 发现使用当前编辑的地址与代理而不是旧持久化快照()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.LoadSettings().Returns(_ => new AppSettings { EncryptedApiKey = [1], AnthropicBaseUrl = "https://old.example" });
        settings.GetApiKey(Arg.Any<AppSettings>()).Returns("测试密钥");
        using var clients = new ModelHttpClientFactory(() => new Handler(request =>
        {
            Assert.Equal("new.example", request.RequestUri!.Host);
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"data\":[{\"id\":\"新模型\"}]}") };
        }));
        var vm = new SettingsViewModel(settings, new ModelConnectionTester(new ModelClientFactory(clients)), new ThemeManager(settings), new ModelCatalogService(clients));
        vm.AnthropicBaseUrl = "https://new.example";
        vm.SelectedProxyKind = ProxyKind.Custom;
        vm.ProxyHost = "127.0.0.1";
        vm.ProxyPortText = "7890";
        vm.ProxyUsername = "代理用户";
        vm.ProxyPassword = "代理口令";
        await vm.RefreshModelsCommand.ExecuteAsync(null);
        Assert.Equal("新模型", Assert.Single(vm.ModelCandidates));
    }

    [Fact]
    public async Task 无效代理不发请求也不误报候选耗尽()
    {
        var saved = new AppSettings { EncryptedApiKey = [1], ProxyMode = "custom", ProxyHost = "localhost", ProxyPort = 7890 };
        var settings = Substitute.For<ISettingsService>(); settings.LoadSettings().Returns(saved); settings.GetApiKey(saved).Returns("测试密钥");
        var calls = 0;
        using var clients = new ModelHttpClientFactory(() => new Handler(_ => { calls++; return new(HttpStatusCode.OK); }));
        var vm = new SettingsViewModel(settings, new ModelConnectionTester(new ModelClientFactory(clients)), new ThemeManager(settings), new ModelCatalogService(clients));
        vm.ProxyPortText = "不是端口";
        var popup = false;
        vm.ShowModelDiscoveryFailureAsync = _ => { popup = true; return Task.CompletedTask; };
        await vm.RefreshModelsCommand.ExecuteAsync(null);
        Assert.Equal(0, calls);
        Assert.False(popup);
        Assert.Contains("代理", vm.ModelRefreshMessage);
    }

    [Fact]
    public async Task 失败诊断在控制台和文件中双写且凭据脱敏()
    {
        var directory = Path.Combine(Path.GetTempPath(), "DiskPilot-X-model-discovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "验证.log");
        var sink = new UiLogSink();
        using (var serilog = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).WriteTo.File(file).CreateLogger())
        using (var loggers = new SerilogLoggerFactory(serilog, dispose: false))
        {
            var saved = new AppSettings { EncryptedApiKey = [1], AnthropicBaseUrl = "https://relay.example", ModelsUrl = "https://relay.example/models?token=查询密钥" };
            var settings = Substitute.For<ISettingsService>(); settings.LoadSettings().Returns(saved); settings.GetApiKey(saved).Returns("我的测试密钥");
            using var clients = new ModelHttpClientFactory(() => new Handler(_ => new(HttpStatusCode.Unauthorized)
            { Content = new StringContent("{\"error\":\"我的测试密钥\",\"password\":\"不可记录口令\"}") }));
            var catalog = new ModelCatalogService(clients, loggers.CreateLogger<ModelCatalogService>());
            var vm = new SettingsViewModel(settings, new ModelConnectionTester(new ModelClientFactory(clients)), new ThemeManager(settings), catalog,
                logger: loggers.CreateLogger<SettingsViewModel>());
            await vm.RefreshModelsCommand.ExecuteAsync(null);
            Assert.Contains("401", vm.ModelRefreshMessage);
        }
        var disk = await File.ReadAllTextAsync(file);
        var console = string.Join("\n", sink.Entries.Select(item => item.DisplayText));
        foreach (var text in new[] { disk, console })
        {
            Assert.Contains("401", text);
            Assert.Contains("模型目录尝试", text);
            Assert.Contains("模型列表获取失败", text);
            Assert.DoesNotContain("我的测试密钥", text);
            Assert.DoesNotContain("查询密钥", text);
            Assert.DoesNotContain("不可记录口令", text);
        }
    }

    [Fact]
    public async Task 重新载入设置后恢复已保存的代理口令()
    {
        var saved = new AppSettings { EncryptedApiKey = [1], ProxyMode = "custom", ProxyHost = "localhost", ProxyPort = 7890, ProxyUsername = "user" };
        var settings = Substitute.For<ISettingsService>(); settings.LoadSettings().Returns(saved); settings.GetApiKey(saved).Returns("key");
        settings.GetProxyPassword(saved).Returns("saved-password");
        var handlers = 0;
        using var clients = new ModelHttpClientFactory(() => { handlers++; return new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent("{\"data\":[{\"id\":\"模型\"}]}") }); });
        clients.Create(ProxyKind.Custom, "localhost", 7890, "user", "saved-password", allowAutoRedirect: false);
        var vm = new SettingsViewModel(settings, new ModelConnectionTester(new ModelClientFactory(clients)), new ThemeManager(settings), new ModelCatalogService(clients));
        vm.ProxyPassword = "edited-password";
        vm.LoadSettings();
        await vm.RefreshModelsCommand.ExecuteAsync(null);
        Assert.Equal(1, handlers);
        Assert.Equal("模型", Assert.Single(vm.ModelCandidates));
    }
    [Theory]
    [InlineData("{\"error\":\"prefix\\\"private-tail\",\"password\":\"prefix\\\"upstream-tail\"}")]
    [InlineData("{\"error\":\"prefix\\u0022private-tail\",\"pass\\u0077ord\":\"upstream-tail\"}")]
    [InlineData("{\"error\":\"prefix\\\"private-tail")]
    public async Task JSON转义凭据在控制台和落盘摘要中都不能残留(string body)
    {
        var directory = Path.Combine(Path.GetTempPath(), "DiskPilot-X-json-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "验证.log");
        var sink = new UiLogSink();
        using (var serilog = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).WriteTo.File(file).CreateLogger())
        using (var loggers = new SerilogLoggerFactory(serilog, dispose: false))
        using (var clients = new ModelHttpClientFactory(() => new Handler(_ => new(HttpStatusCode.Unauthorized) { Content = new StringContent(body) })))
        {
            var config = ModelEndpointConfig.FromSettings(new AppSettings { AnthropicBaseUrl = "https://relay.example" }, "test-key", "model") with { ProxyPassword = "prefix\"private-tail" };
            await Assert.ThrowsAsync<TinyHarness.ModelClientException>(() => new ModelCatalogService(clients, loggers.CreateLogger<ModelCatalogService>()).FetchAsync(config, CancellationToken.None));
        }
        foreach (var text in new[] { await File.ReadAllTextAsync(file), string.Join("\n", sink.Entries.Select(item => item.DisplayText)) })
        {
            Assert.Contains("401", text);
            Assert.DoesNotContain("private-tail", text);
            Assert.DoesNotContain("upstream-tail", text);
        }
    }
    [Fact]
    public async Task 嵌套与认证组合响应在双写日志中均无敏感尾部()
    {
        var directory = Path.Combine(Path.GetTempPath(), "DiskPilot-X-nested-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "验证.log");
        var sink = new UiLogSink();
        using (var serilog = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).WriteTo.File(file).CreateLogger())
        using (var loggers = new SerilogLoggerFactory(serilog, dispose: false))
        {
            foreach (var (secret, depth) in new[] { ("Bearer abc\"known-tail", 0), ("prefix\"nested-private-tail", 3), ("Bearer token space-tail", 2) })
            {
                var body = System.Text.Json.JsonSerializer.Serialize(new { error = secret, password = "Basic abc\"field-tail" });
                for (var i = 0; i < depth; i++) body = System.Text.Json.JsonSerializer.Serialize(new { error = body });
                using var clients = new ModelHttpClientFactory(() => new Handler(_ => new(HttpStatusCode.Unauthorized) { Content = new StringContent(body) }));
                var config = ModelEndpointConfig.FromSettings(new AppSettings { AnthropicBaseUrl = "https://relay.example" }, "test-key", "model") with { ProxyPassword = secret };
                await Assert.ThrowsAsync<TinyHarness.ModelClientException>(() => new ModelCatalogService(clients, loggers.CreateLogger<ModelCatalogService>()).FetchAsync(config, CancellationToken.None));
            }
        }
        foreach (var text in new[] { await File.ReadAllTextAsync(file), string.Join("\n", sink.Entries.Select(item => item.DisplayText)) })
        {
            Assert.Contains("401", text);
            foreach (var marker in new[] { "known-tail", "nested-private-tail", "space-tail", "field-tail" }) Assert.DoesNotContain(marker, text);
        }
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(send(request));
    }
}
