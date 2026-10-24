namespace SpaceMonger.Core.Tests;

internal sealed class CleanupTestDirectory : IDisposable
{
    private readonly string _temporaryRoot = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
    public string Path { get; }

    public CleanupTestDirectory()
    {
        Path = System.IO.Path.Combine(_temporaryRoot, $"SpaceMonger-cleanup-snapshot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string WriteFile(string relativePath, string content = "原始内容")
    {
        var target = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
        File.WriteAllText(target, content);
        return target;
    }

    public void Dispose()
    {
        var target = System.IO.Path.GetFullPath(Path);
        if (!target.StartsWith(System.IO.Path.TrimEndingDirectorySeparator(_temporaryRoot) + System.IO.Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) ||
            !System.IO.Path.GetFileName(target).StartsWith("SpaceMonger-cleanup-snapshot-", StringComparison.Ordinal))
            throw new InvalidOperationException("拒绝清理本次测试目录以外的路径。");
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
    }
}
