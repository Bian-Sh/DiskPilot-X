using Microsoft.Extensions.Logging;
using System.Diagnostics;
using DiskPilotX.Core.Models;

namespace DiskPilotX.Core.Services.Scanning;

public partial class IncrementalFileScanner
{
    private ScanSession? TryIncrementalRescan(
        VolumeScanState state,
        IProgress<ScanProgress> progress,
        CancellationToken ct)
    {
        try
        {
            progress.Report(new ScanProgress("Reading change journal...", 0, 0));

            var changes = UsnJournalReader.ReadChanges(state.Watermark!, ct);
            if (changes == null)
            {
                _logger.LogWarning("ReadChanges returned null; falling back to full scan");
                return null;
            }

            if (ct.IsCancellationRequested)
                return null;

            if (changes.Count == 0)
            {
                // No changes — return the cached session with refreshed drive info
                RefreshDriveInfo(state.CachedSession!);
                state.CachedSession!.StartTime = DateTime.Now;
                state.CachedSession!.EndTime = DateTime.Now;

                // Update watermark to current position
                var newWatermark = UsnJournalReader.QueryJournal(state.Watermark!.VolumeRoot);
                if (newWatermark != null)
                    state.Watermark = newWatermark;

                progress.Report(new ScanProgress(
                    "No changes detected",
                    state.CachedSession!.TotalFiles,
                    state.CachedSession!.TotalFolders));

                return state.CachedSession;
            }

            progress.Report(new ScanProgress(
                $"Applying {changes.Count} changes...",
                state.CachedSession!.TotalFiles,
                state.CachedSession!.TotalFolders));

            ApplyChanges(changes, state, ct);

            if (ct.IsCancellationRequested)
                return null;

            // Recount totals
            var (fileCount, folderCount) = CountEntries(state.CachedSession!.RootEntry!);
            state.CachedSession!.TotalFiles = fileCount;
            state.CachedSession!.TotalFolders = folderCount;
            state.CachedSession!.TotalSize = state.CachedSession!.RootEntry!.Size;
            state.CachedSession!.StartTime = DateTime.Now;
            state.CachedSession!.EndTime = DateTime.Now;

            RefreshDriveInfo(state.CachedSession!);

            // Update watermark
            var updatedWatermark = UsnJournalReader.QueryJournal(state.Watermark!.VolumeRoot);
            if (updatedWatermark != null)
                state.Watermark = updatedWatermark;
            else
                return null; // Can't get new watermark — fallback next time

            progress.Report(new ScanProgress(
                "Incremental rescan complete",
                state.CachedSession!.TotalFiles,
                state.CachedSession!.TotalFolders));

            return state.CachedSession;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Incremental rescan failed");
            return null; // Any error — fallback to full scan
        }
    }

    private static void ApplyChanges(List<UsnChange> changes, VolumeScanState state, CancellationToken ct)
    {
        // Process in order: Deletes → Renames → Creates → Modifies
        var deletes = new List<UsnChange>();
        var renames = new List<UsnChange>();
        var creates = new List<UsnChange>();
        var modifies = new List<UsnChange>();

        foreach (var change in changes)
        {
            switch (change.Kind)
            {
                case UsnChangeKind.Delete: deletes.Add(change); break;
                case UsnChangeKind.Rename: renames.Add(change); break;
                case UsnChangeKind.Create: creates.Add(change); break;
                case UsnChangeKind.Modify: modifies.Add(change); break;
            }
        }

        foreach (var del in deletes)
        {
            if (ct.IsCancellationRequested) return;
            ApplyDelete(del, state);
        }

        foreach (var ren in renames)
        {
            if (ct.IsCancellationRequested) return;
            ApplyRename(ren, state);
        }

        foreach (var create in creates)
        {
            if (ct.IsCancellationRequested) return;
            ApplyCreate(create, state);
        }

        foreach (var mod in modifies)
        {
            if (ct.IsCancellationRequested) return;
            ApplyModify(mod, state);
        }
    }

    private static void ApplyDelete(UsnChange change, VolumeScanState state)
    {
        if (!state.FrnIndex.TryGetValue(change.ParentFileReferenceNumber, out var parentDir))
            return; // Parent not in our tree — skip

        var child = parentDir.Children.FirstOrDefault(c =>
            string.Equals(c.Name, change.FileName, StringComparison.OrdinalIgnoreCase));

        if (child == null)
            return;

        parentDir.RemoveChild(child);

        // If it was a directory, remove from FRN index
        if (child.IsDirectory && child.FileReferenceNumber != 0)
        {
            state.FrnIndex.Remove(child.FileReferenceNumber);
            // Also remove any descendant directories from FRN index
            RemoveDescendantFrns(child, state.FrnIndex);
        }
    }

    private static void RemoveDescendantFrns(FileEntry dir, Dictionary<long, FileEntry> frnIndex)
    {
        foreach (var child in dir.Children)
        {
            if (child.IsDirectory)
            {
                if (child.FileReferenceNumber != 0)
                    frnIndex.Remove(child.FileReferenceNumber);
                RemoveDescendantFrns(child, frnIndex);
            }
        }
    }

    private static void ApplyRename(UsnChange change, VolumeScanState state)
    {
        // The USN record tells us the new parent FRN and new name
        if (!state.FrnIndex.TryGetValue(change.ParentFileReferenceNumber, out var newParent))
            return; // New parent not in our tree — skip

        // Find the entry by its FRN (directories) or by old parent + old name (files)
        FileEntry? entry = null;

        if (change.IsDirectory && state.FrnIndex.TryGetValue(change.FileReferenceNumber, out var dirEntry))
        {
            entry = dirEntry;
        }
        else if (change.OldParentFileReferenceNumber != 0
            && change.OldFileName != null
            && state.FrnIndex.TryGetValue(change.OldParentFileReferenceNumber, out var oldParentDir))
        {
            // Find the file in its old parent by old name
            entry = oldParentDir.Children.FirstOrDefault(c =>
                string.Equals(c.Name, change.OldFileName, StringComparison.OrdinalIgnoreCase));
        }

        if (entry == null)
            return;

        // Remove from old parent
        var oldParent = entry.Parent;
        if (oldParent != null)
        {
            oldParent.Children.Remove(entry);
            oldParent.RecalculateSize();
        }

        // Update entry
        entry.Name = change.FileName;
        entry.Path = Path.Combine(newParent.Path, change.FileName);
        entry.Parent = newParent;
        entry.Depth = newParent.Depth + 1;

        // Add to new parent
        newParent.Children.Add(entry);
        newParent.RecalculateSize();
    }

    private static void ApplyCreate(UsnChange change, VolumeScanState state)
    {
        if (!state.FrnIndex.TryGetValue(change.ParentFileReferenceNumber, out var parentDir))
            return; // Parent not in our tree — skip

        var fullPath = Path.Combine(parentDir.Path, change.FileName);

        if (change.IsDirectory)
        {
            var newDir = new FileEntry
            {
                Path = fullPath,
                Name = change.FileName,
                IsDirectory = true,
                Depth = parentDir.Depth + 1,
                Parent = parentDir
            };

            // Get FRN for the new directory
            var frn = NtfsUsnNative.GetFileReferenceNumber(fullPath);
            if (frn != 0)
            {
                newDir.FileReferenceNumber = frn;
                state.FrnIndex[frn] = newDir;
            }

            parentDir.Children.Add(newDir);
        }
        else
        {
            long size = 0;
            long allocatedSize = 0;
            try
            {
                var fi = new FileInfo(fullPath);
                if (fi.Exists)
                {
                    size = fi.Length;
                    allocatedSize = FileScanner.GetAllocatedSize(fullPath, size);
                }
            }
            catch
            {
                // Best-effort size
            }

            var newFile = new FileEntry
            {
                Path = fullPath,
                Name = change.FileName,
                IsDirectory = false,
                Extension = Path.GetExtension(change.FileName)?.ToLowerInvariant(),
                Size = size,
                AllocatedSize = allocatedSize,
                HasAllocatedSize = true,
                Depth = parentDir.Depth + 1,
                Parent = parentDir
            };

            parentDir.Children.Add(newFile);
            parentDir.RecalculateSize();
        }
    }

    private static void ApplyModify(UsnChange change, VolumeScanState state)
    {
        if (!state.FrnIndex.TryGetValue(change.ParentFileReferenceNumber, out var parentDir))
            return; // Parent not in our tree — skip

        var child = parentDir.Children.FirstOrDefault(c =>
            string.Equals(c.Name, change.FileName, StringComparison.OrdinalIgnoreCase));

        if (child == null || child.IsDirectory)
            return; // Only refresh size for files

        try
        {
            var fi = new FileInfo(child.Path);
            if (fi.Exists)
            {
                child.Size = fi.Length;
                child.AllocatedSize = FileScanner.GetAllocatedSize(child.Path, child.Size);
                child.HasAllocatedSize = true;
                child.LastModified = fi.LastWriteTime;
                parentDir.RecalculateSize();
            }
        }
        catch
        {
            // Best-effort — if we can't read the file, leave size as-is
        }
    }

    private static (int files, int folders) CountEntries(FileEntry root)
    {
        int files = 0;
        int folders = 0;
        var stack = new Stack<FileEntry>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var entry = stack.Pop();
            if (entry.IsDirectory)
            {
                folders++;
                foreach (var child in entry.Children)
                    stack.Push(child);
            }
            else
            {
                files++;
            }
        }

        return (files, folders);
    }

    private static void RefreshDriveInfo(ScanSession session)
    {
        try
        {
            var rootPath = Path.GetPathRoot(session.TargetPath);
            if (rootPath != null
                && string.Equals(Path.GetFullPath(session.TargetPath), rootPath, StringComparison.OrdinalIgnoreCase))
            {
                var driveInfo = new DriveInfo(rootPath);
                if (driveInfo.IsReady)
                {
                    session.DriveCapacity = driveInfo.TotalSize;
                    session.DriveFreeSpace = driveInfo.TotalFreeSpace;
                }
            }
        }
        catch
        {
            // Best-effort
        }
    }

}
