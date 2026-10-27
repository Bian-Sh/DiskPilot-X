using DiskPilotX.Core.Services.Skills;

namespace DiskPilotX.Core.Tests;

public sealed class CleanupSkillSourceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "cleanup-source-" + Guid.NewGuid().ToString("N"));
    private CleanupSkillStore Store => new(Path.Combine(root, "skills"), []);
    private CleanupSkillPackage Package(string source) => new()
    {
        Id = "source-cleanup", Title = "源码检查", Description = "可信检测器", SkillMarkdown = "# 可信检测器，不是安全沙箱", Source = source
    };
    private const string Provider = """
        using System;
        using System.Collections.Generic;
        using System.IO;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using DiskPilotX.Core.Services.Skills;
        public sealed class Provider : IAppDataProvider
        {
            public Task<AppDataDiscoveryResult> DiscoverAsync(AppDataDiscoveryContext context, CancellationToken token)
            {
                IReadOnlyList<AppDataEntry> entries = new[] { new AppDataEntry("730", context.Parameters!["name"], "C:/games/app730", 12345, "完全安装") };
                return Task.FromResult(new AppDataDiscoveryResult(entries));
            }
        }
        """;

    private const string Detector = """
        using System;
        using System.IO;
        using System.Threading;
        using System.Threading.Tasks;
        using System.Collections.Generic;
        using DiskPilotX.Core.Services.Skills;
        public sealed class Detector : ICleanupSkillDetector
        {
            public Task<IReadOnlyList<CleanupSkillCandidate>> DetectAsync(CleanupSkillDetectionContext context, CancellationToken token)
            {
                IReadOnlyList<CleanupSkillCandidate> result = new[] { new CleanupSkillCandidate("source", Path.Combine(context.ScopePath, "cache.bin"), 999999, "缓存", "可再生", new[] { "用户授权源码" }) };
                return Task.FromResult(result);
            }
        }
        """;

    [Fact]
    public async Task CompileDiagnosticsAndAuthorizedExecutionUseRealFileSize()
    {
        var store = Store; var package = store.Save(Package(Detector));
        Directory.CreateDirectory(Path.Combine(root, "scope"));
        File.WriteAllBytes(Path.Combine(root, "scope", "cache.bin"), new byte[73]);
        var service = new CleanupSkillSourceService(store);
        var validation = await service.ValidateSourceAsync(package.Id, default);
        Assert.True(validation.Success, string.Join(";", validation.Diagnostics.Select(d => d.Message)));
        Assert.Equal(package.Digest, validation.Digest);
        var authorization = service.Authorize(package.Id, package.Digest);
        var result = await service.RunAsync(authorization, Path.Combine(root, "scope"), default);
        Assert.Equal(73, Assert.Single(result.Candidates).SizeBytes);
        Assert.True(File.Exists(result.Candidates[0].Path));
    }

    [Fact]
    public async Task DetectorReceivesExplicitFormParameters()
    {
        var store=Store;
        var package=store.Save(Package(Detector.Replace("\"缓存\"", "context.Parameters![\"label\"]")));
        Directory.CreateDirectory(Path.Combine(root,"scope"));
        File.WriteAllBytes(Path.Combine(root,"scope","cache.bin"),new byte[3]);
        var service=new CleanupSkillSourceService(store);
        var result=await service.RunAsync(service.Authorize(package.Id,package.Digest),Path.Combine(root,"scope"),default,new Dictionary<string,string>{{"label","用户选择的缓存"}});
        Assert.Equal("用户选择的缓存",Assert.Single(result.Candidates).Description);
    }

    [Fact]
    public async Task MalformedSourceReturnsLocatedDiagnostics()
    {
        var store = Store; var package = store.Save(Package("class Broken { broken syntax }"));
        var result = await new CleanupSkillSourceService(store).ValidateSourceAsync(package.Id, default);
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Severity == "error" && d.Line > 0);
    }

    [Fact]
    public async Task SuccessfulCompilationWithoutContractIsRejected()
    {
        var store = Store; var package = store.Save(Package("public class NoContract {}"));
        var result = await new CleanupSkillSourceService(store).ValidateSourceAsync(package.Id, default);
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == "detector_contract");
    }

    [Fact]
    public async Task SourceOrUiChangeInvalidatesHostAuthorization()
    {
        var store = Store; var package = store.Save(Package(Detector));
        var service = new CleanupSkillSourceService(store);
        var authorization = service.Authorize(package.Id, package.Digest);
        store.Save(package with { UiXaml = "<StackPanel/>" }, true);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RunAsync(authorization, root, default));
        Assert.Throws<UnauthorizedAccessException>(() => service.Authorize(package.Id, package.Digest));
    }

    [Fact]
    public async Task AuthorizationCannotBeReusedByAnotherService()
    {
        var store = Store; var package = store.Save(Package(Detector));
        var authorization = new CleanupSkillSourceService(store).Authorize(package.Id, package.Digest);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new CleanupSkillSourceService(store).RunAsync(authorization, root, default));
    }

    [Fact]
    public async Task NestedDetectorIsRejectedBeforeRuntimeSelection()
    {
        var index = Detector.IndexOf("public sealed class", StringComparison.Ordinal);
        var source = Detector[..index] + "public class Outer { " + Detector[index..] + " }";
        var store = Store; var package = store.Save(Package(source));
        Assert.False((await new CleanupSkillSourceService(store).ValidateSourceAsync(package.Id)).Success);
    }

    [Fact]
    public async Task ValidationDoesNotRunModuleInitializers()
    {
        var marker = Path.Combine(root, "marker");
        var initializer = "public static class Module { [System.Runtime.CompilerServices.ModuleInitializer] public static void Init() { System.IO.File.WriteAllText(" + System.Text.Json.JsonSerializer.Serialize(marker) + ", \"执行了\"); } }";
        var store = Store; var package = store.Save(Package(Detector + initializer));
        var validation = await new CleanupSkillSourceService(store).ValidateSourceAsync(package.Id);
        Assert.True(validation.Success, string.Join(";", validation.Diagnostics.Select(d => d.Message)));
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public async Task SourceOutputCannotEscapeScope()
    {
        var source = Detector.Replace("Path.Combine(context.ScopePath, \"cache.bin\")", "Path.Combine(context.ScopePath, \"..\", \"outside.bin\")");
        var store = Store; var package = store.Save(Package(source));
        Directory.CreateDirectory(Path.Combine(root, "scope")); File.WriteAllBytes(Path.Combine(root, "outside.bin"), [1]);
        var service = new CleanupSkillSourceService(store);
        await Assert.ThrowsAnyAsync<Exception>(() => service.RunAsync(service.Authorize(package.Id, package.Digest), Path.Combine(root, "scope")));
    }

    [Fact]
    public async Task KnownRuleIdDoesNotPermitUnrelatedPath()
    {
        var store = Store;
        var package = store.Save(Package(Detector) with { Rules = [new() { Id = "source", Path = "expected-cache", Description = "指定缓存", Risk = "可再生", Sources = ["用户"] }] });
        Directory.CreateDirectory(Path.Combine(root, "scope")); File.WriteAllBytes(Path.Combine(root, "scope", "cache.bin"), [1]);
        var service = new CleanupSkillSourceService(store);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.RunAsync(service.Authorize(package.Id, package.Digest), Path.Combine(root, "scope")));
    }

    [Fact]
    public async Task SourceCannotReturnPreservedFile()
    {
        var store = Store;
        var package = store.Save(Package(Detector) with { Rules = [new() { Id = "source", Path = "cache.bin", Preserve = ["cache.bin"], Description = "缓存", Risk = "可再生", Sources = ["用户"] }] });
        Directory.CreateDirectory(Path.Combine(root, "scope")); File.WriteAllBytes(Path.Combine(root, "scope", "cache.bin"), [1]);
        var service = new CleanupSkillSourceService(store);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.RunAsync(service.Authorize(package.Id, package.Digest), Path.Combine(root, "scope")));
    }

    [Fact]
    public async Task ProviderSourceCompilesAndRunsDiscovery()
    {
        var store = Store; var package = store.Save(Package(Provider));
        var service = new CleanupSkillSourceService(store);
        var validation = await service.ValidateSourceAsync(package.Id, default);
        Assert.True(validation.Success, string.Join(";", validation.Diagnostics.Select(d => d.Message)));
        Assert.Equal("provider", validation.Contract);
        var authorization = service.Authorize(package.Id, package.Digest);
        var parameters = new Dictionary<string, string> { ["name"] = "Counter-Strike 2" };
        var entries = (await service.RunDiscoveryAsync(authorization, new StubBridge(package.Id, parameters), default, parameters)).Entries;
        var entry = Assert.Single(entries);
        Assert.Equal("730", entry.AppId);
        Assert.Equal("Counter-Strike 2", entry.Name);
        Assert.Equal(12345, entry.SizeBytes);
    }

    [Fact]
    public async Task ProviderAndDetectorCannotBeMixed()
    {
        var mixed = Provider.Replace("public sealed class Provider : IAppDataProvider", "public sealed class Provider : IAppDataProvider, ICleanupSkillDetector") + """
            public partial class Provider
            {
                public Task<IReadOnlyList<CleanupSkillCandidate>> DetectAsync(CleanupSkillDetectionContext context, CancellationToken token) => Task.FromResult<IReadOnlyList<CleanupSkillCandidate>>(Array.Empty<CleanupSkillCandidate>());
            }
            """;
        var store = Store; var package = store.Save(Package(mixed));
        var validation = await new CleanupSkillSourceService(store).ValidateSourceAsync(package.Id);
        Assert.False(validation.Success);
        Assert.Contains(validation.Diagnostics, d => d.Code == "detector_contract");
    }

    [Fact]
    public async Task SteamSampleProviderRealMachineSmoke()
    {
        // 真机冒烟：编译内置 steam skill 源码并执行真实发现；本机未装 Steam 时结果为空。
        var sample = FindSolutionRoot();
        Assert.True(sample is not null, "未定位到解决方案根目录");
        if (sample is null) return;
        var source = await File.ReadAllTextAsync(Path.Combine(sample, "skills", "steam-library-organizer", "src", "Provider.cs"));
        var store = Store;
        var package = store.Save(Package(source) with { Id = "steam-smoke" });
        var service = new CleanupSkillSourceService(store);
        var validation = await service.ValidateSourceAsync(package.Id, default);
        Assert.True(validation.Success, string.Join(";", validation.Diagnostics.Select(d => d.Code + ":" + d.Message)));
        Assert.Equal("provider", validation.Contract);
        var result = await service.RunDiscoveryAsync(service.Authorize(package.Id, package.Digest), new StubBridge(package.Id), default);
        foreach (var entry in result.Entries)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Name));
            Assert.True(entry.SizeBytes >= 0);
        }
    }

    /// <summary>
    /// 内置发现类技能必须可运行：随包发布的 Provider 源码不在用户库里，
    /// 若只读载入被拒绝，用户拿到的是一个永远跑不起来的技能。
    /// </summary>
    [Fact]
    public async Task BuiltInProviderSkillIsRunnableButNotOverwritable()
    {
        var builtIn = Path.Combine(root, "builtin");
        var directory = Path.Combine(builtIn, "steam-like");
        Directory.CreateDirectory(Path.Combine(directory, "src"));
        await File.WriteAllTextAsync(Path.Combine(directory, "SKILL.md"),
            "---\nname: steam-like\ndescription: 内置发现技能\n---\n\n# 内置发现\n\n## Purpose\n只读盘点。\n");
        await File.WriteAllTextAsync(Path.Combine(directory, "src", "Provider.cs"), Provider);

        var store = new CleanupSkillStore(Path.Combine(root, "skills"), [builtIn]);
        Assert.True(store.IsProtected("steam-like"));

        var loaded = store.LoadReadOnly("steam-like");
        Assert.Equal("内置发现", loaded.Title);
        Assert.False(string.IsNullOrWhiteSpace(loaded.Source));
        Assert.Equal(64, loaded.Digest.Length);
        Assert.Equal(loaded.Digest, store.LoadReadOnly("steam-like").Digest);

        var service = new CleanupSkillSourceService(store);
        var validation = await service.ValidateSourceAsync("steam-like", default);
        Assert.True(validation.Success, string.Join(";", validation.Diagnostics.Select(d => d.Message)));
        Assert.Equal("provider", validation.Contract);

        var parameters = new Dictionary<string, string>(StringComparer.Ordinal) { ["name"] = "内置游戏" };
        var result = await service.RunDiscoveryAsync(service.Authorize("steam-like", validation.Digest),
            new StubBridge("steam-like", parameters), default, parameters);
        Assert.Equal("内置游戏", Assert.Single(result.Entries).Name);

        Assert.Throws<ArgumentException>(() => store.Save(new CleanupSkillPackage
        {
            Id = "steam-like", Title = "覆盖尝试", Description = "不应成功", SkillMarkdown = "# 覆盖", Source = Provider
        }, overwrite: true));
    }

    /// <summary>真实内置根（仓库 skills/）里的 Steam 发现技能必须能被只读载入并通过编译。</summary>
    [Fact]
    public async Task ShippedSteamProviderCompilesFromBuiltInRoot()
    {
        var solutionRoot = FindSolutionRoot();
        Assert.True(solutionRoot is not null, "未定位到解决方案根目录");
        if (solutionRoot is null) return;

        var store = new CleanupSkillStore(Path.Combine(root, "user"), [Path.Combine(solutionRoot, "skills")]);
        Assert.True(store.IsProtected("steam-library-organizer"));
        var package = store.LoadReadOnly("steam-library-organizer");
        Assert.False(string.IsNullOrWhiteSpace(package.Source));
        Assert.Equal(64, package.Digest.Length);

        var service = new CleanupSkillSourceService(store);
        var validation = await service.ValidateSourceAsync("steam-library-organizer", default);
        Assert.True(validation.Success, string.Join(";", validation.Diagnostics.Select(d => d.Code + ":" + d.Message)));
        Assert.Equal("provider", validation.Contract);
    }

    /// <summary>测试用能力桥桩：不触碰真实注册表与磁盘，仅回传固定数据。</summary>
    private sealed class StubBridge(string skillId, IReadOnlyDictionary<string, string>? parameters = null) : ISkillHostBridge
    {
        public string SkillId { get; } = skillId;
        public IReadOnlyDictionary<string, string> Parameters { get; } = parameters ?? new Dictionary<string, string>(StringComparer.Ordinal);
        public void Log(string message) { }
        public string? ReadRegistry(string keyPath, string valueName) => null;
        public IReadOnlyList<string> EnumerateDirectories(string path) => [];
        public IReadOnlyList<string> EnumerateFiles(string path, string searchPattern = "*") => [];
        public bool DirectoryExists(string path) => false;
        public bool FileExists(string path) => false;
        public string? ReadTextFile(string path) => null;
        public long GetDirectorySize(string path) => 0;
        public DateTime? GetDirectoryLastWriteTimeUtc(string path) => null;
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
