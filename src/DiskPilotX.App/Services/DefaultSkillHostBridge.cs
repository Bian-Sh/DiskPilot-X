using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Skills;
using Microsoft.Win32;

namespace DiskPilotX.App.Services;

/// <summary>
/// 宿主侧默认的只读能力桥实现。把本机读取（注册表 / 文件系统 / 扫描快照）集中到一处，
/// 便于统一审计、限流与降级，也让 skill 源码本身保持"不知道机器细节"。
/// </summary>
public sealed class DefaultSkillHostBridge : ISkillHostBridge
{
    private readonly FileEntry? _root;
    private readonly Action<string>? _logger;

    public DefaultSkillHostBridge(
        string skillId,
        FileEntry? root = null,
        IReadOnlyDictionary<string, string>? parameters = null,
        Action<string>? logger = null)
    {
        SkillId = skillId;
        _root = root;
        Parameters = parameters ?? new Dictionary<string, string>(StringComparer.Ordinal);
        _logger = logger;
    }

    public string SkillId { get; }

    public IReadOnlyDictionary<string, string> Parameters { get; }

    public void Log(string message) => _logger?.Invoke(message);

    public string? ReadRegistry(string keyPath, string valueName)
    {
        if (string.IsNullOrWhiteSpace(keyPath) || string.IsNullOrWhiteSpace(valueName)) return null;
        // 32 位程序装在 64 位系统上会落到 WOW6432Node，故两种视图各试一次；
        // 这是通用的 WOW64 兼容处理，不含任何具体应用的路径知识。
        foreach (var candidate in RegistryKeyCandidates(keyPath))
        {
            try
            {
                var hive = candidate.Hive == RegistryHive.LocalMachine ? Registry.LocalMachine : Registry.CurrentUser;
                using var key = hive.OpenSubKey(candidate.Path, false);
                if (key?.GetValue(valueName) is string text && !string.IsNullOrWhiteSpace(text)) return text;
            }
            catch
            {
                // 键不存在或无读取权限时继续尝试下一个候选视图。
            }
        }
        return null;
    }

    private static IEnumerable<(RegistryHive Hive, string Path)> RegistryKeyCandidates(string keyPath)
    {
        var normalized = keyPath.Trim().TrimStart('\\');
        yield return (RegistryHive.LocalMachine, normalized);
        yield return (RegistryHive.CurrentUser, normalized);
        if (normalized.StartsWith(@"SOFTWARE\", StringComparison.OrdinalIgnoreCase))
        {
            var wowPath = @"SOFTWARE\WOW6432Node\" + normalized[@"SOFTWARE\".Length..];
            yield return (RegistryHive.LocalMachine, wowPath);
            yield return (RegistryHive.CurrentUser, wowPath);
        }
    }

    public bool DirectoryExists(string path)
    {
        if (!TryNormalize(path, out var full)) return false;
        try { return Directory.Exists(full); }
        catch { return false; }
    }

    public bool FileExists(string path)
    {
        if (!TryNormalize(path, out var full)) return false;
        try { return File.Exists(full); }
        catch { return false; }
    }

    public IReadOnlyList<string> EnumerateDirectories(string path)
    {
        if (!TryNormalize(path, out var full)) return [];
        try { return [.. Directory.EnumerateDirectories(full)]; }
        catch { return []; }
    }

    public IReadOnlyList<string> EnumerateFiles(string path, string searchPattern = "*")
    {
        if (!TryNormalize(path, out var full)) return [];
        try { return [.. Directory.EnumerateFiles(full, searchPattern)]; }
        catch { return []; }
    }

    public string? ReadTextFile(string path)
    {
        if (!TryNormalize(path, out var full)) return null;
        try { return File.ReadAllText(full); }
        catch { return null; }
    }

    public long GetDirectorySize(string path)
    {
        if (!TryNormalize(path, out var full) || !Directory.Exists(full)) return 0;
        long total = 0;
        var pending = new Stack<string>();
        pending.Push(full);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            try
            {
                foreach (var file in Directory.EnumerateFiles(current))
                {
                    try { total += new FileInfo(file).Length; }
                    catch { }
                }
                foreach (var directory in Directory.EnumerateDirectories(current))
                {
                    try
                    {
                        var info = new DirectoryInfo(directory);
                        if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    }
                    catch { }
                    pending.Push(directory);
                }
            }
            catch { }
        }
        return total;
    }

    public DateTime? GetDirectoryLastWriteTimeUtc(string path)
    {
        if (!TryNormalize(path, out var full) || !Directory.Exists(full)) return null;
        try { return Directory.GetLastWriteTimeUtc(full); }
        catch { return null; }
    }

    /// <summary>查询宿主已扫描的快照；rootPath 为空表示全量（按体积降序截断）。</summary>
    public IReadOnlyList<SkillHostEntry> QueryScan(string? rootPath = null, int maxCount = 20_000)
    {
        if (_root is null || maxCount <= 0) return [];
        var limit = Math.Min(maxCount, 20_000);
        var results = new List<SkillHostEntry>();
        var pending = new Stack<FileEntry>();
        pending.Push(_root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            results.Add(new(current.Path, current.Size, current.IsDirectory ? "directory" : "file", current.LastModified));
            foreach (var child in current.Children) pending.Push(child);
        }
        if (results.Count <= limit) return results;
        // 超量时保留体积最大的条目，避免 skill 只看到扫描起点附近的碎片。
        return [.. results.OrderByDescending(e => e.SizeBytes).Take(limit)];
    }

    private static bool TryNormalize(string? path, out string full)
    {
        full = string.Empty;
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return false;
        try { full = Path.GetFullPath(path); return true; }
        catch { return false; }
    }
}
