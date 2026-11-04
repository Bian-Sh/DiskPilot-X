using System.Text.Json;
using System.Text.Json.Nodes;
using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Analysis;

namespace DiskPilotX.Core.Services.Assistant;

public sealed partial class DiskAssistantService
{
    // 在宿主确认返回后、写入列表前检查，不能用写入后的失败代替事务边界。
    // 隐私签名仅供宿主核验，绝不能作为模型上下文的数据字段。
    private static object? PublicScanSummary(AnalysisScanContext? summary) => summary is null ? null : new
    {
        scan_id = summary.ScanId, scope_path = summary.ScopePath, source = summary.Source,
        conversation_id = summary.ConversationId, message_id = summary.MessageId, goal = summary.Goal,
        preserve = summary.Preserve, rule_id = summary.RuleId, rule_version = summary.RuleVersion
    };

    private ScanProvenance? VisibleScanOrigin(ScanSession? scan)
    {
        var origin = scan?.Provenance;
        if (origin?.ConversationId is not { } id) return origin;
        try
        {
            if (conversationStore?.ReadMetadata(id) is { } metadata && NormalizePrivacySignature(metadata.PrivacySignature) == CurrentPrivacy()) return origin;
        }
        catch (Exception e) when (ConversationStore.IsStorageError(e) || e is ArgumentException) { }
        return origin with { Goal = "来源会话不可访问，仅使用当前扫描事实。", MessageId = null };
    }

    private static string SummarizeRunGoal(string message)
    {
        var text = System.Text.RegularExpressions.Regex.Replace(message, @"[a-zA-Z]:[\\/][^\s]*|\\\\[^\s]+", "所选路径");
        text = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (!IsPublicAnalysisQuery(text)) return "用户请求扫描指定范围";
        return text.Length > 500 ? text[..500] : text;
    }

    private bool CanCommitAnalysis(JsonNode? args) => AnalysisCommitFailure(args) is null;

    private string? AnalysisCommitFailure(JsonNode? args)
    {
        if (analysisRules is not null && readRuleDigests.Count > 0)
        {
            var catalog=analysisRules.GetCatalog();
            if(readRuleDigests.Any(x=>!catalog.Any(rule=>rule.Id.Equals(x.Key,StringComparison.OrdinalIgnoreCase)&&rule.Digest==x.Value)))return "rules_changed";
        }
        if(args?["analysis_mode"]?.GetValue<bool>()!=true && args?["task_id"] is null)return null;
        if(analysisTask is null || args?["task_id"]?.GetValue<string>()!=analysisTask.Id.ToString())return "task_expired";
        if(args?["scan_id"]?.GetValue<string>()!=analysisTask.ScanId.ToString())return "scan_changed";
        if(analysisPrivacy!=CurrentPrivacy())return "privacy_changed";
        if(analysisScanPolicy!=JsonSerializer.Serialize(settings.LoadSettings().ScanWhitelist))return "scan_policy_changed";
        if(analysisContexts is not null && analysisPreferences!=JsonSerializer.Serialize(analysisContexts.GetPreferences(analysisTask.ScopePath)))return "preferences_changed";
        return null;
    }

    private void AccumulateDiagnostics(AnalysisDiagnostics d)
    {
        // 清单可替换，但已观察到的限制不能被下一次空发布抹掉。
        d.ProtectedFilteredCount = accumulatedDiagnostics.ProtectedFilteredCount = Math.Max(accumulatedDiagnostics.ProtectedFilteredCount, d.ProtectedFilteredCount);
        d.ExcludedFilteredCount = accumulatedDiagnostics.ExcludedFilteredCount = Math.Max(accumulatedDiagnostics.ExcludedFilteredCount, d.ExcludedFilteredCount);
        d.HostFilteredCount = accumulatedDiagnostics.HostFilteredCount = Math.Max(accumulatedDiagnostics.HostFilteredCount, d.HostFilteredCount);
        d.MissingEntryCount = accumulatedDiagnostics.MissingEntryCount = Math.Max(accumulatedDiagnostics.MissingEntryCount, d.MissingEntryCount);
        d.MalformedRecommendationCount = accumulatedDiagnostics.MalformedRecommendationCount = Math.Max(accumulatedDiagnostics.MalformedRecommendationCount, d.MalformedRecommendationCount);
        d.MissingFieldRecommendationCount = accumulatedDiagnostics.MissingFieldRecommendationCount = Math.Max(accumulatedDiagnostics.MissingFieldRecommendationCount, d.MissingFieldRecommendationCount);
        d.ParseError = accumulatedDiagnostics.ParseError = d.ParseError ?? accumulatedDiagnostics.ParseError;
    }

    public void RecordCleanupUsage(IEnumerable<CleanupAction> results)
    {
        if (analysisContexts is null || !analysisContexts.UsageRecordingEnabled) return;
        var succeeded = results.Where(x => x.Result == CleanupResult.Success).ToArray();
        if (succeeded.Length == 0) return;
        completedCleanupThisRun = true;
        var topics = succeeded.SelectMany(x => recommendationTopics.TryGetValue(x.Recommendation.TargetPath, out var known) && known.Length > 0
            ? known : new[] { "category-" + x.Recommendation.Category.ToString().ToLowerInvariant() }).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var topic in topics)
        {
            try { analysisContexts.RecordUsage(topic, "cleanup", true); }
            catch (Exception e) when (ConversationStore.IsStorageError(e) || e is ArgumentException) { }
        }
    }
}
