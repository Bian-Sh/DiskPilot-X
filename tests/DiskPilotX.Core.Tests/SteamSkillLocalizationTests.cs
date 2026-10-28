using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Skills;

namespace DiskPilotX.Core.Tests;

/// <summary>编译真实 Steam 源码；所有 Steam 数据均由只读能力桥提供，不访问真实客户端。</summary>
public sealed class SteamSkillLocalizationTests
{
    private const string SkillId = "steam-library-organizer";
    private static readonly string[] Cultures = ["en-US", "zh-CN", "zh-TW"];

    [Fact]
    public void TranslationTablesHaveMatchingKeysAndFormats()
    {
        var tables = ReadTables();
        var baseline = tables["en-US"];
        Assert.Contains("skill.name", baseline.Keys);
        Assert.Contains("skill.description", baseline.Keys);
        foreach (var culture in Cultures)
        {
            Assert.Equal(baseline.Keys.Order(), tables[culture].Keys.Order());
            foreach (var (key, value) in tables[culture])
            {
                Assert.False(string.IsNullOrWhiteSpace(value), key);
                Assert.Equal(Placeholders(baseline[key]), Placeholders(value));
                var count = System.Text.CompositeFormat.Parse(value).MinimumArgumentCount;
                _ = string.Format(CultureInfo.GetCultureInfo(culture), value,
                    Enumerable.Repeat<object>(123, count).ToArray());
            }
        }
        var source = File.ReadAllText(Path.Combine(SkillDirectory(), "src", "Provider.cs"));
        foreach (Match match in Regex.Matches(source, "SkillTextCatalog\\.Bind\\(\"([^\"]+)\""))
            Assert.Contains(match.Groups[1].Value, baseline.Keys);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("zh-CN")]
    [InlineData("zh-TW")]
    public async Task RealProviderPreservesLaunchStatesAndLiveViewTokens(string culture)
    {
        var bridge = new ReadOnlyBridge(culture);
        var result = await Discover(bridge);
        var view = Assert.IsType<SkillUiSpec>(result.View);
        Assert.NotNull(view.Localizations);
        var catalog = new SkillTextCatalog(view.Localizations);
        var expected = new Dictionary<string, string>
        {
            ["10"] = SkillTextCatalog.Bind("launch.played", SkillTextCatalog.Bind("relative.years", 2)),
            ["11"] = SkillTextCatalog.Bind("launch.never"),
            ["12"] = SkillTextCatalog.Bind("launch.downloadedHistory", SkillTextCatalog.Bind("relative.years", 1)),
            ["13"] = SkillTextCatalog.Bind("launch.downloadedHistoryUnknownTime"),
            ["14"] = SkillTextCatalog.Bind("launch.unknown"),
            ["15"] = SkillTextCatalog.Bind("launch.historyUnknownTime"),
            ["16"] = SkillTextCatalog.Bind("launch.played", SkillTextCatalog.Bind("relative.justNow")),
            ["17"] = SkillTextCatalog.Bind("launch.played", SkillTextCatalog.Bind("relative.months", 1)),
            ["18"] = SkillTextCatalog.Bind("launch.played", SkillTextCatalog.Bind("relative.days", 3)),
            ["19"] = SkillTextCatalog.Bind("launch.played", SkillTextCatalog.Bind("relative.today")),
            ["20"] = SkillTextCatalog.Bind("launch.played", SkillTextCatalog.Bind("relative.months", 4)),
            ["21"] = SkillTextCatalog.Bind("launch.played", SkillTextCatalog.Bind("relative.days", 20)),
            ["22"] = SkillTextCatalog.Bind("launch.played", SkillTextCatalog.Bind("relative.days", 5)),
            ["23"] = SkillTextCatalog.Bind("launch.never"),
            ["24"] = SkillTextCatalog.Bind("launch.downloadedHistory", SkillTextCatalog.Bind("relative.days", 2))
        };
        Assert.Equal(expected.Count, result.Entries.Count);
        Assert.DoesNotContain(result.Entries, entry => entry.AppId == "228980");
        foreach (var entry in result.Entries)
        {
            Assert.Equal("Game " + entry.AppId + " 真實名稱", entry.Name);
            Assert.Equal(Path.Combine(bridge.Root, "steamapps", "common", "Game" + entry.AppId), entry.Path);
            Assert.Equal(catalog.Resolve(expected[entry.AppId], culture), entry.Detail);
            Assert.DoesNotContain("{loc", entry.Detail!);
        }
        var posters = Assert.Single(view.Sections, section => section.Kind == SkillUiSectionKind.PosterGrid);
        var table = Assert.Single(view.Sections, section => section.Kind == SkillUiSectionKind.Table);
        Assert.Equal(12, posters.Items.Count);
        Assert.Equal(expected.Count, table.Items.Count);
        foreach (var item in posters.Items.Concat(table.Items))
        {
            Assert.False(item.Selectable);
            Assert.NotNull(item.Action);
            Assert.Equal("uninstall", item.Action.Kind);
            Assert.Equal(item.AppId, item.Action.Target);
            Assert.Equal(SkillTextCatalog.Bind("action.uninstall"), item.Action.Label);
            var status = item.Cells.Count == 0 ? item.Subtitle : item.Cells[2];
            Assert.Equal(expected[item.AppId!], status);
        }
        Assert.All(view.Actions, action => Assert.Equal("close", action.Kind));
        Assert.Equal("badge.stale", BadgeKey(posters, "10"));
        Assert.Equal("badge.never", BadgeKey(posters, "11"));
        Assert.Equal("badge.unknown", BadgeKey(posters, "14"));
        Assert.Equal("badge.played", BadgeKey(posters, "15"));
        Assert.Equal("badge.recent", BadgeKey(posters, "16"));
        Assert.Equal("badge.rare", BadgeKey(posters, "20"));
        Assert.Equal("badge.occasional", BadgeKey(posters, "21"));

        // 同一个视图在三语间解析；序列化快照及 Bridge 读取次数不变，防止靠重跑刷新语言。
        var snapshot = JsonSerializer.Serialize(view);
        var reads = bridge.ReadCount;
        var texts = ViewTexts(view).ToArray();
        // 除真实游戏名称、库路径外，所有面向用户字段都必须保留绑定。
        Assert.All(texts.Where(value => !value.StartsWith("Game ", StringComparison.Ordinal) &&
            !value.StartsWith(bridge.Root, StringComparison.OrdinalIgnoreCase)), value => Assert.StartsWith("{loc", value));
        foreach (var language in Cultures)
        {
            bridge.Culture = language;
            Assert.Equal(ReadTables()[language]["view.title"], catalog.Resolve(view.Title, language));
            foreach (var value in texts)
            {
                var resolved = catalog.Resolve(value, language);
                Assert.DoesNotContain("{loc", resolved);
                if (value.StartsWith("{loc", StringComparison.Ordinal)) Assert.NotEqual(value, resolved);
            }
            Assert.Equal(catalog.Resolve(expected["12"], language), catalog.Resolve(table.Items.Single(i => i.AppId == "12").Cells[2], language));
        }
        Assert.Equal(reads, bridge.ReadCount);
        Assert.Equal(snapshot, JsonSerializer.Serialize(view));
        Assert.All(bridge.Logs, log => Assert.DoesNotContain("{loc", log));
        Assert.Contains(bridge.Logs, log => log.Contains("15", StringComparison.Ordinal));
        if (culture == "en-US") Assert.All(bridge.Logs, log => Assert.DoesNotMatch("[\\u4e00-\\u9fff]", log));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("zh-CN")]
    [InlineData("zh-TW")]
    public async Task EmptyLibraryAndMissingClientUseLocalizedMessages(string culture)
    {
        var bridge = new ReadOnlyBridge(culture, empty: true);
        var empty = await Discover(bridge);
        Assert.Empty(empty.Entries);
        var view = Assert.IsType<SkillUiSpec>(empty.View);
        Assert.NotNull(view.Localizations);
        Assert.Equal(SkillUiSurface.Entry, view.Surface);
        Assert.Equal(SkillTextCatalog.Bind("empty.title"), Assert.Single(Assert.Single(view.Sections).Items).Title);
        Assert.Equal(SkillTextCatalog.Bind("action.acknowledge"), Assert.Single(view.Actions).Label);
        var catalog = new SkillTextCatalog(view.Localizations);
        Assert.All(ViewTexts(view), value => Assert.StartsWith("{loc", value));
        foreach (var language in Cultures)
            Assert.All(ViewTexts(view), value => Assert.DoesNotContain("{loc", catalog.Resolve(value, language)));
        Assert.Contains(bridge.Logs, log => log == catalog.Resolve(SkillTextCatalog.Bind("log.libraryMissing", Path.Combine(bridge.Root, "steamapps", "libraryfolders.vdf")), culture));

        var missing = new ReadOnlyBridge(culture, missing: true);
        var absent = await Discover(missing);
        Assert.Empty(absent.Entries);
        Assert.Null(absent.View);
        Assert.Equal(catalog.Resolve(SkillTextCatalog.Bind("log.rootMissing"), culture), Assert.Single(missing.Logs));
    }

    private static string BadgeKey(SkillUiSection section, string id) =>
        SkillUiDocument.LocalizationKey(section.Items.Single(item => item.AppId == id).Badge!)!;

    private static string[] Placeholders(string value) => Regex.Matches(value, @"\{\d+(?:,-?\d+)?(?::[^}]+)?\}")
        .Select(match => match.Value).Order().ToArray();

    private static IEnumerable<string> ViewTexts(SkillUiSpec view)
    {
        IEnumerable<string?> values = new[] { view.Title, view.Subtitle, view.EntryTitle, view.EntrySummary }
            .Concat(view.Actions.Select(action => action.Label));
        foreach (var section in view.Sections)
        {
            values = values.Concat(new[] { section.Title, section.Subtitle }).Concat(section.Headers ?? []);
            foreach (var item in section.Items)
                values = values.Concat(new[] { item.Title, item.Subtitle, item.Value, item.ValueLabel, item.Badge, item.Action?.Label }).Concat(item.Cells);
        }
        return values.OfType<string>();
    }

    private static async Task<AppDataDiscoveryResult> Discover(ReadOnlyBridge bridge)
    {
        // 只读加载仓库内置包，不调用 Save，不创建用户技能库。
        var store = new CleanupSkillStore(Path.Combine(Path.GetTempPath(), "steam-localization-unused-" + Guid.NewGuid().ToString("N")), [Path.GetDirectoryName(SkillDirectory())!]);
        var package = store.LoadReadOnly(SkillId);
        var service = new CleanupSkillSourceService(store);
        return await service.RunDiscoveryAsync(service.Authorize(SkillId, package.Digest), bridge);
    }

    private static Dictionary<string, Dictionary<string, string>> ReadTables() => Cultures.ToDictionary(culture => culture,
        culture => JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(SkillDirectory(), "i18n", culture + ".json")))!);

    private static string SkillDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if ((Directory.Exists(Path.Combine(directory.FullName, ".git")) || File.Exists(Path.Combine(directory.FullName, ".git"))) && Directory.Exists(Path.Combine(directory.FullName, "skills", SkillId)))
                return Path.Combine(directory.FullName, "skills", SkillId);
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, "skills", SkillId))) return Path.Combine(directory.FullName, "skills", SkillId);
        throw new DirectoryNotFoundException("未找到真实 Steam 技能目录。");
    }

    private sealed class ReadOnlyBridge : ISkillHostBridge
    {
        public string Root { get; } = Path.GetFullPath(@"R:\Steam-Locale-Fixture");
        public string Culture { get; set; }
        public string SkillId => SteamSkillLocalizationTests.SkillId;
        public IReadOnlyDictionary<string, string> Parameters { get; } = new Dictionary<string, string>();
        public List<string> Logs { get; } = [];
        public int ReadCount { get; private set; }
        private readonly bool missing;
        private readonly Dictionary<string, string> files = new(StringComparer.OrdinalIgnoreCase);

        public ReadOnlyBridge(string culture, bool empty = false, bool missing = false)
        {
            Culture = culture;
            this.missing = missing;
            if (empty || missing) return;
            long Stamp(double days) => new DateTimeOffset(DateTime.UtcNow.AddDays(-days)).ToUnixTimeSeconds();
            var moments = new Dictionary<int, string?> { [10] = Stamp(800).ToString(), [11] = "0", [12] = "0", [13] = "0", [14] = null, [15] = null,
                [16] = null, [17] = Stamp(45).ToString(), [18] = Stamp(3.1).ToString(), [19] = Stamp(0.1).ToString(), [20] = Stamp(120).ToString(),
                [21] = Stamp(20.1).ToString(), [22] = null, [23] = "0", [24] = "0", [228980] = "0" };
            foreach (var (id, moment) in moments)
                files[Path.Combine(Root, "steamapps", $"appmanifest_{id}.acf")] = $"\"name\" \"Game {id} 真實名稱\" \"installdir\" \"Game{id}\" " + (moment is null ? "" : $"\"LastPlayed\" \"{moment}\"");
            files[Path.Combine(Root, "userdata", "account", "config", "localconfig.vdf")] =
                $"\"12\" {{ \"LastPlayed\" \"{Stamp(400)}\" \"Playtime\" \"10\" }} " +
                $"\"13\" {{ \"Playtime\" \"10\" }} \"15\" {{ \"Playtime\" \"10\" }} " +
                $"\"16\" {{ \"LastPlayed\" \"{Stamp(-2)}\" }} \"22\" {{ \"LastPlayed\" \"{Stamp(5.1)}\" }} " +
                $"\"23\" {{ \"LastPlayed\" \"{Stamp(3)}\" }} \"24\" {{ \"autocloud\" {{ \"lastlaunch\" \"{Stamp(2.1)}\" }} }}";
        }
        public SkillLocalizationState GetLocalizationState() => new(Culture, Culture, Culture);
        public void Log(string message) => Logs.Add(message);
        public string? ReadRegistry(string keyPath, string valueName) { ReadCount++; return missing ? null : Root; }
        public IReadOnlyList<string> EnumerateDirectories(string path) { ReadCount++; return path == Path.Combine(Root, "userdata") ? [Path.Combine(path, "account")] : []; }
        public IReadOnlyList<string> EnumerateFiles(string path, string searchPattern = "*") { ReadCount++; return files.Keys.Where(file => Path.GetDirectoryName(file) == path && file.EndsWith(".acf", StringComparison.Ordinal)).ToArray(); }
        public bool DirectoryExists(string path) { ReadCount++; return !missing && path.StartsWith(Root, StringComparison.OrdinalIgnoreCase); }
        public bool FileExists(string path) { ReadCount++; return files.ContainsKey(path); }
        public string? ReadTextFile(string path) { ReadCount++; return files.GetValueOrDefault(path); }
        public long GetDirectorySize(string path) { ReadCount++; return (30 - int.Parse(Path.GetFileName(path)[4..], CultureInfo.InvariantCulture)) * 1024L * 1024 * 1024; }
        public DateTime? GetDirectoryLastWriteTimeUtc(string path) => throw new InvalidOperationException("启动状态不得使用目录时间。");
        public IReadOnlyList<SkillHostEntry> QueryScan(string? rootPath = null, int maxCount = 20_000) => throw new InvalidOperationException("不得改用扫描快照替代 Steam 清单。");
    }
}