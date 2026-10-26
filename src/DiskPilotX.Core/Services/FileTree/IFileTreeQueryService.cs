using DiskPilotX.Core.Models;

namespace DiskPilotX.Core.Services.FileTree;

public interface IFileTreeQueryService
{
    FileEntry? FindByPath(ScanSession session, string path);

    IReadOnlyList<FileEntry> FindByName(ScanSession session, string name, bool exactMatch = false, int maxResults = 50);

    IReadOnlyList<FileEntry> ListChildren(ScanSession session, string path, int maxResults = 100);

    SubtreeSummary SummarizeSubtree(ScanSession session, string path, int topChildren = 20);

    IReadOnlyList<FileEntry> FindLargeFiles(ScanSession session, string? underPath = null, int maxResults = 50, long? minSizeBytes = null);
}

public sealed record SubtreeSummary(
    string Path,
    string Name,
    long SizeBytes,
    int FileCount,
    int DirectoryCount,
    DateTime? LastModified,
    IReadOnlyList<FileEntry> LargestChildren);
