namespace DiskPilotX.Core.Services.Scanning;

/// <summary>区间仅来自近期成功实测；缺少证据时为null，不能把未知变成精确ETA。</summary>
public sealed record ScanCostRange(double LowerMilliseconds, double UpperMilliseconds, int Samples, string Source);
public sealed record ScanVolumeCapability(string VolumeKey, string FileSystem, bool Elevated, bool CanUseMft,
    string ReasonCode, string Description, int? Win32Error = null);
public sealed record ScanRouteDecision(string PolicyVersion, ScanMethod PlannedMethod, string ReasonCode, string Explanation,
    ScanVolumeCapability Capability, ScanCostRange? TraversalCost = null, ScanCostRange? MftCost = null,
    int ProbeEntryLimit = 2048, int ProbeMilliseconds = 500, double RequiredSavingRatio = .25, int RequiredSavingMilliseconds = 2000,
    long? EstimatedIndexMemoryBytes = null)
{
    public TraversalObservation? Observation { get; init; }
}
public sealed record ScanRouteAttempt(ScanMethod Method, DateTime StartedAt, DateTime CompletedAt, string Outcome,
    string Phase, int? Win32Error = null, string? Detail = null);
public sealed record TraversalObservation(long Entries, int PendingDirectories, TimeSpan Elapsed, bool Completed);
public sealed record ScanPerformanceSample(string VolumeKey, string ScopeKey, ScanMethod Method, long Entries,
    double ElapsedMilliseconds, double IndexMilliseconds, double TreeMilliseconds, double SizeMilliseconds,
    long VolumeRecords, DateTime RecordedAt, string PolicyVersion = "adaptive-v1");

/// <summary>纯决策函数：预测未来剩余代价，不把GB或已花费时间当成未来收益。</summary>
public static class ScanRoutePolicy
{
    public static ScanRouteDecision Decide(ScanVolumeCapability capability, bool wholeVolume,
        IReadOnlyList<ScanPerformanceSample> history, string scopeKey, DateTime now, TraversalObservation? probe = null)
    {
        var recent = history.Where(s => s.VolumeKey == capability.VolumeKey && s.PolicyVersion == "adaptive-v1"
            && s.RecordedAt <= now && now - s.RecordedAt <= TimeSpan.FromDays(7) && s.Entries > 0
            && s.ElapsedMilliseconds > 0 && double.IsFinite(s.ElapsedMilliseconds)
            && s.IndexMilliseconds >= 0 && double.IsFinite(s.IndexMilliseconds)
            && s.TreeMilliseconds >= 0 && double.IsFinite(s.TreeMilliseconds)
            && s.SizeMilliseconds >= 0 && double.IsFinite(s.SizeMilliseconds)).ToArray();
        if (!capability.CanUseMft)
            return Result(ScanMethod.DirectoryTraversal, capability.ReasonCode, capability.Description);
        bool hasVolumeEvidence = recent.Any(s => s.ScopeKey == scopeKey && s.Method == ScanMethod.DirectoryTraversal)
            && recent.Any(s => s.Method == ScanMethod.Mft && s.VolumeRecords > 0);
        if (wholeVolume && !hasVolumeEvidence) return Result(ScanMethod.Mft, "whole_volume", "目标是整个卷；采用卷索引路线，首次需读取整卷索引，耗时尚未实测。");
        var scopes = recent.Where(s => s.ScopeKey == scopeKey).ToArray();
        var mfts = recent.Where(s => s.Method == ScanMethod.Mft && s.VolumeRecords > 0).ToArray();
        if (scopes.Length == 0 || mfts.Length == 0)
            return Result(ScanMethod.DirectoryTraversal, "insufficient_evidence", "正在渐进探测：缺少近期有效规模或MFT成本记录，暂不能可靠估算；已收集结果直接继续使用。");
        double observed = probe?.Entries ?? 0;
        double lowerEntries = Math.Max(0, scopes.Min(s => s.Entries) * .65 - observed);
        double upperEntries = Math.Max(lowerEntries, scopes.Max(s => s.Entries) * 1.5 - observed);
        var traversalSamples = recent.Where(s => s.Method == ScanMethod.DirectoryTraversal).ToArray();
        if (traversalSamples.Length == 0) return Result(ScanMethod.DirectoryTraversal, "insufficient_evidence", "缺少本卷目录遍历速率，继续探测，不虚报总量和预计时间。");
        var rates = traversalSamples.Select(s => s.ElapsedMilliseconds / s.Entries).ToList();
        if (probe is { Entries: >= 128 } && probe.Elapsed.TotalMilliseconds > 0)
            rates.Add(probe.Elapsed.TotalMilliseconds / probe.Entries);
        var traverse = new ScanCostRange(lowerEntries * rates.Min() * .7, upperEntries * rates.Max() * 1.5,
            traversalSamples.Length, "本卷近7日成功遍历与同范围规模；使用保守区间");
        var indexTimes = mfts.Select(s => s.IndexMilliseconds).Where(x => x > 0 && double.IsFinite(x)).ToArray();
        if (indexTimes.Length == 0) return Result(ScanMethod.DirectoryTraversal, "insufficient_evidence", "缺少MFT索引阶段测量，继续遍历，不只凭提权选择快速扫描。");
        long expectedMemory = mfts.Max(s => s.VolumeRecords) > long.MaxValue / 256 ? long.MaxValue : mfts.Max(s => s.VolumeRecords) * 256;
        if (expectedMemory > 1024L * 1024 * 1024)
            return new("adaptive-v1", ScanMethod.DirectoryTraversal, "index_memory_budget",
                "继续目录遍历：按近期卷记录数估计，重建索引可能超过1 GiB内存预算；不为预期提速冒内存风险。",
                capability, traverse, null, EstimatedIndexMemoryBytes: expectedMemory);
        double targetEntries = scopes.Max(s => s.Entries);
        double treeUnit = mfts.Max(s => s.TreeMilliseconds / Math.Max(1, s.Entries));
        double sizeUnit = mfts.Max(s => s.SizeMilliseconds / Math.Max(1, s.Entries));
        var mft = new ScanCostRange(indexTimes.Min() * .8 + targetEntries * (treeUnit + sizeUnit) * .65,
            indexTimes.Max() * 1.5 + targetEntries * (treeUnit + sizeUnit) * 1.5, mfts.Length,
            "本卷近7日MFT索引、目标构树与大小采集阶段；包含首次索引成本");
        bool advantage = traverse.LowerMilliseconds > 0 && mft.UpperMilliseconds <= traverse.LowerMilliseconds * .75
            && traverse.LowerMilliseconds - mft.UpperMilliseconds >= 2000;
        return new("adaptive-v1", advantage ? ScanMethod.Mft : ScanMethod.DirectoryTraversal,
            advantage ? "measured_advantage" : "insufficient_saving",
            advantage ? "选择MFT：保守预计至少快25%且节省2秒；读取卷索引后只返回所选范围。"
                : "继续目录遍历：MFT的保守收益未同时达到快25%和节省2秒；不为不确定收益重建卷索引。",
            capability, traverse, mft, EstimatedIndexMemoryBytes: expectedMemory);

        ScanRouteDecision Result(ScanMethod method, string code, string reason) => new("adaptive-v1", method, code, reason, capability);
    }
}
