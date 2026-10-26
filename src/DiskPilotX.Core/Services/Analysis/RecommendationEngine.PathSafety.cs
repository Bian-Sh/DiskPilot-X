using DiskPilotX.Core.Models;
using DiskPilotX.Core.Enums;

namespace DiskPilotX.Core.Services.Analysis;

public partial class RecommendationEngine
{
    private enum PathSafetyKind
    {
        Normal,
        RiskAdjusted,
        HardProtected,
    }

    // Future extension point: load additional hard-protected or risk-adjusted
    // prefixes from settings, policy files, or a rule provider before applying
    // the built-in Windows defaults below.
    private static readonly string[] AdditionalHardProtectedPathPrefixes = [];
    private static readonly string[] AdditionalRiskAdjustedPathPrefixes = [];

    private static readonly string[] HardProtectedWindowsRelativeRoots =
    [
        "System32",
        "SysWOW64",
        "WinSxS",
        "servicing",
        "Installer",
        "SystemResources",
        "Boot",
    ];

    private static FileEntry? FindEntryByPath(FileEntry root, string targetPath)
    {
        if (!Path.IsPathFullyQualified(targetPath) || !TryNormalizePath(targetPath, out var normalizedTarget))
        {
            return null;
        }
        return EnumerateEntries(root).FirstOrDefault(entry =>
            string.Equals(NormalizePath(entry.Path), normalizedTarget, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsHardProtectedPath(string path)
    {
        return ClassifyPathSafety(path) == PathSafetyKind.HardProtected;
    }

    private static PathSafetyKind ClassifyPathSafety(string path)
    {
        var normalizedPath = NormalizePath(path);
        var segments = normalizedPath.Split('\\', StringSplitOptions.RemoveEmptyEntries);

        if (AdditionalHardProtectedPathPrefixes.Any(prefix => IsSameOrUnderPrefix(normalizedPath, prefix)))
            return PathSafetyKind.HardProtected;

        if (AdditionalRiskAdjustedPathPrefixes.Any(prefix => IsSameOrUnderPrefix(normalizedPath, prefix)))
            return PathSafetyKind.RiskAdjusted;

        if (IsUnderProtectedUserFolder(segments))
            return PathSafetyKind.HardProtected;

        if (segments.Any(segment => ProtectedProgramFolders.Any(protectedFolder =>
                string.Equals(segment, protectedFolder, StringComparison.OrdinalIgnoreCase))))
        {
            return PathSafetyKind.HardProtected;
        }

        var windowsIndex = Array.FindIndex(segments, segment =>
            string.Equals(segment, "Windows", StringComparison.OrdinalIgnoreCase));
        if (windowsIndex >= 0)
        {
            var windowsRelativePath = string.Join('\\', segments.Skip(windowsIndex + 1));
            if (HardProtectedWindowsRelativeRoots.Any(root => IsSameOrUnderPrefix(windowsRelativePath, root)))
                return PathSafetyKind.HardProtected;

            return PathSafetyKind.RiskAdjusted;
        }

        return PathSafetyKind.Normal;
    }

    private static bool IsUnderProtectedUserFolder(string[] segments)
    {
        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (string.Equals(segments[i], "Users", StringComparison.OrdinalIgnoreCase) &&
                i + 2 < segments.Length)
            {
                var folderAfterUsername = segments[i + 2];
                if (ProtectedUserFolders.Any(protectedFolder =>
                    string.Equals(protectedFolder, folderAfterUsername, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsSameOrUnderPrefix(string normalizedPath, string prefix)
    {
        var normalizedPrefix = Path.IsPathFullyQualified(prefix) ? NormalizePath(prefix) : prefix.Replace('/', '\\').TrimEnd('\\');
        return string.Equals(normalizedPath, normalizedPrefix, StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.StartsWith(normalizedPrefix.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Replace('/', '\\')));
    }

    private static bool TryNormalizePath(string path, out string normalizedPath)
    {
        try
        {
            normalizedPath = NormalizePath(path);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            normalizedPath = string.Empty;
            return false;
        }
    }

    private static void ApplySystemPathRiskAdjustment(CleanupRecommendation recommendation)
    {
        var safetyKind = ClassifyPathSafety(recommendation.TargetPath);
        if (safetyKind == PathSafetyKind.Normal)
            return;

        if (recommendation.SafetyRating == SafetyRating.Safe)
        {
            recommendation.SafetyRating = SafetyRating.ReviewFirst;
        }

        var suffix = safetyKind == PathSafetyKind.RiskAdjusted
            ? " (System-adjacent location - review carefully before deleting.)"
            : " (Protected system/user location - review carefully before deleting.)";
        var marker = safetyKind == PathSafetyKind.RiskAdjusted
            ? "System-adjacent location"
            : "Protected system/user location";

        if (!recommendation.Explanation.Contains(marker, StringComparison.OrdinalIgnoreCase))
        {
            recommendation.Explanation += suffix;
        }
    }
}
