using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using DiskPilotX.App.Localization;
using DiskPilotX.App.Services;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Llm;
using DiskPilotX.Core.Services.Settings;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TinyHarness;

namespace DiskPilotX.App.Tests;

public sealed class ConnectionValidationTests
{
    [Theory]
    [InlineData(null, "gpt-a")]
    [InlineData("旧接口模型", "gpt-a")]
    [InlineData("gpt-b", "gpt-b")]
    public async Task 从本次真实目录选测试模型且不改聊天选择(string? selected, string expected)
    {
        using var f = new Fixture(selected);
        var profiles = JsonSerializer.Serialize(f.Saved.ModelThinkingConfigurations);
        await f.Vm.ValidateCommand.ExecuteAsync(null);
        var body = Assert.Single(f.Posts);
        Assert.Equal(expected, body.GetProperty("model").GetString());
        Assert.Equal(selected, f.Saved.ChatModelName);
        Assert.Equal(profiles, JsonSerializer.Serialize(f.Saved.ModelThinkingConfigurations));
        Assert.Equal(selected, f.Vm.ChatModelName);
        Assert.True(f.Vm.IsApiKeyValid);
        Assert.Contains(expected, f.Vm.ValidationStatusText);
        Assert.Equal(new[] { "GET", "POST" }, f.Order);
    }

    [Fact]
    public async Task 目录未完成之前不使用默认模型发生成请求()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var f = new Fixture(null) { Get = () => { entered.TrySetResult(); return gate.Task; } };
        var task = f.Vm.ValidateCommand.ExecuteAsync(null);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(f.Posts);
        gate.SetResult(Models("gpt-a", "gpt-b")); await task;
        Assert.Equal("gpt-a", Assert.Single(f.Posts).GetProperty("model").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 目录失败或为空不能借缓存及隐藏默认模型继续(bool empty)
    {
        using var f = new Fixture("旧模型") { Get = () => Task.FromResult(empty ? Models() : Json(HttpStatusCode.BadGateway, "{}")) };
        await f.Vm.ValidateCommand.ExecuteAsync(null);
        Assert.Empty(f.Posts);
        Assert.False(f.Vm.IsApiKeyValid);
        Assert.NotEqual(ValidationState.Invalid, f.Vm.ValidationState);
        Assert.Equal("旧模型", f.Saved.ChatModelName);
    }

    [Theory]
    [InlineData(401, ModelErrorKind.Authentication)]
    [InlineData(403, ModelErrorKind.PermissionDenied)]
    [InlineData(404, ModelErrorKind.ModelNotFound)]
    [InlineData(400, ModelErrorKind.InvalidRequest)]
    [InlineData(422, ModelErrorKind.InvalidRequest)]
    [InlineData(429, ModelErrorKind.RateLimited)]
    [InlineData(402, ModelErrorKind.InsufficientQuota)]
    [InlineData(500, ModelErrorKind.ServerError)]
    public async Task 生成失败明确分类并保留目录成功及结构化脱敏诊断(int status, ModelErrorKind kind)
    {
        using var f = new Fixture("gpt-b")
        {
            Post = () => Task.FromResult(Json((HttpStatusCode)status, JsonSerializer.Serialize(new { error = new { code = "fixture_error", message = "fixture detail " + Fixture.Secret + " " + Fixture.ProxySecret } })))
        };
        await f.Vm.ValidateCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "gpt-a", "gpt-b" }, f.Vm.ModelCandidates);
        Assert.Contains("gpt-b", f.Vm.ValidationStatusText);
        Assert.Contains(status.ToString(), f.Vm.ValidationStatusText);
        Assert.Contains(L.Text("SettingsConnectionError" + kind), f.Vm.ValidationStatusText);
        Assert.Equal(kind == ModelErrorKind.Authentication, f.Vm.ValidationState == ValidationState.Invalid);
        Assert.False(f.Vm.IsApiKeyValid);
        var log = Assert.Single(f.Logger.Rows.Where(r => r.TryGetValue("Operation", out var op) && Equals(op, "模型连接验证失败")));
        Assert.Equal(status, log["StatusCode"]);
        Assert.Equal(kind, log["Kind"]);
        Assert.Equal("fixture_error", log["ErrorCode"]);
        Assert.Equal("gpt-b", log["Model"]);
        Assert.Contains("fixture detail", log["UpstreamMessage"]?.ToString());
        Assert.Contains("/v1/responses", log["Endpoint"]?.ToString());
        var text = JsonSerializer.Serialize(f.Logger.Rows) + f.Vm.ValidationStatusText;
        Assert.DoesNotContain(Fixture.Secret, text);
        Assert.DoesNotContain(Fixture.ProxySecret, text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 网络和超时不误报密钥无效(bool timeout)
    {
        using var f = new Fixture("gpt-a") { Post = () => throw (timeout ? new TaskCanceledException() : new HttpRequestException()) };
        await f.Vm.ValidateCommand.ExecuteAsync(null);
        Assert.NotEqual(ValidationState.Invalid, f.Vm.ValidationState);
        Assert.Contains(L.Text("SettingsConnectionError" + (timeout ? ModelErrorKind.Timeout : ModelErrorKind.ConnectionFailed)), f.Vm.ValidationStatusText);
        Assert.Equal(timeout ? ModelErrorKind.Timeout : ModelErrorKind.ConnectionFailed, f.Logger.Rows.Last()["Kind"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 目录等待期间改变配置或模型不发送旧验证(bool changeModel)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var f = new Fixture("gpt-a") { Get = () => { entered.TrySetResult(); return gate.Task; } };
        var task = f.Vm.ValidateCommand.ExecuteAsync(null); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (changeModel) f.Vm.ChatModelName = "gpt-b"; else f.Vm.AnthropicBaseUrl = "https://other.example/v1";
        gate.SetResult(Models("gpt-a", "gpt-b")); await task;
        Assert.Empty(f.Posts); Assert.False(f.Vm.IsApiKeyValid);
        Assert.Equal(ValidationState.None, f.Vm.ValidationState);
    }

    [Fact]
    public async Task 生成等待期间改变模型不会把旧成功写回新配置()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var f = new Fixture("gpt-a") { Post = () => { entered.TrySetResult(); return gate.Task; } };
        var task = f.Vm.ValidateCommand.ExecuteAsync(null); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("gpt-a", f.Vm.ValidationStatusText);
        f.Vm.ChatModelName = "gpt-b"; gate.SetResult(Completed()); await task;
        Assert.False(f.Vm.IsApiKeyValid); Assert.Equal(ValidationState.None, f.Vm.ValidationState);
        Assert.Equal("gpt-b", f.Saved.ChatModelName);
    }

    [Fact]
    public async Task 不可信模型名和上游地址在显示及日志均脱敏()
    {
        using var f = new Fixture(null)
        {
            Get = () => Task.FromResult(Models("模型-" + Fixture.Secret)),
            Post = () => Task.FromResult(Json(HttpStatusCode.BadRequest, JsonSerializer.Serialize(new { error = new { message = "https://user:pwd@example.com/error?token=private-query" } })))
        };
        await f.Vm.ValidateCommand.ExecuteAsync(null);
        var text = JsonSerializer.Serialize(f.Logger.Rows) + f.Vm.ValidationStatusText;
        Assert.DoesNotContain(Fixture.Secret, text);
        Assert.DoesNotContain("private-query", text); Assert.DoesNotContain("user:pwd", text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 聊天区实际存储选择变化也撤销在途验证(bool duringGeneration)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var f = new Fixture("gpt-a");
        if (duringGeneration) f.Post = () => { entered.TrySetResult(); return gate.Task; };
        else f.Get = () => { entered.TrySetResult(); return gate.Task; };
        var task = f.Vm.ValidateCommand.ExecuteAsync(null); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        f.Saved.ChatModelName = "gpt-b"; // 聊天 VM 直接保存，不经过设置 VM 的属性回调。
        gate.SetResult(duringGeneration ? Completed() : Models("gpt-a", "gpt-b")); await task;
        if (!duringGeneration) Assert.Empty(f.Posts);
        Assert.False(f.Saved.IsApiKeyValid); Assert.Equal(ValidationState.None, f.Vm.ValidationState);
    }

    [Fact]
    public async Task 设置窗口未同步但聊天实际选择已改变时验证实际选择()
    {
        using var f = new Fixture("gpt-a"); f.Saved.ChatModelName = "gpt-b";
        await f.Vm.ValidateCommand.ExecuteAsync(null);
        Assert.Equal("gpt-b", Assert.Single(f.Posts).GetProperty("model").GetString());
        Assert.Equal("gpt-b", f.Saved.ChatModelName);
    }

    [Fact]
    public async Task 重开设置不清除已保存的成功状态()
    {
        using var f = new Fixture("gpt-a"); await f.Vm.ValidateCommand.ExecuteAsync(null);
        Assert.True(f.Saved.IsApiKeyValid); f.Vm.LoadSettings();
        Assert.True(f.Saved.IsApiKeyValid); Assert.True(f.Vm.IsApiKeyValid);
        Assert.Equal(ValidationState.Valid, f.Vm.ValidationState);
    }

    [Theory]
    [InlineData(401, ModelErrorKind.Authentication)]
    [InlineData(403, ModelErrorKind.PermissionDenied)]
    [InlineData(429, ModelErrorKind.RateLimited)]
    [InlineData(500, ModelErrorKind.ServerError)]
    public async Task 目录失败保留HTTP分类且日志使用实际自定义目录地址(int status, ModelErrorKind kind)
    {
        using var f = new Fixture("gpt-a") { Get = () => Task.FromResult(Json((HttpStatusCode)status,
            JsonSerializer.Serialize(new { error = new { code = "fixture_error", message = "catalogue detail " + Fixture.Secret } }))) };
        f.Vm.ModelsUrl = "https://relay.example/custom/catalogue?token=private-query";
        await f.Vm.ValidateCommand.ExecuteAsync(null);
        Assert.Empty(f.Posts);
        Assert.Contains(status.ToString(), f.Vm.ValidationStatusText);
        Assert.Contains(L.Text("SettingsConnectionError" + kind), f.Vm.ValidationStatusText);
        var row = f.Logger.Rows.Last(r => r.ContainsKey("Endpoint"));
        Assert.Equal(status, row["StatusCode"]); Assert.Equal(kind, row["Kind"]);
        Assert.Contains("/custom/catalogue", row["Endpoint"]?.ToString());
        Assert.DoesNotContain("/responses", row["Endpoint"]?.ToString());
        Assert.DoesNotContain("private-query", JsonSerializer.Serialize(f.Logger.Rows));
        Assert.DoesNotContain(Fixture.Secret, f.Vm.ValidationStatusText);
    }

    [Fact]
    public async Task 测试模型使用自己的思考配置且不改旧聊天配置()
    {
        using var f = new Fixture("旧模型");
        f.Saved.ModelThinkingConfigurations[ThinkingCapabilities.GetProfileKey(f.Saved, "gpt-a")] = new ModelThinkingSettings { SelectedLevel = "high" };
        var before = JsonSerializer.Serialize(f.Saved.ModelThinkingConfigurations);
        await f.Vm.ValidateCommand.ExecuteAsync(null);
        var body = Assert.Single(f.Posts);
        Assert.Equal("gpt-a", body.GetProperty("model").GetString()); Assert.Equal("high", body.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.Equal(before, JsonSerializer.Serialize(f.Saved.ModelThinkingConfigurations)); Assert.Equal("旧模型", f.Saved.ChatModelName);
    }

    [Fact]
    public async Task 目录网络失败在界面和日志使用相同类别()
    {
        using var f = new Fixture(null) { Get = () => throw new HttpRequestException("网络夹具拒绝连接") };
        await f.Vm.ValidateCommand.ExecuteAsync(null);
        Assert.Empty(f.Posts);
        Assert.Contains(L.Text("SettingsConnectionErrorConnectionFailed"), f.Vm.ValidationStatusText);
        Assert.Equal(ModelErrorKind.ConnectionFailed, f.Logger.Rows.Last()["Kind"]);
    }
    [Fact]
    public async Task 在途验证存储读取持续失败时不抛异常也不残留验证中()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var f = new Fixture("gpt-a") { Post = () => { entered.TrySetResult(); return gate.Task; } };
        var task = f.Vm.ValidateCommand.ExecuteAsync(null); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var unavailable = true;
        f.Settings.LoadSettings().Returns(_ => unavailable ? throw new IOException("配置读取夹具失败") : f.Saved);
        try
        {
            gate.SetResult(Completed());
            Assert.Null(await Record.ExceptionAsync(() => task));
            Assert.NotEqual(ValidationState.Validating, f.Vm.ValidationState); Assert.False(f.Vm.IsApiKeyValid);
        }
        finally { unavailable = false; } // VM 订阅语言事件；故障注入必须恢复，不能污染后续本地化回归。
    }
    [Fact]
    public async Task 目录格式异常重包装后日志仍使用实际目录端点()
    {
        using var f = new Fixture(null) { Get = () => throw new FormatException("格式夹具") };
        f.Vm.ModelsUrl = "https://relay.example/custom/catalogue";
        await f.Vm.ValidateCommand.ExecuteAsync(null);
        var row = f.Logger.Rows.Last(); Assert.Equal(ModelErrorKind.InvalidRequest, row["Kind"]);
        Assert.Contains("/custom/catalogue", row["Endpoint"]?.ToString());
    }
    [Fact]
    public async Task 目录总体超时重包装后保留实际端点()
    {
        using var f = new Fixture(null, TimeSpan.FromMilliseconds(1)) { Get = async () => { await Task.Delay(80); return Models("gpt-a"); } };
        f.Vm.ModelsUrl = "https://relay.example/custom/catalogue";
        await f.Vm.ValidateCommand.ExecuteAsync(null);
        Assert.Empty(f.Posts);
        var row = f.Logger.Rows.Last(); Assert.Equal(ModelErrorKind.Timeout, row["Kind"]);
        Assert.Contains("/custom/catalogue", row["Endpoint"]?.ToString());
    }
    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Models(params string[] ids) => Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { data = ids.Select(id => new { id }) }));
    private static HttpResponseMessage Completed() => new(HttpStatusCode.OK) { Content = new StringContent("data: {\"type\":\"response.completed\"}\n\n", Encoding.UTF8, "text/event-stream") };
    private sealed class Fixture : IDisposable
    {
        public const string Secret = "fixture-secret-not-real";
        public const string ProxySecret = "fixture-proxy-not-real";
        public AppSettings Saved { get; }
        public SettingsViewModel Vm { get; }
        public List<JsonElement> Posts { get; } = [];
        public List<string> Order { get; } = [];
        public RecordingLogger Logger { get; } = new();
        public ISettingsService Settings { get; private set; } = null!;
        public Func<Task<HttpResponseMessage>> Get { get; set; } = () => Task.FromResult(Models("gpt-a", "gpt-b"));
        public Func<Task<HttpResponseMessage>> Post { get; set; } = () => Task.FromResult(Completed());
        private readonly ModelHttpClientFactory clients;
        public Fixture(string? model, TimeSpan? budget = null)
        {
            Saved = new AppSettings { EncryptedApiKey = [1], AnthropicBaseUrl = "https://relay.example/v1", ApiFormat = "openai-responses", ChatModelName = model, DiscoveredModels = ["缓存模型"], ProxyMode = "direct" };
            var settings = Settings = Substitute.For<ISettingsService>(); settings.LoadSettings().Returns(Saved); settings.GetApiKey(Saved).Returns(Secret); settings.GetProxyPassword(Saved).Returns(ProxySecret);
            clients = new ModelHttpClientFactory(() => new Handler(async (r, _) =>
            {
                Order.Add(r.Method.Method);
                if (r.Method == HttpMethod.Get) return await Get();
                Posts.Add(JsonDocument.Parse(await r.Content!.ReadAsStringAsync()).RootElement.Clone()); return await Post();
            }));
            Vm = new SettingsViewModel(settings, new ModelConnectionTester(new ModelClientFactory(clients)), new ThemeManager(settings), new ModelCatalogService(clients, overallTimeout: budget), logger: Logger);
        }
        public void Dispose() => clients.Dispose();
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => send(r, ct); }
    private sealed class RecordingLogger : ILogger<SettingsViewModel>
    {
        public List<Dictionary<string, object?>> Rows { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter)
        { if (state is IEnumerable<KeyValuePair<string, object?>> fields) Rows.Add(fields.ToDictionary(p => p.Key, p => p.Value)); }
    }
}
