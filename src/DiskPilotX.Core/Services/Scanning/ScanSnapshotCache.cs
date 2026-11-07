using DiskPilotX.Core.Models;

namespace DiskPilotX.Core.Services.Scanning;

/// <summary>进程内有界快照。查看缓存与清理授权无关，活动证据通过具名所有者保护。</summary>
public sealed class ScanSnapshotCache(int maximumSnapshots = 12, long maximumEntries = 5_000_000)
{
    private sealed record Item(ScanSnapshotReference Reference, ScanSession Session, long Weight) { public long Used { get; set; } }
    private readonly Dictionary<Guid, Item> items = [];
    private readonly HashSet<(Guid Id, string Owner)> protectedItems = [];
    private long clock;
    public Guid Epoch { get; } = Guid.NewGuid();
    public IReadOnlyList<ScanSession> Sessions => items.Values.Select(item => item.Session).ToArray();

    public ScanSnapshotBudget GetBudget()
    {
        var protectedIds = protectedItems.Select(item => item.Id).ToHashSet();
        var used = items.Values.Sum(item => item.Weight);
        return new(maximumSnapshots, maximumEntries, items.Count, used,
            items.Keys.Count(protectedIds.Contains), items.Keys.Count(id => !protectedIds.Contains(id)),
            Math.Max(0, maximumSnapshots - items.Count), Math.Max(0, maximumEntries - used));
    }

    public ScanSnapshotReference Add(ScanSession session)
    {
        if (session.RootEntry is null || session.IsCancelled) throw new InvalidOperationException("扫描未完成，不能登记快照。");
        if (items.TryGetValue(session.Id, out var existing)) { existing.Used = ++clock; return existing.Reference; }
        var weight = Math.Max(1L, (long)session.TotalFiles + session.TotalFolders + 1);
        if (maximumSnapshots < 1 || weight > maximumEntries) throw new InvalidOperationException("扫描结果超过内存快照预算，请缩小范围。");
        // 先选足够的可回收项，再提交回收，失败不影响已有结果。
        var candidates = items.Where(pair => !protectedItems.Any(p => p.Id == pair.Key)).OrderBy(pair => pair.Value.Used).ToList();
        var count = items.Count; var total = items.Values.Sum(item => item.Weight); var remove = new List<Guid>();
        foreach (var candidate in candidates)
        {
            if (count < maximumSnapshots && total + weight <= maximumEntries) break;
            remove.Add(candidate.Key); count--; total -= candidate.Value.Weight;
        }
        if (count >= maximumSnapshots || total + weight > maximumEntries)
            throw new InvalidOperationException("内存预算被当前展示或活动分析占用，请先结束任务或释放清单。");
        foreach (var id in remove) items.Remove(id);
        var reference = new ScanSnapshotReference(session.Id, Epoch, session.TargetPath,
            new DateTimeOffset(session.EndTime ?? session.StartTime), session.TotalSize, session.TotalFiles, session.TotalFolders);
        items.Add(reference.Id, new(reference, session, weight) { Used = ++clock });
        return reference;
    }

    public bool TryGet(ScanSnapshotReference reference, out ScanSession? session)
    {
        session = null;
        if (reference.CacheEpoch != Epoch || !items.TryGetValue(reference.Id, out var item)
            || !string.Equals(reference.Path, item.Reference.Path, StringComparison.OrdinalIgnoreCase)) return false;
        item.Used = ++clock; session = item.Session; return true;
    }
    public void Protect(Guid id, string owner = "default") { if (items.ContainsKey(id)) protectedItems.Add((id, owner)); }
    public void Unprotect(Guid id, string owner = "default") => protectedItems.Remove((id, owner));
    public void ReleaseOwner(string owner) => protectedItems.RemoveWhere(item => item.Owner == owner);
    /// <summary>只回收明确结束且没有任何活动所有者的快照。</summary>
    public bool RemoveUnprotected(Guid id) => !protectedItems.Any(item => item.Id == id) && items.Remove(id);
    public ScanSession? FindById(Guid id)
    {
        if(!items.TryGetValue(id,out var item))return null;
        item.Used=++clock;return item.Session;
    }
    public ScanSession? FindByPath(string path) => items.Values.Where(item => IsUnder(path, item.Reference.Path))
        .OrderByDescending(item => item.Reference.Path.Length).ThenByDescending(item => item.Used).Select(item => item.Session).FirstOrDefault();
    private static bool IsUnder(string path, string root) => string.Equals(path.TrimEnd('\\', '/'), root.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(root.TrimEnd('\\', '/') + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}

/// <summary>当前预算回执；剩余数量不包含可回收项，保护项不能由AI自动释放。</summary>
public sealed record ScanSnapshotBudget(int MaximumSnapshots, long MaximumEntries, int UsedSnapshots, long UsedEntries,
    int ProtectedSnapshots, int ReclaimableSnapshots, int RemainingSnapshots, long RemainingEntries);
