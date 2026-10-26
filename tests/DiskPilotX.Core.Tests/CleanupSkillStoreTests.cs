using System.Text.Json;
using DiskPilotX.Core.Services.Skills;
using DiskPilotX.Core.Services.Agent;

namespace DiskPilotX.Core.Tests;

public sealed class CleanupSkillStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "cleanup-store-" + Guid.NewGuid().ToString("N"));
    private CleanupSkillStore Store => new(Path.Combine(root, "skills"), [Path.Combine(root, "builtin")]);
    private string Scope => Path.Combine(root, "scope");
    private CleanupSkillPackage Package(string path = "cache") => new()
    {
        Id = "sample-cleanup", Title = "缓存检查", Description = "只读识别缓存", SkillMarkdown = "# 缓存检查\n只读预览。",
        Rules = [new() { Id = "cache", Path = path, Description = "可以再生的缓存", Risk = "重新下载", Sources = ["用户指定"], Excludes = ["keep/**"] }]
    };

    [Fact]
    public void SaveLoadAndUpdatePreserveUiAndInvalidateDigest()
    {
        var store = Store;
        var first = store.Save(Package() with { UiXaml = "<StackPanel/>" });
        Assert.Equal("<StackPanel/>", store.Load(first.Id).UiXaml);
        Assert.Single(store.List());
        var second = store.Save(first with { UiXaml = "<Grid/>" }, true);
        Assert.NotEqual(first.Digest, second.Digest);
        Assert.Equal("<Grid/>", store.Load(first.Id).UiXaml);
        Assert.Throws<IOException>(() => store.Save(Package()));
        Assert.Equal(second.Digest, store.Load(first.Id).Digest);
    }

    [Theory]
    [InlineData("skill-creator")]
    [InlineData("disk-management")]
    [InlineData("../escape")]
    [InlineData("CON")]
    public void InvalidAndBuiltinIdsCannotBeSaved(string id)
    {
        Assert.ThrowsAny<Exception>(() => Store.Save(Package() with { Id = id }));
    }

    [Fact]
    public void DiscoveredBuiltinCannotBeShadowed()
    {
        var builtin = Path.Combine(root, "builtin", "custom-builtin");
        Directory.CreateDirectory(builtin);
        File.WriteAllText(Path.Combine(builtin, "SKILL.md"), "# 内置");
        Assert.ThrowsAny<Exception>(() => Store.Save(Package() with { Id = "custom-builtin" }));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("C:/Windows")]
    [InlineData("cache/../../outside")]
    [InlineData("cache:stream")]
    public void InvalidUpdateKeepsPreviousPackage(string path)
    {
        var store = Store;
        var saved = store.Save(Package());
        Assert.ThrowsAny<Exception>(() => store.Save(Package(path), true));
        Assert.Equal(saved.Digest, store.Load(saved.Id).Digest);
    }

    [Fact]
    public void FailedAtomicPointerCommitKeepsOldPackage()
    {
        var store = Store;
        var saved = store.Save(Package());
        using var locked = new FileStream(Path.Combine(store.UserRoot, saved.Id, "current.json"), FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert.True(Record.Exception(() => store.Save(saved with { Title = "新标题" }, true)) is IOException or UnauthorizedAccessException);
        Assert.Equal(saved.Digest, store.Load(saved.Id).Digest);
    }

    [Fact]
    public async Task PreviewReportsRealBytesAndPreservesExcludedFiles()
    {
        Directory.CreateDirectory(Path.Combine(Scope, "cache", "keep"));
        File.WriteAllBytes(Path.Combine(Scope, "cache", "remove.bin"), new byte[37]);
        File.WriteAllBytes(Path.Combine(Scope, "cache", "keep", "important.bin"), new byte[99]);
        Store.Save(Package());
        var result = await Store.PreviewAsync("sample-cleanup", Scope, default);
        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(37, candidate.SizeBytes);
        Assert.EndsWith("remove.bin", candidate.Path);
        Assert.Equal("cache", candidate.RuleId);
        Assert.True(File.Exists(candidate.Path));
        Assert.True(File.Exists(Path.Combine(Scope, "cache", "keep", "important.bin")));
    }

    [Fact]
    public async Task PreviewRejectsReparsePointScopeAndSkipsLinkedCandidates()
    {
        Directory.CreateDirectory(Scope);
        Directory.CreateDirectory(Path.Combine(root, "outside"));
        File.WriteAllBytes(Path.Combine(root, "outside", "secret"), new byte[123]);
        Directory.CreateSymbolicLink(Path.Combine(Scope, "cache"), Path.Combine(root, "outside"));
        Store.Save(Package());
        Assert.Empty((await Store.PreviewAsync("sample-cleanup", Scope, default)).Candidates);
        await Assert.ThrowsAnyAsync<Exception>(() => Store.PreviewAsync("sample-cleanup", Path.Combine(Scope, "cache"), default));
    }

    [Fact]
    public async Task ImportLocalReferenceRecordsSourceAndContentDigest()
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "rules.md");
        await File.WriteAllTextAsync(path, "# 可再生缓存");
        var reference = await Store.ImportReferenceAsync(path, default);
        Assert.Equal(path, reference.Source);
        Assert.Contains("可再生缓存", reference.Content);
        Assert.Equal(64, reference.Digest.Length);
        var saved = Store.Save(Package() with { References = [reference] });
        Assert.Equal(reference, Store.Load(saved.Id).References.Single());
    }

    [Theory]
    [InlineData("http://127.0.0.1/private")]
    [InlineData("http://169.254.169.254/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://10.0.0.1/")]
    [InlineData("ftp://example.com/file")]
    public async Task ImportRejectsNonpublicDestinations(string source)
    {
        await Assert.ThrowsAnyAsync<Exception>(() => Store.ImportReferenceAsync(source, default));
    }

    [Fact]
    public async Task ToolSchemaDoesNotExposeSourceExecutionOrTrust()
    {
        var tool = new CleanupSkillTool(Store);
        Assert.Equal("manage_cleanup_skill", tool.Name);
        Assert.DoesNotContain("run_source", tool.Schema.GetRawText());
        var result = await tool.ExecuteAsync(new(null, null, null, null, false), JsonSerializer.SerializeToElement(new { operation = "run_source", trust = true }), default);
        Assert.False(result.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task DraftPreviewRequiresNoSaveAndMatchesSavedDigest()
    {
        Directory.CreateDirectory(Path.Combine(Scope, "cache"));
        await File.WriteAllBytesAsync(Path.Combine(Scope, "cache", "file"), new byte[9]);
        var store = Store;
        var preview = await store.PreviewAsync(Package(), Scope, default);
        Assert.Single(preview.Candidates);
        Assert.Empty(store.List());
        Assert.Equal(preview.Digest, store.Save(Package()).Digest);
    }

    [Fact]
    public void OldRevisionCanBeRestoredWithoutLosingAtomicity()
    {
        var store = Store;
        var first = store.Save(Package());
        store.Save(first with { Title = "改名" }, true);
        Assert.Equal(first.Digest, store.Save(first, true).Digest);
    }

    [Fact]
    public void InvalidUiAndTamperedFilesAreRejected()
    {
        var store = Store; var saved = store.Save(Package());
        Assert.ThrowsAny<Exception>(() => store.Save(saved with { UiXaml = "<ObjectDataProvider/>" }, true));
        Assert.Equal(saved.Digest, store.Load(saved.Id).Digest);
        File.WriteAllText(Path.Combine(store.UserRoot, saved.Id, "versions", saved.Digest, "SKILL.md"), "外部改写");
        Assert.Throws<InvalidDataException>(() => store.Load(saved.Id));
        Assert.Empty(store.List());
    }

    [Fact]
    public async Task GlobRulesPreserveNamedSubtree()
    {
        Directory.CreateDirectory(Path.Combine(Scope, "project", "node_modules", "keep"));
        await File.WriteAllBytesAsync(Path.Combine(Scope, "project", "node_modules", "remove"), new byte[31]);
        await File.WriteAllBytesAsync(Path.Combine(Scope, "project", "node_modules", "keep", "data"), new byte[58]);
        var result = await Store.PreviewAsync(Package("**/node_modules"), Scope, default);
        Assert.Equal(31, Assert.Single(result.Candidates).SizeBytes);
    }

    [Fact]
    public async Task LocalReferenceLimitAndCancellationAreEnforced()
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "large.txt");
        await File.WriteAllBytesAsync(path, new byte[1024 * 1024 + 1]);
        await Assert.ThrowsAsync<InvalidDataException>(() => Store.ImportReferenceAsync(path, default));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Store.ImportReferenceAsync(path, cancelled.Token));
    }

    [Fact]
    public async Task DraftToolPreviewAndSaveUseSameJsonContract()
    {
        Directory.CreateDirectory(Path.Combine(Scope, "cache"));
        File.WriteAllBytes(Path.Combine(Scope, "cache", "test"), new byte[17]);
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        var tool = new CleanupSkillTool(Store);
        var preview = await tool.ExecuteAsync(new(null, null, null, null, false), JsonSerializer.SerializeToElement(new { operation = "preview", package = Package(), scope_path = Scope }, options), default);
        Assert.True(preview.GetProperty("ok").GetBoolean());
        Assert.Equal(17, preview.GetProperty("preview").GetProperty("candidates")[0].GetProperty("size_bytes").GetInt64());
        var save = await tool.ExecuteAsync(new(null, null, null, null, false), JsonSerializer.SerializeToElement(new { operation = "save", package = Package() }, options), default);
        Assert.True(save.GetProperty("ok").GetBoolean());
        Assert.Equal(preview.GetProperty("preview").GetProperty("digest").GetString(), save.GetProperty("package").GetProperty("digest").GetString());
        Assert.False(save.GetProperty("package").TryGetProperty("skill_markdown", out _));
        Assert.False(save.GetProperty("package").TryGetProperty("rules", out _));
        var read = await tool.ExecuteAsync(new(null, null, null, null, false), JsonSerializer.SerializeToElement(new { operation = "read", id = "sample-cleanup" }), default);
        Assert.Contains("缓存检查", read.GetProperty("package").GetProperty("skill_markdown").GetString());
    }

    [Fact]
    public async Task InvalidDraftAndNullRuleAreErrorsWithoutSaving()
    {
        var tool = new CleanupSkillTool(Store);
        var arguments = JsonSerializer.SerializeToElement(new { operation = "save", package = new { id = "bad", title = "坏包", description = "坏包", skill_markdown = "测试", rules = new object?[] { null } } });
        var result = await tool.ExecuteAsync(new(null, null, null, null, false), arguments, default);
        Assert.False(result.GetProperty("ok").GetBoolean());
        await Assert.ThrowsAnyAsync<Exception>(() => Store.PreviewAsync(Package("../outside"), Scope, default));
        Assert.Empty(Store.List());
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
