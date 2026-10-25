using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using SpaceMonger.Core.Services.Settings;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Agent;
using SpaceMonger.Core.Services.FileTree;
using SpaceMonger.Core.Services.Analysis;

namespace SpaceMonger.Core.Tests;

public class CompactToolResultTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public async Task 直接子项只返回相对路径和必要字段()
    {
        var result = await Run(new ListChildrenTool(new FileTreeQueryService()), new { path = @"C:\Scan" });
        result.GetProperty("path").GetString().Should().Be(@"C:\Scan");
        result.GetProperty("path_format").GetString().Should().Be("relative");
        var item = result.GetProperty("children")[0];
        item.GetProperty("path").GetString().Should().Be("Models");
        item.GetProperty("size_bytes").GetInt64().Should().Be(700);
        foreach (var redundant in new[] { "name", "extension", "last_modified", "flags" })
            item.TryGetProperty(redundant, out _).Should().BeFalse();
        result.GetProperty("truncated").GetBoolean().Should().BeFalse();
    }

    [Theory]
    [InlineData("list_children")]
    [InlineData("find_by_name")]
    [InlineData("find_large_files")]
    [InlineData("summarize_subtree")]
    public async Task 列表明确报告截断且详情可以按需读取(string name)
    {
        var tool = Tool(name);
        var result = await Run(tool, new { path = @"C:\Scan", under_path = @"C:\Scan", name = "o", max_results = 1, top_children = 1, details = true });
        result.GetProperty("truncated").GetBoolean().Should().BeTrue();
        var items = result.GetProperty(name switch { "list_children" => "children", "summarize_subtree" => "largest_children", _ => "results" });
        items.GetArrayLength().Should().Be(1);
        items[0].GetProperty("last_modified").GetString().Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("find_by_name")]
    [InlineData("find_large_files")]
    public async Task 深层搜索结果共享根路径且保留大小排序(string name)
    {
        var result = await Run(Tool(name), new { name = "o", max_results = 10 });
        result.GetProperty("base_path").GetString().Should().Be(@"C:\Scan");
        var paths = result.GetProperty("results").EnumerateArray().Select(e => e.GetProperty("path").GetString()).ToArray();
        paths.Should().Contain("Models/hero.fbx").And.Contain("Models/villain.obj");
        paths.Should().OnlyContain(p => !Path.IsPathFullyQualified(p!));
    }

    [Fact]
    public async Task 异常标志不会被精简丢失()
    {
        var session = FileTreeQueryServiceTests.BuildSession();
        session.RootEntry!.Children[0].IsAccessDenied = true;
        session.RootEntry.Children[0].IsCloudPlaceholder = true;
        var result = await Run(new ListChildrenTool(new FileTreeQueryService()), new { path = @"C:\Scan" }, session);
        var flags = result.GetProperty("children")[0].GetProperty("flags");
        flags.GetProperty("is_access_denied").GetBoolean().Should().BeTrue();
        flags.GetProperty("is_cloud_placeholder").GetBoolean().Should().BeTrue();
        flags.TryGetProperty("is_reparse_point", out _).Should().BeFalse();
    }

    [Fact]
    public async Task 树形结果共享目录并标出原始排名()
    {
        var result = await Run(new FindLargeFilesTool(new FileTreeQueryService()), new { format = "tree", max_results = 2 });
        result.TryGetProperty("results", out _).Should().BeFalse();
        result.GetProperty("tree").GetString().Should().Contain("Models").And.Contain("hero.fbx").And.Contain("villain.obj").And.Contain("#1").And.Contain("#2");
        result.GetProperty("truncated").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task 树形无损折叠深目录且保留同名文件和异常()
    {
        var session = Session(@"D:\项目", ("a/b/c/同名.bin", 8L), ("a/b/c/other/同名.bin", 7L));
        session.RootEntry!.Children[0].IsReparsePoint = true;
        var result = await Run(new FindLargeFilesTool(new FileTreeQueryService()), new { format = "tree", details = true }, session);
        var tree = result.GetProperty("tree").GetString()!;
        tree.Should().Contain("a/b/c").And.Contain("other/同名.bin").And.Contain("is_reparse_point");
        tree.Split("同名.bin").Should().HaveCount(3);
    }

    [Theory]
    [InlineData(@"D:\项目", "多层/文件.txt")]
    [InlineData(@"C:\", "目录/文件.txt")]
    [InlineData(@"\\server\share\", "目录/文件.txt")]
    public async Task 中文盘符根和UNC路径可完整还原(string root, string relative)
    {
        var session = Session(root, (relative, 3L));
        var result = await Run(new FindLargeFilesTool(new FileTreeQueryService()), new { }, session);
        var actual = result.GetProperty("results")[0].GetProperty("path").GetString()!;
        actual.Should().Be(relative);
        Path.GetFullPath(Path.Combine(result.GetProperty("base_path").GetString()!, actual)).Should().Be(Path.GetFullPath(session.RootEntry!.Children[0].Path));
    }

    [Fact]
    public async Task 空结果和非法格式有明确结果()
    {
        var empty = await Run(new FindByNameTool(new FileTreeQueryService()), new { name = "不存在" });
        empty.GetProperty("count").GetInt32().Should().Be(0);
        empty.GetProperty("truncated").GetBoolean().Should().BeFalse();
        var invalid = await Run(new ListChildrenTool(new FileTreeQueryService()), new { path = @"C:\Scan", format = "invalid" });
        invalid.GetProperty("ok").GetBoolean().Should().BeFalse();
        invalid.GetProperty("error").GetProperty("code").GetString().Should().Be("invalid_arguments");
    }

    [Fact]
    public async Task 返回路径可用于下一次定位查询()
    {
        var list = await Run(new ListChildrenTool(new FileTreeQueryService()), new { path = @"C:\Scan" });
        var absolute = Path.Combine(list.GetProperty("path").GetString()!, list.GetProperty("children")[0].GetProperty("path").GetString()!);
        var detail = await Run(new FindByPathTool(new FileTreeQueryService()), new { path = absolute });
        detail.GetProperty("result").GetProperty("path").GetString().Should().Be(@"C:\Scan\Models");
        detail.GetProperty("result").GetProperty("last_modified").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void 分析证据共享基准路径()
    {
        var session = FileTreeQueryServiceTests.BuildSession();
        var evidence = new RecommendationEngine().BuildEvidence(session);
        evidence.Should().Contain(@"BASE_PATH: C:\Scan").And.Contain("Models/hero.fbx|400");
        evidence.Should().NotContain(@"C:\Scan\Models\hero.fbx");
    }

    [Fact]
    public async Task 固定长路径样例明显缩短且树形进一步合并前缀()
    {
        var root = @"D:\AppData\Visual Studio\Projects\spacemonger-next\work\cua-unity-flow\DemoUnityProject";
        var files = Enumerable.Range(0, 80).Select(i => ($"Library/PackageCache/deep/common/cache-{i:D3}.bin", 1000L - i)).ToArray();
        var session = Session(root, files);
        var tool = new FindLargeFilesTool(new FileTreeQueryService());
        var json = await Run(tool, new { under_path = root, max_results = 100 }, session);
        var tree = await Run(tool, new { under_path = root, max_results = 100, format = "tree" }, session);
        var old = JsonSerializer.Serialize(new { ok = true, under_path = root, count = files.Length,
            results = session.RootEntry!.Children.Select(e => new { path = e.Path, name = e.Name, size_bytes = e.Size,
                type = "file", child_count = 0, flags = new { is_reparse_point = false, is_access_denied = false, is_cloud_placeholder = false } }) });
        var compact = json.GetRawText();
        var treeText = tree.GetRawText();
        compact.Length.Should().BeLessThan((int)(old.Length * 0.55));
        treeText.Length.Should().BeLessThan(compact.Length);
        output.WriteLine($"固定样例：旧结果={old.Length} 字符，精简 JSON={compact.Length} 字符，tree={treeText.Length} 字符；分别减少 {1.0 - (double)compact.Length / old.Length:P1}、{1.0 - (double)treeText.Length / old.Length:P1}。这是字符数，不是 tokenizer 计数。");
    }

    [Theory]
    [InlineData("Models")]
    [InlineData(@"..\outside")]
    [InlineData(@"C:\outside")]
    public async Task 不会使用工作目录隐式解析相对路径或越界路径(string path)
    {
        var result = await Run(new FindByPathTool(new FileTreeQueryService()), new { path });
        result.GetProperty("ok").GetBoolean().Should().BeFalse();
        result.GetProperty("error").GetProperty("code").GetString().Should().Be("not_found");
    }

    [Theory]
    [InlineData("json")]
    [InlineData("tree")]
    public async Task 两种精简格式均保留白名单过滤(string format)
    {
        var temp = Directory.CreateTempSubdirectory("compact-tool-whitelist-");
        try
        {
            var hiddenPath = Directory.CreateDirectory(Path.Combine(temp.FullName, "Models")).FullName;
            File.WriteAllText(Path.Combine(hiddenPath, "hidden.bin"), "隐藏样例");
            File.WriteAllText(Path.Combine(temp.FullName, "visible.bin"), "可见样例");
            var session = Session(temp.FullName, ("Models/hidden.bin", 2L), ("visible.bin", 1L));
            var settings = Substitute.For<ISettingsService>();
            settings.LoadSettings().Returns(new AppSettings { AiConversationWhitelist = [new() { Path = hiddenPath }] });
            var query = new FileTreeQueryService(settings);
            var result = await Run(new FindLargeFilesTool(query, settings), new { format }, session);
            result.GetRawText().Should().NotContain("Models").And.NotContain("hidden.bin");
            result.GetProperty("count").GetInt32().Should().Be(1);
            var hidden = await Run(new ListChildrenTool(query, settings), new { path = hiddenPath, format }, session);
            hidden.GetProperty("error").GetProperty("code").GetString().Should().Be("hidden_by_whitelist");
        }
        finally { temp.Delete(true); }
    }

    private static IAgentTool Tool(string name) => name switch
    {
        "find_by_name" => new FindByNameTool(new FileTreeQueryService()),
        "find_large_files" => new FindLargeFilesTool(new FileTreeQueryService()),
        "summarize_subtree" => new SummarizeSubtreeTool(new FileTreeQueryService()),
        _ => new ListChildrenTool(new FileTreeQueryService())
    };

    private static Task<JsonElement> Run(IAgentTool tool, object args, ScanSession? session = null)
    {
        session ??= FileTreeQueryServiceTests.BuildSession();
        return tool.ExecuteAsync(new AgentContext(session, session.RootEntry!, null, null), JsonSerializer.SerializeToElement(args), default);
    }

    private static ScanSession Session(string rootPath, params (string Path, long Size)[] files)
    {
        var root = new FileEntry { Path = rootPath, Name = "根", IsDirectory = true };
        root.Children = files.Select(f => new FileEntry { Path = Path.Combine(rootPath, f.Path.Replace('/', '\\')), Name = Path.GetFileName(f.Path), Size = f.Size }).ToList();
        return new ScanSession { TargetPath = rootPath, RootEntry = root };
    }
}
