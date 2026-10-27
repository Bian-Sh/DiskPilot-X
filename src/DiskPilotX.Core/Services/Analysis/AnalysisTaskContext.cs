using DiskPilotX.Core.Models;

namespace DiskPilotX.Core.Services.Analysis;

public enum AnalysisTaskStatus { NotStarted, Running, WaitingForUser, Completed, Failed, Cancelled }
public enum AnalysisOutcome { Unknown, Recommendations, NoRecommendations, Partial, Restricted, NeedsInput }
public sealed record ScanProvenance(string Source, string? ConversationId = null, string? MessageId = null,
    string Goal = "扫描指定范围", string? RuleId = null, string? RuleVersion = null);
public sealed record AnalysisTaskResult(Guid Id, Guid ScanId, string ScopePath, AnalysisTaskStatus Status,
    AnalysisOutcome Outcome, int AcceptedCount, IReadOnlyList<string> Unassessed, IReadOnlyDictionary<string, string> RuleVersions);

/// <summary>生命周期和分析结论分开；空数组不能证明整个范围已完成评估。</summary>
public sealed class AnalysisTaskContext(Guid scanId, string scopePath)
{
    public Guid Id { get; } = Guid.NewGuid();
    public Guid ScanId { get; } = scanId;
    public string ScopePath { get; } = scopePath;
    public AnalysisTaskStatus Status { get; set; } = AnalysisTaskStatus.Running;
    public AnalysisOutcome Outcome { get; private set; } = AnalysisOutcome.Unknown;
    public bool EvidenceRead { get; set; }
    public bool Reported { get; private set; }
    public int AcceptedCount { get; set; }
    public Dictionary<string, string> RuleVersions { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Unassessed { get; } = [];
    public void Defer(string reason)
    {
        var safe = reason.Length > 500 ? reason[..500] : reason;
        if (Unassessed.Count < 100 && !Unassessed.Contains(safe)) Unassessed.Add(safe);
    }
    public void Complete(AnalysisDiagnostics? diagnostics, bool coverageComplete)
    {
        Reported = true;
        Status = AnalysisTaskStatus.Completed;
        var restricted = diagnostics is not null && diagnostics.ProtectedFilteredCount + diagnostics.ExcludedFilteredCount + diagnostics.HostFilteredCount > 0;
        Outcome = !coverageComplete || !EvidenceRead || diagnostics is null || Unassessed.Count > 0 ? AnalysisOutcome.Partial
            : restricted && AcceptedCount == 0 ? AnalysisOutcome.Restricted
            : AcceptedCount > 0 ? AnalysisOutcome.Recommendations : AnalysisOutcome.NoRecommendations;
    }
    public AnalysisTaskResult Snapshot() => new(Id, ScanId, ScopePath, Status, Outcome, AcceptedCount,
        Unassessed.ToArray(), new Dictionary<string, string>(RuleVersions));
    public bool Contains(string path) => IsWithin(path, ScopePath);
    public static bool IsWithin(string path, string scope)
    {
        if (!Path.IsPathFullyQualified(path) || !Path.IsPathFullyQualified(scope)) return false;
        try
        {
            var full = Path.GetFullPath(path).TrimEnd('\\', '/');
            var root = Path.GetFullPath(scope).TrimEnd('\\', '/');
            return full.Equals(root, StringComparison.OrdinalIgnoreCase) || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException) { return false; }
    }
}
