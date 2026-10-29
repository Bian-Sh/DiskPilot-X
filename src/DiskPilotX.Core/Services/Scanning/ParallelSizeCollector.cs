using System.IO.Enumeration;
using DiskPilotX.Core.Models;

namespace DiskPilotX.Core.Services.Scanning;

internal static class ParallelSizeCollector
{
    // Cloud placeholder file attributes (same as FileScanner)
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;
    private const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
    private const FileAttributes CloudPlaceholderMask = RecallOnDataAccess | RecallOnOpen | FileAttributes.Offline;

    private const int ProgressInterval = 500;

    /// <summary>
    /// 并行采集已构建目标树的文件大小，进度计数仅从当前目标树计算。
    /// </summary>
    internal static (int files, int folders) CollectSizes(
        FileEntry root,
        IProgress<ScanProgress> progress,
        CancellationToken ct)
    {
        // Collect all directory nodes into a flat list
        var directories = new List<FileEntry>();
        var stack = new Stack<FileEntry>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var entry = stack.Pop();
            if (entry.IsDirectory)
            {
                directories.Add(entry);
                if (entry.IsReparsePoint)
                {
                    continue;
                }

                foreach (var child in entry.Children)
                {
                    if (child.IsDirectory && !child.IsReparsePoint)
                        stack.Push(child);
                }
            }
        }

        // 仅统计筛选及白名单裁剪后的目标树，不从调用方接收整盘 MFT 总数。
        var totalFiles = directories.Sum(dir => dir.Children.Count(child => !child.IsDirectory));
        var totalFolders = directories.Count;
        int fileCount = 0;
        int folderCount = directories.Count;
        int directoriesProcessed = 0;

        Parallel.ForEach(
            directories,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = ct
            },
            dir =>
            {
                try
                {
                    if (dir.IsReparsePoint)
                    {
                        return;
                    }

                    // Build a lookup of existing children by name for fast matching
                    var childByName = new Dictionary<string, FileEntry>(
                        dir.Children.Count, StringComparer.OrdinalIgnoreCase);
                    foreach (var child in dir.Children)
                        childByName.TryAdd(child.Name, child);

                    var enumerable = new FileSystemEnumerable<(string FullPath, string Name, FileAttributes Attributes, long Length, DateTime LastWrite, bool IsDir)>(
                        dir.Path,
                        (ref FileSystemEntry entry) => (
                            entry.ToFullPath(),
                            entry.FileName.ToString(),
                            entry.Attributes,
                            entry.Length,
                            entry.LastWriteTimeUtc.LocalDateTime,
                            entry.IsDirectory
                        ),
                        new EnumerationOptions
                        {
                            IgnoreInaccessible = false,
                            RecurseSubdirectories = false,
                            AttributesToSkip = 0
                        });

                    foreach (var item in enumerable)
                    {
                        if (!childByName.TryGetValue(item.Name, out var child))
                            continue;

                        var isReparsePoint = (item.Attributes & FileAttributes.ReparsePoint) != 0;
                        child.IsReparsePoint = isReparsePoint;
                        child.LastModified = item.LastWrite;

                        if (!item.IsDir)
                        {
                            var isCloudPlaceholder = (item.Attributes & CloudPlaceholderMask) != 0;
                            child.IsCloudPlaceholder = isCloudPlaceholder;
                            child.Size = isCloudPlaceholder ? 0 : item.Length;
                            child.AllocatedSize = FileScanner.GetAllocatedSize(item.FullPath, item.Length, isCloudPlaceholder);
                            child.HasAllocatedSize = true;
                            child.Extension = Path.GetExtension(item.Name)?.ToLowerInvariant();
                            Interlocked.Increment(ref fileCount);
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    dir.IsAccessDenied = true;
                }
                catch (IOException)
                {
                    // Skip directories that cannot be read
                }

                var processed = Interlocked.Increment(ref directoriesProcessed);
                if (processed % ProgressInterval == 0)
                {
                    // 大小采集阶段只显示目标树的稳定计数。
                    progress.Report(new ScanProgress(
                        $"Collecting file sizes ({processed:N0} of {directories.Count:N0} folders)",
                        totalFiles, totalFolders));
                }
            });

        // Calculate directory sizes bottom-up
        progress.Report(new ScanProgress("Calculating sizes...", totalFiles, totalFolders));
        FileScanner.CalculateSizesBottomUp(root);

        return (fileCount, folderCount);
    }
}
