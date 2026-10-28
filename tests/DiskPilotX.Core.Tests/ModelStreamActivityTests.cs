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
    public async Task 每个数据帧含工具参数分片均通知活动但不产生额外模型事件(string protocol, int expected)
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
        Assert.Equal(2, events.Count);
        Assert.Equal("{\"path\":\"fixture\"}", Assert.Single(events.OfType<ModelToolCallEvent>()).ToolCall.ArgumentsJson);
        Assert.Single(events.OfType<ModelCompletedEvent>());
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
