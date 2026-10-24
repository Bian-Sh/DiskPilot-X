using System.Security.Cryptography;
using System.Text.Json;
using SpaceMonger.Core.Services.Analysis;

namespace SpaceMonger.Core.Services.Cleanup;

/// <summary>仅采集真实文件系统元数据，不读取正文，也不跟随重解析点。</summary>
internal sealed record CleanupFilesystemSnapshot(string Fingerprint, long Size, bool IsDirectory, bool Exists)
{
    public static CleanupFilesystemSnapshot Capture(string targetPath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (RecommendationEngine.IsDeletionProtected(targetPath))
            throw new InvalidOperationException("清理目标受保护或路径无效。");

        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetPath));
        if (string.Equals(path, Path.TrimEndingDirectorySeparator(Path.GetPathRoot(path)!), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("禁止清理磁盘根目录。");

        CheckAncestors(path, ct);
        FileAttributes attributes;
        try { attributes = File.GetAttributes(path); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new(Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { Path = path, Missing = true }))),
                0, false, false);
        }

        try
        {
            var first = ReadTree(path, ct);
            CheckAncestors(path, ct);
            var second = ReadTree(path, ct);
            CheckAncestors(path, ct);
            if (first != second || File.GetAttributes(path) != attributes)
                throw new IOException("采集清理快照期间目标发生变化。");
            return new(second.Fingerprint, second.Size, (attributes & FileAttributes.Directory) != 0, true);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // 仅最初不存在的目标可视为已移除；遍历时缺失任何节点均拒绝继续。
            throw new IOException("采集清理快照期间有节点消失。", ex);
        }
    }

    private static (string Fingerprint, long Size) ReadTree(string path, CancellationToken ct)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long size = Visit(path, hash, ct);
        return (Convert.ToHexString(hash.GetHashAndReset()), size);
    }

    private static long Visit(string path, IncrementalHash hash, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var before = ReadMetadata(path);
        hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(before));
        long size = before.Length;
        if (before.IsDirectory)
        {
            var children = ReadChildren(path, ct);
            foreach (var child in children)
                size = checked(size + Visit(child, hash, ct));
            if (!children.SequenceEqual(ReadChildren(path, ct), StringComparer.Ordinal))
                throw new IOException("采集清理快照期间目录内容发生变化。");
        }
        ct.ThrowIfCancellationRequested();
        if (before != ReadMetadata(path))
            throw new IOException("采集清理快照期间节点元数据发生变化。");
        return size;
    }

    private static string[] ReadChildren(string path, CancellationToken ct)
    {
        var children = new List<string>();
        foreach (var child in Directory.EnumerateFileSystemEntries(path, "*", new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = false,
            AttributesToSkip = 0
        }))
        {
            ct.ThrowIfCancellationRequested();
            children.Add(child);
        }
        children.Sort(StringComparer.Ordinal);
        ct.ThrowIfCancellationRequested();
        return children.ToArray();
    }

    private static Metadata ReadMetadata(string path)
    {
        if (RecommendationEngine.IsDeletionProtected(path))
            throw new InvalidOperationException("清理目录包含受保护项。");
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("禁止清理链接或重解析点。");
        FileSystemInfo info = (attributes & FileAttributes.Directory) != 0 ? new DirectoryInfo(path) : new FileInfo(path);
        info.Refresh();
        var metadata = new Metadata(path, attributes, info is FileInfo file ? file.Length : 0,
            info.LastWriteTimeUtc.Ticks, info.CreationTimeUtc.Ticks);
        if (File.GetAttributes(path) != attributes)
            throw new IOException("采集清理快照期间节点属性发生变化。");
        return metadata;
    }

    private static void CheckAncestors(string path, CancellationToken ct)
    {
        var ancestors = new Stack<string>();
        for (var parent = Path.GetDirectoryName(path); parent is not null; parent = Path.GetDirectoryName(parent))
            ancestors.Push(parent);
        foreach (var ancestor in ancestors)
        {
            ct.ThrowIfCancellationRequested();
            FileAttributes attributes;
            try { attributes = File.GetAttributes(ancestor); }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { break; }
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("清理路径经过链接或重解析点。");
        }
    }

    private sealed record Metadata(string Path, FileAttributes Attributes, long Length, long LastWriteUtcTicks, long CreationUtcTicks)
    {
        public bool IsDirectory => (Attributes & FileAttributes.Directory) != 0;
    }
}
