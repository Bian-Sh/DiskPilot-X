using DiskPilotX.Core.Models;

namespace DiskPilotX.Core.Services.Scanning;

public record ScanProgress(string CurrentPath, int FileCount, int FolderCount);

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
