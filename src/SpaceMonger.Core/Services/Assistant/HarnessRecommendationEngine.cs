using TinyHarness;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Analysis;

namespace SpaceMonger.Core.Services.Assistant;

/// <summary>手动分析按钮的适配器，与对话共用同一个 Harness 及推荐发布工具。</summary>
public sealed class HarnessRecommendationEngine(DiskAssistantService assistant) : IRecommendationEngine
{
    public async Task<List<CleanupRecommendation>> AnalyzeAsync(ScanSession session,string apiKey,string? baseUrl,string? modelName,bool enableThinking,string? responseLanguage,CancellationToken ct,FileEntry? focusEntry=null) =>
        (await AnalyzeWithDiagnosticsAsync(session,apiKey,baseUrl,modelName,enableThinking,responseLanguage,ct,focusEntry)).Recommendations;
    public async Task<AnalysisResult> AnalyzeWithDiagnosticsAsync(ScanSession session,string apiKey,string? baseUrl,string? modelName,bool enableThinking,string? responseLanguage,CancellationToken ct,FileEntry? focusEntry=null)
    {
        var model=!string.IsNullOrWhiteSpace(modelName)?modelName.Trim():Uri.TryCreate(baseUrl,UriKind.Absolute,out var uri)&&uri.Host.EndsWith("deepseek.com",StringComparison.OrdinalIgnoreCase)?"deepseek-v4-pro":"claude-sonnet-4-20250514";
        var result=await assistant.RunAsync("请检查当前范围的磁盘证据，生成可复核的清理建议并用 publish_recommendations 发布；无候选时也发布空数组。",new(apiKey,baseUrl,model,enableThinking,responseLanguage??"zh-CN",FocusPath:focusEntry?.Path??session.RootEntry?.Path,IsAnalysis:true),null,ct);
        if(result.Analysis is { } published) return published;
        if(result.State==AgentSessionState.Cancelled) throw new OperationCanceledException("分析已停止。",ct);
        throw new InvalidOperationException("分析未发布有效结果，请查看 Copilot 的运行状态后重试。");
    }
}
