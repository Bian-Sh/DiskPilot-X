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
}
