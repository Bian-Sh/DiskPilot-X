using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using DiskPilotX.Core.Services.Analysis;
using DiskPilotX.Core.Services.Copilot;
using DiskPilotX.Core.Services.Skills;
using NSubstitute;

namespace DiskPilotX.Core.Tests;

public sealed class AnalysisRuleCatalogTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "analysis-catalog-" + Guid.NewGuid().ToString("N"));
    private string UserRoot => Path.Combine(root, "user");
    private string BuiltInRoot => Path.Combine(root, "builtin");
    private CleanupSkillStore Store => new(UserRoot, []);
    private FileSkillPromptProvider Provider => new([BuiltInRoot], UserRoot);
    private AnalysisRuleCatalog Catalog => new(Provider, Store);

    [Fact]
    public void 分析侧提供独立只读规则目录()
    {
        var type = Assembly.Load("DiskPilotX.Core")
            .GetType("DiskPilotX.Core.Services.Analysis.AnalysisRuleCatalog");
        Assert.NotNull(type);
        Assert.DoesNotContain(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            method => method.Name.Contains("Save") || method.Name.Contains("Delete") || method.Name.Contains("Preview"));
    }

    [Fact]
    public void 每轮发现新增Markdown与包且无需重建目录服务()
    {
        var catalog = Catalog;
        Assert.DoesNotContain(catalog.GetCatalog(), item => item.Id == "fresh-markdown" || item.Id == "sample-cleanup");
        Assert.True(Provider.CreateOrUpdateSkill("fresh-markdown", "新增规则", "新保存的描述", "必须先核实。", false).Success);
        Store.Save(Package());
        var next = catalog.GetCatalog();
        Assert.Contains(next, item => item.Id == "fresh-markdown" && item.Kind == "markdown");
        Assert.Contains(next, item => item.Id == "sample-cleanup" && item.Kind == "package");
        Assert.Single(next.Where(item => item.Id == "sample-cleanup"));
    }

    [Fact]
    public void 旧Markdown全文兼容且版本是实际正文摘要()
    {
        const string content = "# 旧规则\n\n历史规则，必须保留用户文件。\n";
        Markdown("legacy", content);
        var descriptor = Assert.Single(Catalog.GetCatalog().Where(item => item.Id == "legacy"));
        var read = Catalog.Read("legacy");
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
        Assert.Equal(content, read.Content);
        Assert.Equal(digest, read.Digest);
        Assert.Equal(digest, descriptor.Digest);
        Assert.Equal(digest, descriptor.Version);
        Assert.Equal(descriptor.Version, read.Version);
        Assert.Equal("legacy-full", read.Stage);
        Assert.True(descriptor.Readable);
        Assert.Null(descriptor.Error);
        Assert.False(descriptor.RequiresSource);
        Assert.Empty(descriptor.Hints);
        Assert.Empty(read.Preserve);
        Assert.Empty(read.Excludes);
    }

    [Theory]
    [InlineData("discovery")]
    [InlineData("analysis")]
    [InlineData("handling")]
    [InlineData("constraints")]
    public void 阶段请求保守全文回退不会遗漏保护和恢复代价(string stage)
    {
        const string content = "# 分段规则\n## Discovery\n仅适用缓存。\n## Analysis\n核实内容。\n## Handling\n重新下载成本很高。\n## Constraints\n禁止删除用户文件；保留 keep/**。";
        Markdown("staged", content);
        var read = Catalog.Read("staged", stage);
        Assert.Equal("legacy-full", read.Stage);
        Assert.Equal(content, read.Content);
        Assert.Contains("禁止删除", read.Content);
        Assert.Contains("重新下载成本很高", read.Content);
    }

    [Fact]
    public void 包目录使用真实版本并合并规则路径保护排除和风险()
    {
        var saved = Store.Save(Package() with
        {
            Version = "2.3.4",
            Rules = [Rule(), Rule() with { Id = "logs", Path = "logs/**", Preserve = ["keep/**", "manual/**"], Excludes = ["locked/**"] }]
        });
        var descriptor = Assert.Single(Catalog.GetCatalog().Where(item => item.Id == saved.Id));
        var read = Catalog.Read(saved.Id);
        Assert.Equal("package", descriptor.Kind);
        Assert.Equal(saved.Version, descriptor.Version);
        Assert.Equal(saved.Version, read.Version);
        Assert.Equal(saved.Digest, descriptor.Digest);
        Assert.Equal(saved.Digest, read.Digest);
        Assert.Equal(new[] { "cache/**", "logs/**" }, descriptor.Hints);
        Assert.Equal(new[] { "keep/**", "manual/**" }, read.Preserve);
        Assert.Equal(new[] { "locked/**" }, read.Excludes);
        Assert.Contains("重新下载", read.Content);
        Assert.Contains("keep/**", read.Content);
        Assert.False(descriptor.RequiresSource);
    }

    [Fact]
    public void 不依赖Provider列出额外包()
    {
        var saved = Store.Save(Package());
        var provider = Substitute.For<ISkillPromptProvider>();
        provider.GetSkillCatalog().Returns(Array.Empty<AiSkillCatalogItem>());
        var catalog = new AnalysisRuleCatalog(provider, Store);
        Assert.Contains(catalog.GetCatalog(), item => item.Id == saved.Id && item.Kind == "package");
        Assert.Contains(saved.SkillMarkdown, catalog.Read(saved.Id).Content);
        provider.DidNotReceiveWithAnyArgs().CreateOrUpdateSkill(default!, default!, default!, default!, default);
        provider.DidNotReceiveWithAnyArgs().DeleteSkill(default!);
    }

    [Fact]
    public void 同Id遵循Provider已有Markdown优先级不冒用包版本约束()
    {
        Store.Save(Package());
        const string content = "# 优先的内置Markdown\n内置保护。";
        Markdown("sample-cleanup", content);
        var descriptor = Assert.Single(Catalog.GetCatalog().Where(item => item.Id == "sample-cleanup"));
        var read = Catalog.Read("sample-cleanup");
        Assert.Equal("markdown", descriptor.Kind);
        Assert.Equal("优先的内置Markdown", descriptor.Title);
        Assert.Equal(content, read.Content);
        Assert.Empty(descriptor.Hints);
        Assert.Empty(read.Preserve);
    }

    [Fact]
    public void 同一服务观察Markdown和包实际版本改变()
    {
        Markdown("changing", "# 原始Markdown");
        var store = Store;
        var firstPackage = store.Save(Package());
        var catalog = Catalog;
        var original = catalog.Read("changing");
        var originalPackage = catalog.Read(firstPackage.Id);
        Markdown("changing", "# 修改后的Markdown");
        var secondPackage = store.Save(firstPackage with { Version = "3.0", SkillMarkdown = "# 新包正文" }, true);
        var changed = catalog.Read("changing");
        var changedPackage = catalog.Read(firstPackage.Id);
        Assert.NotEqual(original.Digest, changed.Digest);
        Assert.NotEqual(original.Version, changed.Version);
        Assert.NotEqual(originalPackage.Digest, changedPackage.Digest);
        Assert.Equal(secondPackage.Digest, changedPackage.Digest);
        Assert.Equal("3.0", changedPackage.Version);
        Assert.Contains("新包正文", changedPackage.Content);
        Assert.Equal(secondPackage.Digest, Assert.Single(catalog.GetCatalog().Where(item => item.Id == firstPackage.Id)).Digest);
    }

    [Fact]
    public void 正文分页可以无遗漏重组且结束偏移可靠()
    {
        const string content = "# 旧规则\n甲乙丙丁😀后续内容\n保留全部用户文件。";
        Markdown("paged", content);
        var catalog = Catalog;
        var pages = new StringBuilder();
        var offset = 0;
        AnalysisRuleRead page;
        do
        {
            page = catalog.Read("paged", offset: offset, maxChars: 3);
            Assert.InRange(page.Content.Length, 1, 3);
            Assert.Equal(offset + page.Content.Length, page.NextOffset);
            Assert.Equal(page.NextOffset < content.Length, page.Truncated);
            pages.Append(page.Content);
            offset = page.NextOffset;
        } while (page.Truncated);
        Assert.Equal(content, pages.ToString());
        var end = catalog.Read("paged", offset: offset, maxChars: 3);
        Assert.Empty(end.Content);
        Assert.False(end.Truncated);
        Assert.Equal(content.Length, end.NextOffset);
    }

    [Fact]
    public void 默认读取有界且分页时包保护列表仍完整()
    {
        Store.Save(Package() with { SkillMarkdown = new string('中', 13000) });
        var page = Catalog.Read("sample-cleanup");
        Assert.Equal(12000, page.Content.Length);
        Assert.True(page.Truncated);
        Assert.Equal(12000, page.NextOffset);
        Assert.Equal(new[] { "keep/**" }, page.Preserve);
        Assert.Equal(new[] { "locked/**" }, page.Excludes);
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(0, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 12001)]
    [InlineData(0, int.MaxValue)]
    public void 拒绝负偏移和不合法读取上限(int offset, int maxChars)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Catalog.Read("any", offset: offset, maxChars: maxChars));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../escape")]
    [InlineData("C:/Windows")]
    public void 拒绝不合法Id(string id) => Assert.ThrowsAny<ArgumentException>(() => Catalog.Read(id));

    [Fact]
    public void 拒绝未知阶段和超过正文的偏移()
    {
        Markdown("short", "# 短规则");
        Assert.Throws<ArgumentException>(() => Catalog.Read("short", "delete"));
        Assert.Throws<ArgumentOutOfRangeException>(() => Catalog.Read("short", offset: int.MaxValue));
        Assert.Throws<InvalidDataException>(() => Catalog.Read("missing"));
    }

    [Fact]
    public void 损坏包不隐藏其他条目且不宣称目录完整()
    {
        var store = Store;
        var good = store.Save(Package());
        var bad = store.Save(Package() with { Id = "broken-package" });
        File.WriteAllText(Path.Combine(UserRoot, bad.Id, "current.json"), "{损坏指针");
        Markdown("healthy-markdown", "# 正常Markdown");
        var catalog = Catalog;
        var items = catalog.GetCatalog();
        Assert.Contains(items, item => item.Id == good.Id && item.Readable);
        Assert.Contains(items, item => item.Id == "healthy-markdown" && item.Readable);
        Assert.False(string.IsNullOrWhiteSpace(catalog.Warning));
        Assert.Throws<InvalidDataException>(() => catalog.Read(bad.Id));
        Assert.Equal(good.Digest, catalog.Read(good.Id).Digest);
    }

    [Fact]
    public void 单条Provider读取异常隔离且不伪造已读取版本()
    {
        var provider = Substitute.For<ISkillPromptProvider>();
        provider.GetSkillCatalog().Returns(new[] { new AiSkillCatalogItem("bad", "坏条目", "不可读"), new AiSkillCatalogItem("good", "正常", "可读") });
        provider.GetRawContent("bad").Returns(_ => throw new IOException("读取被拒绝"));
        provider.GetRawContent("good").Returns("# 正常规则");
        var catalog = new AnalysisRuleCatalog(provider, Store);
        var items = catalog.GetCatalog();
        var bad = Assert.Single(items.Where(item => item.Id == "bad"));
        Assert.False(bad.Readable);
        Assert.False(string.IsNullOrWhiteSpace(bad.Error));
        Assert.Empty(bad.Version);
        Assert.Empty(bad.Digest);
        Assert.Contains(items, item => item.Id == "good" && item.Readable);
        Assert.Throws<InvalidDataException>(() => catalog.Read("bad"));
        Assert.Equal("# 正常规则", catalog.Read("good").Content);
    }

    [Fact]
    public void Provider正文缺失不能伪造成功读取()
    {
        var provider = Substitute.For<ISkillPromptProvider>();
        provider.GetSkillCatalog().Returns(new[] { new AiSkillCatalogItem("missing-body", "缺正文", "仍在目录") });
        provider.GetRawContent("missing-body").Returns((string?)null);
        var catalog = new AnalysisRuleCatalog(provider, Store);
        Assert.False(Assert.Single(catalog.GetCatalog()).Readable);
        Assert.Throws<InvalidDataException>(() => catalog.Read("missing-body"));
    }

    [Fact]
    public void 源码包仅标记依赖不返回或运行源码且不写入现有规则()
    {
        const string source = "此内容不是可执行的检测器，也不得尝试编译运行。";
        var saved = Store.Save(Package() with { Source = source });
        var files = Directory.GetFiles(UserRoot, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes);
        var catalog = Catalog;
        var descriptor = Assert.Single(catalog.GetCatalog().Where(item => item.Id == saved.Id));
        var read = catalog.Read(saved.Id);
        Assert.True(descriptor.RequiresSource);
        Assert.DoesNotContain(source, read.Content);
        Assert.Equal(files.Keys.Order(), Directory.GetFiles(UserRoot, "*", SearchOption.AllDirectories).Order());
        foreach (var file in files) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
    }

    [Fact]
    public void 包显式版本不变时仍使用实际新摘要()
    {
        var store = Store;
        var first = store.Save(Package());
        var catalog = Catalog;
        var old = catalog.Read(first.Id);
        var updated = store.Save(first with { Rules = [Rule() with { Preserve = ["new-keep/**"] }] }, true);
        var current = catalog.Read(first.Id);
        Assert.Equal(first.Version, current.Version);
        Assert.NotEqual(old.Digest, current.Digest);
        Assert.Equal(updated.Digest, current.Digest);
        Assert.Equal(new[] { "new-keep/**" }, current.Preserve);
    }

    [Fact]
    public void Provider目录失败仍能读包并标记覆盖缺口()
    {
        var saved = Store.Save(Package());
        var provider = Substitute.For<ISkillPromptProvider>();
        provider.GetSkillCatalog().Returns(_ => throw new IOException("目录读取失败"));
        var catalog = new AnalysisRuleCatalog(provider, Store);
        Assert.Contains(catalog.GetCatalog(), item => item.Id == saved.Id && item.Readable);
        Assert.Contains("Provider 目录读取失败", catalog.Warning);
        Assert.Equal(saved.Digest, catalog.Read(saved.Id).Digest);
    }

    [Fact]
    public void 明确四阶段规则只读取对应阶段但始终附带约束()
    {
        Markdown("staged", "<!-- diskpilot-analysis-stages:v1 -->\n# 专用规则\n\n全局保留说明\n\n## discovery\n定位步骤\n\n## analysis\n核实依据\n\n## handling\n处理方法\n\n## constraints\n必须保留用户资料\n");
        var read = Catalog.Read("staged", "analysis");
        Assert.Equal("analysis", read.Stage);
        Assert.Contains("核实依据", read.Content);
        Assert.Contains("必须保留用户资料", read.Content);
        Assert.Contains("全局保留说明", read.Content);
        Assert.DoesNotContain("定位步骤", read.Content);
        Assert.DoesNotContain("处理方法", read.Content);
    }

    private void Markdown(string id, string content)
    {
        var directory = Path.Combine(BuiltInRoot, id);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "SKILL.md"), content, new UTF8Encoding(false));
    }

    private static CleanupSkillRule Rule() => new()
    {
        Id = "cache", Path = "cache/**", Description = "可再生缓存", Risk = "重新下载",
        Sources = ["用户确认"], Preserve = ["keep/**"], Excludes = ["locked/**"]
    };

    private static CleanupSkillPackage Package() => new()
    {
        Id = "sample-cleanup", Title = "缓存规则", Description = "先核实再建议", SkillMarkdown = "# 缓存规则\n禁止删除个人文件。", Rules = [Rule()]
    };

    public void Dispose()
    {
        // 仅清理本测试实例创建的隔离目录，不接触仓库或其他会话产物。
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}