using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Settings;
using DiskPilotX.Core.Services.Whitelist;

namespace DiskPilotX.Core.Services.Scanning;

/// <summary>
/// Decorator around FileScanner that uses the NTFS USN change journal
/// for near-instant rescans of previously-scanned paths.
/// Falls back to full scan on any failure.
/// </summary>
public partial class IncrementalFileScanner : IFileScanner
{
    private readonly FileScanner _inner;
    private readonly ISettingsService? _settingsService;
    private readonly IPathWhitelistMatcher _whitelistMatcher;
    private readonly ILogger<IncrementalFileScanner> _logger;
    private readonly Dictionary<string, VolumeScanState> _volumeStates = new(StringComparer.OrdinalIgnoreCase);
    private Task? _pendingIndexBuild;

    public bool IsReady => _pendingIndexBuild is null or { IsCompleted: true };
    public event Action? IsReadyChanged;

    public IncrementalFileScanner(FileScanner inner, ISettingsService? settingsService = null, IPathWhitelistMatcher? whitelistMatcher = null, ILogger<IncrementalFileScanner>? logger = null)
    {
        _inner = inner;
        _settingsService = settingsService;
        _whitelistMatcher = whitelistMatcher ?? new PathWhitelistMatcher();
        _logger = logger ?? NullLogger<IncrementalFileScanner>.Instance;
    }

    public async Task<ScanSession> ScanAsync(
        string path,
        IProgress<ScanProgress> progress,
        CancellationToken cancellationToken)
    {
        // If a background FRN index build is still running, wait for it.
        if (_pendingIndexBuild is { IsCompleted: false })
        {
            _logger.LogInformation("Waiting for background FRN index build to finish");
            progress.Report(new ScanProgress("Finalizing directory index...", 0, 0));
            await _pendingIndexBuild.ConfigureAwait(false);
        }

        var fullPath = ScanPathResolver.Resolve(path);
        var execution = new ScanExecutionInfo(fullPath, ScanMethod.Unknown, ScanExecutionStatus.Running, DateTime.Now);
        void Enter(ScanMethod method)
        {
            execution = execution with { Method = method };
            progress.Report(new ScanProgress("扫描路线：" + method, 0, 0) { Execution = execution });
        }
        void Fallback(string reason) => execution = execution with { FallbackReasons = [.. execution.FallbackReasons ?? [], reason] };
        ScanSession Complete(ScanSession scanned)
        {
            scanned.Execution = execution with { Status = scanned.IsCancelled ? ScanExecutionStatus.Cancelled : ScanExecutionStatus.Completed, CompletedAt = scanned.EndTime ?? DateTime.Now };
            progress.Report(new ScanProgress("扫描结束", scanned.TotalFiles, scanned.TotalFolders) { Execution = scanned.Execution });
            return scanned;
        }
        var scanWhitelist = _settingsService?.LoadSettings().ScanWhitelist ?? [];
        if (_whitelistMatcher.IsExcluded(fullPath, scanWhitelist))
        {
            throw new InvalidOperationException("Scan target is excluded by whitelist settings.");
        }

        var volumeRoot = Path.GetPathRoot(fullPath);

        _logger.LogInformation("Incremental scan requested: fullPath={FullPath}, volumeRoot={VolumeRoot}, hasState={HasState}", fullPath, volumeRoot, volumeRoot != null && _volumeStates.ContainsKey(volumeRoot));

        // Can we do incremental?
        if (volumeRoot != null
            && _volumeStates.TryGetValue(volumeRoot, out var state)
            && state.Watermark != null
            && string.Equals(state.ScannedPath, fullPath, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Trying USN incremental path: cachedPath={CachedPath}, nextUsn={NextUsn}", state.ScannedPath, state.Watermark.NextUsn);
            var result = await Task.Run(
                () => { Enter(ScanMethod.UsnIncremental); return TryIncrementalRescan(state, progress, cancellationToken); },
                cancellationToken).ConfigureAwait(false);
            if (result != null)
            {
                // 增量结果建立新的扫描标识，保留上一次结果的路线事实。
                var refreshed = new ScanSession { TargetPath = result.TargetPath, RootEntry = result.RootEntry,
                    StartTime = execution.StartedAt ?? result.StartTime, EndTime = result.EndTime,
                    TotalFiles = result.TotalFiles, TotalFolders = result.TotalFolders, TotalSize = result.TotalSize,
                    DriveCapacity = result.DriveCapacity, DriveFreeSpace = result.DriveFreeSpace, Provenance = result.Provenance };
                state.CachedSession = refreshed;
                return Complete(refreshed);
            }
            cancellationToken.ThrowIfCancellationRequested();
            Fallback("USN 增量扫描未成功，继续尝试其他路线。");
            // Fallback — incremental failed, do full scan below
        }

        // Try MFT scan (fast path for NTFS volumes)
        if (volumeRoot != null)
        {
            _logger.LogInformation("Attempting MFT scan");
            var mftResult = await Task.Run(
                () => { Enter(ScanMethod.Mft); return TryMftScan(fullPath, volumeRoot, progress, cancellationToken); },
                cancellationToken).ConfigureAwait(false);

            if (mftResult != null)
            {
                var (mftSession, frnIndex) = mftResult.Value;

                // FRN index was built during tree construction — just capture watermark
                _pendingIndexBuild = Task.Run(() =>
                {
                    try
                    {
                        var watermark = UsnJournalReader.QueryJournal(volumeRoot);
                        if (watermark != null)
                        {
                            _logger.LogInformation("Post-MFT watermark captured: journalId={JournalId}, nextUsn={NextUsn}", watermark.JournalId, watermark.NextUsn);
                            _volumeStates[volumeRoot] = new VolumeScanState
                            {
                                ScannedPath = fullPath,
                                CachedSession = mftSession,
                                Watermark = watermark,
                                FrnIndex = frnIndex
                            };
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Post-MFT watermark capture failed");
                    }
                }).ContinueWith(_ => IsReadyChanged?.Invoke(), TaskScheduler.Default);
                IsReadyChanged?.Invoke();

                return Complete(mftSession);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (volumeRoot != null) Fallback("MFT 扫描未成功，回退普通目录遍历。");
        Enter(ScanMethod.DirectoryTraversal);
        // Full scan (first scan or fallback)
        _logger.LogInformation("Performing full scan via FileScanner");
        var session = await _inner.ScanAsync(fullPath, progress, cancellationToken).ConfigureAwait(false);

        if (!session.IsCancelled && session.RootEntry != null && volumeRoot != null)
        {
            // Build FRN index in the background so the treemap renders immediately.
            // The index is only needed for the *next* rescan. If the user rescans
            // before this completes, we await it at the top of ScanAsync.
            _pendingIndexBuild = Task.Run(() =>
            {
                try
                {
                    CaptureWatermark(session, fullPath, volumeRoot);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "CaptureWatermark failed");
                }
            }).ContinueWith(_ => IsReadyChanged?.Invoke(), TaskScheduler.Default);
            IsReadyChanged?.Invoke();
        }

        return Complete(session);
    }

    private class VolumeScanState
    {
        public string ScannedPath { get; set; } = string.Empty;
        public ScanSession? CachedSession { get; set; }
        public UsnWatermark? Watermark { get; set; }
        public Dictionary<long, FileEntry> FrnIndex { get; set; } = new();
    }
}
