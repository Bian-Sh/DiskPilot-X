using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TinyHarness;
using SpaceMonger.Core.Services.Llm;
using Message = TinyHarness.ChatMessage;

namespace SpaceMonger.Core.Tests;

// 与 AnthropicModelClientTests 共用串行集合：转储测试依赖进程级静态目录配置。
[Collection("ModelClientSequential")]
public sealed class OpenAiModelClientTests
{
    private const string Key = "tiny-secret-key";
    private static readonly ModelRequest Request = new([Message.System("规则"), Message.User("列出文件")], []);

    [Fact]
    public async Task ChatCompletions流式解析正文思考与工具调用()
    {
        var bodies = new List<JsonElement>();
        using var http = new HttpClient(new FakeHandler(async request =>
        {
            Assert.Equal("https://relay.example/v1/chat/completions", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal(Key, request.Headers.Authorization?.Parameter);
            bodies.Add(JsonDocument.Parse(await request.Content!.ReadAsStringAsync()).RootElement.Clone());
            return Response(
                "data: {\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"reasoning_content\":\"思考中\"}}]}\n\n" +
                "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"你好\"}}]}\n\n" +
                "data: {\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call-1\",\"type\":\"function\",\"function\":{\"name\":\"list_files\",\"arguments\":\"\"}}]}}]}\n\n" +
                "data: {\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"{\\\"path\\\":\\\"C:/\\\"}\"}}]}}]}\n\n" +
                "data: {\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"tool_calls\"}]}\n\n" +
                "data: [DONE]\n\n");
        }));
        var client = ChatClient(http, ThinkingLevel.Low);
        var events = await Collect(client, Request);
        Assert.Equal("思考中", Assert.Single(events.OfType<ReasoningDeltaEvent>()).Text);
        Assert.Equal("你好", Assert.Single(events.OfType<TextDeltaEvent>()).Text);
        var tool = Assert.Single(events.OfType<ModelToolCallEvent>()).ToolCall;
        Assert.Equal(new ToolCall("call-1", "list_files", "{\"path\":\"C:/\"}"), tool);
        Assert.Equal("tool_use", events.OfType<ModelCompletedEvent>().Single().FinishReason);
        Assert.Equal("low", bodies[0].GetProperty("reasoning_effort").GetString());
        Assert.Equal("规则", bodies[0].GetProperty("messages")[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task ChatCompletions关闭思考时不发送effort字段()
    {
        var bodies = new List<JsonElement>();
        using var http = new HttpClient(new FakeHandler(async request =>
        {
            bodies.Add(JsonDocument.Parse(await request.Content!.ReadAsStringAsync()).RootElement.Clone());
            return Response("data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"好\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n");
        }));
        await Collect(ChatClient(http), Request);
        Assert.False(bodies[0].TryGetProperty("reasoning_effort", out _));
        Assert.Equal(4096, bodies[0].GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public async Task ChatCompletions非流式JSON也能完成()
    {
        const string json = "{\"choices\":[{\"message\":{\"reasoning_content\":\"想\",\"content\":\"答\",\"tool_calls\":null},\"finish_reason\":\"stop\"}]}";
        using var http = new HttpClient(new FakeHandler(_ => JsonHttp(json)));
        var events = await Collect(ChatClient(http), Request);
        Assert.Equal("想", Assert.Single(events.OfType<ReasoningDeltaEvent>()).Text);
        Assert.Equal("答", Assert.Single(events.OfType<TextDeltaEvent>()).Text);
        Assert.Equal("end_turn", Assert.Single(events.OfType<ModelCompletedEvent>()).FinishReason);
    }

    [Fact]
    public async Task ChatCompletions多轮保留工具调用与结果()
    {
        var bodies = new List<JsonElement>();
        using var http = new HttpClient(new FakeHandler(async request =>
        {
            bodies.Add(JsonDocument.Parse(await request.Content!.ReadAsStringAsync()).RootElement.Clone());
            return Response("data: {\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n");
        }));
        var client = ChatClient(http);
        var call = new ToolCall("call-1", "list_files", "{\"path\":\"C:/\"}");
        await Collect(client, new([Message.System("规则"), Message.User("开始"), Message.AssistantToolCalls([call], "查询中"), Message.ToolResult(call, "结果")], []));
        var messages = bodies[0].GetProperty("messages");
        Assert.Equal(4, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        var assistant = messages[2];
        Assert.Equal("查询中", assistant.GetProperty("content").GetString());
        Assert.Equal("call-1", assistant.GetProperty("tool_calls")[0].GetProperty("id").GetString());
        Assert.Equal("list_files", assistant.GetProperty("tool_calls")[0].GetProperty("function").GetProperty("name").GetString());
        var tool = messages[3];
        Assert.Equal("tool", tool.GetProperty("role").GetString());
        Assert.Equal("call-1", tool.GetProperty("tool_call_id").GetString());
    }

    [Fact]
    public async Task Responses流式解析并在下一轮回传加密思考()
    {
        var bodies = new List<JsonElement>();
        var first =
            "data: {\"type\":\"response.output_item.added\",\"output_index\":0,\"item\":{\"type\":\"reasoning\",\"id\":\"rs_1\"}}\n\n" +
            "data: {\"type\":\"response.reasoning_text.delta\",\"delta\":\"先想\"}\n\n" +
            "data: {\"type\":\"response.output_item.done\",\"output_index\":0,\"item\":{\"type\":\"reasoning\",\"id\":\"rs_1\",\"encrypted_content\":\"ENC\"}}\n\n" +
            "data: {\"type\":\"response.output_item.added\",\"output_index\":1,\"item\":{\"type\":\"function_call\",\"call_id\":\"call-1\",\"name\":\"list_files\"}}\n\n" +
            "data: {\"type\":\"response.function_call_arguments.delta\",\"output_index\":1,\"delta\":\"{\\\"path\\\":\\\"C:/\\\"}\"}\n\n" +
            "data: {\"type\":\"response.output_item.done\",\"output_index\":1,\"item\":{\"type\":\"function_call\",\"call_id\":\"call-1\",\"name\":\"list_files\",\"arguments\":\"{}\"}}\n\n" +
            "data: {\"type\":\"response.completed\"}\n\n";
        using var http = new HttpClient(new FakeHandler(async request =>
        {
            Assert.Equal("https://relay.example/v1/responses", request.RequestUri!.AbsoluteUri);
            bodies.Add(JsonDocument.Parse(await request.Content!.ReadAsStringAsync()).RootElement.Clone());
            return Response(bodies.Count == 1 ? first : "data: {\"type\":\"response.completed\"}\n\n");
        }));
        var client = ResponsesClient(http, ThinkingLevel.Medium);
        var events = await Collect(client, Request);
        var call = Assert.Single(events.OfType<ModelToolCallEvent>()).ToolCall;
        Assert.Equal(new ToolCall("call-1", "list_files", "{\"path\":\"C:/\"}"), call);
        Assert.Equal("先想", Assert.Single(events.OfType<ReasoningDeltaEvent>()).Text);
        await Collect(client, new([Message.System("规则"), Message.User("开始"), Message.AssistantToolCalls([call]), Message.ToolResult(call, "结果")], []));
        // 第二次请求：instructions 抽出 system，reasoning 加密块紧邻其后的 function_call 回传。
        Assert.Equal("规则", bodies[1].GetProperty("instructions").GetString());
        var input = bodies[1].GetProperty("input");
        Assert.Equal("message", input[0].GetProperty("type").GetString());
        Assert.Equal("reasoning", input[1].GetProperty("type").GetString());
        Assert.Equal("rs_1", input[1].GetProperty("id").GetString());
        Assert.Equal("ENC", input[1].GetProperty("encrypted_content").GetString());
        Assert.Equal("function_call", input[2].GetProperty("type").GetString());
        Assert.Equal("function_call_output", input[3].GetProperty("type").GetString());
        Assert.Equal("medium", bodies[0].GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.True(bodies[0].GetProperty("include")[0].GetString()!.Contains("reasoning"));
        Assert.False(bodies[0].GetProperty("store").GetBoolean());
    }

    [Fact]
    public async Task Responses思考关闭时不带reasoning与include字段()
    {
        var bodies = new List<JsonElement>();
        using var http = new HttpClient(new FakeHandler(async request =>
        {
            bodies.Add(JsonDocument.Parse(await request.Content!.ReadAsStringAsync()).RootElement.Clone());
            return Response("data: {\"type\":\"response.completed\"}\n\n");
        }));
        await Collect(ResponsesClient(http), Request);
        Assert.False(bodies[0].TryGetProperty("reasoning", out _));
        Assert.False(bodies[0].TryGetProperty("include", out _));
    }

    [Fact]
    public async Task Codex身份附加会话头与prompt_cache_key()
    {
        string? sessionId = null;
        var bodies = new List<JsonElement>();
        using var http = new HttpClient(new FakeHandler(async request =>
        {
            sessionId = request.Headers.TryGetValues("session_id", out var values) ? values.FirstOrDefault() : null;
            Assert.Equal("codex_cli_rs/" + CliIdentityService.GetVersion(CliIdentityKind.Codex), request.Headers.GetValues("User-Agent").Single());
            Assert.Equal("codex_cli_rs", request.Headers.GetValues("originator").Single());
            bodies.Add(JsonDocument.Parse(await request.Content!.ReadAsStringAsync()).RootElement.Clone());
            return Response("data: {\"type\":\"response.completed\"}\n\n");
        }));
        await Collect(ResponsesClient(http, identity: CliIdentityKind.Codex), Request);
        Assert.NotNull(sessionId);
        Assert.Equal(sessionId, bodies[0].GetProperty("prompt_cache_key").GetString());
    }

    [Fact]
    public async Task 缺失完成事件的流会失败而不是假装成功()
    {
        using var http = new HttpClient(new FakeHandler(_ => Response("data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"断\"}}]}\n\n")));
        var observed = new List<ModelStreamEvent>();
        await Assert.ThrowsAsync<ModelClientException>(() => Collect(ChatClient(http), Request, observed));
        Assert.Empty(observed.OfType<ModelCompletedEvent>());
    }

    [Theory]
    [InlineData(401, "authentication_error", ModelErrorKind.Authentication)]
    [InlineData(429, "rate_limit_error", ModelErrorKind.RateLimited)]
    public async Task HTTP错误分类且不泄漏密钥(int status, string code, ModelErrorKind kind)
    {
        var errorBody = JsonSerializer.Serialize(new { error = new { message = $"拒绝 {Key}", type = code } });
        using var http = new HttpClient(new FakeHandler(_ => new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(errorBody) }));
        var error = await Assert.ThrowsAsync<ModelClientException>(() => Collect(ChatClient(http), Request));
        Assert.Equal(kind, error.Kind);
        Assert.DoesNotContain(Key, error.ToString());
    }

    [Fact]
    public async Task 流被切断时把原始帧转储到诊断目录并带站点标记()
    {
        var root = Path.Combine(Path.GetTempPath(), "smc-llm-diagnostics-" + Guid.NewGuid().ToString("N"));
        LlmStreamDiagnostics.Configure(root);
        var directory = Path.Combine(root, "llm-streams");
        try
        {
            // 收到正文增量，但流在 finish_reason 与 [DONE] 之前结束。
            using var http = new HttpClient(new FakeHandler(_ => Response("data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"部分输出\"}}]}\n\n")));
            var error = await Assert.ThrowsAsync<ModelClientException>(() => Collect(ChatClient(http), Request));
            Assert.Contains("[stream-end-without-completion]", error.Message);
            // 其他测试类可能并发触发转储，按本类独有的模型名过滤。
            var dump = Assert.Single(Directory.EnumerateFiles(directory, "llm-stream-*.txt")
                .Where(file => File.ReadAllText(file).Contains("chat-model")));
            var content = File.ReadAllText(dump);
            Assert.Contains("openai", content);
            Assert.Contains("stream-end-without-completion", content);
            Assert.Contains("部分输出", content);
            Assert.DoesNotContain(Key, content);
        }
        finally
        {
            LlmStreamDiagnostics.Configure(null);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static OpenAiModelClient ChatClient(HttpClient http, ThinkingLevel thinking = ThinkingLevel.Off, CliIdentityKind identity = CliIdentityKind.None) =>
        new(http, Key, "https://relay.example/v1", "chat-model", LlmApiFormat.OpenAiChatCompletions, thinking, identity);
    private static OpenAiModelClient ResponsesClient(HttpClient http, ThinkingLevel thinking = ThinkingLevel.Off, CliIdentityKind identity = CliIdentityKind.None) =>
        new(http, Key, "https://relay.example/v1", "responses-model", LlmApiFormat.OpenAiResponses, thinking, identity);
    private static HttpResponseMessage JsonHttp(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Response(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "text/event-stream") };
    private static async Task<List<ModelStreamEvent>> Collect(OpenAiModelClient client, ModelRequest request, List<ModelStreamEvent>? events = null)
    {
        events ??= [];
        await foreach (var item in client.StreamAsync(request, default)) events.Add(item);
        return events;
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> send;
        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : this(request => Task.FromResult(send(request))) { }
        public FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) => this.send = send;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
