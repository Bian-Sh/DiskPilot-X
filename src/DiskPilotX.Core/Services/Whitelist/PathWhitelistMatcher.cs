using DiskPilotX.Core.Models;

namespace DiskPilotX.Core.Services.Whitelist;

public sealed class PathWhitelistMatcher : IPathWhitelistMatcher
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public bool IsExcluded(string? path, IEnumerable<PathWhitelistEntry>? whitelist)
    {
        if (string.IsNullOrWhiteSpace(path) || whitelist is null)
        {
            return false;
        }

        var normalizedPath = TryNormalizeExistingPath(path);
        if (normalizedPath is null)
        {
            return false;
        }

        foreach (var entry in whitelist)
        {
            var normalizedEntry = TryNormalizeExistingPath(entry.Path);
            if (normalizedEntry is null)
            {
                continue;
            }

            if (IsSameOrChildPath(normalizedPath, normalizedEntry))
            {
                return true;
            }
        }

        return false;
    }

    public IReadOnlyList<PathWhitelistEntry> MergeEntries(
        IEnumerable<PathWhitelistEntry>? current,
        IEnumerable<PathWhitelistEntry>? incoming)
    {
        var merged = new Dictionary<string, PathWhitelistEntry>(PathComparer);

        AddRange(current, replaceDescription: false);
        AddRange(incoming, replaceDescription: true);

        return merged.Values.ToList();

        void AddRange(IEnumerable<PathWhitelistEntry>? entries, bool replaceDescription)
        {
            if (entries is null)
            {
                return;
            }

            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Path))
                {
                    continue;
                }

                var originalPath = entry.Path.Trim();
                var key = TryNormalizePath(originalPath) ?? NormalizeLoosePath(originalPath);
                var description = string.IsNullOrWhiteSpace(entry.Description) ? null : entry.Description.Trim();

                if (!merged.TryGetValue(key, out var existing))
                {
                    merged[key] = new PathWhitelistEntry
                    {
                        Path = originalPath,
                        Description = description
                    };
                    continue;
                }

                if (replaceDescription && !string.IsNullOrWhiteSpace(description))
                {
                    existing.Description = description;
                }
            }
        }
    }

    private static bool IsSameOrChildPath(string path, string prefix)
    {
        return string.Equals(path, prefix, PathComparison)
            || path.StartsWith(prefix + Path.DirectorySeparatorChar, PathComparison)
            || path.StartsWith(prefix + Path.AltDirectorySeparatorChar, PathComparison);
    }

    private static string? TryNormalizeExistingPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var normalized = TryNormalizePath(path);
        if (normalized is null)
        {
            return null;
        }

        return File.Exists(normalized) || Directory.Exists(normalized) ? normalized : null;
    }

    private static string? TryNormalizePath(string path)
    {
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

    private static string NormalizeLoosePath(string path)
    {
        return path.Trim()
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimEnd(Path.DirectorySeparatorChar);
    }
}

