namespace DiskPilotX.Core;

/// <summary>品牌更名不应改变既有用户数据和启动配置的可用性。</summary>
public static class AppCompatibility
{
    public static string ResolveDataPath(string baseDirectory, string relativePath) =>
        Path.Combine(baseDirectory, "DiskPilot-X", relativePath);

    public static string? GetEnvironmentVariable(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) && name.StartsWith("DISKPILOT_X_", StringComparison.Ordinal)
            ? Environment.GetEnvironmentVariable("SPACEMONGER_" + name[12..]) : value;
    }

    /// <summary>
    /// 一次性把品牌更名前 SpaceMonger 目录下的既有数据并入统一的 DiskPilot-X 目录。
    /// 目标已存在的文件一律保留（新版优先），旧目录及其文件原样留存、绝不覆盖或删除。
    /// </summary>
    public static IReadOnlyList<string> MigrateLegacyData() => MigrateLegacyData(new[]
    {
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    });

    public static IReadOnlyList<string> MigrateLegacyData(IEnumerable<string> baseDirectories)
    {
        var migrated = new List<string>();
        foreach (var baseDirectory in baseDirectories)
        {
            if (string.IsNullOrWhiteSpace(baseDirectory))
                continue;

            var legacyRoot = Path.Combine(baseDirectory, "SpaceMonger");
            var currentRoot = Path.Combine(baseDirectory, "DiskPilot-X");
            if (!Directory.Exists(legacyRoot))
                continue;

            CopyLegacyTree(legacyRoot, currentRoot, migrated);
        }

        return migrated;
    }

    private static void CopyLegacyTree(string source, string destination, List<string> migrated)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.GetFiles(source))
        {
            var target = Path.Combine(destination, Path.GetFileName(file));
            if (File.Exists(target))
                continue;

            File.Copy(file, target);
            migrated.Add(target);
        }

        foreach (var directory in Directory.GetDirectories(source))
            CopyLegacyTree(directory, Path.Combine(destination, Path.GetFileName(directory)), migrated);
    }
}
