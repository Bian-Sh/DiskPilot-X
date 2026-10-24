using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Settings;
using SpaceMonger.Core.Services.Whitelist;

namespace SpaceMonger.Core.Services.Analysis;

public partial class RecommendationEngine
{
    // 限制目录、文件和内容指纹数量，控制证据体积。
    private const int MaxTopDirs = 100;
    private const int MaxTopFiles = 100;
    private const int MaxKnownPatternItems = 50;
    private const int MaxDuplicateGroups = 20;
    private const int MaxContentFingerprint = 30;
    private const int MaxSampleFiles = 15;
    private const int MaxDuplicateCandidates = 5_000;
    private const int MaxFingerprintFiles = 2_000;
    private const long DuplicateMinSize = 1_048_576; // 1 MB

    // Well-known OS and application temp/cache paths that are safe to match
    // by convention. These use full path segments to avoid false positives
    // on user-created folders that happen to contain "temp" or "cache".
    private static readonly string[] StandardTempPaths =
    [
        @"AppData\Local\Temp",
        @"Windows\Temp",
        @"Windows\Prefetch",
    ];

    // Directory names that are safe cleanup targets when they appear as an
    // exact folder name anywhere in the tree (build artifacts, package caches).
    private static readonly string[] SafeDirectoryNames =
    [
        ".npm", ".nuget", "node_modules", "obj", "bin",
        "__pycache__", ".gradle", "CacheStorage",
        "Code Cache", "GPUCache", "INetCache", ".cache",
        "_cacache",
    ];

    // Names that are *suggestive* of cleanup but need content inspection
    // before recommending deletion. These get a content fingerprint attached.
    private static readonly string[] AmbiguousPatterns =
    [
        "Temp", "Cache", "Logs", "Log", "tmp",
    ];

    // File extensions that indicate user-created, potentially important content.
    private static readonly HashSet<string> UserContentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".pdf", ".odt", ".ods", ".odp",
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".svg", ".psd", ".ai",
        ".mp3", ".mp4", ".avi", ".mkv", ".mov", ".wav", ".flac",
        ".zip", ".rar", ".7z", ".tar", ".gz",
        ".cs", ".py", ".js", ".ts", ".java", ".cpp", ".h", ".go", ".rs",
        ".sln", ".csproj", ".json", ".xml", ".yaml", ".yml", ".toml",
        ".sql", ".db", ".sqlite", ".bak",
        ".iso", ".vhd", ".vhdx", ".vmdk",
        ".exe", ".msi",
    };

    private static readonly string[] ProtectedProgramFolders =
    [
        "Program Files",
        "Program Files (x86)"
    ];

    private static readonly string[] ProtectedUserFolders =
    [
        "Desktop",
        "Documents",
        "Pictures",
        "Music",
        "Videos"
    ];

    private readonly ISettingsService? _settingsService;
    private readonly IPathWhitelistMatcher _whitelistMatcher;
    private readonly ILogger<RecommendationEngine> _logger;

    public RecommendationEngine(
        ISettingsService? settingsService = null,
        IPathWhitelistMatcher? whitelistMatcher = null,
        ILogger<RecommendationEngine>? logger = null)
    {
        _settingsService = settingsService;
        _whitelistMatcher = whitelistMatcher ?? new PathWhitelistMatcher();
        _logger = logger ?? NullLogger<RecommendationEngine>.Instance;
    }

    public string BuildEvidence(ScanSession session, FileEntry? focusEntry = null)
    {
        var whitelist = SnapshotWhitelist();
        var root = ResolveAnalysisRoot(session, focusEntry);
        if (root is null || IsCleanupExcluded(root.Path, whitelist))
        {
            return string.Empty;
        }

        return BuildCompactMetadata(session, root, whitelist);
    }

    public AnalysisResult ValidateCandidates(ScanSession session, JsonElement candidates, FileEntry? focusEntry = null)
    {
        var whitelist = SnapshotWhitelist();
        var result = new AnalysisResult
        {
            Diagnostics = new AnalysisDiagnostics
            {
                TargetPath = session.TargetPath,
                ScopePath = focusEntry?.Path ?? session.RootEntry?.Path ?? session.TargetPath,
                IsFocusedScope = focusEntry is not null
            }
        };
        var root = ResolveAnalysisRoot(session, focusEntry);
        if (root is null || IsCleanupExcluded(root.Path, whitelist))
        {
            result.Diagnostics.ParseError = "分析范围不存在于扫描树中或已被推荐白名单排除。";
            return result;
        }

        if (candidates.ValueKind != JsonValueKind.Object ||
            !candidates.TryGetProperty("recommendations", out var items) ||
            items.ValueKind != JsonValueKind.Array)
        {
            result.Diagnostics.ParseError = "工具参数必须是包含 recommendations 数组的对象。";
            return result;
        }

        var entries = new Dictionary<string, FileEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in EnumerateEntries(root))
        {
            var normalizedEntryPath = NormalizePath(entry.Path);
            if (IsSameOrUnderPrefix(normalizedEntryPath, root.Path))
            {
                entries.TryAdd(normalizedEntryPath, entry);
            }
        }

        foreach (var item in items.EnumerateArray())
        {
            if (!TryReadCandidate(item, result.Diagnostics, out var recommendation))
            {
                continue;
            }

            if (!Path.IsPathFullyQualified(recommendation.TargetPath) ||
                !TryNormalizePath(recommendation.TargetPath, out var normalizedPath) ||
                !entries.TryGetValue(normalizedPath, out var entry))
            {
                result.Diagnostics.MissingEntryCount++;
                continue;
            }

            recommendation.TargetPath = entry.Path;
            recommendation.Entry = entry;
            recommendation.Size = entry.Size;
            recommendation.LastModified = entry.LastModified;
            AddValidatedRecommendation(result, recommendation, whitelist);
        }

        foreach (var recommendation in BuildUnityLibraryRecommendations(root))
        {
            if (entries.ContainsKey(NormalizePath(recommendation.TargetPath)))
            {
                AddValidatedRecommendation(result, recommendation, whitelist);
            }
        }

        for (var i = 0; i < result.Recommendations.Count; i++)
        {
            result.Recommendations[i].Id = (i + 1).ToString();
        }

        _logger.LogInformation("候选校验完成：通过 {Count} 项，硬保护过滤 {ProtectedCount} 项", result.Recommendations.Count, result.Diagnostics.ProtectedFilteredCount);
        return result;
    }

    public static bool IsDeletionProtected(string path)
    {
        return string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) ||
            !TryNormalizePath(path, out var normalizedPath) || IsHardProtectedPath(normalizedPath);
    }

    public bool IsRecommendationExcluded(string path)
    {
        return IsCleanupExcluded(path, SnapshotWhitelist());
    }

    private void AddValidatedRecommendation(AnalysisResult result, CleanupRecommendation recommendation, IReadOnlyList<PathWhitelistEntry> whitelist)
    {
        // 目录清理会同时删除后代，必须检查整棵候选子树以免绕过保护或白名单。
        if (EnumerateEntries(recommendation.Entry!).Any(entry => IsDeletionProtected(entry.Path)))
        {
            result.Diagnostics.ProtectedFilteredCount++;
            return;
        }

        if (EnumerateEntries(recommendation.Entry!).Any(entry => IsCleanupExcluded(entry.Path, whitelist)) ||
            result.Recommendations.Any(existing =>
                IsSameOrUnderPrefix(NormalizePath(recommendation.TargetPath), existing.TargetPath) ||
                IsSameOrUnderPrefix(NormalizePath(existing.TargetPath), recommendation.TargetPath)))
        {
            return;
        }

        ApplySystemPathRiskAdjustment(recommendation);
        result.Recommendations.Add(recommendation);
        result.Diagnostics.ParsedRecommendationCount++;
    }

    private IReadOnlyList<PathWhitelistEntry> SnapshotWhitelist()
    {
        return _settingsService?.LoadSettings().CleanupRecommendationWhitelist
            .Select(entry => new PathWhitelistEntry { Path = entry.Path, Description = entry.Description })
            .ToArray() ?? [];
    }

    private bool IsCleanupExcluded(string path, IReadOnlyList<PathWhitelistEntry> whitelist)
    {
        return _whitelistMatcher.IsExcluded(path, whitelist);
    }

    private static FileEntry? ResolveAnalysisRoot(ScanSession session, FileEntry? focusEntry)
    {
        return session.RootEntry is null ? null : focusEntry is null
            ? session.RootEntry : FindEntryByPath(session.RootEntry, focusEntry.Path);
    }

    private static IEnumerable<FileEntry> EnumerateEntries(FileEntry root)
    {
        var pending = new Stack<FileEntry>();
        pending.Push(root);
        while (pending.TryPop(out var entry))
        {
            yield return entry;
            for (var i = entry.Children.Count - 1; i >= 0; i--)
            {
                pending.Push(entry.Children[i]);
            }
        }
    }
}
