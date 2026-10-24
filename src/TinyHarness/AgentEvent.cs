namespace TinyHarness;

/// <summary>
/// Agent 结构化事件类型。UI 通过事件投影，不直接绑定内部状态。
/// </summary>
public enum AgentEventKind
{
    SessionStarted,
    StepStarted,
    TextDelta,
    ReasoningDelta,
    MessageCompleted,
    ToolCallStarted,
    ToolCallCompleted,
    SkillLoaded,
    SubagentStarted,
    SubagentCompleted,
    TaskCompleted,
    TaskFailed,
    TaskCancelled,
    PersistenceFailed,
    WaitingForUser,
    ToolApprovalRequested,

    // 可扩展事件（规格 #15）
    SecretStored,
    BrowserTargetCreated,
    BrowserTargetClosed,
    ConfigBackupCreated,
    ConfigChanged,
    TestStarted,
    TestCompleted,
}

/// <summary>
/// 小助手事件。只允许携带安全摘要，禁止包含 Secret、请求/响应正文等敏感内容。
/// </summary>
public sealed record AgentEvent(
    string SessionId,
    AgentEventKind Kind,
    DateTimeOffset Timestamp)
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string? Text { get; init; }
    public string? ToolName { get; init; }
    public string? ToolCallId { get; init; }
    public bool? Success { get; init; }
    public string? Detail { get; init; }
    public int? Step { get; init; }
    public bool IsSummary { get; init; }
    public ChatMessage? Message { get; init; }

    public static AgentEvent Create(string sessionId, AgentEventKind kind) =>
        new(sessionId, kind, DateTimeOffset.UtcNow);
}
