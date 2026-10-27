using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Skills;

/// <summary>
/// Steam 游戏库盘点（只读发现）。
///
/// 设计要点：源码不硬编码任何本机安装路径。客户端位置从注册表读取，
/// 库目录从 libraryfolders.vdf 解析（支持多个盘），游戏清单从
/// steamapps/appmanifest_*.acf 逐个解析；占用大小实测目录，
/// 上次游玩时间优先取 localconfig.vdf，缺失时回退安装目录时间戳。
///
/// 一切本机读取都经 context.Host（API Bridge）完成。
/// 本源码不是安全沙箱：只做只读发现，不写文件、不启进程、不联网。
/// </summary>
public sealed class Provider : IAppDataProvider
{
    private static readonly Regex NameRegex = new("\"name\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex InstallDirRegex = new("\"installdir\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LibraryPathRegex = new("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AppBlockRegex = new("\"(?<appid>\\d+)\"\\s*\\{(?<body>[^{}]*)\\}", RegexOptions.Compiled);
    private static readonly Regex LastPlayedRegex = new("\"LastPlayed\"\\s+\"(\\d+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 客户端引导程序 / 运行时组件，不是游戏，不计入盘点
    private static readonly HashSet<string> ExcludedAppIds = new(StringComparer.Ordinal)
    {
        "228980",   // Steamworks Common Redistributables
        "1070560",  // Steam Linux Runtime - Soldier
        "1391110",  // Steam Linux Runtime - Sniper
        "1628350",  // Steam Linux Runtime - Sniper (updated)
    };

    private const int MaxEntries = 20000;
    private const int TopPosterCount = 12;
    private const int MaxTableRows = 60;

    public Task<AppDataDiscoveryResult> DiscoverAsync(AppDataDiscoveryContext context, CancellationToken token)
    {
        var host = context.Host;

        var root = ResolveSteamRoot(host);
        if (root is null)
        {
            host.Log("未定位到 Steam 安装目录（已尝试 HKLM/HKCU 的 SOFTWARE\\Valve\\Steam 的 InstallPath/SteamPath）。");
            return Task.FromResult(new AppDataDiscoveryResult([], null));
        }

        var libraries = ResolveLibraries(host, root);
        var lastPlayed = ResolveLastPlayed(host, root);

        var entries = new List<AppDataEntry>();
        foreach (var library in libraries)
        {
            token.ThrowIfCancellationRequested();
            entries.AddRange(ScanLibrary(host, library, lastPlayed, token));
        }

        entries = [.. entries.OrderByDescending(entry => entry.SizeBytes).Take(MaxEntries)];
        host.Log("发现 " + entries.Count + " 个已安装项目，合计 " + FormatSize(entries.Sum(entry => entry.SizeBytes)) + "。");

        return Task.FromResult(new AppDataDiscoveryResult(entries, BuildView(root, libraries.Count, entries, lastPlayed)));
    }

    /// <summary>经 Bridge 读注册表定位客户端根目录；桥已处理 32/64 位视图差异。</summary>
    private static string? ResolveSteamRoot(ISkillHostBridge host)
    {
        string[] keys = [@"SOFTWARE\Valve\Steam", @"SOFTWARE\Wow6432Node\Valve\Steam"];
        string[] values = ["InstallPath", "SteamPath", "SteamExe"];

        foreach (var key in keys)
            foreach (var value in values)
            {
                var text = host.ReadRegistry(key, value);
                if (string.IsNullOrWhiteSpace(text)) continue;

                // SteamExe 形如 "D:/Program Files/Steam/steam.exe"，取其所在目录
                var candidate = text.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetDirectoryName(text)
                    : text;

                if (string.IsNullOrWhiteSpace(candidate)) continue;
                if (!host.DirectoryExists(candidate)) continue;
                try { return Path.GetFullPath(candidate); }
                catch { }
            }

        return null;
    }

    /// <summary>解析库清单；缺失时仅保留根目录，不猜测。</summary>
    private static List<string> ResolveLibraries(ISkillHostBridge host, string root)
    {
        var libraries = new List<string> { root };
        var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
        var text = host.ReadTextFile(vdf);

        if (text is null)
        {
            host.Log("未读取到库清单：" + vdf + "（仅盘点根目录库）");
            return libraries;
        }

        foreach (Match match in LibraryPathRegex.Matches(text))
        {
            var candidate = match.Groups[1].Value.Replace("\\\\", "\\");
            if (string.IsNullOrWhiteSpace(candidate) || !host.DirectoryExists(candidate)) continue;
            try
            {
                var full = Path.GetFullPath(candidate);
                if (!libraries.Any(existing => string.Equals(existing, full, StringComparison.OrdinalIgnoreCase)))
                    libraries.Add(full);
            }
            catch { }
        }

        return libraries;
    }

    /// <summary>扫某个库下的全部游戏清单。</summary>
    private static IEnumerable<AppDataEntry> ScanLibrary(
        ISkillHostBridge host,
        string library,
        IReadOnlyDictionary<string, DateTime> lastPlayed,
        CancellationToken token)
    {
        var steamapps = Path.Combine(library, "steamapps");
        if (!host.DirectoryExists(steamapps)) return [];

        var entries = new List<AppDataEntry>();
        foreach (var manifest in host.EnumerateFiles(steamapps, "appmanifest_*.acf"))
        {
            token.ThrowIfCancellationRequested();
            var entry = ParseManifest(host, steamapps, manifest, lastPlayed);
            if (entry is not null) entries.Add(entry);
        }
        return entries;
    }

    private static AppDataEntry? ParseManifest(
        ISkillHostBridge host,
        string steamapps,
        string manifest,
        IReadOnlyDictionary<string, DateTime> lastPlayed)
    {
        var text = host.ReadTextFile(manifest);
        if (string.IsNullOrWhiteSpace(text)) return null;

        var name = FirstGroup(NameRegex, text);
        if (string.IsNullOrWhiteSpace(name)) return null;

        var appId = Path.GetFileNameWithoutExtension(manifest);
        const string prefix = "appmanifest_";
        if (appId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) appId = appId[prefix.Length..];
        if (string.IsNullOrWhiteSpace(appId) || ExcludedAppIds.Contains(appId)) return null;

        var installDir = FirstGroup(InstallDirRegex, text);
        var installPath = string.IsNullOrWhiteSpace(installDir)
            ? string.Empty
            : Path.Combine(steamapps, "common", installDir);

        var size = !string.IsNullOrWhiteSpace(installPath) && host.DirectoryExists(installPath)
            ? host.GetDirectorySize(installPath)
            : 0L;

        var played = lastPlayed.TryGetValue(appId, out var stamp)
            ? (DateTime?)stamp
            : host.GetDirectoryLastWriteTimeUtc(installPath);

        return new AppDataEntry(appId, name, installPath, size, Describe(played));
    }

    /// <summary>解析 localconfig.vdf，取每个 appid 的 LastPlayed（Unix 秒）。</summary>
    private static Dictionary<string, DateTime> ResolveLastPlayed(ISkillHostBridge host, string root)
    {
        var result = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        var userdata = Path.Combine(root, "userdata");
        if (!host.DirectoryExists(userdata)) return result;

        foreach (var account in host.EnumerateDirectories(userdata))
        {
            var config = Path.Combine(account, "config", "localconfig.vdf");
            var text = host.ReadTextFile(config);
            if (string.IsNullOrWhiteSpace(text)) continue;

            foreach (Match match in AppBlockRegex.Matches(text))
            {
                var stamp = LastPlayedRegex.Match(match.Groups["body"].Value);
                if (!stamp.Success) continue;
                var seconds = 0L;
                if (!long.TryParse(stamp.Groups[1].Value, out seconds) || seconds <= 0) continue;

                var moment = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
                var appId = match.Groups["appid"].Value;
                if (!result.TryGetValue(appId, out var existing) || moment > existing) result[appId] = moment;
            }
        }

        return result;
    }

    private static string? Describe(DateTime? moment)
    {
        if (moment is null) return null;
        var elapsed = DateTime.UtcNow - moment.Value;
        if (elapsed < TimeSpan.Zero) return "上次游玩：刚刚";
        if (elapsed.TotalDays >= 365) return "上次游玩：" + (int)(elapsed.TotalDays / 365) + " 年前";
        if (elapsed.TotalDays >= 30) return "上次游玩：" + (int)(elapsed.TotalDays / 30) + " 个月前";
        if (elapsed.TotalDays >= 1) return "上次游玩：" + (int)elapsed.TotalDays + " 天前";
        return "上次游玩：今天";
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var index = 0;
        while (value >= 1024 && index < units.Length - 1) { value /= 1024; index++; }
        // 手工定点取整：不依赖区域设置，避免某些区域把小数点渲染成逗号
        var scaled = (long)(value * 10 + 0.5);
        var whole = scaled / 10;
        var fraction = scaled % 10;
        return (index == 0 ? whole.ToString() : whole + "." + fraction) + " " + units[index];
    }

    private static string? FirstGroup(Regex regex, string text)
    {
        var match = regex.Match(text);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>所在库的短名（安装目录往上三级：&lt;库&gt;/steamapps/common/&lt;game&gt;）。</summary>
    private static string ShortLibrary(string? installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath)) return "-";
        try
        {
            var library = Directory.GetParent(installPath)?.Parent?.Parent;
            return library?.FullName ?? installPath;
        }
        catch { return installPath; }
    }

    /// <summary>按上次游玩时间给出语义徽章，配色由宿主的 VP 主题决定。</summary>
    private static string PlayBadge(DateTime? moment)
    {
        if (moment is null) return "未知";
        var days = (DateTime.UtcNow - moment.Value).TotalDays;
        if (days >= 365) return "久未游玩";
        if (days >= 90) return "较少游玩";
        if (days >= 14) return "偶尔游玩";
        return "近期常玩";
    }

    /// <summary>
    /// 声明按需构建的视图：概览统计 + 封面海报网格（带上次游玩）+ 全量明细表。
    /// 气泡里只放一张入口卡，完整界面在独立窗口中呈现，勾选后可回传继续分析。
    /// </summary>
    private static SkillUiSpec BuildView(
        string root,
        int libraryCount,
        IReadOnlyList<AppDataEntry> entries,
        IReadOnlyDictionary<string, DateTime> lastPlayed)
    {
        if (entries.Count == 0)
        {
            return new SkillUiSpec
            {
                Title = "Steam 游戏库盘点",
                Subtitle = "客户端位于 " + root + "，但没有解析到任何已安装项目。",
                Surface = SkillUiSurface.Entry,
                Sections =
                [
                    new SkillUiSection
                    {
                        Kind = SkillUiSectionKind.Text,
                        Items =
                        [
                            new SkillUiItem
                            {
                                Title = "库里暂时没有已安装的游戏",
                                Subtitle = "如果游戏确实装着，可能是库清单 libraryfolders.vdf 里指向的盘当前不可访问。"
                            }
                        ]
                    }
                ],
                Actions = [new SkillUiAction { Kind = "close", Label = "知道了" }]
            };
        }

        var total = entries.Sum(entry => entry.SizeBytes);
        var average = total / entries.Count;
        var top = entries[0];

        var stats = new SkillUiSection
        {
            Kind = SkillUiSectionKind.StatRow,
            Title = "库概览",
            Subtitle = "客户端：" + root + " · 共 " + libraryCount + " 个库位置",
            Items =
            [
                new SkillUiItem { Title = "已安装项目", Value = entries.Count.ToString(), ValueLabel = "个" },
                new SkillUiItem { Title = "占用合计", Value = FormatSize(total), ValueLabel = "所有库累加" },
                new SkillUiItem { Title = "单体最大", Value = FormatSize(top.SizeBytes), ValueLabel = top.Name },
                new SkillUiItem { Title = "平均占用", Value = FormatSize(average), ValueLabel = "每个项目" }
            ]
        };

        var posters = new SkillUiSection
        {
            Kind = SkillUiSectionKind.PosterGrid,
            Title = "占用排行",
            Subtitle = "按实测体积从大到小，封面自动取自 Steam CDN，副标题显示上次游玩时间。",
            Columns = 4,
            Items = [.. entries.Take(TopPosterCount).Select(entry =>
            {
                var moment = lastPlayed.TryGetValue(entry.AppId, out var stamp) ? (DateTime?)stamp : null;
                return new SkillUiItem
                {
                    Title = entry.Name,
                    Value = FormatSize(entry.SizeBytes),
                    Subtitle = entry.Detail ?? "上次游玩时间未知",
                    Badge = PlayBadge(moment),
                    AppId = entry.AppId,
                    Path = entry.Path,
                    SizeBytes = entry.SizeBytes,
                    Metric = entry.SizeBytes,
                    Selectable = true
                };
            })]
        };

        var table = new SkillUiSection
        {
            Kind = SkillUiSectionKind.Table,
            Title = "全部明细",
            Subtitle = "共 " + entries.Count + " 行，可在窗口中滚动查看；勾选后可回传给助手继续清理分析。",
            Headers = ["游戏", "占用", "上次游玩", "所在库"],
            Items = [.. entries.Take(MaxTableRows).Select(entry => new SkillUiItem
            {
                Cells =
                [
                    entry.Name,
                    FormatSize(entry.SizeBytes),
                    entry.Detail ?? "-",
                    ShortLibrary(entry.Path)
                ],
                Path = entry.Path,
                AppId = entry.AppId,
                SizeBytes = entry.SizeBytes,
                Selectable = true
            })]
        };

        return new SkillUiSpec
        {
            Title = "Steam 游戏库盘点",
            Subtitle = "共 " + entries.Count + " 个已安装项目，合计 " + FormatSize(total) + "，分布在 " + libraryCount + " 个库位置。",
            // auto：气泡放入口卡的同时，把完整盘点界面直接弹出来。
            Surface = SkillUiSurface.Auto,
            EntryTitle = "Steam 游戏库盘点",
            EntrySummary = entries.Count + " 个已安装项目 · 合计 " + FormatSize(total) + " · 点击查看海报与上次游玩时间",
            Sections = [stats, posters, table],
            Actions =
            [
                new SkillUiAction { Kind = "continue", Label = "回传选中项继续分析" },
                new SkillUiAction { Kind = "close", Label = "关闭" }
            ]
        };
    }
}
