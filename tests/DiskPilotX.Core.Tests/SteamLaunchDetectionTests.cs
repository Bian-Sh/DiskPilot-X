using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Skills;

namespace DiskPilotX.Core.Tests;

/// <summary>
/// Steam 启动判定回归：技能源码以 appmanifest 的 LastPlayed 为权威
/// （0 = 本次安装从未启动），账号 localconfig 历史只能补充说明，
/// 目录时间戳不得再被当成"上次游玩"。
/// 用假 Steam 树编译并运行真实 Provider 源码来锁住行为。
/// </summary>
public sealed class SteamLaunchDetectionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "steam-launch-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task LaunchStateFollowsManifestAndNeverFabricatesFromDirectoryMtime()
    {
        var solutionRoot = FindSolutionRoot();
        Assert.True(solutionRoot is not null, "未定位到解决方案根目录");
        if (solutionRoot is null) return;

        var store = new CleanupSkillStore(Path.Combine(root, "user"), [Path.Combine(solutionRoot, "skills")]);
        var service = new CleanupSkillSourceService(store);
        var validation = await service.ValidateSourceAsync("steam-library-organizer", default);
        Assert.True(validation.Success, string.Join(";", validation.Diagnostics.Select(d => d.Code + ":" + d.Message)));

        var result = await service.RunDiscoveryAsync(
            service.Authorize("steam-library-organizer", validation.Digest),
            new FakeSteamBridge(),
            default);

        var details = result.Entries.ToDictionary(entry => entry.AppId, entry => entry.Detail ?? "");

        // ① 清单 LastPlayed>0 → 真启动过
        Assert.StartsWith("上次游玩：", details["100"]);

        // ② 清单 LastPlayed=0 且账号无游玩证据 → 从未启动。
        //    600 的账号里只有弱时间戳（无 Playtime/autocloud），同样不得冒充"N 年前"。
        //    两者的安装目录 mtime 被假桥设为 1995 年，绝不能漏进文案。
        Assert.Equal("从未启动", details["200"]);
        Assert.Equal("从未启动", details["600"]);

        // ③ 清单 LastPlayed=0 但账号有硬游玩证据（Playtime>0）→ 下载后未启动 + 历史注脚。
        //    300 的账号块带 cloud{} 嵌套（旧的平铺正则会漏掉它）。
        Assert.StartsWith("下载后未启动（", details["300"]);
        Assert.EndsWith("玩过）", details["300"]);

        // ④ 老清单（无 LastPlayed 字段）回退账号记录
        Assert.StartsWith("上次游玩：", details["400"]);

        // ⑤ 两者皆空 → 如实说未知，不编造
        Assert.Equal("启动记录未知", details["500"]);

        // 任何条目都不得出现 1995（目录 mtime 陷阱）
        Assert.All(details.Values, text => Assert.DoesNotContain("1995", text));

        // 视图徽章同步：未启动的卡挂「未启动」徽章
        var badges = PosterBadges(result.View);
        Assert.Equal("未启动", badges["200"]);
        Assert.Equal("未启动", badges["600"]);
        Assert.Equal("未启动", badges["300"]);
        Assert.Equal("未知", badges["500"]);
        Assert.NotEqual("未启动", badges["100"]);

        // 明细表的「启动情况」列与 Detail 一致
        var table = result.View!.Sections.Single(section => section.Kind == SkillUiSectionKind.Table);
        var row200 = table.Items.Single(item => item.AppId == "200");
        Assert.Equal("从未启动",new SkillTextCatalog(result.View.Localizations).Resolve(row200.Cells![2],"zh-CN"));
    }

    private static Dictionary<string, string> PosterBadges(SkillUiSpec? view)
    {
        Assert.NotNull(view);
        var posters = view!.Sections.Single(section => section.Kind == SkillUiSectionKind.PosterGrid);
        return posters.Items.ToDictionary(item => item.AppId!, item => new SkillTextCatalog(view.Localizations).Resolve(item.Badge!,"zh-CN"));
    }

    /// <summary>
    /// 假 Steam 树：注册表/清单/localconfig 全在内存里，
    /// 目录时间戳固定返回 1995 年——一旦有人把目录 mtime 接回判定，断言立刻翻车。
    /// </summary>
    private sealed class FakeSteamBridge : ISkillHostBridge
    {
        private const string SteamRoot = @"D:\FakeSteam";
        private const string ExtraLibrary = @"G:\FakeLib";

        private readonly Dictionary<string, string> files = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> directories = new(StringComparer.OrdinalIgnoreCase);

        public FakeSteamBridge()
        {
            directories.Add(Norm(SteamRoot));
            directories.Add(Norm(ExtraLibrary));
            directories.Add(Norm(Path.Combine(SteamRoot, "userdata")));
            directories.Add(Norm(Path.Combine(SteamRoot, "userdata", "42")));

            files[Norm(Path.Combine(SteamRoot, "steamapps", "libraryfolders.vdf"))] = """
                "libraryfolders"
                {
                    "0" { "path" "D:\\FakeSteam" }
                    "1" { "path" "G:\\FakeLib" }
                }
                """;

            files[Norm(Path.Combine(SteamRoot, "userdata", "42", "config", "localconfig.vdf"))] = """
                "UserLocalConfigStore"
                {
                    "Software"
                    {
                        "Valve"
                        {
                            "Steam"
                            {
                                "apps"
                                {
                                    "300"
                                    {
                                        "LastPlayed"		"1600000000"
                                        "Playtime"		"130"
                                        "cloud"
                                        {
                                            "last_sync_state"		"synchronized"
                                        }
                                    }
                                    "400"
                                    {
                                        "LastPlayed"		"1700000000"
                                        "Playtime"		"30"
                                    }
                                    "600"
                                    {
                                        "LastPlayed"		"1600000000"
                                    }
                                }
                            }
                        }
                    }
                }
                """;

            AddManifest(SteamRoot, "100", "Played Game", """
                "LastPlayed"		"1700000000"
                """);
            AddManifest(SteamRoot, "200", "Never Clean", """
                "LastPlayed"		"0"
                """);
            AddManifest(ExtraLibrary, "300", "Never History", """
                "LastPlayed"		"0"
                """);
            AddManifest(SteamRoot, "400", "Legacy Played", "");
            AddManifest(SteamRoot, "500", "Legacy Unknown", "");
            AddManifest(SteamRoot, "600", "Weak History", """
                "LastPlayed"		"0"
                """);
        }

        private void AddManifest(string library, string appId, string name, string extraFields)
        {
            var steamapps = Path.Combine(library, "steamapps");
            var installDir = "Game" + appId;
            var installPath = Path.Combine(steamapps, "common", installDir);
            directories.Add(Norm(steamapps));
            directories.Add(Norm(Path.Combine(steamapps, "common")));
            directories.Add(Norm(installPath));

            files[Norm(Path.Combine(steamapps, "appmanifest_" + appId + ".acf"))] = """
                "AppState"
                {
                    "appid"		"__APPID__"
                    "name"		"__NAME__"
                    "StateFlags"		"4"
                    "installdir"		"__DIR__"
                    "LastUpdated"		"1700000000"
                __EXTRA__
                }
                """
                .Replace("__APPID__", appId)
                .Replace("__NAME__", name)
                .Replace("__DIR__", installDir)
                .Replace("__EXTRA__", extraFields);
        }

        private static string Norm(string path) => Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        public string SkillId => "steam-library-organizer";
        public IReadOnlyDictionary<string, string> Parameters { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
        public void Log(string message) { }

        public string? ReadRegistry(string keyPath, string valueName) =>
            valueName is "InstallPath" or "SteamPath" ? SteamRoot : null;

        public IReadOnlyList<string> EnumerateDirectories(string path)
        {
            var parent = Norm(path);
            return directories.Where(directory =>
                string.Equals(Path.GetDirectoryName(directory), parent, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        public IReadOnlyList<string> EnumerateFiles(string path, string searchPattern = "*")
        {
            var parent = Norm(path);
            var parts = searchPattern.Split('*');
            return files.Keys.Where(file =>
            {
                if (!string.Equals(Path.GetDirectoryName(file), parent, StringComparison.OrdinalIgnoreCase)) return false;
                var name = Path.GetFileName(file);
                if (parts.Length == 1) return name.Equals(searchPattern, StringComparison.OrdinalIgnoreCase);
                return name.Length >= parts[0].Length + parts[^1].Length &&
                       name.StartsWith(parts[0], StringComparison.OrdinalIgnoreCase) &&
                       name.EndsWith(parts[^1], StringComparison.OrdinalIgnoreCase);
            }).ToList();
        }

        public bool DirectoryExists(string path) => directories.Contains(Norm(path));
        public bool FileExists(string path) => files.ContainsKey(Norm(path));
        public string? ReadTextFile(string path) => files.TryGetValue(Norm(path), out var text) ? text : null;
        public long GetDirectorySize(string path) => 1_000_000;

        /// <summary>永远 1995 年：目录 mtime 不是游玩信号，谁接回来谁翻车。</summary>
        public DateTime? GetDirectoryLastWriteTimeUtc(string path) => new(1995, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public IReadOnlyList<SkillHostEntry> QueryScan(string? rootPath = null, int maxCount = 20_000) => [];
    }

    private static string? FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "skills", "steam-library-organizer", "src", "Provider.cs"))) return directory.FullName;
            directory = directory.Parent;
        }
        return null;
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
