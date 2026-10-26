using System.IO.Enumeration;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Settings;
using DiskPilotX.Core.Services.Whitelist;

namespace DiskPilotX.Core.Services.Scanning;

public class FileScanner : IFileScanner
{
    private readonly ISettingsService? _settingsService;
    private readonly IPathWhitelistMatcher _whitelistMatcher;
    private readonly ILogger<FileScanner> _logger;

    public FileScanner(ISettingsService? settingsService = null, IPathWhitelistMatcher? whitelistMatcher = null, ILogger<FileScanner>? logger = null)
    {
        _settingsService = settingsService;
        _whitelistMatcher = whitelistMatcher ?? new PathWhitelistMatcher();
        _logger = logger ?? NullLogger<FileScanner>.Instance;
    }

    public bool IsReady => true;
    public event Action? IsReadyChanged { add { } remove { } }

    // Windows cloud placeholder file attributes (OneDrive Files On-Demand, etc.)
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;
    private const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
    private const FileAttributes CloudPlaceholderMask = RecallOnDataAccess | RecallOnOpen | FileAttributes.Offline;

    // Throttle progress reports to avoid flooding the UI thread.
    private const int ProgressReportInterval = 200;

    public async Task<ScanSession> ScanAsync(
        string path,
        IProgress<ScanProgress> progress,
        CancellationToken cancellationToken)
    {
        path = ScanPathResolver.Resolve(path);
        _logger.LogInformation("Full scan starting for {Path}", path);

        var session = new ScanSession
        {
            TargetPath = path,
            StartTime = DateTime.Now
        };

        PopulateDriveInfo(session, path);

        var scanWhitelist = _settingsService?.LoadSettings().ScanWhitelist ?? [];
        if (_whitelistMatcher.IsExcluded(path, scanWhitelist))
        {
            _logger.LogWarning("Scan target is excluded by whitelist settings: {Path}", path);
            throw new InvalidOperationException("Scan target is excluded by whitelist settings.");
        }

        var rootEntry = CreateRootEntry(path);

        int fileCount = 0;
        int folderCount = 1;

        await Task.Run(() =>
        {
            var queue = new Queue<FileEntry>();
            queue.Enqueue(rootEntry);
            int reportCounter = 0;

            while (queue.Count > 0)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    session.IsCancelled = true;
                    break;
                }

                var current = queue.Dequeue();

                try
                {
                    // Use FileSystemEnumerable<T> to read all entries in one pass.
                    // This maps directly to FindFirstFile/FindNextFile 閳?one syscall per entry,
                    // no extra GetFileAttributesEx or stat calls like FileInfo/DirectoryInfo would cause.
                    var enumerable = new FileSystemEnumerable<(string FullPath, string Name, FileAttributes Attributes, long Length, DateTime LastWrite, bool IsDir)>(
                        current.Path,
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
                            AttributesToSkip = 0 // Don't skip anything 閳?we handle attributes ourselves.
                        });

                    foreach (var item in enumerable)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            session.IsCancelled = true;
                            break;
                        }

                        if (_whitelistMatcher.IsExcluded(item.FullPath, scanWhitelist))
                        {
                            continue;
                        }

                        var isReparsePoint = (item.Attributes & FileAttributes.ReparsePoint) != 0;

                        if (item.IsDir)
                        {
                            if (IsSystemVolumeInformationAtVolumeRoot(current.Path, item.Name))
                                continue;

                            var childDir = new FileEntry
                            {
                                Path = item.FullPath,
                                Name = item.Name,
                                IsDirectory = true,
                                IsReparsePoint = isReparsePoint,
                                Attributes = item.Attributes,
                                LastModified = item.LastWrite,
                                Depth = current.Depth + 1,
                                Parent = current
                            };
                            current.Children.Add(childDir);
                            folderCount++;

                            if (!isReparsePoint)
                            {
                                queue.Enqueue(childDir);
                            }
                        }
                        else
                        {
                            var isCloudPlaceholder = (item.Attributes & CloudPlaceholderMask) != 0;

                            var childFile = new FileEntry
                            {
                                Path = item.FullPath,
                                Name = item.Name,
                                IsDirectory = false,
                                IsReparsePoint = isReparsePoint,
                                IsCloudPlaceholder = isCloudPlaceholder,
                                Attributes = item.Attributes,
                                LastModified = item.LastWrite,
                                Extension = System.IO.Path.GetExtension(item.Name)?.ToLowerInvariant(),
                                // Length comes straight from WIN32_FIND_DATA 閳?no extra syscall.
                                // Cloud placeholders report 0 to avoid triggering downloads.
                                Size = isCloudPlaceholder ? 0 : item.Length,
                                AllocatedSize = GetAllocatedSize(item.FullPath, item.Length, isCloudPlaceholder),
                                HasAllocatedSize = true,
                                Depth = current.Depth + 1,
                                Parent = current
                            };
                            current.Children.Add(childFile);
                            fileCount++;
                        }
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    current.IsAccessDenied = true;
                    _logger.LogDebug(ex, "Access denied while scanning {Path}", current.Path);
                }
                catch (IOException ex)
                {
                    _logger.LogDebug(ex, "I/O error while scanning {Path}", current.Path);
                }

                // Throttle progress reports 閳?reporting every directory hammers the UI thread.
                if (++reportCounter % ProgressReportInterval == 0)
                {
                    progress.Report(new ScanProgress(current.Path, fileCount, folderCount));
                }
            }

            // Final progress report.
            progress.Report(new ScanProgress("Calculating sizes...", fileCount, folderCount));

            // Calculate directory sizes bottom-up.
            CalculateSizesBottomUp(rootEntry);

        }, cancellationToken).ConfigureAwait(false);

        session.TotalFiles = fileCount;
        session.TotalFolders = folderCount;
        session.TotalSize = rootEntry.Size;
        session.EndTime = DateTime.Now;
        session.RootEntry = rootEntry;

        if (session.IsCancelled)
        {
            _logger.LogWarning("Full scan cancelled for {Path}; files={FileCount}, folders={FolderCount}", path, fileCount, folderCount);
        }
        else
        {
            _logger.LogInformation("Full scan completed for {Path}; files={FileCount}, folders={FolderCount}, bytes={TotalSize}, elapsedMs={ElapsedMs}", path, fileCount, folderCount, session.TotalSize, ((session.EndTime ?? DateTime.Now) - session.StartTime).TotalMilliseconds);
        }

        return session;
    }

    internal static void PopulateDriveInfo(ScanSession session, string path)
    {
        try
        {
            var rootPath = Path.GetPathRoot(path);
            if (rootPath != null && string.Equals(Path.GetFullPath(path), rootPath, StringComparison.OrdinalIgnoreCase))
            {
                var driveInfo = new DriveInfo(rootPath);
                if (driveInfo.IsReady)
                {
                    session.DriveCapacity = driveInfo.TotalSize;
                    session.DriveFreeSpace = driveInfo.TotalFreeSpace;
                }
            }
        }
        catch (Exception)
        {
            // Drive info is best-effort; failures are non-fatal.
        }
    }

    private static bool IsSystemVolumeInformationAtVolumeRoot(string parentPath, string name)
    {
        if (!string.Equals(name, "System Volume Information", StringComparison.OrdinalIgnoreCase))
            return false;

        var fullParent = Path.GetFullPath(parentPath);
        var root = Path.GetPathRoot(fullParent);
        return !string.IsNullOrEmpty(root)
               && string.Equals(
                   fullParent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                   root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                   StringComparison.OrdinalIgnoreCase);
    }

    private static FileEntry CreateRootEntry(string path)
    {
        // Only the root needs a DirectoryInfo call 閳?one syscall for the entire scan.
        var dirInfo = new DirectoryInfo(path);
        return new FileEntry
        {
            Path = dirInfo.FullName,
            Name = dirInfo.Name,
            IsDirectory = true,
            IsReparsePoint = (dirInfo.Attributes & FileAttributes.ReparsePoint) != 0,
            Attributes = dirInfo.Attributes,
            LastModified = dirInfo.LastWriteTime,
            Depth = 0
        };
    }

    /// <summary>
    /// Calculates directory sizes bottom-up using a post-order traversal.
    /// Each directory's size is the sum of its children's sizes.
    /// </summary>
    internal static void CalculateSizesBottomUp(FileEntry root)
    {
        var stack = new Stack<FileEntry>();
        var postOrder = new List<FileEntry>();

        stack.Push(root);
        while (stack.Count > 0)
        {
            var entry = stack.Pop();
            postOrder.Add(entry);

            foreach (var child in entry.Children)
            {
                stack.Push(child);
            }
        }

        for (int i = postOrder.Count - 1; i >= 0; i--)
        {
            var entry = postOrder[i];
            if (entry.IsDirectory)
            {
                entry.Size = entry.Children.Sum(c => c.Size);
                entry.AllocatedSize = entry.Children.Sum(c => c.HasAllocatedSize ? c.AllocatedSize : c.Size);
                entry.HasAllocatedSize = true;
                entry.SubtreeFileCount = entry.Children.Sum(c => c.SubtreeFileCount > 0 ? c.SubtreeFileCount : c.IsDirectory ? 0 : 1);
                entry.SubtreeFolderCount = 1 + entry.Children.Sum(c => c.SubtreeFolderCount > 0 ? c.SubtreeFolderCount : c.IsDirectory ? 1 : 0);
                entry.SubtreeItemCount = entry.SubtreeFileCount + entry.SubtreeFolderCount;
            }
            else
            {
                entry.SubtreeFileCount = 1;
                entry.SubtreeFolderCount = 0;
                entry.SubtreeItemCount = 1;
                if (entry.AllocatedSize == 0 && entry.Size > 0)
                {
                    entry.AllocatedSize = entry.Size;
                    entry.HasAllocatedSize = true;
                }
            }
        }
    }

    internal static long GetAllocatedSize(string path, long logicalSize, bool isCloudPlaceholder = false)
    {
        if (isCloudPlaceholder || logicalSize == 0)
        {
            return 0;
        }

        if (!OperatingSystem.IsWindows())
        {
            return logicalSize;
        }

        try
        {
            var low = GetCompressedFileSize(path, out var high);
            if (low == uint.MaxValue && Marshal.GetLastWin32Error() != 0)
            {
                return logicalSize;
            }

            return ((long)high << 32) + low;
        }
        catch
        {
            return logicalSize;
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "GetCompressedFileSizeW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetCompressedFileSize(string fileName, out uint fileSizeHigh);
}
