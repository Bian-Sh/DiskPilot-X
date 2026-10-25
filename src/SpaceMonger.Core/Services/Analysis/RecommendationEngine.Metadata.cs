using SpaceMonger.Core.Models;

namespace SpaceMonger.Core.Services.Analysis;

public partial class RecommendationEngine
{
    private string BuildCompactMetadata(ScanSession session, FileEntry analysisRoot, IReadOnlyList<PathWhitelistEntry> whitelist)
    {
        var snapshot = BuildMetadataSnapshot(analysisRoot, whitelist);
        string EvidencePath(string path) => System.IO.Path.GetRelativePath(analysisRoot.Path, path).Replace('\\', '/');

        // Top files by size
        var topFiles = snapshot.TopFiles
            .OrderByDescending(f => f.Size)
            .Take(MaxTopFiles)
            .Select(f => $"{EvidencePath(f.Path)}|{f.Size}")
            .ToList();

        // Known cleanup pattern directories — now with content fingerprints
        // so the AI can distinguish real temp folders from user working folders.
        var topDirSet = snapshot.TopDirs
            .Where(d => d.Depth >= 1)
            .OrderByDescending(d => d.Size)
            .Take(MaxTopDirs)
            .ToHashSet();

        var patternDirs = snapshot.PatternDirs
            .Where(d => !topDirSet.Contains(d) && MatchesKnownPattern(d) && d.Size > 0)
            .OrderByDescending(d => d.Size)
            .Take(MaxKnownPatternItems)
            .ToList();

        var patternItems = patternDirs
            .Select(d => $"{EvidencePath(d.Path)}|{d.Size}|{BuildContentFingerprint(d, whitelist)}")
            .ToList();

        // Also attach content fingerprints to top directories that match
        // ambiguous patterns so the AI gets content context for those too.
        var topDirsWithFingerprints = snapshot.TopDirs
            .Where(d => d.Depth >= 1)
            .OrderByDescending(d => d.Size)
            .Take(MaxTopDirs)
            .Select(d =>
            {
                bool needsFingerprint = false;
                foreach (var pattern in AmbiguousPatterns)
                {
                    if (d.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        needsFingerprint = true;
                        break;
                    }
                }
                var baseLine = $"{EvidencePath(d.Path)}|{d.Size}|{d.Children.Count(c => c.IsDirectory)} dirs, {d.Children.Count(c => !c.IsDirectory)} files";
                return needsFingerprint
                    ? $"{baseLine}|CONTENT_FINGERPRINT: {BuildContentFingerprint(d, whitelist)}"
                    : baseLine;
            })
            .ToList();

        // Lightweight duplicate detection by metadata only
        var duplicates = snapshot.DuplicateCandidates
            .Where(f => f.Size > DuplicateMinSize)
            .GroupBy(f => (f.Name, f.Size))
            .Where(g => g.Count() >= 2)
            .OrderByDescending(g => g.Key.Size * g.Count())
            .Take(MaxDuplicateGroups)
            .Select(g => $"{g.Key.Name}|{g.Key.Size}|{g.Count()} copies: {string.Join("; ", g.Select(f => EvidencePath(f.Path)))}")
            .ToList();

        // Build a compact pipe-delimited format instead of verbose JSON.
        // This is ~5-10x smaller than the equivalent JSON with full property names.
        var sb = new System.Text.StringBuilder();
        var isFocused = analysisRoot != session.RootEntry;
        sb.AppendLine($"SCAN: {session.TargetPath}");
        if (isFocused)
            sb.AppendLine($"FOCUS: {analysisRoot.Path}");
        sb.AppendLine($"BASE_PATH: {analysisRoot.Path}");
        sb.AppendLine("PATH_FORMAT: relative; join BASE_PATH with paths below before tool calls or recommendations.");
        sb.AppendLine($"TOTAL_FILES: {snapshot.TotalFiles}");
        sb.AppendLine($"TOTAL_FOLDERS: {snapshot.TotalFolders}");
        sb.AppendLine($"TOTAL_SIZE: {analysisRoot.Size}");
        sb.AppendLine();

        sb.AppendLine("## LARGEST DIRECTORIES (path|size_bytes|contents [|CONTENT_FINGERPRINT if ambiguous name])");
        foreach (var line in topDirsWithFingerprints)
            sb.AppendLine(line);
        sb.AppendLine();

        sb.AppendLine("## LARGEST FILES (path|size_bytes)");
        foreach (var line in topFiles)
            sb.AppendLine(line);
        sb.AppendLine();

        if (patternItems.Count > 0)
        {
            sb.AppendLine("## CLEANUP CANDIDATES (path|size_bytes|content_fingerprint)");
            sb.AppendLine("NOTE: Each entry includes a content fingerprint. Inspect it before recommending deletion.");
            foreach (var line in patternItems)
                sb.AppendLine(line);
            sb.AppendLine();
        }

        if (duplicates.Count > 0)
        {
            sb.AppendLine("## LIKELY DUPLICATES (name|size_bytes|locations)");
            foreach (var line in duplicates)
                sb.AppendLine(line);
            sb.AppendLine();
        }

        var unityLibraries = FindUnityLibraryDirectories(analysisRoot, null)
            .Where(library => library.Size > 0 && !IsCleanupExcluded(library.Path, whitelist))
            .Take(MaxKnownPatternItems)
            .ToList();
        if (unityLibraries.Count > 0)
        {
            sb.AppendLine("## UNITY LIBRARY EVIDENCE (path|size_bytes|project_markers)");
            foreach (var library in unityLibraries)
            {
                sb.AppendLine($"{EvidencePath(library.Path)}|{library.Size}|Assets + ProjectSettings");
            }
        }

        return sb.ToString();
    }

    private MetadataSnapshot BuildMetadataSnapshot(FileEntry root, IReadOnlyList<PathWhitelistEntry> whitelist)
    {
        var snapshot = new MetadataSnapshot();
        var pending = new Stack<FileEntry>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var entry = pending.Pop();
            if (IsCleanupExcluded(entry.Path, whitelist))
            {
                continue;
            }

            if (entry.IsDirectory)
            {
                snapshot.TotalFolders++;
                KeepLargest(snapshot.TopDirs, entry, MaxTopDirs);
                if (MatchesKnownPattern(entry) && entry.Size > 0)
                {
                    KeepLargest(snapshot.PatternDirs, entry, MaxKnownPatternItems);
                }

                for (var childIndex = entry.Children.Count - 1; childIndex >= 0; childIndex--)
                {
                    pending.Push(entry.Children[childIndex]);
                }
            }
            else
            {
                snapshot.TotalFiles++;
                KeepLargest(snapshot.TopFiles, entry, MaxTopFiles);
                if (entry.Size > DuplicateMinSize)
                {
                    KeepLargest(snapshot.DuplicateCandidates, entry, MaxDuplicateCandidates);
                }
            }
        }

        return snapshot;
    }

    private static void KeepLargest(List<FileEntry> entries, FileEntry entry, int maxCount)
    {
        if (entries.Count >= maxCount && entry.Size <= entries[^1].Size)
        {
            return;
        }

        entries.Add(entry);
        entries.Sort(static (left, right) => right.Size.CompareTo(left.Size));
        if (entries.Count > maxCount)
        {
            entries.RemoveAt(entries.Count - 1);
        }
    }

    private static bool MatchesKnownPattern(FileEntry dir)
    {
        // Exact directory name match against safe cleanup targets.
        foreach (var name in SafeDirectoryNames)
        {
            if (string.Equals(dir.Name, name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Standard OS/app temp paths matched by full path convention.
        var normalizedPath = dir.Path.Replace('/', '\\');
        foreach (var stdPath in StandardTempPaths)
        {
            if (normalizedPath.Contains(stdPath, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Ambiguous names (e.g. "Temp", "Cache") — include them so they get
        // a content fingerprint, but the AI will decide based on contents.
        foreach (var pattern in AmbiguousPatterns)
        {
            if (dir.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Builds a content fingerprint for a directory: file type distribution,
    /// date range, and sample file names. This gives the AI enough context to
    /// distinguish a real temp folder from a user working folder that happens
    /// to have "temp" in its name.
    /// </summary>
    private string BuildContentFingerprint(FileEntry dir, IReadOnlyList<PathWhitelistEntry> whitelist)
    {
        var allFiles = new List<FileEntry>();
        var omittedFiles = CollectFilesOnly(dir, allFiles, MaxFingerprintFiles, whitelist);

        if (allFiles.Count == 0)
            return "empty";

        // Extension distribution
        var extGroups = allFiles
            .GroupBy(f => f.Extension ?? "(none)", StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Sum(f => f.Size))
            .Take(MaxContentFingerprint)
            .Select(g => $"{g.Key}:{g.Count()}/{FormatSize(g.Sum(f => f.Size))}")
            .ToList();

        // Date range
        var oldest = allFiles.Min(f => f.LastModified);
        var newest = allFiles.Max(f => f.LastModified);
        var dateSpan = newest - oldest;

        // User-content file ratio — what fraction of files look like user documents,
        // source code, media, etc. vs. throwaway temp files
        int userContentCount = allFiles.Count(f =>
            f.Extension is not null && UserContentExtensions.Contains(f.Extension));
        int userContentPct = (int)(100.0 * userContentCount / allFiles.Count);

        // Sample of largest files by name (most informative for the AI)
        var sampleFiles = allFiles
            .OrderByDescending(f => f.Size)
            .Take(MaxSampleFiles)
            .Select(f => $"{f.Name}({FormatSize(f.Size)},{f.LastModified:yyyy-MM-dd})")
            .ToList();

        // Is this in a standard temp location?
        var normalizedPath = dir.Path.Replace('/', '\\');
        bool isStandardTempLocation = false;
        foreach (var stdPath in StandardTempPaths)
        {
            if (normalizedPath.Contains(stdPath, StringComparison.OrdinalIgnoreCase))
            {
                isStandardTempLocation = true;
                break;
            }
        }

        var sb = new System.Text.StringBuilder();
        sb.Append($"files:{allFiles.Count}");
        if (omittedFiles > 0)
            sb.Append($"+{omittedFiles} more");
        sb.Append($"|user_content:{userContentPct}%");
        sb.Append($"|dates:{oldest:yyyy-MM-dd}..{newest:yyyy-MM-dd}(span:{(int)dateSpan.TotalDays}d)");
        sb.Append($"|std_temp_location:{(isStandardTempLocation ? "yes" : "no")}");
        sb.Append($"|types:{string.Join(",", extGroups)}");
        sb.Append($"|largest:{string.Join(",", sampleFiles)}");
        return sb.ToString();
    }

    private int CollectFilesOnly(FileEntry entry, List<FileEntry> files, int maxCount, IReadOnlyList<PathWhitelistEntry> whitelist)
    {
        var omittedFiles = 0;
        var pending = new Stack<FileEntry>();
        pending.Push(entry);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var child in current.Children)
            {
                if (IsCleanupExcluded(child.Path, whitelist))
                {
                    continue;
                }

                if (child.IsDirectory)
                {
                    pending.Push(child);
                }
                else if (files.Count < maxCount)
                {
                    files.Add(child);
                }
                else
                {
                    omittedFiles++;
                }
            }
        }

        return omittedFiles;
    }

    private sealed class MetadataSnapshot
    {
        public int TotalFiles { get; set; }
        public int TotalFolders { get; set; }
        public List<FileEntry> TopFiles { get; } = [];
        public List<FileEntry> TopDirs { get; } = [];
        public List<FileEntry> PatternDirs { get; } = [];
        public List<FileEntry> DuplicateCandidates { get; } = [];
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024L) return $"{bytes}B";
        if (bytes < 1024L * 1024) return $"{bytes / 1024.0:F1}KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1}MB";
        if (bytes < 1024L * 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F1}GB";
        return $"{bytes / (1024.0 * 1024 * 1024 * 1024):F1}TB";
    }

}
