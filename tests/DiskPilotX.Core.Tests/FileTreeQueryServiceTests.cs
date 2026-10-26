using System.Text.Json;
using FluentAssertions;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Agent;
using DiskPilotX.Core.Services.FileTree;

namespace DiskPilotX.Core.Tests;

public class FileTreeQueryServiceTests
{
    [Fact]
    public void FindByPath_NormalizesSlashAndCase()
    {
        var session = BuildSession();
        var service = new FileTreeQueryService();

        var result = service.FindByPath(session, @"c:/scan/models/HERO.FBX");

        result.Should().NotBeNull();
        result!.Path.Should().Be(@"C:\Scan\Models\hero.fbx");
    }

    [Fact]
    public void FindByName_SearchesWholeScannedTree()
    {
        var session = BuildSession();
        var service = new FileTreeQueryService();

        var results = service.FindByName(session, "model", maxResults: 10);

        results.Select(entry => entry.Path).Should().Contain(@"C:\Scan\Models");
    }

    [Fact]
    public void ListChildren_ReturnsDirectChildrenOrderedBySize()
    {
        var session = BuildSession();
        var service = new FileTreeQueryService();

        var results = service.ListChildren(session, @"C:\Scan", maxResults: 10);

        results.Select(entry => entry.Name).Should().Equal("Models", "Temp");
    }

    [Fact]
    public void SummarizeSubtree_CountsDescendantsAndLargestChildren()
    {
        var session = BuildSession();
        var service = new FileTreeQueryService();

        var summary = service.SummarizeSubtree(session, @"C:\Scan\Models", topChildren: 2);

        summary.SizeBytes.Should().Be(700);
        summary.FileCount.Should().Be(2);
        summary.DirectoryCount.Should().Be(1);
        summary.LargestChildren.Select(entry => entry.Name).Should().Equal("hero.fbx", "villain.obj");
    }

    [Fact]
    public void FindLargeFiles_SearchesUnderOptionalSubtree()
    {
        var session = BuildSession();
        var service = new FileTreeQueryService();

        var results = service.FindLargeFiles(session, @"C:\Scan\Models", maxResults: 2, minSizeBytes: 250);

        results.Select(entry => entry.Name).Should().Equal("hero.fbx", "villain.obj");
    }

    internal static ScanSession BuildSession()
    {
        var root = Directory(@"C:\Scan", "Scan", 900,
            Directory(@"C:\Scan\Models", "Models", 700,
                File(@"C:\Scan\Models\hero.fbx", "hero.fbx", 400, ".fbx"),
                File(@"C:\Scan\Models\villain.obj", "villain.obj", 300, ".obj")),
            Directory(@"C:\Scan\Temp", "Temp", 200,
                File(@"C:\Scan\Temp\cache.bin", "cache.bin", 200, ".bin")));

        return new ScanSession
        {
            TargetPath = root.Path,
            RootEntry = root,
            TotalSize = root.Size,
            TotalFiles = 3,
            TotalFolders = 3
        };
    }

    private static FileEntry Directory(string path, string name, long size, params FileEntry[] children)
    {
        var entry = new FileEntry
        {
            Path = path,
            Name = name,
            Size = size,
            IsDirectory = true,
            LastModified = new DateTime(2026, 6, 20, 10, 0, 0, DateTimeKind.Utc)
        };

        foreach (var child in children)
        {
            child.Parent = entry;
            child.Depth = entry.Depth + 1;
            entry.Children.Add(child);
        }

        return entry;
    }

    private static FileEntry File(string path, string name, long size, string extension)
    {
        return new FileEntry
        {
            Path = path,
            Name = name,
            Size = size,
            Extension = extension,
            IsDirectory = false,
            LastModified = new DateTime(2026, 6, 20, 11, 0, 0, DateTimeKind.Utc)
        };
    }
}

public class FileTreeAgentToolTests
{
    [Fact]
    public async Task FindByPathTool_ReturnsStructuredJsonResult()
    {
        var session = FileTreeQueryServiceTests.BuildSession();
        var tool = new FindByPathTool(new FileTreeQueryService());
        var arguments = JsonSerializer.SerializeToElement(new { path = @"c:\scan\models" });

        var result = await tool.ExecuteAsync(new AgentContext(session, session.RootEntry!, null, null), arguments, CancellationToken.None);

        result.GetProperty("ok").GetBoolean().Should().BeTrue();
        result.GetProperty("result").GetProperty("path").GetString().Should().Be(@"C:\Scan\Models");
        tool.RiskLevel.Should().Be(ToolRiskLevel.ReadOnly);
    }

    [Fact]
    public async Task ListChildrenTool_ReturnsStructuredErrorForFilePath()
    {
        var session = FileTreeQueryServiceTests.BuildSession();
        var tool = new ListChildrenTool(new FileTreeQueryService());
        var arguments = JsonSerializer.SerializeToElement(new { path = @"C:\Scan\Models\hero.fbx" });

        var result = await tool.ExecuteAsync(new AgentContext(session, session.RootEntry!, null, null), arguments, CancellationToken.None);

        result.GetProperty("ok").GetBoolean().Should().BeFalse();
        result.GetProperty("error").GetProperty("code").GetString().Should().Be("not_directory");
    }

    [Fact]
    public async Task FindLargeFilesTool_ReturnsLargestFilesAsJson()
    {
        var session = FileTreeQueryServiceTests.BuildSession();
        var tool = new FindLargeFilesTool(new FileTreeQueryService());
        var arguments = JsonSerializer.SerializeToElement(new { under_path = @"C:\Scan", max_results = 2 });

        var result = await tool.ExecuteAsync(new AgentContext(session, session.RootEntry!, null, null), arguments, CancellationToken.None);

        result.GetProperty("ok").GetBoolean().Should().BeTrue();
        result.GetProperty("results").EnumerateArray().Select(item => item.GetProperty("path").GetString())
            .Should().Equal("Models/hero.fbx", "Models/villain.obj");
    }
}
