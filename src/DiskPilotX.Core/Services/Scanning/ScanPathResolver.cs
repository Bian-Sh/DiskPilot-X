using System.IO;

namespace DiskPilotX.Core.Services.Scanning;

public static class ScanPathResolver
{
    public static string Resolve(string path)
    {
        var trimmed = path.Trim();
        var expanded = Environment.ExpandEnvironmentVariables(trimmed);
        return Path.GetFullPath(expanded);
    }

    public static bool TryResolve(string? path, out string resolvedPath, out string? error)
    {
        resolvedPath = string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "Path is empty.";
            return false;
        }

        try
        {
            resolvedPath = Resolve(path);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = ex.Message;
            return false;
        }
    }
}
