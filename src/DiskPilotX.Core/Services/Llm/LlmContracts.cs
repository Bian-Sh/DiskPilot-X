namespace DiskPilotX.Core.Services.Llm;

/// <summary>上游接口协议：Anthropic Messages / OpenAI Responses / OpenAI Chat Completions。</summary>
public enum LlmApiFormat
{
    AnthropicMessages = 0,
    OpenAiResponses = 1,
    OpenAiChatCompletions = 2,
}

/// <summary>出站请求模拟的 CLI 客户端身份；部分中转站按客户端指纹放行。</summary>
public enum CliIdentityKind
{
    None = 0,
    ClaudeCode = 1,
    Codex = 2,
    Grok = 3,
}

/// <summary>模型请求的代理方式。</summary>
public enum ProxyKind
{
    /// <summary>跟随系统代理（与历史版本默认行为一致）。</summary>
    System = 0,
    /// <summary>强制直连，忽略系统代理。</summary>
    Direct = 1,
    /// <summary>使用自定义 HTTP(S) 代理。</summary>
    Custom = 2,
}

/// <summary>思考等级；Minimal 仅 OpenAI 格式区分，Anthropic 按最低档处理。</summary>
public enum ThinkingLevel
{
    Off = 0,
    Minimal = 1,
    Low = 2,
    Medium = 3,
    High = 4,
}

/// <summary>设置字符串与枚举的容错解析；空值回落到兼容历史行为的默认档。</summary>
public static class LlmContracts
{
    public static LlmApiFormat ParseApiFormat(string? value) => value switch
    {
        "openai-responses" => LlmApiFormat.OpenAiResponses,
        "openai-chat-completions" => LlmApiFormat.OpenAiChatCompletions,
        _ => LlmApiFormat.AnthropicMessages,
    };

    public static string FormatApiFormat(LlmApiFormat value) => value switch
    {
        LlmApiFormat.OpenAiResponses => "openai-responses",
        LlmApiFormat.OpenAiChatCompletions => "openai-chat-completions",
        _ => "anthropic-messages",
    };

    public static CliIdentityKind ParseCliIdentity(string? value) => value switch
    {
        "claude-code" => CliIdentityKind.ClaudeCode,
        "codex" => CliIdentityKind.Codex,
        "grok" => CliIdentityKind.Grok,
        _ => CliIdentityKind.None,
    };

    public static string FormatCliIdentity(CliIdentityKind value) => value switch
    {
        CliIdentityKind.ClaudeCode => "claude-code",
        CliIdentityKind.Codex => "codex",
        CliIdentityKind.Grok => "grok",
        _ => "none",
    };

    public static ProxyKind ParseProxyKind(string? value) => value switch
    {
        "direct" => ProxyKind.Direct,
        "custom" => ProxyKind.Custom,
        _ => ProxyKind.System,
    };

    public static string FormatProxyKind(ProxyKind value) => value switch
    {
        ProxyKind.Direct => "direct",
        ProxyKind.Custom => "custom",
        _ => "system",
    };

    public static ThinkingLevel ParseThinkingLevel(string? value) => value switch
    {
        "off" => ThinkingLevel.Off,
        "minimal" => ThinkingLevel.Minimal,
        "low" => ThinkingLevel.Low,
        "medium" => ThinkingLevel.Medium,
        "high" => ThinkingLevel.High,
        _ => ThinkingLevel.Off,
    };

    public static string FormatThinkingLevel(ThinkingLevel value) => value switch
    {
        ThinkingLevel.Minimal => "minimal",
        ThinkingLevel.Low => "low",
        ThinkingLevel.Medium => "medium",
        ThinkingLevel.High => "high",
        _ => "off",
    };

    /// <summary>OpenAI reasoning effort 值；Anthropic 无 minimal，按低档换算。</summary>
    public static string ToReasoningEffort(ThinkingLevel level) => level switch
    {
        ThinkingLevel.Minimal => "minimal",
        ThinkingLevel.Low => "low",
        ThinkingLevel.Medium => "medium",
        ThinkingLevel.High => "high",
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };

    /// <summary>Anthropic thinking 预算（token）。max_tokens 必须大于预算。</summary>
    public static (int budgetTokens, int maxTokens) ToAnthropicThinkingBudget(ThinkingLevel level) => level switch
    {
        ThinkingLevel.Off => (0, 4096),
        ThinkingLevel.Minimal or ThinkingLevel.Low => (1024, 4096),
        ThinkingLevel.Medium => (8192, 16384),
        ThinkingLevel.High => (16384, 32768),
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };

    /// <summary>OpenAI 格式的输出 token 上限；思考开启时需要覆盖推理与正文。</summary>
    public static int ToOpenAiMaxOutputTokens(ThinkingLevel level) => level switch
    {
        ThinkingLevel.Off => 4096,
        ThinkingLevel.Minimal or ThinkingLevel.Low => 8192,
        ThinkingLevel.Medium => 16384,
        ThinkingLevel.High => 32768,
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };
}
