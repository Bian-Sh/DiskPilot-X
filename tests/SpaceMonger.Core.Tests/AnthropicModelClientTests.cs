using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using TinyHarness;
using SpaceMonger.Core.Services.Assistant;
using SpaceMonger.Core.Services.Llm;
using Message = TinyHarness.ChatMessage;

namespace SpaceMonger.Core.Tests;

public sealed class AnthropicModelClientTests
{
    private const string Key = "tiny-secret-key";
    private static readonly ModelRequest Request = new([Message.User("列出文件")], []);

    [Fact]
    public async Task 首段正文和思考在流结束前发出且取消不会成功完成()
    {
        using var stream = new ControlledStream();
        using var http = new HttpClient(new FakeHandler(_ => Response(stream)));
        using var cancellation = new CancellationTokenSource();
        var client = Client(http);
        await using var events = client.StreamAsync(Request, cancellation.Token).GetAsyncEnumerator();
        stream.Write(Start + Block(2, "thinking") + Delta(2, "thinking_delta", "thinking", "先思考"));
        Assert.True(await events.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("先思考", Assert.IsType<ReasoningDeltaEvent>(events.Current).Text);
        stream.Write(StopBlock(2) + Block(4, "text") + Delta(4, "text_delta", "text", "你好"));
        Assert.True(await events.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("你好", Assert.IsType<TextDeltaEvent>(events.Current).Text);
        Assert.False(stream.Ended);
        var pending = events.MoveNextAsync().AsTask();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task 多行data和多个索引的参数切片仅在block结束后发出完整工具()
    {
        using var stream = new ControlledStream();
        using var http = new HttpClient(new FakeHandler(_ => Response(stream)));
        await using var events = Client(http).StreamAsync(Request, default).GetAsyncEnumerator();
        stream.Write(Start + ToolBlock(3, "call-a", "list_files") + ToolBlock(7, "call-b", "inspect")
            + Delta(3, "input_json_delta", "partial_json", "{\"path\":")
            + Delta(7, "input_json_delta", "partial_json", "{}"));
        var pending = events.MoveNextAsync().AsTask();
        stream.Write(": 心跳\r\nevent: content_block_delta\r\ndata: {\"type\":\"content_block_delta\",\n"
            + "data: \"index\":3,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"\\\"C:/测试\\\"}\"}}\n\n");
        Assert.False(pending.IsCompleted);
        stream.Write(StopBlock(7));
        Assert.True(await pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(new ToolCall("call-b", "inspect", "{}"), Assert.IsType<ModelToolCallEvent>(events.Current).ToolCall);
        stream.Write(StopBlock(3) + End("tool_use"));
        stream.End();
        Assert.True(await events.MoveNextAsync());
        var tool = Assert.IsType<ModelToolCallEvent>(events.Current).ToolCall;
        Assert.Equal("call-a", tool.Id);
        using var arguments = JsonDocument.Parse(tool.ArgumentsJson);
        Assert.Equal("C:/测试", arguments.RootElement.GetProperty("path").GetString());
        Assert.True(await events.MoveNextAsync());
        Assert.Equal("tool_use", Assert.IsType<ModelCompletedEvent>(events.Current).FinishReason);
        Assert.False(await events.MoveNextAsync());
    }

    [Fact]
    public async Task 普通JSON正文不能触发工具调用()
    {
        const string text = "{\"tool\":\"delete_files\",\"arguments\":{}}";
        using var http = TextHttp(Start + Block(0, "text") + Delta(0, "text_delta", "text", text) + StopBlock(0) + End());
        var events = await Collect(Client(http), Request);
        Assert.Equal(text, Assert.Single(events.OfType<TextDeltaEvent>()).Text);
        Assert.Empty(events.OfType<ModelToolCallEvent>());
        Assert.Single(events.OfType<ModelCompletedEvent>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 多轮请求保留原生工具schema调用与结果并忽略无签名思考(bool enableThinking)
    {
        var bodies = new List<JsonElement>();
        using var http = new HttpClient(new FakeHandler(async request =>
        {
            Assert.Equal("https://gateway.example/proxy/v1/messages", request.RequestUri!.AbsoluteUri);
            Assert.Equal(Key, Assert.Single(request.Headers.GetValues("x-api-key")));
            Assert.Equal("2023-06-01", Assert.Single(request.Headers.GetValues("anthropic-version")));
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal(Key, request.Headers.Authorization?.Parameter);
            bodies.Add(JsonDocument.Parse(await request.Content!.ReadAsStringAsync()).RootElement.Clone());
            return Response(Start + End());
        }));
        var client = Client(http, enableThinking);
        var tool = new ToolDefinition
        {
            Name = "list_files", Description = "列出文件", ParametersSchema = JsonNode.Parse("{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"}},\"required\":[\"path\"]}")!,
            Handler = (_, _) => Task.FromResult(ToolResult.Ok("结果"))
        };
        await Collect(client, new([Message.System("规则"), Message.User("开始")], [tool]));
        var call = new ToolCall("tool-1", "list_files", "{\"path\":\"C:/\"}");
        await Collect(client, new([
            Message.System("规则"), Message.User("开始"),
            Message.AssistantToolCalls([call], "正在查询") with
            {
                Blocks = [new(ChatContentKind.Thinking, "无签名思考"), new(ChatContentKind.Text, "正在查询"), new(ChatContentKind.ToolCall, ToolCall: call)]
            },
            Message.ToolResult(call, "一项结果"), Message.ToolResult(new("tool-2", "inspect", "{}"), "二项结果")
        ], [tool]));
        Assert.Equal("规则", bodies[0].GetProperty("system").GetString());
        Assert.True(bodies[0].GetProperty("stream").GetBoolean());
        Assert.Equal(enableThinking ? "enabled" : "disabled", bodies[0].GetProperty("thinking").GetProperty("type").GetString());
        var schema = bodies[0].GetProperty("tools")[0];
        Assert.Equal("list_files", schema.GetProperty("name").GetString());
        Assert.Equal("path", schema.GetProperty("input_schema").GetProperty("required")[0].GetString());
        var messages = bodies[1].GetProperty("messages");
        Assert.Equal(3, messages.GetArrayLength());
        var assistant = messages[1];
        Assert.Equal("assistant", assistant.GetProperty("role").GetString());
        var content = assistant.GetProperty("content");
        Assert.Equal(2, content.GetArrayLength());
        Assert.Equal("text", content[0].GetProperty("type").GetString());
        Assert.Equal("tool_use", content[1].GetProperty("type").GetString());
        Assert.Equal("tool-1", content[1].GetProperty("id").GetString());
        Assert.Equal("C:/", content[1].GetProperty("input").GetProperty("path").GetString());
        var results = messages[2];
        Assert.Equal("user", results.GetProperty("role").GetString());
        Assert.Equal(2, results.GetProperty("content").GetArrayLength());
        Assert.Equal("tool_result", results.GetProperty("content")[0].GetProperty("type").GetString());
        Assert.Equal("tool-1", results.GetProperty("content")[0].GetProperty("tool_use_id").GetString());
        Assert.Equal("一项结果", results.GetProperty("content")[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task thinking开启时仅回传服务端已签名的思考()
    {
        var bodies = new List<JsonElement>();
        var first = Start + Block(0, "thinking") + Delta(0, "thinking_delta", "thinking", "已签名思考")
            + Delta(0, "signature_delta", "signature", "signed-value") + StopBlock(0)
            + ToolBlock(1, "signed-tool", "inspect") + StopBlock(1) + End("tool_use");
        using var http = new HttpClient(new FakeHandler(async request =>
        {
            bodies.Add(JsonDocument.Parse(await request.Content!.ReadAsStringAsync()).RootElement.Clone());
            return Response(bodies.Count == 1 ? first : Start + End());
        }));
        var client = Client(http, true);
        var events = await Collect(client, Request);
        var call = Assert.Single(events.OfType<ModelToolCallEvent>()).ToolCall;
        await Collect(client, new([Message.User("开始"), Message.AssistantToolCalls([call]), Message.ToolResult(call, "完成")], []));
        Assert.Equal("enabled", bodies[0].GetProperty("thinking").GetProperty("type").GetString());
        Assert.True(bodies[0].GetProperty("max_tokens").GetInt32() > bodies[0].GetProperty("thinking").GetProperty("budget_tokens").GetInt32());
        var thought = bodies[1].GetProperty("messages")[1].GetProperty("content")[0];
        Assert.Equal("thinking", thought.GetProperty("type").GetString());
        Assert.Equal("signed-value", thought.GetProperty("signature").GetString());
        Assert.Equal("已签名思考", thought.GetProperty("thinking").GetString());
    }

    [Theory]
    [InlineData("{\"x\":")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"x\":1} garbage")]
    public async Task 无效工具参数不能发出调用(string arguments)
    {
        using var http = TextHttp(Start + ToolBlock(0, "bad", "inspect") + Delta(0, "input_json_delta", "partial_json", arguments) + StopBlock(0) + End("tool_use"));
        var observed = new List<ModelStreamEvent>();
        await Assert.ThrowsAsync<ModelClientException>(() => Collect(Client(http), Request, observed));
        Assert.Empty(observed.OfType<ModelToolCallEvent>());
        Assert.Empty(observed.OfType<ModelCompletedEvent>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("data: {\"type\":\"message_stop\"}")]
    [InlineData("data: [DONE]\n\n")]
    [InlineData("data: {broken}\n\n")]
    [InlineData("data: {\"type\":\"message_stop\"}\n\n")]
    public async Task 缺失完整结束事件不能算成功(string tail)
    {
        using var http = TextHttp(Start + ToolBlock(0, "unfinished", "inspect") + tail);
        var observed = new List<ModelStreamEvent>();
        await Assert.ThrowsAsync<ModelClientException>(() => Collect(Client(http), Request, observed));
        Assert.Empty(observed.OfType<ModelCompletedEvent>());
        Assert.Empty(observed.OfType<ModelToolCallEvent>());
    }

    [Theory]
    [InlineData(401, "authentication_error", ModelErrorKind.Authentication)]
    [InlineData(429, "rate_limit_error", ModelErrorKind.RateLimited)]
    [InlineData(400, "invalid_request_error", ModelErrorKind.InvalidRequest)]
    public async Task HTTP错误通过Harness分类并脱敏(int status, string code, ModelErrorKind kind)
    {
        var errorBody = JsonSerializer.Serialize(new { error = new { type = code, message = $"拒绝 {Key} Bearer abcdef-123 sk-secret-value" }, private_body = "原始正文哨兵" });
        using var http = new HttpClient(new FakeHandler(_ => new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(errorBody) }));
        var error = await Assert.ThrowsAsync<ModelClientException>(() => Collect(Client(http), Request));
        Assert.Equal(kind, error.Kind);
        Assert.Equal(status, error.StatusCode);
        Assert.Equal(code, error.ErrorCode);
        Assert.DoesNotContain(Key, error.ToString());
        Assert.DoesNotContain(Key, error.UpstreamMessage ?? "");
        Assert.DoesNotContain("sk-secret-value", error.UpstreamMessage ?? "");
        Assert.DoesNotContain("原始正文哨兵", error.ToString());
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task 流中error事件分类且不能成功完成()
    {
        using var http = TextHttp(Start + Sse(new { type = "error", error = new { type = "overloaded_error", message = Key } }));
        var observed = new List<ModelStreamEvent>();
        var error = await Assert.ThrowsAsync<ModelClientException>(() => Collect(Client(http), Request, observed));
        Assert.Equal(ModelErrorKind.ServerOverloaded, error.Kind);
        Assert.DoesNotContain(Key, error.UpstreamMessage ?? "");
        Assert.Empty(observed.OfType<ModelCompletedEvent>());
    }

    [Fact]
    public async Task 网络错误不会通过InnerException泄漏密钥或地址()
    {
        using var http = new HttpClient(new FakeHandler((_, _) => throw new HttpRequestException($"{Key} https://private.example/?token=abc")));
        var error = await Assert.ThrowsAsync<ModelClientException>(() => Collect(Client(http), Request));
        Assert.Equal(ModelErrorKind.ConnectionFailed, error.Kind);
        Assert.DoesNotContain(Key, error.ToString());
        Assert.DoesNotContain("private.example", error.ToString());
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task HTTP等待期间取消保持取消语义()
    {
        using var cancellation = new CancellationTokenSource();
        using var http = new HttpClient(new FakeHandler(async (_, token) =>
        {
            cancellation.Cancel();
            await Task.Delay(Timeout.Infinite, token);
            return Response("");
        }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in Client(http).StreamAsync(Request, cancellation.Token)) { }
        });
    }

    [Fact]
    public async Task 官方地址不附带网关bearer()
    {
        using var http = new HttpClient(new FakeHandler(request =>
        {
            Assert.Equal("https://api.anthropic.com/v1/messages", request.RequestUri!.AbsoluteUri);
            Assert.Null(request.Headers.Authorization);
            return Response(Start + End());
        }));
        await Collect(new AnthropicModelClient(http, Key, "https://api.anthropic.com", "claude-test"), Request);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"type\":\"content_block_start\",\"index\":\"泄漏哨兵\",\"content_block\":{\"type\":\"text\"}}")]
    [InlineData("{\"type\":\"content_block_delta\",\"index\":0}")]
    [InlineData("{\"type\":\"content_block_stop\",\"index\":2147483648}")]
    public async Task 缺失或错误类型的协议字段也转换为安全异常(string frame)
    {
        using var http = TextHttp(Start + Block(0, "text") + "data: " + frame + "\n\n");
        var observed = new List<ModelStreamEvent>();
        var error = await Assert.ThrowsAsync<ModelClientException>(() => Collect(Client(http), Request, observed));
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("泄漏哨兵", error.ToString());
        Assert.Empty(observed.OfType<ModelCompletedEvent>());
    }

    [Fact]
    public async Task 读取正文时网络中断不会成功完成或泄漏异常原文()
    {
        using var http = new HttpClient(new FakeHandler(_ => Response(new BrokenStream())));
        var error = await Assert.ThrowsAsync<ModelClientException>(() => Collect(Client(http), Request));
        Assert.Equal(ModelErrorKind.ConnectionFailed, error.Kind);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(Key, error.ToString());
    }

    [Fact]
    public async Task 非用户取消的超时转换为Timeout()
    {
        using var http = new HttpClient(new FakeHandler((_, _) => throw new TaskCanceledException(Key)));
        var error = await Assert.ThrowsAsync<ModelClientException>(() => Collect(Client(http), Request));
        Assert.Equal(ModelErrorKind.Timeout, error.Kind);
        Assert.DoesNotContain(Key, error.ToString());
    }

    [Fact]
    public async Task 工具历史中无效参数在发请求前安全失败()
    {
        var sent = false;
        using var http = new HttpClient(new FakeHandler(_ => { sent = true; return Response(Start + End()); }));
        var request = new ModelRequest([Message.AssistantToolCalls([new("bad", "inspect", Key)])], []);
        var error = await Assert.ThrowsAsync<ModelClientException>(() => Collect(Client(http), request));
        Assert.Equal(ModelErrorKind.InvalidRequest, error.Kind);
        Assert.False(sent);
        Assert.DoesNotContain(Key, error.ToString());
    }

    private sealed class BrokenStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => throw new IOException(Key);
    }
    private static AnthropicModelClient Client(HttpClient http, bool thinking = false) => new(http, Key, "https://gateway.example/proxy/v1", "claude-test", thinking ? ThinkingLevel.Medium : ThinkingLevel.Off);
    private static HttpClient TextHttp(string text) => new(new FakeHandler(_ => Response(text)));
    private static HttpResponseMessage Response(string text) => Response(new MemoryStream(Encoding.UTF8.GetBytes(text)));
    private static HttpResponseMessage Response(Stream stream) => new(HttpStatusCode.OK) { Content = new StreamContent(stream) };
    private static string Sse(object value) => "data: " + JsonSerializer.Serialize(value) + "\n\n";
    private static readonly string Start = Sse(new { type = "message_start", message = new { id = "msg-1", type = "message", role = "assistant", content = Array.Empty<object>(), stop_reason = (string?)null } });
    private static string Block(int index, string type) => Sse(new { type = "content_block_start", index, content_block = new { type, text = "", thinking = "" } });
    private static string ToolBlock(int index, string id, string name) => Sse(new { type = "content_block_start", index, content_block = new { type = "tool_use", id, name, input = new { } } });
    private static string Delta(int index, string type, string field, string value) => Sse(new { type = "content_block_delta", index, delta = new Dictionary<string, object> { ["type"] = type, [field] = value } });
    private static string StopBlock(int index) => Sse(new { type = "content_block_stop", index });
    private static string End(string reason = "end_turn") => Sse(new { type = "message_delta", delta = new { stop_reason = reason } }) + Sse(new { type = "message_stop" });
    private static async Task<List<ModelStreamEvent>> Collect(AnthropicModelClient client, ModelRequest request, List<ModelStreamEvent>? events = null)
    {
        events ??= [];
        await foreach (var item in client.StreamAsync(request, default)) events.Add(item);
        return events;
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send;
        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : this((request, _) => Task.FromResult(send(request))) { }
        public FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : this((request, _) => send(request)) { }
        public FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) => this.send = send;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    // 每次只供给少量字节，覆盖 UTF-8 字符和 SSE 行被网络任意切分的情况。
    private sealed class ControlledStream : Stream
    {
        private readonly Channel<byte[]> chunks = Channel.CreateUnbounded<byte[]>();
        private byte[] current = [];
        private int offset;
        public bool Ended { get; private set; }
        public void Write(string text) => chunks.Writer.TryWrite(Encoding.UTF8.GetBytes(text));
        public void End() { Ended = true; chunks.Writer.TryComplete(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (offset == current.Length)
            {
                if (!await chunks.Reader.WaitToReadAsync(cancellationToken)) return 0;
                current = await chunks.Reader.ReadAsync(cancellationToken);
                offset = 0;
            }
            var count = Math.Min(3, Math.Min(buffer.Length, current.Length - offset));
            current.AsMemory(offset, count).CopyTo(buffer);
            offset += count;
            return count;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}