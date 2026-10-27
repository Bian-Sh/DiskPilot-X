using System.Text.Json.Nodes;
using DiskPilotX.Core.Services.Agent;
using DiskPilotX.Core.Services.Copilot;
using DiskPilotX.Core.Services.Llm;
using TinyHarness;

namespace DiskPilotX.Core.Services.Assistant;

public sealed record AssistantRunOptions(ModelEndpointConfig Endpoint,
    string Language = "zh-CN", IReadOnlyList<AiSkill>? Skills = null, string? FocusPath = null, bool IsAnalysis = false,
    bool InConversationSession = false, string? MessageId = null);

/// <summary>FailureDetail 携带 Harness 的失败摘要（已脱敏），供分析入口直接展示失败原因。</summary>
public sealed record AssistantRunResult(string Text, AgentSessionState State,
    DiskPilotX.Core.Models.AnalysisResult? Analysis = null, string? FailureDetail = null, DiskPilotX.Core.Services.Analysis.AnalysisTaskResult? Task = null);

/// <summary>宿主的实时工作区连接；工具调用每次读取当前扫描，避免捕获过期视图。</summary>
public sealed class AssistantWorkspace
{
    public Func<JsonNode?, bool> ValidateAnalysisCommit { get; set; } = _ => true;
    public Func<CancellationToken, Task<AgentContext>> ReadContext { get; set; } =
        _ => Task.FromResult(new AgentContext(null, null, null, null));
    public Func<string, JsonNode?, CancellationToken, Task<JsonNode>> Execute { get; set; } =
        (_, _, _) => Task.FromResult<JsonNode>(new JsonObject { ["ok"] = false, ["error"] = "磁盘工作区尚未连接。" });
}
