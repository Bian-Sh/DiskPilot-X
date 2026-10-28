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

/// <summary>单次调用使用的思考等级；能力集合单独保存，不把本枚举数值作为位掩码。</summary>
public enum ThinkingLevel
{
    Off = 0,
    Minimal = 1,
    Low = 2,
    Medium = 3,
    High = 4,
    Default = 5,
    XHigh = 6,
    Max = 7,
}

/// <summary>Anthropic 的思考调节方式；预算预设兼容旧设置，原生 effort 由用户明确选择。</summary>
public enum ThinkingControlMode { Budget, Effort }

/// <summary>设置字符串与枚举的容错解析；空值回落到兼容历史行为的默认档。</summary>
public static class LlmContracts
{
    public const string DefaultProxyHost = "127.0.0.1";

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
        "default" => ThinkingLevel.Default,
        "off" => ThinkingLevel.Off,
        "minimal" => ThinkingLevel.Minimal,
        "low" => ThinkingLevel.Low,
        "medium" => ThinkingLevel.Medium,
        "high" => ThinkingLevel.High,
        "xhigh" => ThinkingLevel.XHigh,
        "max" => ThinkingLevel.Max,
        _ => ThinkingLevel.Off,
    };

    public static string FormatThinkingLevel(ThinkingLevel value) => value switch
    {
        ThinkingLevel.Default => "default",
        ThinkingLevel.XHigh => "xhigh",
        ThinkingLevel.Max => "max",
        ThinkingLevel.Minimal => "minimal",
        ThinkingLevel.Low => "low",
        ThinkingLevel.Medium => "medium",
        ThinkingLevel.High => "high",
        _ => "off",
    };

    /// <summary>原生 effort 值；默认表示省略字段，不在这里转换为关闭。</summary>
    public static string ToReasoningEffort(ThinkingLevel level) => level switch
    {
        ThinkingLevel.Off => "none",
        ThinkingLevel.XHigh => "xhigh",
        ThinkingLevel.Max => "max",
        ThinkingLevel.Minimal => "minimal",
        ThinkingLevel.Low => "low",
        ThinkingLevel.Medium => "medium",
        ThinkingLevel.High => "high",
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };

    /// <summary>Anthropic thinking 预算（token）。max_tokens 必须大于预算。</summary>
    public static (int budgetTokens, int maxTokens) ToAnthropicThinkingBudget(ThinkingLevel level) => level switch
    {
        ThinkingLevel.Default or ThinkingLevel.Off => (0, 4096),
        ThinkingLevel.Minimal or ThinkingLevel.Low => (1024, 4096),
        ThinkingLevel.Medium => (8192, 16384),
        ThinkingLevel.High => (16384, 32768),
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };

    /// <summary>OpenAI 格式的输出 token 上限；思考开启时需要覆盖推理与正文。</summary>
    public static int ToOpenAiMaxOutputTokens(ThinkingLevel level) => level switch
    {
        ThinkingLevel.Default or ThinkingLevel.Off => 4096,
        ThinkingLevel.Minimal or ThinkingLevel.Low => 8192,
        ThinkingLevel.Medium => 16384,
        ThinkingLevel.High => 32768,
        ThinkingLevel.XHigh or ThinkingLevel.Max => 65536,
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };
}
