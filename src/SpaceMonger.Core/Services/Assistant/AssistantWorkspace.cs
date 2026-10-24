using System.Text.Json.Nodes;
using SpaceMonger.Core.Services.Agent;
using SpaceMonger.Core.Services.Copilot;
using TinyHarness;

namespace SpaceMonger.Core.Services.Assistant;

public sealed record AssistantRunOptions(string ApiKey, string? BaseUrl, string Model,
    bool EnableThinking = false, string Language = "zh-CN", IReadOnlyList<AiSkill>? Skills = null, string? FocusPath = null, bool IsAnalysis = false);

public sealed record AssistantRunResult(string Text, AgentSessionState State, SpaceMonger.Core.Models.AnalysisResult? Analysis = null);

/// <summary>宿主的实时工作区连接；工具调用每次读取当前扫描，避免捕获过期视图。</summary>
public sealed class AssistantWorkspace
{
    public Func<CancellationToken, Task<AgentContext>> ReadContext { get; set; } =
        _ => Task.FromResult(new AgentContext(null, null, null, null));
    public Func<string, JsonNode?, CancellationToken, Task<JsonNode>> Execute { get; set; } =
        (_, _, _) => Task.FromResult<JsonNode>(new JsonObject { ["ok"] = false, ["error"] = "磁盘工作区尚未连接。" });
}
