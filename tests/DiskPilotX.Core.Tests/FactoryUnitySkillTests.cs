using System.Text.Json;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Copilot;
using DiskPilotX.Core.Services.Skills;

namespace DiskPilotX.Core.Tests;

public sealed class FactoryUnitySkillTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "diskpilot-unity-factory-" + Guid.NewGuid().ToString("N"));
    private static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", ".."));
    private CleanupSkillStore FactoryStore => new(Path.Combine(root, "empty-user-skills"), [Path.Combine(RepositoryRoot(), "skills")]);

    [Fact]
    public void 空用户库也能发现唯一出厂Unity技能并载入完整表单与三语文案()
    {
        var store = FactoryStore;
        Assert.True(store.IsProtected("unity-cache-check"), "Unity 独立技能必须来自随包目录，而非开发机用户库。");
        var catalog = new FileSkillPromptProvider([Path.Combine(RepositoryRoot(), "skills")], store.UserRoot, () => "zh-CN").GetSkillCatalog();
        var item = Assert.Single(catalog.Where(s => s.Id == "unity-cache-check"));
        Assert.Equal("Unity 项目缓存盘点", item.DisplayName);
        Assert.DoesNotContain(catalog, s => s.Id == "unity-project-cleanup");
        var package = store.LoadReadOnly(item.Id);
        Assert.False(string.IsNullOrWhiteSpace(package.Source));
        Assert.False(string.IsNullOrWhiteSpace(package.UiXaml));
        Assert.NotNull(package.Localizations);
        Assert.Equal(new[] { "en-US", "zh-CN", "zh-TW" }, package.Localizations.Keys.Order());
        var panel = SkillUiDocument.Parse(package.UiXaml!);
        Assert.Contains(panel.Children, child => child.Type == "TextBox" && child.Properties.GetValueOrDefault("Name") == "roots");
        Assert.Contains(panel.Children, child => child.Type == "TextBox" && child.Properties.GetValueOrDefault("Name") == "depth" && child.Properties.GetValueOrDefault("Text") == "6");
        Assert.False(Directory.Exists(store.UserRoot), "只读载入出厂技能不得依赖或生成用户版本指针。");
    }

    [Fact]
    public void 通用内置技能加载表单且表单变化会更新摘要()
    {
        var builtIn = Path.Combine(root, "built-in");
        var skill = Path.Combine(builtIn, "factory-panel");
        Directory.CreateDirectory(skill);
        File.WriteAllText(Path.Combine(skill, "SKILL.md"), "# 出厂任务面板");
        const string first = "<StackPanel><TextBox Name=\"roots\" /></StackPanel>";
        File.WriteAllText(Path.Combine(skill, "ui.xaml"), first);
        var store = new CleanupSkillStore(Path.Combine(root, "empty"), [builtIn]);
        var before = store.LoadReadOnly("factory-panel");
        Assert.Equal(first, before.UiXaml);
        File.WriteAllText(Path.Combine(skill, "ui.xaml"), "<StackPanel><TextBox Name=\"depth\" Text=\"6\" /></StackPanel>");
        Assert.NotEqual(before.Digest, store.LoadReadOnly("factory-panel").Digest);
    }

    [Fact]
    public void 内置任务面板沿用受约束描述解析而不是加载任意Xaml()
    {
        var skill = Path.Combine(root, "built-in", "factory-panel");
        Directory.CreateDirectory(skill);
        File.WriteAllText(Path.Combine(skill, "SKILL.md"), "# 出厂任务面板");
        File.WriteAllText(Path.Combine(skill, "ui.xaml"), "<ObjectDataProvider />");
        var store = new CleanupSkillStore(Path.Combine(root, "empty"), [Path.GetDirectoryName(skill)!]);
        Assert.Throws<ArgumentException>(() => store.LoadReadOnly("factory-panel"));
    }

    [Fact]
    public async Task 出厂Unity源码可授权执行且只度量四类缓存并保持空范围不扫描()
    {
        var store = FactoryStore;
        Assert.True(store.IsProtected("unity-cache-check"));
        var package = store.LoadReadOnly("unity-cache-check");
        var service = new CleanupSkillSourceService(store);
        var validation = await service.ValidateSourceAsync(package.Id);
        Assert.True(validation.Success, JsonSerializer.Serialize(validation.Diagnostics));
        Assert.Equal("provider", validation.Contract);
        var fixture = Path.Combine(root, "fixture");
        var project = Path.Combine(fixture, "Unity项目");
        foreach (var name in new[] { "Assets", "ProjectSettings", "Packages", "Builds", "Library", "Temp", "obj", "Logs" })
        {
            Directory.CreateDirectory(Path.Combine(project, name));
            File.WriteAllBytes(Path.Combine(project, name, "sample.bin"), new byte[1024]);
        }
        File.WriteAllText(Path.Combine(project, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 6000.0.23f1");
        var before = Directory.GetFiles(fixture, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllBytes);
        var host = new FixtureBridge(fixture);
        var result = await service.RunDiscoveryAsync(service.Authorize(package.Id, package.Digest), host,
            parameters: new Dictionary<string, string> { ["roots"] = fixture, ["depth"] = "3" });
        var entry = Assert.Single(result.Entries);
        Assert.Equal(project, entry.Path);
        Assert.Equal(4096, entry.SizeBytes);
        Assert.NotNull(result.View);
        Assert.Equal(new[] { "Library", "Logs", "Temp", "obj" }, host.Measured.Select(Path.GetFileName).Order(StringComparer.Ordinal));
        foreach (var (path, bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal(before.Keys.Order(), Directory.GetFiles(fixture, "*", SearchOption.AllDirectories).Order());
        var emptyHost = new FixtureBridge(fixture);
        var empty = await service.RunDiscoveryAsync(service.Authorize(package.Id, package.Digest), emptyHost,
            parameters: new Dictionary<string, string> { ["roots"] = "", ["depth"] = "3" });
        Assert.Empty(empty.Entries);
        Assert.Empty(emptyHost.Enumerated);
        Assert.Empty(emptyHost.Measured);
    }

    [Theory]
    [InlineData("\\")]
    [InlineData("C:")]
    [InlineData("D:Projects")]
    [InlineData("\\Projects")]
    public async Task 不完整绝对路径不得补全后访问宿主(string candidate)
    {
        var store = FactoryStore;
        var package = store.LoadReadOnly("unity-cache-check");
        var service = new CleanupSkillSourceService(store);
        var host = new FixtureBridge(root);
        var result = await service.RunDiscoveryAsync(service.Authorize(package.Id, package.Digest), host,
            parameters: new Dictionary<string, string> { ["roots"] = candidate });
        Assert.Empty(result.Entries);
        Assert.Empty(host.Probed);
        Assert.Empty(host.Enumerated);
        Assert.Empty(host.Measured);
    }

    [Fact]
    public async Task 父子根目录和尾部分隔符不得重复度量或渲染同一项目()
    {
        var store = FactoryStore;
        var package = store.LoadReadOnly("unity-cache-check");
        var service = new CleanupSkillSourceService(store);
        var fixture = Path.Combine(root, "fixture");
        var project = Path.Combine(fixture, "Unity项目");
        foreach (var folder in new[] { "Assets", "ProjectSettings", "Library" }) Directory.CreateDirectory(Path.Combine(project, folder));
        File.WriteAllBytes(Path.Combine(project, "Library", "cache.bin"), new byte[4096]);
        var host = new FixtureBridge(fixture);
        var result = await service.RunDiscoveryAsync(service.Authorize(package.Id, package.Digest), host,
            parameters: new Dictionary<string, string> { ["roots"] = $"{fixture};{project};{project}{Path.DirectorySeparatorChar}", ["depth"] = "3" });
        Assert.Equal(4096, Assert.Single(result.Entries).SizeBytes);
        var view = Assert.IsType<SkillUiSpec>(result.View);
        Assert.Single(view.Sections.Single(section => section.Kind == SkillUiSectionKind.Table && section.Items.Any(item => item.Selectable)).Items);
        Assert.Single(host.Measured);
        Assert.Equal("4.0 KB", view.Sections.Single(section => section.Kind == SkillUiSectionKind.StatRow).Items[1].Value);
    }

    [Fact]
    public void 宿主不再注册Unity专用工具动作和推荐元数据()
    {
        var core = typeof(CleanupSkillStore).Assembly;
        Assert.Null(core.GetType("DiskPilotX.Core.Services.Agent.UnityRegistryAgentTool"));
        Assert.DoesNotContain(Enum.GetNames<AiActionKind>(), name => name.Contains("Unity", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(core.GetTypes(), type => type.GetProperties().Any(p => p.Name.StartsWith("Unity", StringComparison.Ordinal)));
    }

    private sealed class FixtureBridge(string root) : ISkillHostBridge
    {
        public string SkillId => "unity-cache-check";
        public IReadOnlyDictionary<string, string> Parameters { get; } = new Dictionary<string, string>();
        public List<string> Probed { get; } = [];
        public List<string> Measured { get; } = [];
        public List<string> Enumerated { get; } = [];
        private bool Allowed(string path) => Path.GetFullPath(path).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || Path.GetFullPath(path).Equals(root, StringComparison.OrdinalIgnoreCase);
        public void Log(string message) { }
        public string? ReadRegistry(string keyPath, string valueName) => throw new InvalidOperationException("独立 Unity 技能不得依赖 Unity 注册表。");
        public IReadOnlyList<string> EnumerateDirectories(string path) { Enumerated.Add(path); return Allowed(path) && Directory.Exists(path) ? Directory.GetDirectories(path) : []; }
        public IReadOnlyList<string> EnumerateFiles(string path, string searchPattern = "*") => Allowed(path) && Directory.Exists(path) ? Directory.GetFiles(path, searchPattern) : [];
        public bool DirectoryExists(string path) { Probed.Add(path); return Allowed(path) && Directory.Exists(path); }
        public bool FileExists(string path) => Allowed(path) && File.Exists(path);
        public string? ReadTextFile(string path) => FileExists(path) ? File.ReadAllText(path) : null;
        public long GetDirectorySize(string path) { Measured.Add(path); return DirectoryExists(path) ? Directory.GetFiles(path, "*", SearchOption.AllDirectories).Sum(p => new FileInfo(p).Length) : 0; }
        public DateTime? GetDirectoryLastWriteTimeUtc(string path) => DirectoryExists(path) ? Directory.GetLastWriteTimeUtc(path) : null;
        public IReadOnlyList<SkillHostEntry> QueryScan(string? rootPath = null, int maxCount = 20_000) => [];
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
