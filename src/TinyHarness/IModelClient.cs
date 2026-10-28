namespace TinyHarness;

/// <summary>
/// 一次模型请求：完整消息历史 + 可用工具列表。
/// </summary>
public sealed record ModelRequest(
    IReadOnlyList<ChatMessage> Messages,
    IReadOnlyCollection<ToolDefinition> Tools)
{
    /// <summary>宿主收到新流数据时刷新空闲计时；不生成展示事件，也不发送给上游。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Action? OnStreamActivity { get; init; }
}

/// <summary>
/// 模型流式输出事件。ToolCall 在参数完整累积后一次性发出。
/// </summary>
public abstract record ModelStreamEvent;

public sealed record TextDeltaEvent(string Text) : ModelStreamEvent;

public sealed record ReasoningDeltaEvent(string Text, bool IsSummary = false) : ModelStreamEvent;

public sealed record ModelToolCallEvent(ToolCall ToolCall) : ModelStreamEvent;

public sealed record ModelCompletedEvent(string FinishReason, ModelTokenUsage? Usage = null, string? ModelId = null) : ModelStreamEvent;

/// <summary>
/// 模型客户端抽象。Phase 1 仅要求 streaming + tool calling。
/// </summary>
public interface IModelClient
{
    IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// 模型调用失败。Message 只允许安全摘要，禁止包含 Secret 或请求/响应正文。
/// 结构化字段（StatusCode/ErrorCode/Kind/UpstreamMessage）供 UI 组装多语言错误详情；
/// UpstreamMessage 必须已过 <see cref="ModelErrorClassifier.SanitizeUpstreamMessage"/> 脱敏。
/// </summary>
public sealed class ModelClientException : Exception
{
    public ModelClientException(string message) : base(message)
    {
    }

    public ModelClientException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public ModelClientException(
        string message,
        ModelErrorKind kind,
        int? statusCode = null,
        string? errorCode = null,
        string? upstreamMessage = null,
        Exception? innerException = null) : base(message, innerException)
    {
        Kind = kind;
        StatusCode = statusCode;
        ErrorCode = errorCode;
        UpstreamMessage = upstreamMessage;
    }

    /// <summary>服务端建议的重试等待时间，仅用于后台调度，不含响应正文。</summary>
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>错误类别，用于映射多语言文案（assistant.error.kind.*）。</summary>
    public ModelErrorKind Kind { get; init; } = ModelErrorKind.Unknown;

    /// <summary>HTTP 状态码；网络层失败时为 null。</summary>
    public int? StatusCode { get; init; }

    /// <summary>Provider 返回的错误码/类型（如 invalid_api_key、rate_limit_exceeded）。</summary>
    public string? ErrorCode { get; init; }

    /// <summary>脱敏后的上游错误描述（截断、遮蔽 Key 片段）。</summary>
    public string? UpstreamMessage { get; init; }
}
