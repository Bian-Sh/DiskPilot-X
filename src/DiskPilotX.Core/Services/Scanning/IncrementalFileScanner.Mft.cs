using Microsoft.Extensions.Logging;
using System.Diagnostics;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Whitelist;

namespace DiskPilotX.Core.Services.Scanning;

public partial class IncrementalFileScanner
{
    private (ScanSession session, Dictionary<long, FileEntry> frnIndex)? TryMftScan(
        string fullPath,
        string volumeRoot,
        IProgress<ScanProgress> progress,
        CancellationToken ct)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInformation("MFT scan starting: fullPath={FullPath}, volumeRoot={VolumeRoot}", fullPath, volumeRoot);

            // Phase 1: Enumerate MFT
            var mftRecords = MftEnumerator.EnumerateVolume(volumeRoot, progress, ct, logger: _logger);
            if (mftRecords == null)
            {
                _logger.LogWarning("MFT EnumerateVolume returned null; falling back");
                return null;
            }

            if (ct.IsCancellationRequested)
                return null;

            _logger.LogInformation("MFT enumeration complete: {RecordCount} records in {ElapsedMs}ms", mftRecords.Count, sw.ElapsedMilliseconds);

            // Phase 2: Build tree from flat records
            // Count files/folders from MFT for progress reporting during tree build
            int mftFiles = 0, mftFolders = 0;
            foreach (var rec in mftRecords.Values)
            {
                if (rec.IsDirectory) mftFolders++;
                else mftFiles++;
            }
            progress.Report(new ScanProgress("Building directory tree", mftFiles, mftFolders));

            var isWholeVolume = string.Equals(
                Path.GetFullPath(fullPath).TrimEnd('\\'),
                volumeRoot.TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase);
            _logger.LogDebug("MFT scan whole volume: {IsWholeVolume}", isWholeVolume);

            // Get the actual FRN for the target path. Don't hardcode segment 5 for root 鈥?
            // the full FRN includes a sequence number in the upper 16 bits.
            var targetFrn = NtfsUsnNative.GetFileReferenceNumber(fullPath);
            _logger.LogDebug("MFT target FRN: {TargetFrn:X16} segment {Segment}", targetFrn, targetFrn & 0x0000_FFFF_FFFF_FFFF);
            if (targetFrn == 0)
            {
                _logger.LogWarning("GetFileReferenceNumber returned 0; falling back");
                return null;
            }

            // Check if targetFrn exists in MFT records
            _logger.LogDebug("MFT target FRN found in records: {Found}", mftRecords.ContainsKey(targetFrn));

            // Determine which FRNs belong to the target subtree
            HashSet<long> subtreeFrns;
            if (isWholeVolume)
            {
                // All records are in scope
                subtreeFrns = new HashSet<long>(mftRecords.Keys);
            }
            else
            {
                // BFS from target FRN to collect descendant FRNs
                subtreeFrns = new HashSet<long> { targetFrn };
                var bfsQueue = new Queue<long>();
                bfsQueue.Enqueue(targetFrn);

                // Build parent鈫抍hildren lookup for BFS
                var childrenByParent = new Dictionary<long, List<long>>();
                foreach (var (frn, rec) in mftRecords)
                {
                    if (!childrenByParent.TryGetValue(rec.ParentFileReferenceNumber, out var list))
                    {
                        list = new List<long>();
                        childrenByParent[rec.ParentFileReferenceNumber] = list;
                    }
                    list.Add(frn);
                }

                while (bfsQueue.Count > 0)
                {
                    var parentFrn = bfsQueue.Dequeue();
                    if (childrenByParent.TryGetValue(parentFrn, out var children))
                    {
                        foreach (var childFrn in children)
                        {
                            if (subtreeFrns.Add(childFrn))
                                bfsQueue.Enqueue(childFrn);
                        }
                    }
                }
            }

            _logger.LogDebug("MFT subtree FRN count: {Count}", subtreeFrns.Count);

            // Ensure the target FRN is in the subtree set 鈥?FSCTL_ENUM_USN_DATA may not
            // return certain system entries (e.g. root directory has no USN record).
            subtreeFrns.Add(targetFrn);

            // Create FileEntry for each record in the subtree
            var frnToEntry = new Dictionary<long, FileEntry>(subtreeFrns.Count);
            foreach (var frn in subtreeFrns)
            {
                if (mftRecords.TryGetValue(frn, out var rec))
                {
                    frnToEntry[frn] = new FileEntry
                    {
                        Name = rec.FileName,
                        IsDirectory = rec.IsDirectory,
                        IsReparsePoint = (rec.FileAttributes & (uint)FileAttributes.ReparsePoint) != 0,
                        Attributes = (FileAttributes)rec.FileAttributes,
                        FileReferenceNumber = rec.FileReferenceNumber
                    };
                }
                else if (frn == targetFrn)
                {
                    // Synthetic root 鈥?not returned by FSCTL_ENUM_USN_DATA (no USN record)
                    var dirInfo = new DirectoryInfo(fullPath);
                    frnToEntry[frn] = new FileEntry
                    {
                        Name = dirInfo.Name,
                        IsDirectory = true,
                        IsReparsePoint = (dirInfo.Attributes & FileAttributes.ReparsePoint) != 0,
                        Attributes = dirInfo.Attributes,
                        FileReferenceNumber = targetFrn
                    };
                    _logger.LogWarning("Created synthetic MFT root for FRN {TargetFrn:X16}", targetFrn);
                }
            }

            _logger.LogDebug("MFT entry map count: {Count}", frnToEntry.Count);
            progress.Report(new ScanProgress("Linking directory tree", mftFiles, mftFolders));

            // Link parent/children
            foreach (var (frn, entry) in frnToEntry)
            {
                if (frn == targetFrn)
                    continue;

                var rec = mftRecords[frn];
                if (frnToEntry.TryGetValue(rec.ParentFileReferenceNumber, out var parent))
                {
                    entry.Parent = parent;
                    parent.Children.Add(entry);
                }
            }

            var rootEntry = frnToEntry[targetFrn];
            if (IsWholeVolumeRoot(fullPath))
            {
                rootEntry.Children.RemoveAll(child => string.Equals(
                    child.Name,
                    "System Volume Information",
                    StringComparison.OrdinalIgnoreCase));
            }
            _logger.LogDebug("MFT root entry: name={Name}, children={Children}", rootEntry.Name, rootEntry.Children.Count);

            // Set root path and reconstruct paths via BFS
            rootEntry.Path = fullPath;
            rootEntry.Depth = 0;
            var pathQueue = new Queue<FileEntry>();
            pathQueue.Enqueue(rootEntry);
            while (pathQueue.Count > 0)
            {
                var parent = pathQueue.Dequeue();
                foreach (var child in parent.Children)
                {
                    child.Path = Path.Combine(parent.Path, child.Name);
                    child.Depth = parent.Depth + 1;
                    if (child.IsDirectory)
                        pathQueue.Enqueue(child);
                }
            }

            PruneWhitelistedEntries(rootEntry, BuildNormalizedWhitelist(_settingsService?.LoadSettings().ScanWhitelist ?? []));

            // Free MFT records 鈥?no longer needed
            mftRecords = null;

            _logger.LogInformation("MFT tree built: {EntryCount} entries in {ElapsedMs}ms", frnToEntry.Count, sw.ElapsedMilliseconds);

            if (ct.IsCancellationRequested)
                return null;

            // Phase 3: Collect sizes via parallel directory enumeration
            var (fileCount, folderCount) = ParallelSizeCollector.CollectSizes(
                rootEntry, mftFiles, mftFolders, progress, ct);

            if (ct.IsCancellationRequested)
                return null;

            _logger.LogInformation("MFT size collection complete: {FileCount} files, {FolderCount} folders in {ElapsedMs}ms", fileCount, folderCount, sw.ElapsedMilliseconds);

            // Phase 4: Build session
            var session = new ScanSession
            {
                TargetPath = fullPath,
                StartTime = DateTime.Now,
                TotalFiles = fileCount,
                TotalFolders = folderCount,
                TotalSize = rootEntry.Size,
                EndTime = DateTime.Now,
                RootEntry = rootEntry
            };

            FileScanner.PopulateDriveInfo(session, fullPath);

            _logger.LogInformation("MFT scan completed in {ElapsedMs}ms", sw.ElapsedMilliseconds);

            return (session, frnToEntry);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MFT scan failed");
            return null;
        }
    }


    private static void PruneWhitelistedEntries(FileEntry root, IReadOnlyList<string> normalizedWhitelist)
    {
        if (normalizedWhitelist.Count == 0)
        {
            return;
        }

        for (var index = root.Children.Count - 1; index >= 0; index--)
        {
            var child = root.Children[index];
            if (IsWhitelistedPath(child.Path, normalizedWhitelist))
            {
                root.Children.RemoveAt(index);
                continue;
            }

            if (child.IsDirectory)
            {
                PruneWhitelistedEntries(child, normalizedWhitelist);
            }
        }
    }

    private static IReadOnlyList<string> BuildNormalizedWhitelist(IEnumerable<PathWhitelistEntry> whitelist)
    {
        return whitelist
            .Select(entry => TryNormalizePath(entry.Path))
            .Where(path => path is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsWhitelistedPath(string path, IReadOnlyList<string> normalizedWhitelist)
    {
        var normalizedPath = TryNormalizePath(path);
        if (normalizedPath is null)
        {
            return false;
        }

        return normalizedWhitelist.Any(prefix => string.Equals(normalizedPath, prefix, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(prefix + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(prefix + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    private static string? TryNormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path.Trim())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            return null;
        }
    }

    private static bool IsWholeVolumeRoot(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        return !string.IsNullOrEmpty(root)
               && string.Equals(
                   fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                   root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                   StringComparison.OrdinalIgnoreCase);
    }

}
