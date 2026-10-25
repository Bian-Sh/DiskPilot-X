using System.Text.Json;
using FluentAssertions;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Llm;
using TinyHarness;

namespace SpaceMonger.Core.Tests;

public class LlmSupportTests
{
    [Fact]
    public void ClaudeCode指纹包含完整特征头()
    {
        var headers = CliIdentityService.BuildHeaders(CliIdentityKind.ClaudeCode);
        headers["User-Agent"].Should().StartWith("claude-cli/");
        headers["User-Agent"].Should().Contain("(external, cli)");
        headers["anthropic-version"].Should().Be("2023-06-01");
        headers["x-stainless-lang"].Should().Be("js");
        headers["x-stainless-runtime"].Should().Be("node");
        headers["x-stainless-package-version"].Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Codex与Grok指纹头正确()
    {
        var codex = CliIdentityService.BuildHeaders(CliIdentityKind.Codex);
        codex["User-Agent"].Should().StartWith("codex_cli_rs/");
        codex["originator"].Should().Be("codex_cli_rs");
        codex["version"].Should().NotBeNullOrWhiteSpace();

        var grok = CliIdentityService.BuildHeaders(CliIdentityKind.Grok);
        grok["User-Agent"].Should().StartWith("grok-cli/");
        grok["x-grok-client-identifier"].Should().Be("grok-cli");
        grok["X-XAI-Token-Auth"].Should().Be("true");

        CliIdentityService.BuildHeaders(CliIdentityKind.None).Should().BeEmpty();
    }

    [Fact]
    public void 思考等级映射满足各协议约束()
    {
        // Anthropic 要求 max_tokens 严格大于 budget_tokens。
        foreach (var level in new[] { ThinkingLevel.Minimal, ThinkingLevel.Low, ThinkingLevel.Medium, ThinkingLevel.High })
        {
            var (budget, max) = LlmContracts.ToAnthropicThinkingBudget(level);
            max.Should().BeGreaterThan(budget);
        }
        LlmContracts.ToAnthropicThinkingBudget(ThinkingLevel.Off).budgetTokens.Should().Be(0);

        LlmContracts.ToAnthropicThinkingBudget(ThinkingLevel.Minimal).budgetTokens.Should()
            .Be(LlmContracts.ToAnthropicThinkingBudget(ThinkingLevel.Low).budgetTokens);

        LlmContracts.ToOpenAiMaxOutputTokens(ThinkingLevel.Off).Should().BeLessThan(LlmContracts.ToOpenAiMaxOutputTokens(ThinkingLevel.High));
    }

    [Fact]
    public void 旧版EnableThinking布尔迁移到思考等级()
    {
        ModelEndpointConfig.ResolveThinkingLevel(new AppSettings { EnableThinking = true }).Should().Be(ThinkingLevel.Low);
        ModelEndpointConfig.ResolveThinkingLevel(new AppSettings()).Should().Be(ThinkingLevel.Off);
        ModelEndpointConfig.ResolveThinkingLevel(new AppSettings { EnableThinking = true, ThinkingLevel = "high" }).Should().Be(ThinkingLevel.High);
    }

    [Theory]
    [InlineData("{\"data\":[{\"id\":\"gpt-x\"},{\"id\":\"gpt-y\",\"owned_by\":\"openai\"}]}", "gpt-x", "gpt-y")]
    [InlineData("[{\"name\":\"m-1\"},{\"model\":\"m-2\"}]", "m-1", "m-2")]
    [InlineData("{\"models\":[{\"id\":\"z\"},{\"id\":\"z\"}]}", "z")]
    public void 模型列表解析兼容三种响应形状(string json, params string[] expected)
    {
        var models = ModelCatalogService.Extract(JsonDocument.Parse(json).RootElement);
        models.Select(m => m.Id).Should().Equal(expected.OrderBy(id => id, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void 模型列表响应去重并排序()
    {
        var models = ModelCatalogService.Extract(JsonDocument.Parse("{\"data\":[{\"id\":\"b\"},{\"id\":\"A\"},{\"id\":\"b\"}]}").RootElement);
        models.Select(m => m.Id).Should().Equal("A", "b");
    }

    [Fact]
    public void 无效模型列表响应抛出安全异常()
    {
        var act = () => ModelCatalogService.Extract(JsonDocument.Parse("{\"error\":true}").RootElement);
        act.Should().Throw<ModelClientException>();
    }

    [Fact]
    public void 端点URL规范化处理常见输入()
    {
        OpenAiOptions.GetEndpointUri("/chat/completions", "https://api.openai.com").AbsoluteUri.Should().Be("https://api.openai.com/v1/chat/completions");
        OpenAiOptions.GetEndpointUri("/chat/completions", "https://relay.example/v1").AbsoluteUri.Should().Be("https://relay.example/v1/chat/completions");
        OpenAiOptions.GetEndpointUri("/responses", "https://relay.example/v1/responses").AbsoluteUri.Should().Be("https://relay.example/v1/responses");
        OpenAiOptions.GetEndpointUri("/models", "https://relay.example/openai/v1").AbsoluteUri.Should().Be("https://relay.example/openai/v1/models");
        AnthropicOptions.GetMessagesUri("https://relay.example").AbsoluteUri.Should().Be("https://relay.example/v1/messages");
        AnthropicOptions.GetModelsUri("https://api.anthropic.com").AbsoluteUri.Should().Be("https://api.anthropic.com/v1/models?limit=1000");
    }
}
