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
        var previousSession=state.CachedSession;
        var previousIndex=state.FrnIndex;
        var previousWatermark=state.Watermark;
        var completed=false;
        try
        {
            progress.Report(new ScanProgress("Reading change journal...", 0, 0));

            // 水位先取，下一次宁可重放扫描期间变化，也不能越过未读取的并发写入。
            var refreshWatermark = UsnJournalReader.QueryJournal(state.Watermark!.VolumeRoot);
            if (refreshWatermark is null || refreshWatermark.JournalId != state.Watermark.JournalId
                || refreshWatermark.NextUsn < state.Watermark.NextUsn) return null;
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
                // 新版本即使没有文件变化也不能改写旧会话的时间和树。
                DetachCachedState(state,ct);
                RefreshDriveInfo(state.CachedSession!);
                state.CachedSession!.StartTime = DateTime.Now;
                state.CachedSession!.EndTime = DateTime.Now;

                // Update watermark to current position
                var newWatermark = refreshWatermark;
                if (newWatermark != null)
                    state.Watermark = newWatermark;

                progress.Report(new ScanProgress(
                    "No changes detected",
                    state.CachedSession!.TotalFiles,
                    state.CachedSession!.TotalFolders));

                completed=true;
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
            var updatedWatermark = refreshWatermark;
            if (updatedWatermark != null)
                state.Watermark = updatedWatermark;
            else
                return null; // Can't get new watermark — fallback next time

            progress.Report(new ScanProgress(
                "Incremental rescan complete",
                state.CachedSession!.TotalFiles,
                state.CachedSession!.TotalFolders));

            completed=true;
            return state.CachedSession;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _lastIncrementalFailure=ex.Message;
            _logger.LogWarning(ex, "Incremental rescan failed");
            return null; // 失败回退全量扫描。
        }
        finally
        {
            if(!completed) {state.CachedSession=previousSession;state.FrnIndex=previousIndex;state.Watermark=previousWatermark;}
        }
    }

    /// <summary>写时分离扫描树与FRN索引，已发布的旧会话和快照保持原样。</summary>
    private static void DetachCachedState(VolumeScanState state,CancellationToken ct)
    {
        var source=state.CachedSession!;
        static FileEntry Copy(FileEntry e)=>new() {Path=e.Path,Name=e.Name,Size=e.Size,AllocatedSize=e.AllocatedSize,
            HasAllocatedSize=e.HasAllocatedSize,Extension=e.Extension,LastModified=e.LastModified,IsDirectory=e.IsDirectory,
            IsReparsePoint=e.IsReparsePoint,IsAccessDenied=e.IsAccessDenied,IsCloudPlaceholder=e.IsCloudPlaceholder,
            Attributes=e.Attributes,ContentHash=e.ContentHash?.ToArray(),FileReferenceNumber=e.FileReferenceNumber,
            Depth=e.Depth,SubtreeItemCount=e.SubtreeItemCount,SubtreeFileCount=e.SubtreeFileCount,SubtreeFolderCount=e.SubtreeFolderCount,IsDeleted=e.IsDeleted};
        var root=Copy(source.RootEntry!);var byPath=new Dictionary<string,FileEntry>(StringComparer.OrdinalIgnoreCase);
        var pending=new Stack<(FileEntry Source,FileEntry Target)>();pending.Push((source.RootEntry!,root));
        while(pending.TryPop(out var pair))
        {
            ct.ThrowIfCancellationRequested();byPath[pair.Source.Path]=pair.Target;
            foreach(var child in pair.Source.Children) {var copy=Copy(child);copy.Parent=pair.Target;pair.Target.Children.Add(copy);pending.Push((child,copy));}
        }
        var index=state.FrnIndex.Where(pair=>byPath.ContainsKey(pair.Value.Path)).ToDictionary(pair=>pair.Key,pair=>byPath[pair.Value.Path]);
        state.CachedSession=new ScanSession {Id=source.Id,TargetPath=source.TargetPath,RootEntry=root,StartTime=source.StartTime,EndTime=source.EndTime,
            TotalFiles=source.TotalFiles,TotalFolders=source.TotalFolders,TotalSize=source.TotalSize,DriveCapacity=source.DriveCapacity,
            DriveFreeSpace=source.DriveFreeSpace,IsCancelled=source.IsCancelled,Execution=source.Execution,Provenance=source.Provenance};
        state.FrnIndex=index;
    }

    private static void ApplyChanges(List<UsnChange> changes, VolumeScanState state, CancellationToken ct)
    {
        // USN回执是可重放的历史，不是当前完整目录清单。无法确认范围/类型时应失效而不是猜结果。
        foreach (var change in changes)
            if (string.IsNullOrWhiteSpace(change.FileName) || change.FileName is "." or ".."
                || change.FileName.IndexOfAny(['\\','/']) >= 0)
                throw new InvalidOperationException("USN记录名称不符合单一目录项契约，需要重新扫描。");
        DetachCachedState(state,ct);
        // 应用顺序：删除、重命名、新建、修改。
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
            ApplyRename(ren, state, ct);
        }

        foreach (var create in creates)
        {
            if (ct.IsCancellationRequested) return;
            ApplyCreate(create, state, ct);
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

        if (child == null) return;
        if (child.FileReferenceNumber == 0 && (File.Exists(child.Path) || Directory.Exists(child.Path)))
        {
            long currentReference = NtfsUsnNative.GetFileReferenceNumber(child.Path);
            if (currentReference == 0) throw new InvalidOperationException("删除重放对象的当前身份无法确认，快照需要重新扫描。");
            child.FileReferenceNumber = currentReference;
        }
        if (child.FileReferenceNumber != 0 && change.FileReferenceNumber != 0 && child.FileReferenceNumber != change.FileReferenceNumber)
            return; // 旧删除事件不能删除同名但FRN已变的新对象。

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

    private static FileEntry? LocateChange(UsnChange change, VolumeScanState state)
    {
        if (change.IsDirectory && state.FrnIndex.TryGetValue(change.FileReferenceNumber, out var directory)) return directory;
        if (change.OldParentFileReferenceNumber != 0 && state.FrnIndex.TryGetValue(change.OldParentFileReferenceNumber, out var oldParent))
        {
            var exact = oldParent.Children.FirstOrDefault(c => c.FileReferenceNumber != 0 && c.FileReferenceNumber == change.FileReferenceNumber);
            if (exact is not null) return exact;
            return oldParent.Children.FirstOrDefault(c => string.Equals(c.Name, change.OldFileName, StringComparison.OrdinalIgnoreCase));
        }
        return null;
    }

    private static void RemoveEntry(FileEntry entry, VolumeScanState state)
    {
        if (entry.Parent is null) throw new InvalidOperationException("扫描根在日志期间移动，快照需要重新扫描。");
        if (entry.IsDirectory)
        {
            state.FrnIndex.Remove(entry.FileReferenceNumber);
            RemoveDescendantFrns(entry, state.FrnIndex);
        }
        entry.Parent.RemoveChild(entry);
    }

    private static bool Excluded(string path, VolumeScanState state) => new Whitelist.PathWhitelistMatcher().IsExcluded(path, state.ScanWhitelist);

    private static void ApplyRename(UsnChange change, VolumeScanState state, CancellationToken ct)
    {
        var entry = LocateChange(change, state);
        if (!state.FrnIndex.TryGetValue(change.ParentFileReferenceNumber, out var newParent))
        {
            // 真实新父目录不在所选范围内：移除旧位置，不能留下假装仍存在的条目。
            if (entry is not null) RemoveEntry(entry, state);
            return;
        }
        string path = Path.Combine(newParent.Path, change.FileName);
        if (Excluded(path, state))
        {
            if (entry is not null) RemoveEntry(entry, state);
            return;
        }
        entry ??= newParent.Children.FirstOrDefault(c => c.FileReferenceNumber != 0 && c.FileReferenceNumber == change.FileReferenceNumber
            || string.Equals(c.Name, change.FileName, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            // 范围外对象移入时，日志不包含已存在子孙。仅从新范围读取其现有子树。
            ApplyCreate(change, state, ct);
            return;
        }
        if (entry.FileReferenceNumber != 0 && change.FileReferenceNumber != 0 && entry.FileReferenceNumber != change.FileReferenceNumber)
            throw new InvalidOperationException("重命名重放对象身份已变化，快照需要重新扫描。");
        if (entry.Parent is null) throw new InvalidOperationException("扫描根重命名，拒绝复用旧范围快照。");
        if (entry.IsDirectory != change.IsDirectory) throw new InvalidOperationException("USN对象类型发生冲突，需要重新扫描。");
        // 路径排除会同时影响已缓存和原先被排除的后代；仅修改路径或裁剪旧树都不能证明新范围完整。
        if (entry.IsDirectory && state.ScanWhitelist.Count > 0
            && !string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("目录路径变化可能改变后代排除结果，快照失效并按当前规则重新扫描。");
        var oldParent = entry.Parent;
        oldParent.Children.Remove(entry); oldParent.RecalculateSize();
        entry.Name = change.FileName; entry.Parent = newParent;
        var pending = new Stack<FileEntry>(); pending.Push(entry);
        while (pending.TryPop(out var node))
        {
            ct.ThrowIfCancellationRequested();
            node.Path = Path.Combine(node.Parent!.Path, node.Name); node.Depth = node.Parent.Depth + 1;
            if (!node.IsDirectory) node.Extension = Path.GetExtension(node.Name).ToLowerInvariant();
            foreach (var child in node.Children) pending.Push(child);
        }
        foreach (var child in newParent.Children.Where(c => !ReferenceEquals(c, entry)
            && string.Equals(c.Name, entry.Name, StringComparison.OrdinalIgnoreCase)).ToArray()) RemoveEntry(child, state);
        newParent.Children.Add(entry); newParent.RecalculateSize();
    }

    private static void ApplyCreate(UsnChange change, VolumeScanState state, CancellationToken ct)
    {
        if (!state.FrnIndex.TryGetValue(change.ParentFileReferenceNumber, out var parent)) return;
        string path = Path.Combine(parent.Path, change.FileName);
        if (Excluded(path, state)) return;
        var existing = parent.Children.FirstOrDefault(c => c.FileReferenceNumber != 0 && c.FileReferenceNumber == change.FileReferenceNumber
            || string.Equals(c.Name, change.FileName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (existing.IsDirectory != change.IsDirectory) throw new InvalidOperationException("重放创建记录与快照类型冲突。");
            if (existing.FileReferenceNumber != 0 && change.FileReferenceNumber != 0
                && existing.FileReferenceNumber != change.FileReferenceNumber)
                throw new InvalidOperationException("重放创建记录与当前快照身份冲突，需要重新扫描。");
            // 幂等只保证不重复添加，不能跳过当前实物的存在性、类型与重解析状态核验。
            ApplyModify(change, state);
            if (existing.IsDirectory)
            {
                long currentReference = NtfsUsnNative.GetFileReferenceNumber(path);
                if (currentReference == 0 || currentReference != change.FileReferenceNumber)
                    throw new InvalidOperationException("重放目录创建记录的实物身份无法确认，快照失效并重新扫描。");
            }
            return;
        }
        FileEntry node;
        try
        {
            var attributes = File.GetAttributes(path);
            bool reparse = (attributes & FileAttributes.ReparsePoint) != 0;
            if (change.IsDirectory)
            {
                if ((attributes & FileAttributes.Directory) == 0) throw new InvalidOperationException("新建目录类型已变化。");
                if (reparse)
                    node = new FileEntry {Name=change.FileName,Path=path,IsDirectory=true,IsReparsePoint=true,Attributes=attributes};
                else
                {
                    var scanner = new FileScanner();
                    var scanned = scanner.ScanAdaptiveAsync(path,new RouteProgress(_=>{}),ct,null,state.ScanWhitelist).GetAwaiter().GetResult();
                    if (scanned.IsCancelled || scanned.RootEntry is null) throw new InvalidOperationException("移入目录未完成读取。");
                    node = scanned.RootEntry;
                }
            }
            else
            {
                if ((attributes & FileAttributes.Directory) != 0) throw new InvalidOperationException("新建文件类型已变化。");
                var info = new FileInfo(path);
                bool cloud = (attributes & ((FileAttributes)0x00400000 | (FileAttributes)0x00040000 | FileAttributes.Offline)) != 0;
                long size = cloud ? 0 : info.Length;
                node = new FileEntry {Name=change.FileName,Path=path,IsDirectory=false,IsReparsePoint=reparse,IsCloudPlaceholder=cloud,
                    Attributes=attributes,LastModified=info.LastWriteTime,Extension=Path.GetExtension(change.FileName).ToLowerInvariant(),
                    Size=size,AllocatedSize=FileScanner.GetAllocatedSize(path,size,cloud),HasAllocatedSize=true};
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { throw new InvalidOperationException("USN新建/移入对象无法确认，快照失效并重新扫描。",e); }
        node.FileReferenceNumber = change.FileReferenceNumber; node.Parent = parent;
        var pending = new Stack<FileEntry>(); pending.Push(node);
        while (pending.TryPop(out var n))
        {
            ct.ThrowIfCancellationRequested(); n.Depth = n.Parent is null ? 0 : n.Parent.Depth + 1;
            if (n.IsDirectory)
            {
                if (n.FileReferenceNumber == 0) n.FileReferenceNumber = NtfsUsnNative.GetFileReferenceNumber(n.Path);
                if (n.FileReferenceNumber == 0 && !n.IsReparsePoint)
                    throw new InvalidOperationException("移入目录引用号无法确认，拒绝建立不完整增量索引。");
                if (n.FileReferenceNumber != 0) state.FrnIndex[n.FileReferenceNumber] = n;
                foreach (var child in n.Children) pending.Push(child);
            }
        }
        parent.Children.Add(node); parent.RecalculateSize();
    }

    private static void ApplyModify(UsnChange change, VolumeScanState state)
    {
        if (!state.FrnIndex.TryGetValue(change.ParentFileReferenceNumber, out var parent)) return;
        var child = parent.Children.FirstOrDefault(c => string.Equals(c.Name, change.FileName, StringComparison.OrdinalIgnoreCase));
        if (child is null) throw new InvalidOperationException("修改事件对象不在快照内，需要重新扫描。");
        if (Excluded(child.Path, state)) { RemoveEntry(child, state); return; }
        try
        {
            var attributes = File.GetAttributes(child.Path);
            bool directory = (attributes & FileAttributes.Directory) != 0;
            bool reparse = (attributes & FileAttributes.ReparsePoint) != 0;
            if (directory != child.IsDirectory || reparse != child.IsReparsePoint)
                throw new InvalidOperationException("修改对象类型或重解析点状态已变化，快照失效。");
            if (directory) { child.Attributes = attributes; return; }
            var info = new FileInfo(child.Path);
            bool cloud = (attributes & ((FileAttributes)0x00400000 | (FileAttributes)0x00040000 | FileAttributes.Offline)) != 0;
            child.Size = cloud ? 0 : info.Length;
            child.AllocatedSize = FileScanner.GetAllocatedSize(child.Path, child.Size, cloud); child.HasAllocatedSize = true;
            child.Attributes = attributes; child.IsCloudPlaceholder = cloud; child.LastModified = info.LastWriteTime;
            parent.RecalculateSize();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { throw new InvalidOperationException("修改对象无法确认，拒绝继续返回旧大小。", e); }
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
