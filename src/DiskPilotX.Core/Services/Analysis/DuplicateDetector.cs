using System.IO.Hashing;
using DiskPilotX.Core.Models;

namespace DiskPilotX.Core.Services.Analysis;

public class DuplicateDetector : IDuplicateDetector
{
    public async Task<List<DuplicateGroup>> FindDuplicatesAsync(FileEntry root, CancellationToken cancellationToken = default)
    {
        var allFiles = new List<FileEntry>();
        CollectFiles(root, allFiles);

        var candidateGroups = allFiles
            .GroupBy(f => (f.Name, f.Size, f.LastModified))
            .Where(g => g.Count() >= 2);

        var result = new List<DuplicateGroup>();

        foreach (var group in candidateGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var entry in group)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var hash = await ComputeHashAsync(entry.Path, cancellationToken);
                entry.ContentHash = hash;
            }

            var confirmedDuplicates = group
                .Where(f => f.ContentHash is not null)
                .GroupBy(f => Convert.ToHexString(f.ContentHash!))
                .Where(g => g.Count() >= 2);

            foreach (var dupGroup in confirmedDuplicates)
            {
                var files = dupGroup.ToList();
                result.Add(new DuplicateGroup(
                    Hash: dupGroup.Key,
                    Size: files[0].Size,
                    Files: files));
            }
        }

        return result;
    }

    private static void CollectFiles(FileEntry entry, List<FileEntry> files)
    {
        if (!entry.IsDirectory)
        {
            // Skip cloud placeholder files — reading their content forces a download.
            if (entry.IsCloudPlaceholder)
                return;

            files.Add(entry);
            return;
        }

        foreach (var child in entry.Children)
        {
            CollectFiles(child, files);
        }
    }

    private async Task<byte[]?> ComputeHashAsync(string filePath, CancellationToken ct)
    {
        try
        {
            var hasher = new XxHash3();
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true);
            var buffer = new byte[8192];
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
            {
                hasher.Append(buffer.AsSpan(0, bytesRead));
            }
            return hasher.GetCurrentHash();
        }
        catch
        {
            return null;
        }
    }
}
