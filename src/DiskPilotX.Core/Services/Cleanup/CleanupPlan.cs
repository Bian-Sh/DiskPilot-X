using System.Security.Cryptography;
using System.Text.Json;
using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Models;

namespace DiskPilotX.Core.Services.Cleanup;

/// <summary>批准对应的清理快照；清单、扫描、删除方式或选中目标改变后必须重新确认。</summary>
public sealed class CleanupPlan
{
    public string RecommendationVersion { get; }
    public string Version { get; }
    public Guid ScanId { get; }
    public DeletionMode Mode { get; }
    public IReadOnlyList<CleanupRecommendation> Items { get; }
    private readonly Dictionary<string, (string Path, string Fingerprint)> _approvedTargets;

    private CleanupPlan(string recommendationVersion, Guid scanId, DeletionMode mode, List<CleanupRecommendation> items)
    {
        RecommendationVersion = recommendationVersion;
        ScanId = scanId;
        Mode = mode;
        Items = items.AsReadOnly();
        _approvedTargets = items.ToDictionary(item => item.Id, item => (item.TargetPath, item.ExpectedCleanupFingerprint!));
        Version = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            RecommendationVersion,
            Targets = items.OrderBy(item => item.Id, StringComparer.Ordinal)
                .Select(item => new { item.Id, item.TargetPath, item.ExpectedCleanupFingerprint })
        })));
    }

    public static CleanupPlan Capture(ScanSession session, IReadOnlyList<CleanupRecommendation> all,
        IReadOnlyList<string> ids, DeletionMode mode, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (ids.Count is 0 or > 100 || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
            throw new InvalidOperationException("清理清单为空、重复或过大。");
        var recommendationVersion = ComputeVersion(session, all, mode);
        var selected = ids.Select(id => all.SingleOrDefault(item => item.Id == id)
            ?? throw new InvalidOperationException("清理项已失效。")).ToList();
        var paths = selected.Select(item => Path.TrimEndingDirectorySeparator(Path.GetFullPath(item.TargetPath))).ToArray();
        if (paths.Any(path => string.Equals(path, Path.TrimEndingDirectorySeparator(Path.GetPathRoot(path)!), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("禁止清理磁盘根目录。");
        for (var i = 0; i < paths.Length; i++)
            for (var j = i + 1; j < paths.Length; j++)
                if (IsSameOrChild(paths[i], paths[j]) || IsSameOrChild(paths[j], paths[i]))
                    throw new InvalidOperationException("清理清单存在重复或父子路径。");

        var items = new List<CleanupRecommendation>();
        foreach (var item in selected)
        {
            ct.ThrowIfCancellationRequested();
            var snapshot = CleanupFilesystemSnapshot.Capture(item.TargetPath, ct);
            items.Add(new CleanupRecommendation
            {
                Id = item.Id, TargetPath = Path.GetFullPath(item.TargetPath), Size = snapshot.Size,
                Category = item.Category, SafetyRating = item.SafetyRating, Explanation = item.Explanation,
                Entry = item.Entry, LastModified = item.LastModified, IsAccepted = item.IsAccepted, IsDismissed = item.IsDismissed,
                ExpectedCleanupFingerprint = snapshot.Fingerprint
            });
        }
        ct.ThrowIfCancellationRequested();
        if (recommendationVersion != ComputeVersion(session, all, mode))
            throw new InvalidOperationException("采集批准快照期间清理清单发生变化。");
        return new(recommendationVersion, session.Id, mode, items);
    }

    public bool MatchesCurrentList(ScanSession? session, IReadOnlyList<CleanupRecommendation> current, DeletionMode mode) =>
        session is not null && session.Id == ScanId && Mode == mode && RecommendationVersion == ComputeVersion(session, current, mode);

    public bool IsCurrent(ScanSession? session, IReadOnlyList<CleanupRecommendation> current, DeletionMode mode, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return MatchesCurrentList(session, current, mode) && Items.All(item => IsTargetCurrent(item, ct));
    }

    public bool IsTargetCurrent(CleanupRecommendation item, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            return _approvedTargets.TryGetValue(item.Id, out var approved)
                && string.Equals(Path.GetFullPath(item.TargetPath), approved.Path, StringComparison.OrdinalIgnoreCase)
                && CleanupFilesystemSnapshot.Capture(item.TargetPath, ct).Fingerprint == approved.Fingerprint;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or NotSupportedException or System.Security.SecurityException or OverflowException)
        {
            return false;
        }
    }

    public static string ComputeVersion(ScanSession session, IReadOnlyList<CleanupRecommendation> all, DeletionMode mode)
    {
        // 清单版本只依赖推荐元数据；未选目标不参与文件系统遍历。
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            session.Id, Mode = mode,
            Items = all.Select(item => new { item.Id, Path = Path.GetFullPath(item.TargetPath), item.Size, item.IsAccepted, item.IsDismissed })
        });
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static bool IsSameOrChild(string path, string root) => string.Equals(path, root, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
