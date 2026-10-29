using DiskPilotX.Core.Models;

namespace DiskPilotX.Core.Services.Scanning;

public record ScanProgress(string CurrentPath, int FileCount, int FolderCount)
{
    public ScanExecutionInfo? Execution { get; init; }

    /// <summary>此阶段计数是整盘索引记录处理量，不是目标目录文件数量。</summary>
    public bool IsVolumeIndex { get; init; }
}

public interface IFileScanner
{
    /// <summary>
    /// True when the scanner is ready for a new scan. False while post-scan
    /// background work (e.g. building the FRN index) is still in progress.
    /// </summary>
    bool IsReady { get; }

    /// <summary>
    /// Raised when <see cref="IsReady"/> changes.
    /// </summary>
    event Action? IsReadyChanged;

    Task<ScanSession> ScanAsync(string path, IProgress<ScanProgress> progress, CancellationToken cancellationToken);
}
