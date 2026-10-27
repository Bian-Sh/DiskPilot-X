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
/// steamapps/appmanifest_*.acf 逐个解析，占用大小实测目录。
///
/// 启动判定（是否有开启过）——只认两类 Steam 元数据，绝不编造：
/// ① appmanifest 的 LastPlayed 是"本次安装是否启动过"的权威：
///    >0 → 启动过（时间可信）；=0 → 本次安装从未启动（哪怕账号里留着旧安装的历史）。
/// ② localconfig.vdf 的账号记录（LastPlayed / Playtime / autocloud.lastlaunch）
///    是跨安装的历史佐证，只用来把"从未启动"细分成「下载后未启动（X 前玩过）」。
/// 目录时间戳不是游玩信号（批量迁移/校验都会改写它），因此不再参与判定。
///
/// 一切本机读取都经 context.Host（API Bridge）完成。
/// 本源码不是安全沙箱：只做只读发现，不写文件、不启进程、不联网。
/// </summary>
public sealed class Provider : IAppDataProvider
{
    private static readonly Regex NameRegex = new("\"name\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex InstallDirRegex = new("\"installdir\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LibraryPathRegex = new("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NumericBlockRegex = new("\"(?<appid>\\d+)\"\\s*\\{", RegexOptions.Compiled);
    private static readonly Regex LastPlayedRegex = new("\"LastPlayed\"\\s+\"(\\d+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PlaytimeRegex = new("\"Playtime\"\\s+\"(\\d+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LastLaunchRegex = new("\"lastlaunch\"\\s+\"(\\d+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

    /// <summary>启动判定的状态机。</summary>
    private enum LaunchKind
    {
        /// <summary>清单无 LastPlayed 字段（老客户端）且账号无记录——不知道，不编造。</summary>
        Unknown,

        /// <summary>清单 LastPlayed=0：本次安装从未启动。</summary>
        NeverLaunched,

        /// <summary>有真实启动时间。</summary>
        Played
    }

    /// <summary>
    /// 一个游戏的启动判定结果。
    /// LastPlayedUtc 仅在 Played 时有值；HistoryUtc / HasPlayHistory 用于
    /// NeverLaunched 时标注"账号以前玩过"（旧安装残留的记录）。
    /// </summary>
    private sealed record LaunchInfo(LaunchKind Kind, DateTime? LastPlayedUtc, DateTime? HistoryUtc, bool HasPlayHistory)
    {
        public string Describe() => Kind switch
        {
            LaunchKind.Played when LastPlayedUtc is null => "有游玩记录（时间未知）",
            LaunchKind.Played => "上次游玩：" + Relative(LastPlayedUtc!.Value),
            LaunchKind.NeverLaunched when HasPlayHistory && HistoryUtc is not null => "下载后未启动（" + Relative(HistoryUtc.Value) + "玩过）",
            LaunchKind.NeverLaunched when HasPlayHistory => "下载后未启动（有游玩记录）",
            LaunchKind.NeverLaunched => "从未启动",
            _ => "启动记录未知"
        };

        public string Badge() => Kind switch
        {
            LaunchKind.Played when LastPlayedUtc is null => "已玩过",
            LaunchKind.Played => PlayBadge(LastPlayedUtc!.Value),
            LaunchKind.NeverLaunched => "未启动",
            _ => "未知"
        };

        private static string Relative(DateTime moment)
        {
            var elapsed = DateTime.UtcNow - moment;
            if (elapsed < TimeSpan.Zero) return "刚刚";
            if (elapsed.TotalDays >= 365) return (int)(elapsed.TotalDays / 365) + " 年前";
            if (elapsed.TotalDays >= 30) return (int)(elapsed.TotalDays / 30) + " 个月前";
            if (elapsed.TotalDays >= 1) return (int)elapsed.TotalDays + " 天前";
            return "今天";
        }

        private static string PlayBadge(DateTime moment)
        {
            var days = (DateTime.UtcNow - moment).TotalDays;
            if (days >= 365) return "久未游玩";
            if (days >= 90) return "较少游玩";
            if (days >= 14) return "偶尔游玩";
            return "近期常玩";
        }
    }

    /// <summary>localconfig.vdf 里某个 app 的账号级历史记录（跨安装）。</summary>
    private sealed record AccountRecord(DateTime? LastPlayedUtc, DateTime? LastLaunchUtc, long PlaytimeMinutes)
    {
        /// <summary>真启动过的硬证据：累计运行时长或 autocloud 记录的启动。仅有 LastPlayed 属于弱证据。</summary>
        public bool HasPlayEvidence => PlaytimeMinutes > 0 || LastLaunchUtc is not null;

        /// <summary>历史时间点（用于展示），取两个正时间戳里较新的。</summary>
        public DateTime? HistoryMoment => Latest(LastPlayedUtc, LastLaunchUtc);

        private static DateTime? Latest(DateTime? left, DateTime? right) => (left, right) switch
        {
            (null, null) => null,
            (not null, null) => left,
            (null, not null) => right,
            _ => left > right ? left : right
        };
    }

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
        var accounts = ResolveAccountRecords(host, root);

        var rows = new List<(AppDataEntry Entry, LaunchInfo Launch)>();
        foreach (var library in libraries)
        {
            token.ThrowIfCancellationRequested();
            rows.AddRange(ScanLibrary(host, library, accounts, token));
        }

        rows = [.. rows.OrderByDescending(row => row.Entry.SizeBytes).Take(MaxEntries)];
        host.Log("发现 " + rows.Count + " 个已安装项目，合计 " + FormatSize(rows.Sum(row => row.Entry.SizeBytes)) + "。");

        return Task.FromResult(new AppDataDiscoveryResult(
            [.. rows.Select(row => row.Entry)],
            BuildView(root, libraries.Count, rows)));
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
    private static IEnumerable<(AppDataEntry Entry, LaunchInfo Launch)> ScanLibrary(
        ISkillHostBridge host,
        string library,
        IReadOnlyDictionary<string, AccountRecord> accounts,
        CancellationToken token)
    {
        var steamapps = Path.Combine(library, "steamapps");
        if (!host.DirectoryExists(steamapps)) return [];

        var rows = new List<(AppDataEntry Entry, LaunchInfo Launch)>();
        foreach (var manifest in host.EnumerateFiles(steamapps, "appmanifest_*.acf"))
        {
            token.ThrowIfCancellationRequested();
            var row = ParseManifest(host, steamapps, manifest, accounts);
            if (row is not null) rows.Add(row.Value);
        }
        return rows;
    }

    private static (AppDataEntry Entry, LaunchInfo Launch)? ParseManifest(
        ISkillHostBridge host,
        string steamapps,
        string manifest,
        IReadOnlyDictionary<string, AccountRecord> accounts)
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

        accounts.TryGetValue(appId, out var account);
        var launch = ResolveLaunch(ParseEpoch(LastPlayedRegex, text), account);

        var entry = new AppDataEntry(appId, name, installPath, size, launch.Describe());
        return (entry, launch);
    }

    /// <summary>
    /// 合并两类元数据给出启动判定：
    /// 清单字段存在时它说了算（=0 即本次安装从未启动，账号历史不能翻案）；
    /// 清单缺字段（老客户端）才回退账号记录；两者皆空时如实说"未知"。
    /// </summary>
    private static LaunchInfo ResolveLaunch((bool Present, long Seconds) manifest, AccountRecord? account)
    {
        var history = account?.HistoryMoment;
        var hasEvidence = account?.HasPlayEvidence == true;

        if (manifest.Present)
        {
            if (manifest.Seconds > 0)
            {
                var moment = Epoch(manifest.Seconds);
                if (history is not null && history > moment) moment = history.Value;
                return new LaunchInfo(LaunchKind.Played, moment, null, false);
            }

            // LastPlayed = 0：本次安装从未启动。账号里若有旧安装的游玩证据，
            // 提一句历史，但绝不把旧时间冒充成"上次游玩"。
            return new LaunchInfo(LaunchKind.NeverLaunched, null, hasEvidence ? history : null, hasEvidence);
        }

        if (history is not null) return new LaunchInfo(LaunchKind.Played, history, null, false);
        if (hasEvidence) return new LaunchInfo(LaunchKind.Played, null, null, false);

        return new LaunchInfo(LaunchKind.Unknown, null, null, false);
    }

    /// <summary>
    /// 解析 localconfig.vdf 的账号历史（LastPlayed / Playtime / autocloud.lastlaunch）。
    /// app 块可能嵌套（cloud/autocloud），故按大括号配平取块，而不是平铺正则。
    /// </summary>
    private static Dictionary<string, AccountRecord> ResolveAccountRecords(ISkillHostBridge host, string root)
    {
        var result = new Dictionary<string, AccountRecord>(StringComparer.Ordinal);
        var userdata = Path.Combine(root, "userdata");
        if (!host.DirectoryExists(userdata)) return result;

        foreach (var account in host.EnumerateDirectories(userdata))
        {
            var config = Path.Combine(account, "config", "localconfig.vdf");
            var text = host.ReadTextFile(config);
            if (string.IsNullOrWhiteSpace(text)) continue;

            foreach (var (appId, body) in EnumerateNumericBlocks(text))
            {
                var played = ParseEpoch(LastPlayedRegex, body);
                var launched = ParseEpoch(LastLaunchRegex, body);
                var minutes = ParseEpoch(PlaytimeRegex, body).Seconds;

                var record = new AccountRecord(
                    played.Present && played.Seconds > 0 ? Epoch(played.Seconds) : null,
                    launched.Present && launched.Seconds > 0 ? Epoch(launched.Seconds) : null,
                    minutes);

                // 多账号取最"新"的一份
                if (!result.TryGetValue(appId, out var existing) || IsNewer(record, existing)) result[appId] = record;
            }
        }

        return result;
    }

    private static bool IsNewer(AccountRecord left, AccountRecord right)
    {
        if (left.PlaytimeMinutes != right.PlaytimeMinutes) return left.PlaytimeMinutes > right.PlaytimeMinutes;
        return left.HistoryMoment > right.HistoryMoment;
    }

    /// <summary>枚举文本里所有纯数字键的块（含嵌套块的内容），返回 (appid, 块体)。</summary>
    private static IEnumerable<(string AppId, string Body)> EnumerateNumericBlocks(string text)
    {
        foreach (Match match in NumericBlockRegex.Matches(text))
        {
            var open = match.Index + match.Length - 1;
            var body = ReadBalancedBody(text, open);
            if (body is not null) yield return (match.Groups["appid"].Value, body);
        }
    }

    /// <summary>从 openBrace 处读到配平的右大括号，返回内部文本；字符串里的括号不计数。</summary>
    private static string? ReadBalancedBody(string text, int openBrace)
    {
        var depth = 0;
        var index = openBrace;
        while (index < text.Length)
        {
            var ch = text[index];
            if (ch == '"')
            {
                index++;
                while (index < text.Length && text[index] != '"')
                {
                    if (text[index] == '\\') index++;
                    index++;
                }
            }
            else if (ch == '{') depth++;
            else if (ch == '}')
            {
                depth--;
                if (depth == 0) return text[(openBrace + 1)..index];
            }
            index++;
        }
        return null;
    }

    private static (bool Present, long Seconds) ParseEpoch(Regex regex, string text)
    {
        var match = regex.Match(text);
        if (!match.Success) return (false, 0);
        return long.TryParse(match.Groups[1].Value, out var seconds) ? (true, seconds) : (false, 0);
    }

    private static DateTime Epoch(long seconds) => DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;

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

    /// <summary>
    /// 声明按需构建的视图：概览统计 + 封面海报网格（带启动情况）+ 全量明细表。
    /// 气泡里只放一张入口卡，完整界面在独立窗口中呈现。
    ///
    /// 条目的动作到此为止：每张卡自带「卸载」，宿主会用 Steam 原生方式卸载并在完成后做汇报。
    /// 因此这里不勾选、不回传，也不再让助手对这些目录做深度扫描与清理分析。
    /// </summary>
    private static SkillUiSpec BuildView(
        string root,
        int libraryCount,
        IReadOnlyList<(AppDataEntry Entry, LaunchInfo Launch)> rows)
    {
        if (rows.Count == 0)
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

        var total = rows.Sum(row => row.Entry.SizeBytes);
        var average = total / rows.Count;
        var top = rows[0].Entry;

        var stats = new SkillUiSection
        {
            Kind = SkillUiSectionKind.StatRow,
            Title = "库概览",
            Subtitle = "客户端：" + root + " · 共 " + libraryCount + " 个库位置",
            Items =
            [
                new SkillUiItem { Title = "已安装项目", Value = rows.Count.ToString(), ValueLabel = "个" },
                new SkillUiItem { Title = "占用合计", Value = FormatSize(total), ValueLabel = "所有库累加" },
                new SkillUiItem { Title = "单体最大", Value = FormatSize(top.SizeBytes), ValueLabel = top.Name },
                new SkillUiItem { Title = "平均占用", Value = FormatSize(average), ValueLabel = "每个项目" }
            ]
        };

        var posters = new SkillUiSection
        {
            Kind = SkillUiSectionKind.PosterGrid,
            Title = "占用排行",
            Subtitle = "按实测体积从大到小，封面自动取自 Steam CDN，副标题显示启动情况（从未启动的下载项一眼可辨）；卡片上的「卸载」会调用 Steam 原生卸载。",
            Columns = 4,
            Items = [.. rows.Take(TopPosterCount).Select(row =>
            {
                var entry = row.Entry;
                return new SkillUiItem
                {
                    Title = entry.Name,
                    Value = FormatSize(entry.SizeBytes),
                    Subtitle = entry.Detail ?? "启动记录未知",
                    Badge = row.Launch.Badge(),
                    AppId = entry.AppId,
                    Path = entry.Path,
                    SizeBytes = entry.SizeBytes,
                    Metric = entry.SizeBytes,
                    Action = new SkillUiItemAction { Kind = "uninstall", Label = "卸载", Target = entry.AppId }
                };
            })]
        };

        var table = new SkillUiSection
        {
            Kind = SkillUiSectionKind.Table,
            Title = "全部明细",
            Subtitle = "共 " + rows.Count + " 行，可在窗口中滚动查看；每行右侧的「卸载」与卡片上的同样走 Steam 原生卸载。",
            Headers = ["游戏", "占用", "启动情况", "所在库"],
            Items = [.. rows.Take(MaxTableRows).Select(row =>
            {
                var entry = row.Entry;
                return new SkillUiItem
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
                    Title = entry.Name,
                    SizeBytes = entry.SizeBytes,
                    Action = new SkillUiItemAction { Kind = "uninstall", Label = "卸载", Target = entry.AppId }
                };
            })]
        };

        return new SkillUiSpec
        {
            Title = "Steam 游戏库盘点",
            Subtitle = "共 " + rows.Count + " 个已安装项目，合计 " + FormatSize(total) + "，分布在 " + libraryCount + " 个库位置。",
            // auto：气泡放入口卡的同时，把完整盘点界面直接弹出来。
            Surface = SkillUiSurface.Auto,
            EntryTitle = "Steam 游戏库盘点",
            EntrySummary = rows.Count + " 个已安装项目 · 合计 " + FormatSize(total) + " · 卡片上可直接调用 Steam 原生卸载",
            Sections = [stats, posters, table],
            // 这一屏就是终点：盘点 + 卸载，不再有「回传继续分析」。
            Actions = [new SkillUiAction { Kind = "close", Label = "关闭" }]
        };
    }
}
