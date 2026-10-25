using SpaceMonger.App.Localization;
using SpaceMonger.Core.Services.Llm;

namespace SpaceMonger.App.ViewModels;

public sealed record ThinkingLevelOption(ThinkingLevel Value, string Label);
public sealed record ApiFormatOption(LlmApiFormat Value, string Label);
public sealed record CliIdentityOption(CliIdentityKind Value, string Label);
public sealed record ProxyKindOption(ProxyKind Value, string Label);

/// <summary>AI 设置下拉选项的唯一来源；标签本地化，语言切换后需重新读取。</summary>
public static class AiOptions
{
    public static IReadOnlyList<ThinkingLevelOption> ThinkingLevels() =>
    [
        new(ThinkingLevel.Off, L.Text("ThinkingLevelOffLabel")),
        new(ThinkingLevel.Minimal, L.Text("ThinkingLevelMinimalLabel")),
        new(ThinkingLevel.Low, L.Text("ThinkingLevelLowLabel")),
        new(ThinkingLevel.Medium, L.Text("ThinkingLevelMediumLabel")),
        new(ThinkingLevel.High, L.Text("ThinkingLevelHighLabel")),
    ];

    public static IReadOnlyList<ApiFormatOption> ApiFormats() =>
    [
        new(LlmApiFormat.AnthropicMessages, L.Text("ApiFormatAnthropicLabel")),
        new(LlmApiFormat.OpenAiResponses, L.Text("ApiFormatResponsesLabel")),
        new(LlmApiFormat.OpenAiChatCompletions, L.Text("ApiFormatChatCompletionsLabel")),
    ];

    public static IReadOnlyList<CliIdentityOption> CliIdentities() =>
    [
        new(CliIdentityKind.None, L.Text("CliIdentityNoneLabel")),
        new(CliIdentityKind.ClaudeCode, L.Text("CliIdentityClaudeCodeLabel")),
        new(CliIdentityKind.Codex, L.Text("CliIdentityCodexLabel")),
        new(CliIdentityKind.Grok, L.Text("CliIdentityGrokLabel")),
    ];

    public static IReadOnlyList<ProxyKindOption> ProxyKinds() =>
    [
        new(ProxyKind.System, L.Text("ProxyModeSystemLabel")),
        new(ProxyKind.Direct, L.Text("ProxyModeDirectLabel")),
        new(ProxyKind.Custom, L.Text("ProxyModeCustomLabel")),
    ];
}
