using System.Text.RegularExpressions;

namespace SpaceMonger.Core.Services.Skills;

internal static class CleanupSkillSafety
{
    internal static readonly string[] ReservedIds = ["app-guide", "disk-management", "path-cleanup-recommendation", "unity-project-cleanup", "skill-creator"];

    internal static void ValidateId(string id)
    {
        if (string.IsNullOrEmpty(id) || !Regex.IsMatch(id, "^[a-z0-9][a-z0-9-]{0,63}$") ||
            Regex.IsMatch(id, "^(con|prn|aux|nul|com[0-9]|lpt[0-9])$", RegexOptions.IgnoreCase))
            throw new ArgumentException("技能或规则 ID 必须为小写字母、数字和连字符，不能使用设备名称。");
    }

    internal static void ValidatePattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern) || pattern.Length > 512 || System.IO.Path.IsPathRooted(pattern) ||
            pattern.Any(c => char.IsControl(c) || ":<>\"|".Contains(c)) ||
            pattern.Replace('\\', '/').Split('/').Any(p => p is "" or "." or ".." || p.EndsWith(' ') || p.EndsWith('.')))
            throw new ArgumentException("规则路径必须为范围内的相对路径；不允许父目录、绝对路径、数据流或歧义名称。");
    }

    internal static string SafePath(string root, string relative)
    {
        var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, relative));
        EnsureWithin(root, full);
        EnsureNoReparsePoints(full);
        return full;
    }

    internal static void EnsureWithin(string root, string full)
    {
        var canonicalRoot = System.IO.Path.GetFullPath(root).TrimEnd('\\', '/');
        var canonicalPath = System.IO.Path.GetFullPath(full);
        if (!canonicalPath.Equals(canonicalRoot, StringComparison.OrdinalIgnoreCase) &&
            !canonicalPath.StartsWith(canonicalRoot + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("路径超出用户选定范围。");
    }

    internal static void EnsureNoReparsePoints(string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        for (var current = full; !string.IsNullOrEmpty(current); current = System.IO.Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("不允许重解析点或符号链接：" + current);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    internal static bool Matches(string relative, string pattern)
    {
        relative = relative.Replace('\\', '/');
        pattern = pattern.Replace('\\', '/');
        var expression = Regex.Escape(pattern).Replace(@"\*\*/", "(?:.*/)?").Replace(@"\*\*", ".*").Replace(@"\*", "[^/]*").Replace(@"\?", "[^/]");
        return Regex.IsMatch(relative, "^" + expression + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }

    internal static bool IsExcluded(string relative, IEnumerable<string> exclusions)
    {
        relative = relative.Replace('\\', '/');
        foreach (var pattern in exclusions)
        {
            var prefix = pattern.Replace('\\', '/').TrimEnd('/');
            if (prefix.EndsWith("/**")) prefix = prefix[..^3];
            if (Matches(relative, pattern) || Matches(relative, prefix)) return true;
            for (var slash = relative.LastIndexOf('/'); slash > 0; slash = relative.LastIndexOf('/', slash - 1))
                if (Matches(relative[..slash], prefix)) return true;
        }
        return false;
    }
}
