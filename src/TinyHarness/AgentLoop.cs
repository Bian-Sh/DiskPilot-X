using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace TinyHarness;

/// <summary>工具审批门：返回 true 批准执行，false 拒绝。仅在逐条批准模式下对写/删工具触发。</summary>
public delegate Task<bool> ToolApprovalGate(ToolCall toolCall, ToolDefinition tool, CancellationToken cancellationToken);

/// <summary>
/// 最小 Agent 循环：
/// 用户 → 模型 →（工具调用 → 工具结果 → 模型）→ 回答。
/// 支持 streaming、tool calling、cancellation、timeout、max steps 与错误恢复。
/// </summary>
public sealed class AgentLoop
{
    private const string CancelledToolResult = "{\"cancelled\":true,\"reason\":\"assistant_run_cancelled\"}";
    private readonly IModelClient modelClient;
    private readonly ToolRegistry toolRegistry;
    private readonly ILogger<AgentLoop> logger;
    private readonly ToolApprovalGate? approvalGate;
    private readonly Func<Exception, string> failureFormatter;
    private readonly Func<int, string> maxStepsFormatter;
    private readonly Func<TimeSpan, CancellationToken, Task> retryDelay;

    public AgentLoop(
        IModelClient modelClient,
        ToolRegistry toolRegistry,
        ILogger<AgentLoop> logger,
        ToolApprovalGate? approvalGate = null,
        Func<Exception, string>? failureFormatter = null,
        Func<int, string>? maxStepsFormatter = null,
        Func<TimeSpan, CancellationToken, Task>? retryDelay = null)
    {
        this.modelClient = modelClient;
        this.toolRegistry = toolRegistry;
        this.logger = logger;
        this.approvalGate = approvalGate;
        this.failureFormatter = failureFormatter ?? (exception => exception.Message);
        this.maxStepsFormatter = maxStepsFormatter ?? (steps => $"超过最大步骤数：{steps}");
        this.retryDelay = retryDelay ?? ((delay, token) => Task.Delay(delay, token));
    }

    public async IAsyncEnumerable<AgentEvent> RunAsync(
        AgentSession session,
        string userMessage,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.State == AgentSessionState.Running)
        {
            throw new InvalidOperationException("会话正在运行中。");
        }

        var repairedBeforeRun = session.RepairDanglingToolCalls(CancelledToolResult);
        if (repairedBeforeRun > 0)
        {
            logger.LogWarning("Agent 发送前修复未闭合工具调用 {SessionId} {Count}", session.Id, repairedBeforeRun);
        }

        session.MarkRunning();
        session.AddMessage(ChatMessage.User(userMessage));
        yield return AgentEvent.Create(session.Id, AgentEventKind.SessionStarted);
        yield return AgentEvent.Create(session.Id, AgentEventKind.MessageCompleted) with { Message = session.Messages[^1] };

        var completed = false;
        var cancelled = false;
        string? failedDetail = null;
        // 用户取消 AskUser 后，本轮不再展示面板；若模型仍重复调用则复用原取消结果。
        var disabledToolResults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var step = 0; step < session.Options.MaxSteps && !completed && !cancelled && failedDetail is null; step++)
        {
            yield return AgentEvent.Create(session.Id, AgentEventKind.StepStarted) with { Step = step + 1 };

            var textBuilder = new StringBuilder();
            var blocks = new List<ChatContentBlock>();
            var toolCalls = new List<ToolCall>();
            var finishReason = "stop";
            var completionReceived = false;
            ModelTokenUsage? usage = null;

            // 逐条拉取模型流。yield 不允许出现在带 catch 的 try 内，因此仅在 try 中移动枚举器，事件在 try 外处理。
            var availableTools = disabledToolResults.Count == 0
                ? toolRegistry.All
                : toolRegistry.All.Where(tool => !disabledToolResults.ContainsKey(tool.Name)).ToArray();
            await using var enumerator = StreamWithRetryAsync(
                new ModelRequest(session.Messages, availableTools), session.Options.ModelTimeout,
                session.Id, step + 1, cancellationToken).GetAsyncEnumerator(cancellationToken);

            while (failedDetail is null && !cancelled)
            {
                ModelStreamEvent? streamEvent = null;
                var moved = false;
                try
                {
                    moved = await enumerator.MoveNextAsync();
                    if (moved) streamEvent = enumerator.Current;
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        cancelled = true;
                    }
                    else
                    {
                        logger.LogWarning("Agent 模型请求超时 {SessionId} 步骤 {Step}", session.Id, step + 1);
                        failedDetail = failureFormatter(new ModelClientException("模型请求超时。", ModelErrorKind.Timeout));
                    }
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Agent 模型请求失败 {SessionId} 步骤 {Step}", session.Id, step + 1);
                    failedDetail = failureFormatter(exception);
                }

                if (!moved || streamEvent is null) break;

                switch (streamEvent)
                {
                    case TextDeltaEvent textDelta:
                        textBuilder.Append(textDelta.Text);
                        AppendBlock(blocks, ChatContentKind.Text, textDelta.Text);
                        yield return AgentEvent.Create(session.Id, AgentEventKind.TextDelta) with { Text = textDelta.Text };
                        break;
                    case ReasoningDeltaEvent reasoningDelta:
                        AppendBlock(blocks, ChatContentKind.Thinking, reasoningDelta.Text, reasoningDelta.IsSummary);
                        yield return AgentEvent.Create(session.Id, AgentEventKind.ReasoningDelta) with
                        {
                            Text = reasoningDelta.Text,
                            IsSummary = reasoningDelta.IsSummary,
                        };
                        break;
                    case ModelToolCallEvent toolCallEvent:
                        // 防御：忽略 name 为空的无效工具调用（部分上游会附带空占位调用），
                        // 避免其被误判为「未注册工具」并以空 tool_call_id 回传历史导致 400。
                        if (string.IsNullOrWhiteSpace(toolCallEvent.ToolCall.Name))
                        {
                            logger.LogWarning("Agent 忽略了空名称的工具调用 {SessionId} 步骤 {Step}", session.Id, step + 1);
                            break;
                        }
                        toolCalls.Add(toolCallEvent.ToolCall);
                        break;
                    case ModelCompletedEvent completedEvent:
                        usage = completedEvent.Usage;
                        finishReason = completedEvent.FinishReason;
                        completionReceived = true;
                        break;
                }
            }

            if (cancelled || failedDetail is not null) break;

            if (!completionReceived)
            {
                failedDetail = failureFormatter(new ModelClientException(
                    "模型响应无效：服务未返回完成事件。", ModelErrorKind.Unknown,
                    upstreamMessage: "模型响应无效：服务未返回完成事件。"));
                logger.LogWarning("Agent 模型响应缺少完成事件 {SessionId} 步骤 {Step}", session.Id, step + 1);
                break;
            }

            // 纵深防御：极少数上游会把同一 tool_call_id 扇出成多份重复 delta；
            // 即使解析端已按 id 去重，这里再做一次兜底——同一 assistant 消息内
            // tool_call_id 必须唯一，否则把历史回传给上游时违反 OpenAI 协议触发 400。
            // 保留首次出现的完整调用（arguments 已逐步追加完整）。
            var dedupedToolCalls = DedupeToolCallsById(toolCalls);
            var safeToolCalls = dedupedToolCalls
                .Select(call => ToolCallProjection.Project(
                    call,
                    toolRegistry.TryGet(call.Name, out var definition) ? definition : null))
                .ToArray();

            blocks.AddRange(safeToolCalls.Select(call => new ChatContentBlock(ChatContentKind.ToolCall, ToolCall: call)));
            session.AddMessage(ChatMessage.AssistantToolCalls(
                safeToolCalls,
                textBuilder.Length > 0 ? textBuilder.ToString() : null) with { Blocks = blocks, Usage = usage });
            yield return AgentEvent.Create(session.Id, AgentEventKind.MessageCompleted) with { Message = session.Messages[^1], Step = step + 1 };

            if (dedupedToolCalls.Count == 0)
            {
                completed = true;
                logger.LogDebug("Agent 任务完成 {SessionId} 步骤 {Step} 结束原因 {FinishReason}", session.Id, step + 1, finishReason);
                break;
            }

            for (var toolIndex = 0; toolIndex < dedupedToolCalls.Count; toolIndex++)
            {
                var toolCall = dedupedToolCalls[toolIndex];
                var safeToolCall = safeToolCalls[toolIndex];
                yield return AgentEvent.Create(session.Id, AgentEventKind.ToolCallStarted) with
                {
                    ToolName = safeToolCall.Name,
                    ToolCallId = safeToolCall.Id,
                };

                // 逐条批准模式：写/删工具先等用户批准
                if (approvalGate is not null
                    && toolRegistry.TryGet(toolCall.Name, out var pendingTool)
                    && pendingTool is not null
                    && RequiresApproval(pendingTool.RiskLevel))
                {
                    yield return AgentEvent.Create(session.Id, AgentEventKind.ToolApprovalRequested) with
                    {
                        ToolName = safeToolCall.Name,
                        ToolCallId = safeToolCall.Id,
                        Detail = SummarizeArguments(safeToolCall.ArgumentsJson),
                    };

                    bool approved;
                    try
                    {
                        approved = await approvalGate(safeToolCall, pendingTool, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        cancelled = true;
                        break;
                    }

                    if (!approved)
                    {
                        logger.LogInformation("Agent 工具调用被用户拒绝 {ToolName}", safeToolCall.Name);
                        session.AddMessage(ChatMessage.ToolResult(safeToolCall, "用户拒绝了这次修改操作，没有执行。"));
                        yield return AgentEvent.Create(session.Id, AgentEventKind.MessageCompleted) with { Message = session.Messages[^1] };
                        yield return AgentEvent.Create(session.Id, AgentEventKind.ToolCallCompleted) with
                        {
                            ToolName = safeToolCall.Name,
                            ToolCallId = safeToolCall.Id,
                            Success = false,
                            Detail = "已被你拒绝，未执行。",
                        };
                        continue;
                    }
                }

                ToolResult? result;
                bool toolCancelled;
                if (disabledToolResults.TryGetValue(toolCall.Name, out var disabledResult))
                {
                    result = ToolResult.Ok(disabledResult);
                    toolCancelled = false;
                    logger.LogInformation("Agent 忽略了已取消的重复工具调用 {ToolName}", safeToolCall.Name);
                }
                else
                {
                    (result, toolCancelled) = await ExecuteToolAsync(toolCall, cancellationToken);
                }

                if (toolCancelled)
                {
                    cancelled = true;
                    break;
                }

                result = result!.EnsureSafeFailure();
                session.AddMessage(ChatMessage.ToolResult(safeToolCall, result.Content));
                yield return AgentEvent.Create(session.Id, AgentEventKind.MessageCompleted) with { Message = session.Messages[^1] };

                yield return AgentEvent.Create(session.Id, AgentEventKind.ToolCallCompleted) with
                {
                    ToolName = safeToolCall.Name,
                    ToolCallId = safeToolCall.Id,
                    Success = result.Success,
                    Detail = result.Success ? null : ModelErrorClassifier.SanitizeUpstreamMessage(result.Content),
                };
                if (IsCancelledAskUserResult(safeToolCall.Name, result.Content))
                {
                    disabledToolResults.TryAdd(safeToolCall.Name, result.Content);
                }
            }
        }

        if (cancelled)
        {
            var repairedOnCancel = session.RepairDanglingToolCalls(CancelledToolResult);
            if (repairedOnCancel > 0)
            {
                logger.LogInformation("Agent 取消时补齐未闭合工具调用 {SessionId} {Count}", session.Id, repairedOnCancel);
            }

            session.MarkCancelled();
            yield return AgentEvent.Create(session.Id, AgentEventKind.TaskCancelled);
            yield break;
        }

        if (completed)
        {
            session.MarkCompleted();
            yield return AgentEvent.Create(session.Id, AgentEventKind.TaskCompleted);
            yield break;
        }

        session.MarkFailed();
        yield return AgentEvent.Create(session.Id, AgentEventKind.TaskFailed) with
        {
            Detail = failedDetail ?? maxStepsFormatter(session.Options.MaxSteps),
        };
    }

    /// <summary>仅在尚未产生流式事件时重试模型步骤；首次请求计入五次总尝试。</summary>
    private async IAsyncEnumerable<ModelStreamEvent> StreamWithRetryAsync(
        ModelRequest request, TimeSpan timeout, string sessionId, int step,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            logger.LogInformation("Agent 模型尝试开始 {SessionId} {Step} {Attempt}", sessionId, step, attempt);
            Exception? failure = null;
            var receivedEvent = false;
            using (var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                attemptTimeout.CancelAfter(timeout);
                IAsyncEnumerator<ModelStreamEvent>? enumerator = null;
                try
                {
                    enumerator = modelClient.StreamAsync(request, attemptTimeout.Token).GetAsyncEnumerator(attemptTimeout.Token);
                }
                catch (Exception exception)
                {
                    failure = exception;
                }

                if (enumerator is not null)
                {
                    await using (enumerator)
                    {
                        while (failure is null)
                        {
                            var moved = false;
                            try { moved = await enumerator.MoveNextAsync(); }
                            catch (Exception exception) { failure = exception; }
                            if (!moved) break;
                            receivedEvent = true;
                            yield return enumerator.Current;
                        }
                    }
                }
            }

            if (failure is null)
            {
                logger.LogInformation("Agent 模型尝试完成 {SessionId} {Step} {Attempt}", sessionId, step, attempt);
                yield break;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (failure is OperationCanceledException)
                failure = new ModelClientException("模型请求超时。", ModelErrorKind.Timeout);
            else if (failure is IOException or HttpRequestException)
                failure = new ModelClientException("模型连接中断。", ModelErrorKind.ConnectionFailed);

            var error = failure as ModelClientException;
            var kind = error?.Kind ?? ModelErrorKind.Unknown;
            if (kind == ModelErrorKind.Unknown && error is not null)
                kind = ModelErrorClassifier.Classify(error.StatusCode, error.ErrorCode);
            var retryable = kind is ModelErrorKind.ConnectionFailed or ModelErrorKind.Timeout or
                ModelErrorKind.RateLimited or ModelErrorKind.ServerOverloaded or ModelErrorKind.ServerError;
            if (!retryable || receivedEvent || attempt == 5)
            {
                logger.LogWarning("Agent 模型尝试终止 {SessionId} {Step} {Attempt} {Retryable} {ReceivedEvent}",
                    sessionId, step, attempt, retryable, receivedEvent);
                throw failure;
            }

            var serverDelay = (failure as ModelClientException)?.RetryAfter;
            var delay = serverDelay is { } hint
                ? TimeSpan.FromSeconds(Math.Clamp(hint.TotalSeconds, 0, 60))
                : TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
            logger.LogWarning(failure, "Agent 模型准备重试 {SessionId} {Step} {Attempt} {DelayMs} {StatusCode} {Kind}",
                sessionId, step, attempt + 1, delay.TotalMilliseconds,
                (failure as ModelClientException)?.StatusCode, (failure as ModelClientException)?.Kind);
            await retryDelay(delay, cancellationToken);
        }
    }

    private static void AppendBlock(List<ChatContentBlock> blocks, ChatContentKind kind, string delta, bool summary = false)
    {
        if (blocks.Count > 0 && blocks[^1].Kind == kind && blocks[^1].IsSummary == summary)
            blocks[^1] = blocks[^1] with { Text = blocks[^1].Text + delta };
        else
            blocks.Add(new ChatContentBlock(kind, delta, summary));
    }

    /// <summary>修改类操作（写/删）才需要逐条批准；只读与外部测试直接放行。</summary>
    private static bool RequiresApproval(ToolRiskLevel riskLevel) =>
        riskLevel is ToolRiskLevel.Write or ToolRiskLevel.Destructive;

    /// <summary>
    /// 按 tool_call_id 去重。空 id（极端情况）保留所有项，避免误丢。
    /// 仅对 id 非空的调用折叠到首次出现的条目上（首次最接近完整 arguments 累积起点）。
    /// </summary>
    private static List<ToolCall> DedupeToolCallsById(IReadOnlyList<ToolCall> calls)
    {
        if (calls.Count <= 1) return calls.ToList();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<ToolCall>(calls.Count);
        foreach (var call in calls)
        {
            if (string.IsNullOrEmpty(call.Id))
            {
                result.Add(call);
                continue;
            }
            if (!seen.Add(call.Id)) continue;
            result.Add(call);
        }
        return result;
    }

    /// <summary>审批卡片展示的参数摘要：截断过长的参数 JSON，仅用于 UI 展示。</summary>
    private static string SummarizeArguments(string? argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson)) return string.Empty;
        var compact = argumentsJson.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return compact.Length <= 300 ? compact : compact[..300] + "…";
    }

    private static bool IsCancelledAskUserResult(string toolName, string content)
    {
        if (!string.Equals(toolName, "ask_user", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            return JsonNode.Parse(content) is JsonObject result
                && result["cancelled"] is JsonValue cancelledValue
                && cancelledValue.TryGetValue<bool>(out var cancelled)
                && cancelled;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// 执行工具。Cancelled = true 表示用户取消了整个会话（区别于工具自身超时）。
    /// </summary>
    private async Task<(ToolResult? Result, bool Cancelled)> ExecuteToolAsync(ToolCall toolCall, CancellationToken cancellationToken)
    {
        if (!toolRegistry.TryGet(toolCall.Name, out var tool) || tool is null)
        {
            logger.LogWarning("Agent 调用了未注册的工具");
            return (ToolResult.SafeFail("未注册的工具。"), false);
        }

        JsonNode? arguments;
        try
        {
            arguments = string.IsNullOrWhiteSpace(toolCall.ArgumentsJson) ? null : JsonNode.Parse(toolCall.ArgumentsJson);
        }
        catch (JsonException)
        {
            return (ToolResult.SafeFail("工具参数不是有效的 JSON。"), false);
        }

        using var toolTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        toolTimeout.CancelAfter(tool.Timeout);
        try
        {
            return (await tool.Handler(arguments, toolTimeout.Token), false);
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) return (null, true);
            logger.LogWarning("Agent 工具执行超时 {ToolName}", tool.Name);
            return (ToolResult.SafeFail("工具执行超时。"), false);
        }
        catch (Exception exception)
        {
            var safeException = new InvalidOperationException("工具处理器执行失败。");
            logger.LogError(
                safeException,
                "Agent 工具执行失败 {ToolName} {ExceptionType}",
                tool.Name,
                exception.GetType().FullName ?? exception.GetType().Name);
            return (ToolResult.SafeFail("工具执行失败。"), false);
        }
    }
}
