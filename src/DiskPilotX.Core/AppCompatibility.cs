namespace DiskPilotX.Core;

/// <summary>品牌更名不应改变既有用户数据和启动配置的可用性。</summary>
public static class AppCompatibility
{
    public static string ResolveDataPath(string baseDirectory, string relativePath)
    {
        var current = Path.Combine(baseDirectory, "DiskPilot-X", relativePath);
        var legacy = Path.Combine(baseDirectory, "SpaceMonger", relativePath);
        // 沿用已有数据的位置，不搬移、覆盖用户文件；两处都有数据时以新版为准。
        return !File.Exists(current) && !Directory.Exists(current)
            && (File.Exists(legacy) || Directory.Exists(legacy)) ? legacy : current;
    }

    public static string? GetEnvironmentVariable(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) && name.StartsWith("DISKPILOT_X_", StringComparison.Ordinal)
            ? Environment.GetEnvironmentVariable("SPACEMONGER_" + name[12..]) : value;
    }
}
