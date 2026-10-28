namespace TinyHarness;

public enum AgentSessionState
{
    Created,
    Running,
    Completed,
    Failed,
    Cancelled,
}

public sealed record AgentSessionOptions
{
    /// <summary>
    /// 最大模型调用步数，防止 Agent 无限循环。
    /// </summary>
    public int MaxSteps { get; init; } = 8;

    /// <summary>
    /// 等待下一条模型消息的空闲期限；持续推送不受请求总时长限制。
    /// </summary>
    public TimeSpan ModelTimeout { get; init; } = TimeSpan.FromMinutes(5);

    public string? SystemPrompt { get; init; }
}

/// <summary>
/// 小助手会话：保存消息历史与生命周期状态。历史永不包含 Secret 值。
/// </summary>
public sealed class AgentSession
{
    private readonly List<ChatMessage> messages = [];
    private readonly List<AgentEvent> activities = [];

    public AgentSession(AgentSessionOptions? options = null)
    {
        Options = options ?? new AgentSessionOptions();
        if (Options.SystemPrompt is { Length: > 0 } systemPrompt)
        {
            messages.Add(ChatMessage.System(systemPrompt));
        }
    }

    public string Id { get; private set; } = Guid.NewGuid().ToString("N");

    /// <summary>从持久化恢复会话标识，保证再次保存时写回同一文件。</summary>
    internal void RestoreId(string id) => Id = id;

    public AgentSessionOptions Options { get; private set; }

    public AgentSessionState State { get; private set; } = AgentSessionState.Created;

    public IReadOnlyList<ChatMessage> Messages => messages;
    public IReadOnlyList<AgentEvent> Activities => activities;

    internal void AddMessage(ChatMessage message) => messages.Add(ToolCallProjection.EnsureSafe(message));

    /// <summary>补齐中断运行遗留的工具结果，保证下一次请求仍满足工具调用协议。</summary>
    internal int RepairDanglingToolCalls(string cancelledResult)
    {
        var repaired = 0;
        for (var index = 0; index < messages.Count; index++)
        {
            var assistantMessage = messages[index];
            if (assistantMessage.Role != ChatRole.Assistant || assistantMessage.ToolCalls.Count == 0)
            {
                continue;
            }

            var insertIndex = index + 1;
            var respondedCallIds = new HashSet<string>(StringComparer.Ordinal);
            while (insertIndex < messages.Count && messages[insertIndex].Role == ChatRole.Tool)
            {
                if (messages[insertIndex].ToolCallId is { Length: > 0 } toolCallId)
                {
                    respondedCallIds.Add(toolCallId);
                }

                insertIndex++;
            }

            foreach (var toolCall in assistantMessage.ToolCalls)
            {
                if (!string.IsNullOrEmpty(toolCall.Id) && respondedCallIds.Contains(toolCall.Id))
                {
                    continue;
                }

                messages.Insert(
                    insertIndex++,
                    ToolCallProjection.EnsureSafe(ChatMessage.ToolResult(toolCall, cancelledResult)));
                if (!string.IsNullOrEmpty(toolCall.Id))
                {
                    respondedCallIds.Add(toolCall.Id);
                }
                repaired++;
            }

            index = insertIndex - 1;
        }

        return repaired;
    }

    /// <summary>应用宿主当前系统策略；替换历史策略并保留原消息标识，避免追加重复策略消息。</summary>
    internal void ApplySystemPrompt(string systemPrompt, int? maxSteps = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);
        if (maxSteps is <= 0) throw new ArgumentOutOfRangeException(nameof(maxSteps));
        var persistedSystem = messages.FirstOrDefault(message => message.Role == ChatRole.System);
        messages.RemoveAll(message => message.Role == ChatRole.System);
        var currentSystem = persistedSystem is null
            ? ChatMessage.System(systemPrompt)
            : ChatMessage.System(systemPrompt) with
            {
                Id = persistedSystem.Id,
                Timestamp = persistedSystem.Timestamp,
            };
        messages.Insert(0, currentSystem);
        Options = Options with { SystemPrompt = systemPrompt, MaxSteps = maxSteps ?? Options.MaxSteps };
    }

    /// <summary>从持久化恢复消息（不清空系统提示之外的校验，内容由存储层保证安全）。</summary>
    internal void RestoreMessage(ChatMessage message) => messages.Add(ToolCallProjection.EnsureSafe(message));
    internal void RecordActivity(AgentEvent activity) => activities.Add(activity);

    /// <summary>从持久化恢复状态；Running 属于崩溃残留，恢复为 Cancelled。</summary>
    internal void RestoreState(AgentSessionState state) =>
        State = state == AgentSessionState.Running ? AgentSessionState.Cancelled : state;

    internal void MarkRunning() => State = AgentSessionState.Running;

    internal void MarkCompleted() => State = AgentSessionState.Completed;

    internal void MarkFailed() => State = AgentSessionState.Failed;

    internal void MarkCancelled() => State = AgentSessionState.Cancelled;
}
