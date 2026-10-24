namespace TinyHarness;

public enum ChatRole
{
    System,
    User,
    Assistant,
    Tool,
}

/// <summary>
/// 一次模型工具调用。ArgumentsJson 为模型输出的原始 JSON 参数。
/// </summary>
public sealed record ToolCall(string Id, string Name, string ArgumentsJson)
{
    public bool ArgumentsAreSafe { get; init; }
}

public enum ChatContentKind { Thinking, Text, ToolCall }

/// <summary>按产生顺序保存模型内容；Thinking 的 summary 不冒充原文。</summary>
public sealed record ChatContentBlock(ChatContentKind Kind, string? Text = null, bool IsSummary = false, ToolCall? ToolCall = null);

/// <summary>
/// 会话消息。禁止写入 Secret 值，只能出现 secret_ref 形式的安全引用。
/// </summary>
public sealed record ChatMessage(ChatRole Role, string? Content)
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<ToolCall> ToolCalls { get; init; } = [];
    public string? ToolCallId { get; init; }
    public string? ToolName { get; init; }
    public IReadOnlyList<ChatContentBlock> Blocks { get; init; } = [];

    public static ChatMessage System(string content) => new(ChatRole.System, content);

    public static ChatMessage User(string content) => new(ChatRole.User, content);

    public static ChatMessage Assistant(string content) => new(ChatRole.Assistant, content);

    public static ChatMessage AssistantToolCalls(IReadOnlyList<ToolCall> toolCalls, string? content = null) =>
        new(ChatRole.Assistant, content) { ToolCalls = toolCalls };

    public static ChatMessage ToolResult(ToolCall call, string content) =>
        new(ChatRole.Tool, content) { ToolCallId = call.Id, ToolName = call.Name };
}
