using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace DiskPilotX.App.Services;

/// <summary>Steam 卸载请求解决出来的执行目标。</summary>
public sealed record SteamUninstallTarget(
    string AppId,
    string DisplayName,
    string? InstallPath,
    string Executable,
    string Arguments)
{
    /// <summary>true 表示走的是 Steam 协议（steam://uninstall/&lt;appid&gt;），由 Steam 客户端自己接手。</summary>
    public bool UsesSteamProtocol => Arguments.StartsWith("steam:", StringComparison.OrdinalIgnoreCase);
}

public enum SteamUninstallOutcome
{
    Completed,
    Timeout,
    LaunchFailed,
    MissingEntry
}

public sealed record SteamUninstallResult(
    SteamUninstallOutcome Outcome,
    long BeforeBytes,
    long FreedBytes,
    string Detail)
{
    public bool IsDone => Outcome == SteamUninstallOutcome.Completed;
}

/// <summary>
/// Steam 游戏的「原生卸载」能力：只做三件事——
/// ① 从 Windows 卸载表读出 Steam 自己登记的卸载命令（steam.exe steam://uninstall/&lt;appid&gt;）；
/// ② 原样把这条命令交给 Steam，让 Steam 用自己的方式移除文件与库内条目（宿主不删文件、不自作主张）；
/// ③ 轮询 appmanifest 与安装目录，判断到底卸载完没有。
/// 所有 destructive 行为都必须先由调用方拿到用户强确认。
/// </summary>
public sealed class SteamGameUninstaller
{
    private const string UninstallRoot = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string SteamKey = @"SOFTWARE\Valve\Steam";
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// 解析某个 appid 的卸载命令。优先取 Steam 在卸载表里登记的 UninstallString，
    /// 读不到时回退到自己拼 steam://uninstall/&lt;appid&gt;（客户端位置同样来自注册表）。
    /// </summary>
    public SteamUninstallTarget? Resolve(string appId, string? installPath)
    {
        if (string.IsNullOrWhiteSpace(appId)) return null;

        foreach (var candidate in SteamAppKeyCandidates(appId))
        {
            using var key = OpenKey(candidate.Hive, candidate.Path);
            if (key is null) continue;

            var uninstall = key.GetValue("UninstallString") as string;
            var display = key.GetValue("DisplayName") as string;
            var location = key.GetValue("InstallLocation") as string;
            if (string.IsNullOrWhiteSpace(uninstall)) continue;

            var (exe, args) = SplitCommand(uninstall!);
            if (exe.Length == 0) continue;

            return new SteamUninstallTarget(
                appId,
                string.IsNullOrWhiteSpace(display) ? ("Steam App " + appId) : display!,
               FirstPath(location, installPath),
                exe,
                string.IsNullOrWhiteSpace(args) ? SteamProtocol(appId) : args);
        }

        // 卸载表没登记（游戏可能被清理过），退回协议方式，只要客户端还在就仍可触发卸载。
        var steamExe = ResolveSteamExecutable();
        return steamExe is null
            ? null
            : new SteamUninstallTarget(appId, "Steam App " + appId, installPath, steamExe, SteamProtocol(appId));
    }

    /// <summary>
    /// 执行卸载并等待完成。轮询 Steam 的应用清单文件与安装目录：
    /// 清单消失或安装目录消失即认为卸载已经生效，随后按剩余体积折算释放量。
    /// </summary>
    public async Task<SteamUninstallResult> RunAsync(
        SteamUninstallTarget target,
        long beforeBytes,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        var installPath = string.IsNullOrWhiteSpace(target.InstallPath) ? null : target.InstallPath;
        if (installPath is null && ManifestPaths(target).Count == 0)
            return new SteamUninstallResult(SteamUninstallOutcome.MissingEntry, beforeBytes, 0,
                "Steam 里找不到这个游戏的安装记录，它可能已经被卸载了。");

        var before = beforeBytes > 0 ? beforeBytes : installPath is not null ? Measure(installPath) : 0;
        log?.Invoke("已向 Steam 发出卸载请求：" + target.DisplayName + "（appid " + target.AppId + "）");

        try
        {
            var start = new ProcessStartInfo(target.Executable, target.Arguments) { UseShellExecute = true };
            if (Process.Start(start) is null)
                return new SteamUninstallResult(SteamUninstallOutcome.LaunchFailed, before, 0, "未能启动 Steam，请手动在 Steam 里卸载。");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new SteamUninstallResult(SteamUninstallOutcome.LaunchFailed, before, 0, "启动 Steam 失败：" + ex.Message);
        }

        var deadline = DateTime.UtcNow + DefaultTimeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);

            if (!StillInstalled(target, installPath))
            {
                var freed = Math.Max(before - (installPath is null ? 0 : Measure(installPath)), 0);
                log?.Invoke("检测到 " + target.DisplayName + " 已从 Steam 库中移除。");
                return new SteamUninstallResult(SteamUninstallOutcome.Completed, before, freed > 0 ? freed : before,
                    "Steam 已完成卸载。");
            }
        }

        return new SteamUninstallResult(SteamUninstallOutcome.Timeout, before, 0,
            "已把卸载请求交给 Steam，但在预定时间内没有检测到它被移除（Steam 可能还在等你在它自己的弹窗里确认）。");
    }

    /// <summary>应用是否仍处于「已安装」状态：清单文件与安装目录都在才算。</summary>
    private static bool StillInstalled(SteamUninstallTarget target, string? installPath)
    {
        var manifests = ManifestPaths(target);
        if (manifests.Count > 0 && manifests.Any(File.Exists)) return true;
        return !string.IsNullOrWhiteSpace(installPath) && Directory.Exists(installPath);
    }

    /// <summary>候选清单文件：安装目录所在库 + Steam 客户端自身库。</summary>
    private static List<string> ManifestPaths(SteamUninstallTarget target)
    {
        var paths = new List<string>();
        foreach (var library in LibraryCandidates(target))
        {
            var manifest = Path.Combine(library, "steamapps", "appmanifest_" + target.AppId + ".acf");
            if (!paths.Contains(manifest, StringComparer.OrdinalIgnoreCase)) paths.Add(manifest);
        }
        return paths;
    }

    private static IEnumerable<string> LibraryCandidates(SteamUninstallTarget target)
    {
        // <库>/steamapps/common/<游戏> → <库>（迭代器里不能有 try/catch，故先把结果算出来）
        var fromInstall = LibraryFromInstallPath(target.InstallPath);
        if (fromInstall is not null) yield return fromInstall;

        var root = Path.GetDirectoryName(target.Executable);
        if (!string.IsNullOrWhiteSpace(root)) yield return root!;
    }

    private static string? LibraryFromInstallPath(string? installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath)) return null;
        try { return Directory.GetParent(installPath!)?.Parent?.Parent?.FullName; }
        catch { return null; }
    }

    private static string SteamProtocol(string appId) => "steam://uninstall/" + appId;

    /// <summary>卸载命令 → 可执行文件 + 参数（兼容带引号的路径）。</summary>
    public static (string Executable, string Arguments) SplitCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return (string.Empty, string.Empty);
        var text = command.Trim();

        if (text[0] == '"')
        {
            var close = text.IndexOf('"', 1);
            if (close > 0)
                return (text[1..close], text[(close + 1)..].Trim());
        }

        var space = text.IndexOf(' ');
        return space <= 0 ? (text, string.Empty) : (text[..space], text[(space + 1)..].Trim());
    }

    /// <summary>从注册表拿到 Steam 客户端可执行文件位置。</summary>
    private static string? ResolveSteamExecutable()
    {
        foreach (var candidate in ValveKeyCandidates())
        {
            using var key = OpenKey(candidate.Hive, candidate.Path);
            if (key is null) continue;

            foreach (var name in new[] { "SteamExe", "InstallPath" })
            {
                if (key.GetValue(name) is not string text || string.IsNullOrWhiteSpace(text)) continue;
                var exe = name == "SteamExe" ? text : Path.Combine(text, "steam.exe");
                if (File.Exists(exe)) return exe;
            }
        }
        return null;
    }

    private static long Measure(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return 0;
            long total = 0;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(file).Length; }
                catch { }
            }
            return total;
        }
        catch { return 0; }
    }

    private static string? FirstPath(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value) && Directory.Exists(value!));

    private static IEnumerable<(RegistryHive Hive, string Path)> SteamAppKeyCandidates(string appId)
    {
        var suffix = @"Steam App " + appId;
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            yield return (hive, UninstallRoot + "\\" + suffix);
            yield return (hive, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\" + suffix);
        }
    }

    private static IEnumerable<(RegistryHive Hive, string Path)> ValveKeyCandidates()
    {
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            yield return (hive, SteamKey);
            yield return (hive, @"SOFTWARE\WOW6432Node\Valve\Steam");
        }
    }

    private static RegistryKey? OpenKey(RegistryHive hive, string path)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32);
            return baseKey.OpenSubKey(path, false);
        }
        catch { return null; }
    }
}
