using System.Text.Json;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Llm;

namespace DiskPilotX.Core.Tests;

public class ThinkingCapabilitiesTests
{
    [Fact]
    public void 各协议候选有官方依据且新配置默认全选()
    {
        var openAi = new AppSettings { ApiFormat = "openai-responses", ChatModelName = "任意模型" };
        Assert.Equal(new[] { ThinkingLevel.Default, ThinkingLevel.Off, ThinkingLevel.Minimal, ThinkingLevel.Low, ThinkingLevel.Medium, ThinkingLevel.High, ThinkingLevel.XHigh, ThinkingLevel.Max }, ThinkingCapabilities.GetSupportedLevels(openAi));
        Assert.Equal(new[] { ThinkingLevel.Default, ThinkingLevel.Off, ThinkingLevel.Low, ThinkingLevel.Medium, ThinkingLevel.High }, ThinkingCapabilities.GetSupportedLevels(new AppSettings()));
        ThinkingCapabilities.SetControlMode(openAi, ThinkingControlMode.Effort);
        var anthropic = new AppSettings { ChatModelName = "用户配置模型" };
        ThinkingCapabilities.SetControlMode(anthropic, ThinkingControlMode.Effort);
        Assert.DoesNotContain(ThinkingLevel.Minimal, ThinkingCapabilities.GetSupportedLevels(anthropic));
        Assert.Contains(ThinkingLevel.Max, ThinkingCapabilities.GetSupportedLevels(anthropic));
    }

    [Fact]
    public void 模型端点协议的声明与已选档位分别保存且可序列化恢复()
    {
        var settings = new AppSettings { ApiFormat = "openai-responses", AnthropicBaseUrl = "https://relay.example/v1", ChatModelName = "模型甲" };
        ThinkingCapabilities.SetSupportedLevels(settings, [ThinkingLevel.Low, ThinkingLevel.High]);
        ThinkingCapabilities.SetSelectedLevel(settings, ThinkingLevel.High);
        settings.ChatModelName = "模型乙";
        Assert.Equal(8, ThinkingCapabilities.GetSupportedLevels(settings).Count);
        ThinkingCapabilities.SetSupportedLevels(settings, [ThinkingLevel.Medium]);
        settings.ChatModelName = "模型甲";
        Assert.Equal(ThinkingLevel.High, ModelEndpointConfig.ResolveThinkingLevel(settings));
        settings.AnthropicBaseUrl = "https://other.example/v1";
        Assert.Equal(8, ThinkingCapabilities.GetSupportedLevels(settings).Count);
        settings.AnthropicBaseUrl = "https://relay.example/v1";
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal(new[] { ThinkingLevel.Low, ThinkingLevel.High }, ThinkingCapabilities.GetSupportedLevels(restored));
        Assert.Equal(ThinkingLevel.High, ModelEndpointConfig.FromSettings(restored, "密钥", "模型甲").ThinkingLevel);
        Assert.Equal(ThinkingLevel.Medium, ModelEndpointConfig.FromSettings(restored, "密钥", "模型乙").ThinkingLevel);
    }

    [Fact]
    public void 移除已选档位自动回落且空集合仍有有效默认项()
    {
        var settings = new AppSettings { ApiFormat = "openai-chat-completions", ThinkingLevel = "high" };
        ThinkingCapabilities.SetSupportedLevels(settings, [ThinkingLevel.Low, ThinkingLevel.High]);
        ThinkingCapabilities.SetSelectedLevel(settings, ThinkingLevel.High);
        ThinkingCapabilities.SetSupportedLevels(settings, [ThinkingLevel.Low]);
        Assert.Equal(ThinkingLevel.Low, ModelEndpointConfig.ResolveThinkingLevel(settings));
        ThinkingCapabilities.SetSupportedLevels(settings, []);
        Assert.Equal(new[] { ThinkingLevel.Default }, ThinkingCapabilities.GetSupportedLevels(settings));
        Assert.Equal(ThinkingLevel.Default, ModelEndpointConfig.ResolveThinkingLevel(settings));
    }

    [Fact]
    public void 旧OpenAI关闭迁移为省略参数且不把档位当flags数值()
    {
        var old = new AppSettings { ApiFormat = "openai-responses", ThinkingLevel = "off" };
        Assert.Equal(ThinkingLevel.Default, ModelEndpointConfig.ResolveThinkingLevel(old));
        old.ThinkingLevel = "minimal";
        Assert.Equal(ThinkingLevel.Minimal, ModelEndpointConfig.ResolveThinkingLevel(old));
        Assert.Equal("none", LlmContracts.ToReasoningEffort(ThinkingLevel.Off));
        Assert.Equal("minimal", LlmContracts.ToReasoningEffort(ThinkingLevel.Minimal));
        Assert.Equal("xhigh", LlmContracts.ToReasoningEffort(ThinkingLevel.XHigh));
        Assert.Equal("max", LlmContracts.ToReasoningEffort(ThinkingLevel.Max));
        Assert.Equal(ThinkingLevel.Low, ModelEndpointConfig.ResolveThinkingLevel(new AppSettings { ThinkingLevel = "minimal" }));
    }
}
