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

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
