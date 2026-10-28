using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Skills;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DiskPilotX.Core.Tests;

/// <summary>只读编译工作副本；全部磁盘访问由内存桥模拟，不读取或改写真实用户规则。</summary>
public sealed class UnitySkillLocalizationTests
{
    private static readonly string PackageRoot = FindPackageRoot();
    private static readonly Lazy<Type> ProviderType = new(CompileProvider);
    private static Dictionary<string, Dictionary<string, string>> Tables() =>
        new[] { "en-US", "zh-CN", "zh-TW" }.ToDictionary(c => c, c =>
            JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(PackageRoot, "i18n", c + ".json")))!);

    [Fact]
    public void 三语键与占位符一致且覆盖源码和表单()
    {
        var tables = Tables();
        var english = tables["en-US"];
        var source = File.ReadAllText(Path.Combine(PackageRoot, "Detector.cs.txt"));
        var form = XDocument.Load(Path.Combine(PackageRoot, "ui.xaml"));
        var keys = Regex.Matches(source, "Bind\\(\"([^\"]+)\"").Select(m => m.Groups[1].Value)
            .Concat(form.Descendants().Attributes().Select(a => a.Value)
                .Where(v => v.StartsWith("{loc ")).Select(v => v[5..^1]))
            .Append("skill.name").Append("skill.description").Distinct().ToArray();
        foreach (var table in tables.Values)
        {
            Assert.Equal(english.Keys.Order(), table.Keys.Order());
            foreach (var key in keys) Assert.True(table.ContainsKey(key), "缺少文案：" + key);
            foreach (var (key, template) in table)
            {
                Assert.False(string.IsNullOrWhiteSpace(template));
                Assert.Equal(Placeholders(english[key]), Placeholders(template));
                _ = System.Text.CompositeFormat.Parse(template);
            }
        }
        Assert.Equal("6", form.Descendants("TextBox").Single(e => (string?)e.Attribute("Name") == "depth").Attribute("Text")!.Value);
        Assert.Null(form.Descendants("TextBox").Single(e => (string?)e.Attribute("Name") == "roots").Attribute("Text"));
        Assert.All(form.Descendants("TextBlock"), e => Assert.StartsWith("{loc ", e.Attribute("Text")!.Value));
    }

    [Theory]
    [InlineData("en-US", "Unity project cache inventory", "Cache overview", "Large cache", "Unity version unavailable")]
    [InlineData("zh-CN", "Unity 项目缓存盘点", "缓存概览", "占用较大", "Unity 版本未读到")]
    [InlineData("zh-TW", "Unity 專案快取盤點", "快取概覽", "佔用較大", "未讀取到 Unity 版本")]
    public async Task 真实源码的同一视图可三语解析且只度量白名单(string culture, string title, string stats, string badge, string unknown)
    {
        var host = new MemoryBridge(culture);
        var project = host.AddProject(@"C:\Unity-测试\Game 原名", 6L * 1024 * 1024 * 1024);
        var tables = Tables();
        var result = await Discover(host, new() { ["roots"] = @"C:\Unity-测试", ["depth"] = "6" }, tables);
        var view = Assert.IsType<SkillUiSpec>(result.View);
        Assert.Same(tables, view.Localizations);
        var texts = new SkillTextCatalog(tables);
        Assert.Equal(title, texts.Resolve(view.Title, culture));
        Assert.Equal(stats, texts.Resolve(view.Sections[0].Title!, culture));
        Assert.Equal("Unity project cache inventory", texts.Resolve(view.Title, "en-US"));
        Assert.Equal("Unity 專案快取盤點", texts.Resolve(view.Title, "zh-TW"));
        Assert.StartsWith("{loc", view.Subtitle!);
        var row = Assert.Single(view.Sections.Single(s => s.Kind == SkillUiSectionKind.Table && s.Items.Any(i => i.Selectable)).Items);
        Assert.Equal(badge, texts.Resolve(row.Badge!, culture));
        Assert.Equal(project, row.Path);
        Assert.Equal("Game 原名", row.Title);
        Assert.StartsWith("{loc", row.Cells[^1]);
        var entry = Assert.Single(result.Entries);
        Assert.Equal(6L * 1024 * 1024 * 1024, entry.SizeBytes);
        Assert.Equal(project, entry.Path);
        Assert.Contains(unknown, entry.Detail!);
        Assert.DoesNotContain("{loc", entry.Detail!);
        Assert.All(host.Measured, p => Assert.Contains(Path.GetFileName(p), new[] { "Library", "Temp", "obj", "Logs" }));
        Assert.Equal(4, host.Measured.Count);
        Assert.DoesNotContain(host.Enumerated, p => p.StartsWith(project, StringComparison.OrdinalIgnoreCase));
        Assert.All(host.Logs, log => Assert.DoesNotContain("{loc", log));
        if (culture == "en-US") Assert.All(host.Logs, log => Assert.DoesNotMatch("[\\u4e00-\\u9fff]", log.Replace(@"C:\Unity-测试", "")));
        foreach (var text in ViewTexts(view))
            foreach (var c in tables.Keys) Assert.DoesNotContain("{loc", texts.Resolve(text, c));
    }

    [Theory]
    [InlineData("en-US", "No valid search roots", "No Unity projects found")]
    [InlineData("zh-CN", "没有有效", "没有找到 Unity 项目")]
    [InlineData("zh-TW", "沒有有效", "找不到 Unity 專案")]
    public async Task 空输入非法输入和空态三语且不扩大扫描(string culture, string invalid, string empty)
    {
        var tables = Tables();
        var texts = new SkillTextCatalog(tables);
        foreach (var roots in new[] { "", "relative;..\\Game", @"Z:\missing", "C:\\bad\0path" })
        {
            var host = new MemoryBridge(culture);
            var result = await Discover(host, new() { ["roots"] = roots, ["depth"] = "wrong" }, tables);
            Assert.Empty(result.Entries);
            Assert.Empty(host.Enumerated);
            Assert.Empty(host.Measured);
            Assert.Empty(host.WideQueries);
            Assert.Contains(invalid, string.Join(" ", ViewTexts(result.View!).Select(t => texts.Resolve(t, culture))), StringComparison.OrdinalIgnoreCase);
            Assert.All(host.Logs, log => Assert.DoesNotContain("{loc", log));
            Assert.All(ViewTexts(result.View!), t => Assert.DoesNotContain("{loc", texts.Resolve(t, culture)));
        }
        var emptyHost = new MemoryBridge(culture);
        emptyHost.Directories.Add(@"C:\Empty");
        var emptyResult = await Discover(emptyHost, new() { ["roots"] = @"C:\Empty" }, tables);
        Assert.Contains(empty, string.Join(" ", ViewTexts(emptyResult.View!).Select(t => texts.Resolve(t, culture))));
        Assert.Equal(new[] { @"C:\Empty" }, emptyHost.Enumerated);
        Assert.Same(tables, emptyResult.View!.Localizations);
    }

    [Theory]
    [InlineData("wrong", 6)]
    [InlineData("0", 1)]
    [InlineData("99", 8)]
    public void 深度修正与反射参数兼容保持不变(string raw, int expected)
    {
        var notes = new List<string>();
        var method = ProviderType.Value.GetMethod("ResolveDepth", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.Equal(expected, method.Invoke(null, [raw, notes]));
        Assert.Single(notes);
        foreach (var culture in Tables().Keys)
            Assert.DoesNotContain("{loc", new SkillTextCatalog(Tables()).Resolve(notes[0], culture));
        var read = ProviderType.Value.GetMethod("ReadParameter", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.Equal("property-value", read.Invoke(null, [new { Roots = "property-value" }, new[] { "roots" }]));
        Assert.Equal("alias-value", read.Invoke(null, [new Dictionary<string, string> { ["search_roots"] = "alias-value" }, new[] { "roots", "search_roots" }]));
    }

    [Fact]
    public async Task 显式兜底根目录上限截断明细和根目录摘要不改扫描语义()
    {
        var host = new MemoryBridge("en-US");
        var roots = Enumerable.Range(0, 34).Select(i => @"C:\Root" + i).ToArray();
        foreach (var root in roots) host.Directories.Add(root);
        for (var i = 0; i < 61; i++) host.AddProject(Path.Combine(roots[0], "Game" + i), i);
        var tables = Tables();
        var result = await Discover(host, new() { ["roots"] = string.Join(";", roots) + ";again", ["depth"] = "99" }, tables);
        Assert.Equal(61, result.Entries.Count);
        Assert.Equal(32, host.Enumerated.Count);
        Assert.DoesNotContain(roots[32], host.Enumerated);
        var view = result.View!;
        var texts = new SkillTextCatalog(tables);
        var details = view.Sections.Single(s => s.Items.Any(i => i.Selectable));
        Assert.Equal(60, details.Items.Count);
        foreach (var culture in tables.Keys)
        {
            var content = string.Join(" ", ViewTexts(view).Select(t => texts.Resolve(t, culture)));
            Assert.DoesNotContain("{loc", content);
            Assert.Contains("32", content);
            Assert.Contains("61", texts.Resolve(details.Subtitle!, culture));
            Assert.Contains("60", texts.Resolve(details.Subtitle!, culture));
        }
        var fallback = new MemoryBridge("en-US");
        fallback.Directories.Add(roots[0]);
        await Discover(fallback, new() { ["roots"] = "relative", ["scope_path"] = roots[0] }, tables);
        Assert.Equal(new[] { roots[0] }, fallback.Enumerated);
    }

    [Theory]
    [InlineData("en-US", "No cache", "Has cache")]
    [InlineData("zh-CN", "无缓存", "有缓存")]
    [InlineData("zh-TW", "無快取", "有快取")]
    public async Task 无缓存和普通缓存风险及真实版本不翻译(string culture, string noCache, string hasCache)
    {
        var host = new MemoryBridge(culture);
        var zero = host.AddProject(@"C:\Versions\Empty 原名", 0);
        host.AddProject(@"C:\Versions\Small 原名", 1024);
        host.Texts[Path.Combine(zero, "ProjectSettings", "ProjectVersion.txt")] = "m_EditorVersion: 6000.0.58f2";
        var tables = Tables();
        var texts = new SkillTextCatalog(tables);
        var result = await Discover(host, new() { ["roots"] = @"C:\Versions" }, tables);
        var rows = result.View!.Sections.Single(s => s.Items.Any(i => i.Selectable)).Items;
        var empty = rows.Single(i => i.Path == zero);
        Assert.Equal(noCache, texts.Resolve(empty.Badge!, culture));
        Assert.Equal(hasCache, texts.Resolve(rows.Single(i => i.Path != zero).Badge!, culture));
        Assert.Equal("6000.0.58f2", empty.Cells[2]);
        Assert.StartsWith("6000.0.58f2", result.Entries.Single(i => i.Path == zero).Detail!);
        foreach (var c in tables.Keys)
        {
            Assert.Contains("6000.0.58f2", texts.Resolve(empty.Cells[^1], c));
            Assert.DoesNotContain("{loc", texts.Resolve(empty.Cells[^1], c));
        }
    }

    [Fact]
    public async Task 遍历跳过白名单上限深度及取消语义不变()
    {
        var type = ProviderType.Value;
        var descend = type.GetMethod("ShouldDescend", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var name in new[] { "Windows", "AppData", "Library", "Temp", "obj", "Logs", "Build", "Builds", "MemoryCaptures", "Recordings", ".git", ".vs", ".idea", "Unity Hub", "$Recycle.Bin" })
            Assert.Equal(false, descend.Invoke(null, [Path.Combine(@"C:\Root", name)]));
        Assert.Equal(false, descend.Invoke(null, [@"C:\Unity\Hub\Editor"]));
        Assert.Equal(true, descend.Invoke(null, [@"C:\Root\UserProject"]));
        foreach (var (name, expected) in new[] { ("MaxRoots", 32), ("MaxDepthLimit", 8), ("DefaultDepth", 6), ("MaxTableRows", 60), ("MaxEntries", 20000), ("MaxScannedDirectories", 200000) })
            Assert.Equal(expected, type.GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue());
        var host = new MemoryBridge("en-US");
        host.AddProject(@"C:\Deep\Child\Project", 10);
        host.Directories.Add(@"C:\Deep");
        host.Children[@"C:\Deep"] = [@"C:\Deep\Child"];
        Assert.Empty((await Discover(host, new() { ["roots"] = @"C:\Deep", ["depth"] = "1" }, Tables())).Entries);
        Assert.DoesNotContain(@"C:\Deep\Child", host.Enumerated);
        var provider = (IAppDataProvider)Activator.CreateInstance(type)!;
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var before = host.Enumerated.Count;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.DiscoverAsync(
            new("unity-cache-check", host, new Dictionary<string, string> { ["roots"] = @"C:\Deep" }, Tables()), canceled.Token));
        Assert.Equal(before, host.Enumerated.Count);
    }

    private static IEnumerable<string> ViewTexts(SkillUiSpec view) =>
        new[] { view.Title, view.Subtitle, view.EntryTitle, view.EntrySummary }
            .Concat(view.Actions.Select(a => a.Label))
            .Concat(view.Sections.SelectMany(s => new[] { s.Title, s.Subtitle }.Concat(s.Headers ?? [])
                .Concat(s.Items.SelectMany(i => new[] { i.Title, i.Subtitle, i.Value, i.ValueLabel, i.Badge }.Concat(i.Cells)))))
            .Where(t => t is not null).Select(t => t!);

    private static string[] Placeholders(string template) => Regex.Matches(template, @"\{(\d+)(?:[,:][^}]*)?\}")
        .Select(m => m.Groups[1].Value).Order().ToArray();

    private static Task<AppDataDiscoveryResult> Discover(MemoryBridge host, Dictionary<string, string> parameters,
        Dictionary<string, Dictionary<string, string>> tables) => ((IAppDataProvider)Activator.CreateInstance(ProviderType.Value)!)
        .DiscoverAsync(new("unity-cache-check", host, parameters, tables), CancellationToken.None);

    private static Type CompileProvider()
    {
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(IAppDataProvider).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase);
        var compilation = CSharpCompilation.Create("UnityLocalized_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(PackageRoot, "Detector.cs.txt")), new(LanguageVersion.Latest))],
            paths.Select(p => MetadataReference.CreateFromFile(p)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        using var bytes = new MemoryStream();
        var emit = compilation.Emit(bytes);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        return Assembly.Load(bytes.ToArray()).GetType("Provider", throwOnError: true)!;
    }

    private static string FindPackageRoot()
    {
        // 夹具与真实工作副本内容一致；从仓库根读取，不依赖 .planning、用户包或 csproj 资源复制。
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory, Path.GetDirectoryName(SourceFile())! })
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                var path = Path.Combine(dir.FullName, "tests", "DiskPilotX.Core.Tests", "Fixtures", "unity-cache-check");
                if (File.Exists(Path.Combine(path, "Detector.cs.txt"))) return path;
            }
        throw new DirectoryNotFoundException("未找到 Unity 本地化测试夹具。");
    }

    private static string SourceFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    private sealed class MemoryBridge(string culture) : ISkillHostBridge
    {
        public string SkillId => "unity-cache-check";
        public IReadOnlyDictionary<string, string> Parameters => new Dictionary<string, string>();
        public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<string>> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, long> Sizes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Texts { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Logs { get; } = [];
        public List<string> Measured { get; } = [];
        public List<string> Enumerated { get; } = [];
        public List<string> WideQueries { get; } = [];
        public SkillLocalizationState GetLocalizationState() => SkillLocalizationState.FromCulture(CultureInfo.GetCultureInfo(culture));
        public string AddProject(string path, long size)
        {
            var parent = Path.GetDirectoryName(path)!;
            Directories.Add(parent);
            if (!Children.ContainsKey(parent)) Children[parent] = [];
            Children[parent].Add(path);
            Directories.Add(path);
            foreach (var folder in new[] { "Assets", "Packages", "ProjectSettings", "Library", "Temp", "obj", "Logs", "Builds", "MemoryCaptures", "Recordings", ".vs", ".idea" })
            {
                var child = Path.Combine(path, folder);
                Directories.Add(child);
                Sizes[child] = folder == "Library" ? size : 0;
            }
            return path;
        }
        public void Log(string message) => Logs.Add(message);
        public IReadOnlyList<string> EnumerateDirectories(string path) { Enumerated.Add(path); return Children.TryGetValue(path, out var children) ? children : []; }
        public bool DirectoryExists(string path) => Directories.Contains(path);
        public long GetDirectorySize(string path) { Measured.Add(path); return Sizes[path]; }
        public string? ReadTextFile(string path) => Texts.GetValueOrDefault(path);
        public string? ReadRegistry(string keyPath, string valueName) => throw new InvalidOperationException("禁止读取注册表。");
        public IReadOnlyList<string> EnumerateFiles(string path, string searchPattern = "*") => throw new InvalidOperationException("禁止新增文件枚举。");
        public bool FileExists(string path) => throw new InvalidOperationException("禁止新增文件检查。");
        public DateTime? GetDirectoryLastWriteTimeUtc(string path) => throw new InvalidOperationException("禁止新增时间查询。");
        public IReadOnlyList<SkillHostEntry> QueryScan(string? rootPath = null, int maxCount = 20_000) { WideQueries.Add(rootPath ?? "*"); throw new InvalidOperationException("禁止扩大到宿主快照。"); }
    }
}