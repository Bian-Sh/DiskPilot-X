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
}
