using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Scanning;
using System.Text.Json;

namespace DiskPilotX.Core.Services.Analysis;

public partial class RecommendationEngine
{
    public static IReadOnlyList<CleanupRecommendation> BuildUnityLibraryRecommendations(FileEntry analysisRoot)
    {
        var recommendations = new List<CleanupRecommendation>();
        AddUnityLibraryRecommendations(recommendations, analysisRoot);
        return recommendations;
    }

    public static IReadOnlyList<CleanupRecommendation> BuildUnityLibraryRecommendationsFromMft(
        string scanRoot,
        IProgress<ScanProgress> progress,
        CancellationToken cancellationToken)
    {
        var fullScanRoot = Path.GetFullPath(scanRoot);
        var volumeRoot = Path.GetPathRoot(fullScanRoot);
        if (string.IsNullOrWhiteSpace(volumeRoot))
        {
            return [];
        }

        var records = MftEnumerator.EnumerateDirectories(volumeRoot, progress, cancellationToken);
        if (records is null)
        {
            return [];
        }

        cancellationToken.ThrowIfCancellationRequested();
        var hubProjectRoots = LoadUnityHubProjectRoots();
        var directoryNamesByParent = BuildDirectoryNamesByParent(records.Values);
        var recommendations = new List<CleanupRecommendation>();

        foreach (var library in records.Values.Where(record => record.IsDirectory && string.Equals(record.FileName, "Library", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!directoryNamesByParent.TryGetValue(library.ParentFileReferenceNumber, out var siblingNames)
                || !siblingNames.Contains("Assets")
                || !siblingNames.Contains("ProjectSettings"))
            {
                continue;
            }

            var projectRootPath = BuildMftPath(volumeRoot, library.ParentFileReferenceNumber, records);
            var libraryPath = Path.Combine(projectRootPath, "Library");
            if (!IsPathInScope(libraryPath, fullScanRoot))
            {
                continue;
            }

            var size = TryGetDirectorySize(libraryPath, cancellationToken);
            var lastModified = Directory.Exists(libraryPath) ? Directory.GetLastWriteTime(libraryPath) : (DateTime?)null;
            var hubListed = hubProjectRoots.Contains(NormalizeUnityHubPath(projectRootPath));
            recommendations.Add(new CleanupRecommendation
            {
                TargetPath = libraryPath,
                Size = size,
                Category = RecommendationCategory.BuildCache,
                SafetyRating = SafetyRating.ReviewFirst,
                LastModified = lastModified,
                UnityHubListed = hubListed,
                Explanation = BuildUnityLibraryEvidenceText(lastModified, hubListed)
            });
        }

        return recommendations
            .GroupBy(item => item.TargetPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(item => item.Size)
            .ToList();
    }

    private static void AddUnityLibraryRecommendations(List<CleanupRecommendation> recommendations, FileEntry analysisRoot)
    {
        var hubProjectRoots = LoadUnityHubProjectRoots();
        foreach (var library in FindUnityLibraryDirectories(analysisRoot, null))
        {
            if (library.Size <= 0 || recommendations.Any(r => string.Equals(r.TargetPath, library.Path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var projectRoot = library.Parent;
            var hubListed = projectRoot is not null && hubProjectRoots.Contains(NormalizeUnityHubPath(projectRoot.Path));

            recommendations.Add(new CleanupRecommendation
            {
                TargetPath = library.Path,
                Entry = library,
                Size = library.Size,
                Category = RecommendationCategory.BuildCache,
                SafetyRating = SafetyRating.ReviewFirst,
                LastModified = library.LastModified,
                UnityHubListed = hubListed,
                Explanation = BuildUnityLibraryEvidenceText(library.LastModified, hubListed)
            });
        }
    }


    private static string BuildUnityLibraryEvidenceText(DateTime? lastModified, bool hubListed)
    {
        var evidence = new List<string>
        {
            "Unity project markers found near Library (Assets + ProjectSettings).",
            "Library is generated/import-cache evidence; deletion still needs skill/AI risk review and user confirmation."
        };

        if (lastModified is { } modified)
        {
            evidence.Add($"Last modified: {modified:yyyy-MM-dd HH:mm}.");
        }

        evidence.Add("Unity Hub listed: " + (hubListed ? "yes." : "no/unknown."));
        return string.Join(" ", evidence);
    }

    private static IEnumerable<FileEntry> FindUnityLibraryDirectories(FileEntry entry, FileEntry? parent)
    {
        var pending = new Stack<(FileEntry Entry, FileEntry? Parent)>();
        pending.Push((entry, parent));

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!current.Entry.IsDirectory)
            {
                continue;
            }

            var effectiveParent = current.Entry.Parent ?? current.Parent;
            if (string.Equals(current.Entry.Name, "Library", StringComparison.OrdinalIgnoreCase)
                && effectiveParent is not null
                && LooksLikeUnityProjectRoot(effectiveParent))
            {
                yield return current.Entry;
            }

            for (var childIndex = current.Entry.Children.Count - 1; childIndex >= 0; childIndex--)
            {
                pending.Push((current.Entry.Children[childIndex], current.Entry));
            }
        }
    }

    private static bool LooksLikeUnityProjectRoot(FileEntry directory)
    {
        return directory.Children.Any(child => child.IsDirectory && string.Equals(child.Name, "Assets", StringComparison.OrdinalIgnoreCase))
            && directory.Children.Any(child => child.IsDirectory && string.Equals(child.Name, "ProjectSettings", StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<long, HashSet<string>> BuildDirectoryNamesByParent(IEnumerable<MftRecord> records)
    {
        var namesByParent = new Dictionary<long, HashSet<string>>();
        foreach (var record in records.Where(record => record.IsDirectory))
        {
            if (!namesByParent.TryGetValue(record.ParentFileReferenceNumber, out var names))
            {
                names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                namesByParent[record.ParentFileReferenceNumber] = names;
            }

            names.Add(record.FileName);
        }

        return namesByParent;
    }

    private static string BuildMftPath(string volumeRoot, long frn, IReadOnlyDictionary<long, MftRecord> records)
    {
        var parts = new Stack<string>();
        var seen = new HashSet<long>();
        var current = frn;

        while (records.TryGetValue(current, out var record) && seen.Add(current))
        {
            if (!string.IsNullOrWhiteSpace(record.FileName) && record.FileName != ".")
            {
                parts.Push(record.FileName);
            }

            if (record.ParentFileReferenceNumber == current)
            {
                break;
            }

            current = record.ParentFileReferenceNumber;
        }

        return parts.Count == 0 ? volumeRoot : Path.Combine([volumeRoot, .. parts]);
    }

    private static bool IsPathInScope(string path, string scanRoot)
    {
        var normalizedPath = NormalizeUnityHubPath(path) + Path.DirectorySeparatorChar;
        var normalizedRoot = NormalizeUnityHubPath(scanRoot) + Path.DirectorySeparatorChar;
        return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static long TryGetDirectorySize(string path, CancellationToken cancellationToken)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return 0;
            }

            long total = 0;
            var pending = new Stack<string>();
            pending.Push(path);
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var current = pending.Pop();

                try
                {
                    foreach (var file in Directory.EnumerateFiles(current))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            total += new FileInfo(file).Length;
                        }
                        catch
                        {
                        }
                    }

                    foreach (var directory in Directory.EnumerateDirectories(current))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (IsReparsePoint(directory))
                        {
                            continue;
                        }

                        pending.Push(directory);
                    }
                }
                catch
                {
                }
            }

            return total;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return 0;
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return true;
        }
    }

    private static HashSet<string> LoadUnityHubProjectRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
        {
            return roots;
        }

        foreach (var fileName in new[] { "projects-v1.json", "projectDir.json" })
        {
            var path = Path.Combine(appData, "UnityHub", fileName);
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                CollectUnityHubPaths(document.RootElement, roots);
            }
            catch
            {
            }
        }

        return roots;
    }

    private static void CollectUnityHubPaths(JsonElement element, HashSet<string> roots)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    CollectUnityHubPaths(property.Value, roots);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectUnityHubPaths(item, roots);
                }
                break;
            case JsonValueKind.String:
                AddUnityHubPath(element.GetString(), roots);
                break;
        }
    }

    private static void AddUnityHubPath(string? value, HashSet<string> roots)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var normalized = NormalizeUnityHubPath(value);
        if (Path.IsPathRooted(normalized))
        {
            roots.Add(normalized);
        }
    }

    private static string NormalizeUnityHubPath(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        return expanded.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
