using System.Net;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Llm;
using TinyHarness;

namespace DiskPilotX.Core.Tests;

public sealed class ModelDiscoveryTests
{
    private static ModelEndpointConfig Config(string url = "https://relay.example", string format = "anthropic-messages", string? modelsUrl = null) =>
        ModelEndpointConfig.FromSettings(new AppSettings { AnthropicBaseUrl = url, ApiFormat = format, ModelsUrl = modelsUrl }, "测试密钥", "模型");

    [Theory]
    [InlineData("https://api.deepseek.com/anthropic", "anthropic-messages", "https://api.deepseek.com/models")]
    [InlineData("https://api.deepseek.com/anthropic/v1/messages", "anthropic-messages", "https://api.deepseek.com/models")]
    [InlineData("https://dashscope.aliyuncs.com", "openai-chat-completions", "https://dashscope.aliyuncs.com/compatible-mode/v1/models")]
    [InlineData("https://openrouter.ai", "openai-responses", "https://openrouter.ai/api/v1/models")]
    [InlineData("https://api.groq.com", "openai-chat-completions", "https://api.groq.com/openai/v1/models")]
    [InlineData("https://ark.cn-beijing.volces.com", "openai-responses", "https://ark.cn-beijing.volces.com/api/v3/models")]
    [InlineData("https://open.bigmodel.cn", "openai-chat-completions", "https://open.bigmodel.cn/api/paas/v4/models")]
    public void 服务商目录独立于对话协议(string url, string format, string expected) =>
        Assert.Equal(expected, ModelCatalogService.GetModelsUri(Config(url, format)).AbsoluteUri);

    [Theory]
    [InlineData("openai-responses")]
    [InlineData("openai-chat-completions")]
    public void OpenAI两种协议共用目录(string format) =>
        Assert.Equal("https://api.openai.com/v1/models", ModelCatalogService.GetModelsUri(Config("https://api.openai.com", format)).AbsoluteUri);

    [Fact]
    public async Task 路由不存在时尝试其他同源候选且成功即停止()
    {
        var seen = new List<string>();
        using var clients = new ModelHttpClientFactory(() => new Handler((request, _) =>
        {
            seen.Add(request.RequestUri!.AbsoluteUri);
            Assert.Equal("relay.example", request.RequestUri.Host);
            return Task.FromResult(seen.Count == 1 ? new HttpResponseMessage(HttpStatusCode.NotFound) : Json("{\"data\":[{\"id\":\"model-b\"},{\"id\":\"model-a\"},{\"id\":\"model-a\"}]}"));
        }));
        var models = await new ModelCatalogService(clients).FetchAsync(Config(), CancellationToken.None);
        Assert.Equal(2, seen.Count);
        Assert.Equal(new[] { "model-a", "model-b" }, models.Select(m => m.Id));
    }

    [Theory]
    [InlineData("<html>登录页面</html>")]
    [InlineData("{\"data\":[]}")]
    [InlineData("{\"error\":\"不是模型列表\"}")]
    public async Task 非目录响应继续尝试(string body)
    {
        var calls = 0;
        using var clients = new ModelHttpClientFactory(() => new Handler((_, _) => Task.FromResult(++calls == 1 ? Json(body) : Json("{\"models\":[{\"name\":\"model-ok\"}]}"))));
        Assert.Equal("model-ok", Assert.Single(await new ModelCatalogService(clients).FetchAsync(Config(), CancellationToken.None)).Id);
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(429)]
    public async Task 鉴权限流失败不盲目换地址(int status)
    {
        var calls = 0;
        using var clients = new ModelHttpClientFactory(() => new Handler((_, _) => { calls++; return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)); }));
        var ex = await Assert.ThrowsAsync<ModelClientException>(() => new ModelCatalogService(clients).FetchAsync(Config(), CancellationToken.None));
        Assert.Equal(status, ex.StatusCode);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task 全部候选耗尽且去重不跨源()
    {
        var seen = new HashSet<string>();
        using var clients = new ModelHttpClientFactory(() => new Handler((request, _) =>
        {
            Assert.Equal("relay.example", request.RequestUri!.Host);
            Assert.True(seen.Add(request.RequestUri.AbsoluteUri));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }));
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => new ModelCatalogService(clients).FetchAsync(Config("https://relay.example/tenant/anthropic/v1/messages"), CancellationToken.None));
        Assert.Equal("ModelCatalogExhaustedException", ex.GetType().Name);
        Assert.InRange(seen.Count, 2, 12);
        Assert.All(seen, url => Assert.Contains("/tenant/", url));
    }

    [Fact]
    public async Task 显式模型地址不被替换()
    {
        var seen = new List<string>();
        using var clients = new ModelHttpClientFactory(() => new Handler((request, _) => { seen.Add(request.RequestUri!.AbsoluteUri); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)); }));
        await Assert.ThrowsAnyAsync<Exception>(() => new ModelCatalogService(clients).FetchAsync(Config(modelsUrl: "https://custom.example/list"), CancellationToken.None));
        Assert.Equal(new[] { "https://custom.example/list" }, seen);
    }

    [Fact]
    public async Task 外部取消立即停止不转换为候选耗尽()
    {
        var calls = 0;
        using var clients = new ModelHttpClientFactory(() => new Handler(async (_, token) => { calls++; await Task.Delay(Timeout.Infinite, token); return Json("{}"); }));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ModelCatalogService(clients).FetchAsync(Config(), cancellation.Token));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void 自定义代理非法时不得悄悄直连()
    {
        using var clients = new ModelHttpClientFactory();
        Assert.Throws<InvalidOperationException>(() => clients.Create(ProxyKind.Custom, null, 7890, null, null));
        Assert.Throws<InvalidOperationException>(() => clients.Create(ProxyKind.Custom, "localhost", 70000, null, null));
    }

    [Fact]
    public async Task 单次超时可以换候选但总体超时明确停止()
    {
        var calls = 0;
        using var clients = new ModelHttpClientFactory(() => new Handler(async (_, token) =>
        {
            if (++calls == 1) await Task.Delay(Timeout.Infinite, token);
            return Json("{\"data\":[{\"id\":\"恢复模型\"}]}");
        }));
        var catalog = new ModelCatalogService(clients, requestTimeout: TimeSpan.FromMilliseconds(30), overallTimeout: TimeSpan.FromSeconds(2));
        Assert.Equal("恢复模型", Assert.Single(await catalog.FetchAsync(Config(), CancellationToken.None)).Id);
        Assert.Equal(2, calls);

        using var slowClients = new ModelHttpClientFactory(() => new Handler(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return Json("{}"); }));
        var timeout = await Assert.ThrowsAsync<ModelClientException>(() => new ModelCatalogService(slowClients,
            requestTimeout: TimeSpan.FromSeconds(2), overallTimeout: TimeSpan.FromMilliseconds(30)).FetchAsync(Config(), CancellationToken.None));
        Assert.Equal(ModelErrorKind.Timeout, timeout.Kind);
    }

    [Fact]
    public async Task 超大响应被拒绝后仍可换候选()
    {
        var calls = 0;
        using var clients = new ModelHttpClientFactory(() => new Handler((_, _) => Task.FromResult(Json(++calls == 1
            ? new string('x', 1024 * 1024 + 1) : "{\"data\":[{\"id\":\"安全模型\"}]}"))));
        Assert.Equal("安全模型", Assert.Single(await new ModelCatalogService(clients).FetchAsync(Config(), CancellationToken.None)).Id);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void 中转对话地址去掉端点但保留部署前缀()
    {
        var candidates = ModelCatalogEndpoints.GetCandidates(Config("https://relay.example/tenant/openai/v1/chat/completions", "openai-responses"));
        Assert.Equal("https://relay.example/tenant/openai/v1/models", candidates[0].AbsoluteUri);
        Assert.All(candidates, uri =>
        {
            Assert.StartsWith("/tenant/", uri.AbsolutePath);
            Assert.DoesNotContain("chat/completions", uri.AbsolutePath);
        });
        Assert.Equal(candidates.Count, candidates.Select(uri => uri.AbsoluteUri).Distinct().Count());
    }

    [Fact]
    public async Task 真实HTTP代理承载模型发现请求()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var server = ServeOnceAsync(listener, "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nConnection: close\r\n", "{\"data\":[{\"id\":\"代理模型\"}]}", timeout.Token);
        using var clients = new ModelHttpClientFactory();
        var config = Config("http://catalog.invalid") with { ApiKey = "test-key", ProxyMode = ProxyKind.Custom, ProxyHost = "127.0.0.1", ProxyPort = port };
        Assert.Equal("代理模型", Assert.Single(await new ModelCatalogService(clients).FetchAsync(config, timeout.Token)).Id);
        Assert.StartsWith("GET http://catalog.invalid/v1/models", await server);
    }

    [Fact]
    public async Task 目录重定向不得把凭据发到另一个地址()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        using var destination = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start(); destination.Start();
        var url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/models";
        var target = $"http://127.0.0.1:{((IPEndPoint)destination.LocalEndpoint).Port}/models";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var server = ServeOnceAsync(listener, $"HTTP/1.1 302 Found\r\nLocation: {target}\r\nConnection: close\r\n", "", timeout.Token);
        using var clients = new ModelHttpClientFactory();
        await Assert.ThrowsAsync<ModelCatalogExhaustedException>(() => new ModelCatalogService(clients).FetchAsync(Config(modelsUrl: url) with { ApiKey = "test-key", ProxyMode = ProxyKind.Direct }, timeout.Token));
        await server;
        Assert.False(destination.Pending());
    }

    private static async Task<string> ServeOnceAsync(System.Net.Sockets.TcpListener listener, string headers, string body, CancellationToken token)
    {
        using var client = await listener.AcceptTcpClientAsync(token);
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, leaveOpen: true);
        var first = await reader.ReadLineAsync(token);
        while (!string.IsNullOrEmpty(await reader.ReadLineAsync(token))) { }
        var bytes = System.Text.Encoding.UTF8.GetBytes(body);
        await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(headers + $"Content-Length: {bytes.Length}\r\n\r\n"), token);
        await stream.WriteAsync(bytes, token);
        return first ?? "";
    }

    [Fact]
    public async Task Gemini原生目录只用API密钥头而非伪造Bearer()
    {
        using var clients = new ModelHttpClientFactory(() => new Handler((request, _) =>
        {
            Assert.Null(request.Headers.Authorization);
            Assert.Equal("测试密钥", Assert.Single(request.Headers.GetValues("x-goog-api-key")));
            return Task.FromResult(Json("{\"models\":[{\"name\":\"models/gemini-test\"}]}"));
        }));
        Assert.Single(await new ModelCatalogService(clients).FetchAsync(Config("https://generativelanguage.googleapis.com"), CancellationToken.None));
    }
    [Fact]
    public void 日志脱敏不因短密钥破坏字段识别且覆盖Basic认证()
    {
        var config = Config() with { ApiKey = "key", ProxyPassword = "p@s s" };
        var text = ModelCatalogService.Sanitize(config, "{\"api_key\":\"different-secret\",\"proxy_password\":\"upstream-password\"} Proxy-Authorization: Basic YWJjOmRlZg== http://user:pass@relay.example/models?token=hidden p%40s%20s");
        foreach (var secret in new[] { "different-secret", "upstream-password", "YWJjOmRlZg==", "user:pass", "hidden", "p%40s%20s" })
            Assert.DoesNotContain(secret, text);
    }
    [Theory]
    [InlineData("{\"error\":\"prefix\\\"private-tail\"}", "private-tail")]
    [InlineData("{\"error\":\"prefix\\u0022private-tail\"}", "private-tail")]
    [InlineData("{\"error\":\"\\u0070refix\\\"private-tail\"}", "private-tail")]
    [InlineData("{\"password\":\"prefix\\\"upstream-private-tail\"}", "upstream-private-tail")]
    [InlineData("{\"pass\\u0077ord\":\"prefix\\\"upstream-private-tail\"}", "upstream-private-tail")]
    [InlineData("{\"password\":\"prefix\\\"upstream-private-tail", "upstream-private-tail")]
    public void JSON转义和敏感字段必须完整脱敏(string input, string marker)
    {
        var safe = ModelCatalogService.Sanitize(Config() with { ProxyPassword = "prefix\"private-tail" }, input);
        Assert.DoesNotContain(marker, safe);
    }
    [Theory]
    [InlineData("Bearer abc\"known-tail", "error", "known-tail")]
    [InlineData("Basic abc\"field-tail", "password", "field-tail")]
    [InlineData("Bearer token space-tail", "error", "space-tail")]
    public void 认证片段不能拆散凭据再留下尾部(string secret, string field, string marker)
    {
        var input = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string> { [field] = secret });
        var safe = ModelCatalogService.Sanitize(Config() with { ProxyPassword = secret }, input);
        Assert.DoesNotContain(marker, safe);
    }

    [Theory]
    [InlineData(1, false)] [InlineData(2, false)] [InlineData(3, false)] [InlineData(4, false)] [InlineData(8, false)] [InlineData(12, false)]
    [InlineData(1, true)] [InlineData(2, true)] [InlineData(3, true)] [InlineData(4, true)] [InlineData(8, true)] [InlineData(12, true)]
    public void 嵌套JSON字符串逐层遮蔽且到达上限不返回原文(int depth, bool relaxed)
    {
        var secret = "prefix\"nested-private-tail";
        var options = new System.Text.Json.JsonSerializerOptions();
        if (relaxed) options.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
        var input = secret;
        for (var i = 0; i < depth; i++) input = System.Text.Json.JsonSerializer.Serialize(new { error = input }, options);
        var safe = ModelCatalogService.Sanitize(Config() with { ProxyPassword = secret }, input);
        Assert.DoesNotContain("nested-private-tail", safe);
        Assert.NotEmpty(safe);
    }
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
}
