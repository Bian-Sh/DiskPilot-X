using System.Net;
using System.Text;
using System.Text.Json;
using TinyHarness;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Llm;

namespace DiskPilotX.Core.Tests;

[Collection("ModelClientSequential")]
public sealed class ModelStreamActivityTests
{
    [Theory]
    [InlineData("anthropic", 7)]
    [InlineData("openai-chat", 5)]
    [InlineData("openai-responses", 5)]
    public async Task 工具名称到达即展示准备状态但不暴露参数分片(string protocol, int expected)
    {
        var callback = typeof(ModelRequest).GetProperty("OnStreamActivity");
        Assert.NotNull(callback);
        var request = new ModelRequest([TinyHarness.ChatMessage.User("受控测试")], []);
        var calls = 0;
        callback.SetValue(request, (Action)(() => calls++));
        Assert.DoesNotContain("OnStreamActivity", JsonSerializer.Serialize(request));
        using var http = new HttpClient(new FixtureHandler(Frames(protocol)));
        IModelClient client = protocol == "anthropic" ? new AnthropicModelClient(http, "fixture", "http://fixture.invalid", "fixture")
            : new OpenAiModelClient(http, "fixture", "http://fixture.invalid", "fixture", protocol == "openai-chat" ? LlmApiFormat.OpenAiChatCompletions : LlmApiFormat.OpenAiResponses);
        var events = new List<ModelStreamEvent>();
        await foreach (var item in client.StreamAsync(request, CancellationToken.None)) events.Add(item);
        Assert.Equal(expected, calls);
        Assert.Equal(3, events.Count);
        Assert.Equal("ModelToolCallPreparingEvent", events[0].GetType().Name);
        Assert.DoesNotContain("path", JsonSerializer.Serialize(events[0], events[0].GetType()));
        Assert.Equal("{\"path\":\"fixture\"}", Assert.Single(events.OfType<ModelToolCallEvent>()).ToolCall.ArgumentsJson);
        Assert.Single(events.OfType<ModelCompletedEvent>());
    }

    [Fact]
    public async Task 调用标识分片追加时准备事件同步迁移到完整标识()
    {
        var frames = string.Concat(new[]
        {
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_","function":{"name":"fixture","arguments":""}}]}}]}""",
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"1","function":{"arguments":"{}"}}]}}]}""",
            """{"choices":[{"delta":{},"finish_reason":"tool_calls"}]}""", "[DONE]"
        }.Select(frame => "data: " + frame + "\n\n"));
        using var http = new HttpClient(new FixtureHandler(frames));
        var client = new OpenAiModelClient(http, "fixture", "http://fixture.invalid", "fixture", LlmApiFormat.OpenAiChatCompletions);
        var events = new List<ModelStreamEvent>();
        await foreach (var item in client.StreamAsync(new ModelRequest([TinyHarness.ChatMessage.User("分片标识测试")], []), default)) events.Add(item);
        var preparing = Assert.Single(events.OfType<ModelToolCallPreparingEvent>());
        Assert.StartsWith("preparing:", preparing.ToolCallId);
        Assert.Equal(preparing.ToolCallId, Assert.Single(events.OfType<ModelToolCallEvent>()).PreparationId);
        Assert.Equal("call_1", Assert.Single(events.OfType<ModelToolCallEvent>()).ToolCall.Id);
    }

    private static string Frames(string protocol)
    {
        string[] frames = protocol switch
        {
            "anthropic" => [
                """{"type":"message_start","message":{"role":"assistant"}}""",
                """{"type":"content_block_start","index":0,"content_block":{"type":"tool_use","id":"call","name":"fixture","input":{}}}""",
                """{"type":"content_block_delta","index":0,"delta":{"type":"input_json_delta","partial_json":"{\"path\":"}}""",
                """{"type":"content_block_delta","index":0,"delta":{"type":"input_json_delta","partial_json":"\"fixture\"}"}}""",
                """{"type":"content_block_stop","index":0}""",
                """{"type":"message_delta","delta":{"stop_reason":"tool_use"}}""",
                """{"type":"message_stop"}"""],
            "openai-chat" => [
                """{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call","function":{"name":"fixture","arguments":""}}]}}]}""",
                """{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"path\":"}}]}}]}""",
                """{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"\"fixture\"}"}}]}}]}""",
                """{"choices":[{"delta":{},"finish_reason":"tool_calls"}]}""", "[DONE]"],
            _ => [
                """{"type":"response.output_item.added","output_index":0,"item":{"type":"function_call","call_id":"call","name":"fixture"}}""",
                """{"type":"response.function_call_arguments.delta","output_index":0,"delta":"{\"path\":"}""",
                """{"type":"response.function_call_arguments.delta","output_index":0,"delta":"\"fixture\"}"}""",
                """{"type":"response.output_item.done","output_index":0,"item":{"type":"function_call","call_id":"call","name":"fixture","arguments":"{\"path\":\"fixture\"}"}}""",
                """{"type":"response.completed"}"""]
        };
        return ": keep-alive\n\n" + string.Concat(frames.Select(frame => "data: " + frame + "\n\n"));
    }

    private sealed class FixtureHandler(string frames) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(frames, Encoding.UTF8, "text/event-stream") });
    }
}
