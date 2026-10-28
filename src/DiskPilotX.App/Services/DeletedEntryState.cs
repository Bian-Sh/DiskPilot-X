using System.Runtime.CompilerServices;
using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.FileTree;

namespace DiskPilotX.App.Services;

/// <summary>保留扫描快照供删除反馈使用；计数去重与可清除的视觉标记分离。</summary>
public static class DeletedEntryState
{
    private static readonly ConditionalWeakTable<ScanSession, HashSet<FileEntry>> Counted = new();

    public static bool Apply(ScanSession session, IEnumerable<CleanupAction> results)
    {
        var counted = Counted.GetOrCreateValue(session);
        var changed = false;
        var query = new FileTreeQueryService();
        foreach (var action in results)
        {
            if (action.Result is not (CleanupResult.Success or CleanupResult.AlreadyRemoved)) continue;
            var entry = query.FindByPath(session, action.Recommendation.TargetPath);
            if (entry is null) continue;
            Visit(entry, current =>
            {
                changed |= !current.IsDeleted;
                current.IsDeleted = true;
                if (!counted.Add(current)) return;
                if (current.IsDirectory) session.TotalFolders = Math.Max(0, session.TotalFolders - 1);
                else
                {
                    session.TotalFiles = Math.Max(0, session.TotalFiles - 1);
                    session.TotalSize = Math.Max(0, session.TotalSize - current.Size);
                }
            });
        }
        return changed;
    }

    public static void Clear(FileEntry? root)
    {
        if (root is not null) Visit(root, entry => entry.IsDeleted = false);
    }

    private static void Visit(FileEntry root, Action<FileEntry> action)
    {
        var pending = new Stack<FileEntry>();
        pending.Push(root);
        while (pending.TryPop(out var entry))
        {
            action(entry);
            foreach (var child in entry.Children) pending.Push(child);
        }
    }
}
