using DiskPilotX.Core.Services.Scanning;

namespace DiskPilotX.App.Localization;

/// <summary>让普通入口和无头入口使用相同的扫描范围统计文案。</summary>
public static class ScanProgressFormatter
{
    public static string Format(ScanProgress progress) => progress.IsVolumeIndex
        ? L.Format("ScanVolumeIndexProgressStatus", progress.CurrentPath, progress.FileCount, progress.FolderCount)
        : progress.FileCount > 0 || progress.FolderCount > 0
            ? L.Format("ScanProgressStatus", progress.CurrentPath, progress.FileCount, progress.FolderCount)
            : progress.CurrentPath;
    public static string FormatDecision(ScanExecutionInfo? execution)
    {
        if (execution?.Decision is not { } d) return string.Empty;
        string method = execution.Method switch
        {
            ScanMethod.Mft => L.Text("ScanRouteMft"),
            ScanMethod.UsnIncremental => L.Text("ScanRouteUsn"),
            ScanMethod.DirectoryTraversal => L.Text("ScanRouteTraversal"),
            _ => L.Text("ScanRouteEvaluating")
        };
        string capability = L.Format("ScanRouteCapability", d.Capability.Elevated ? L.Text("ScanRouteElevated") : L.Text("ScanRouteNotElevated"), d.Capability.FileSystem);
        string estimates = d.TraversalCost is { } traversal && d.MftCost is { } mft
            ? L.Format("ScanRouteCosts", traversal.LowerMilliseconds / 1000, traversal.UpperMilliseconds / 1000,
                mft.LowerMilliseconds / 1000, mft.UpperMilliseconds / 1000, Math.Max(traversal.Samples,mft.Samples))
            : L.Text("ScanRouteCostUnknown");
        return $"{method} · {capability}\n{d.Explanation}\n{estimates}";
    }
}
