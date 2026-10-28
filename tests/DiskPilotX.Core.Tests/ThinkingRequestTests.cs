using System.Net;
using System.Text;
using System.Text.Json;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Llm;
using TinyHarness;

namespace DiskPilotX.Core.Tests;

[Collection("ModelClientSequential")]
public class ThinkingRequestTests
{
    [Theory]
    [InlineData(LlmApiFormat.OpenAiChatCompletions)]
    [InlineData(LlmApiFormat.OpenAiResponses)]
    public async Task OpenAI各档位原样发送且默认不同于关闭(LlmApiFormat format)
    {
        foreach (var level in ThinkingCapabilities.GetCandidates(format, ThinkingControlMode.Effort))
        {
            JsonElement body = default;
            using var http = new HttpClient(new CaptureHandler(async request =>
            {
                body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync()).RootElement.Clone();
                return "data: " + (format == LlmApiFormat.OpenAiResponses
                    ? "{\"type\":\"response.completed\",\"response\":{\"status\":\"completed\"}}"
                    : "{\"choices\":[{\"delta\":{\"content\":\"完成\"},\"finish_reason\":\"stop\"}]}") + "\n\ndata: [DONE]\n\n";
            }));
            var client = new OpenAiModelClient(http, "测试密钥", "https://test.example/v1", "用户选择模型", format, level);
            await foreach (var item in client.StreamAsync(new ModelRequest([ChatMessage.User("测试")], []), CancellationToken.None)) { }
            var field = format == LlmApiFormat.OpenAiResponses ? "reasoning" : "reasoning_effort";
            if (level == ThinkingLevel.Default) Assert.False(body.TryGetProperty(field, out _));
            else
            {
                var effort = format == LlmApiFormat.OpenAiResponses ? body.GetProperty(field).GetProperty("effort") : body.GetProperty(field);
                Assert.Equal(LlmContracts.ToReasoningEffort(level), effort.GetString());
            }
            if (format == LlmApiFormat.OpenAiResponses)
                Assert.Equal(level is not ThinkingLevel.Default and not ThinkingLevel.Off, body.TryGetProperty("include", out _));
        }
    }

    [Theory]
    [InlineData(ThinkingControlMode.Budget)]
    [InlineData(ThinkingControlMode.Effort)]
    public async Task Anthropic默认关闭预算与原生effort准确区分(ThinkingControlMode mode)
    {
        foreach (var level in ThinkingCapabilities.GetCandidates(LlmApiFormat.AnthropicMessages, mode))
        {
            JsonElement body = default;
            using var http = new HttpClient(new CaptureHandler(async request =>
            {
                body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync()).RootElement.Clone();
                return "data: {\"type\":\"message_start\",\"message\":{\"id\":\"m1\",\"role\":\"assistant\",\"content\":[]}}\n\n"
                    + "data: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"}}\n\n"
                    + "data: {\"type\":\"message_stop\"}\n\n";
            }));
            var client = new AnthropicModelClient(http, "测试密钥", "https://test.example/v1", "用户选择模型", level, thinkingControlMode: mode);
            await foreach (var item in client.StreamAsync(new ModelRequest([ChatMessage.User("测试")], []), CancellationToken.None)) { }
            if (level == ThinkingLevel.Default)
            {
                Assert.False(body.TryGetProperty("thinking", out _));
                Assert.False(body.TryGetProperty("output_config", out _));
            }
            else if (level == ThinkingLevel.Off)
            {
                Assert.Equal("disabled", body.GetProperty("thinking").GetProperty("type").GetString());
                Assert.False(body.TryGetProperty("output_config", out _));
            }
            else if (mode == ThinkingControlMode.Effort)
            {
                Assert.Equal("adaptive", body.GetProperty("thinking").GetProperty("type").GetString());
                Assert.Equal(LlmContracts.ToReasoningEffort(level), body.GetProperty("output_config").GetProperty("effort").GetString());
                Assert.False(body.GetProperty("thinking").TryGetProperty("budget_tokens", out _));
            }
            else
            {
                var budget = body.GetProperty("thinking").GetProperty("budget_tokens").GetInt32();
                Assert.Equal(LlmContracts.ToAnthropicThinkingBudget(level).budgetTokens, budget);
                Assert.True(body.GetProperty("max_tokens").GetInt32() > budget);
                Assert.False(body.TryGetProperty("output_config", out _));
            }
        }
    }

    private sealed class CaptureHandler(Func<HttpRequestMessage, Task<string>> reply) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(await reply(request), Encoding.UTF8, "text/event-stream"),
        };
    }
}
