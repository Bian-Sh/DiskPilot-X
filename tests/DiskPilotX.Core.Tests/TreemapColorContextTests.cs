using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Treemap;

namespace DiskPilotX.Core.Tests;

public class TreemapColorContextTests
{
    [Fact]
    public void CommonTypes_HaveDistinctStableColors()
    {
        var files = new[] { ".dll", ".exe", ".pdb", ".png" }
            .Select(extension => new FileEntry { Name = "文件" + extension, Extension = extension, Size = 100 }).ToArray();
        var root = new FileEntry { Name = "根", IsDirectory = true, Children = files.ToList() };
        foreach (var file in files) file.Parent = root;
        var context = TreemapColorContext.Create(root);
        var nodes = files.Select(file => new TreemapNode { Entry = file }).ToArray();
        context.Apply(nodes);
        Assert.Equal(4, nodes.Select(node => node.ColorHex).Distinct().Count());
        Assert.All(nodes, node => Assert.Equal(TreemapColorContext.GetFallbackFileColor(node.Entry), node.ColorHex));
        Assert.Same(context.FileTypes, context.FileTypes);
        Assert.Equal(400L, context.FileTypes.Sum(item => item.SizeBytes));
    }
    [Fact]
    public void MajorTypes_AreSelectedByBytes_WithUniqueColorsAndExplicitTail()
    {
        var root = Directory("根");
        for (int i = 0; i < 20; i++) File(root, $"文件{i}.custom{i:D2}", 100 + i);
        var context = TreemapColorContext.Create(root);
        var legend = context.FileTypes;
        Assert.Equal(13, legend.Count);
        Assert.Equal(legend.Count, legend.Select(item => item.ColorHex).Distinct().Count());
        Assert.Contains(legend, item => item.Key == ".custom19");
        Assert.DoesNotContain(legend, item => item.Key == ".custom00");
        var tail = Assert.Single(legend.Where(item => item.Key == "other-types"));
        Assert.Equal(8, tail.FileCount);
        Assert.Equal(Enumerable.Range(0, 8).Sum(i => 100L + i), tail.SizeBytes);
        Assert.Equal(root.Children.Sum(file => file.Size), legend.Sum(item => item.SizeBytes));
        var nodes = root.Children.Select(file => new TreemapNode { Entry = file }).ToArray();
        context.Apply(nodes);
        Assert.All(nodes.Take(8), node => Assert.Equal(tail.ColorHex, node.ColorHex));
    }

    [Fact]
    public void TypeKeys_NormalizeCaseAndFallbackNames_AndExcludeNonPositiveFiles()
    {
        var root = Directory("根");
        File(root, "一.DLL", 10).Extension = ".DLL";
        File(root, "二.dll", 20);
        File(root, "无扩展名", 30);
        File(root, "空值.PNG", 40).Extension = " ";
        File(root, "空文件.tmp", 0);
        File(root, "异常文件.tmp", -100);
        var legend = TreemapColorContext.Create(root).FileTypes;
        Assert.Equal(3, legend.Count);
        Assert.Equal(30, legend.Single(item => item.Key == ".dll").SizeBytes);
        Assert.Equal(2, legend.Single(item => item.Key == ".dll").FileCount);
        Assert.Contains(legend, item => item.Key == "no-extension");
        Assert.Contains(legend, item => item.Key == ".png");
        Assert.Equal(100, legend.Sum(item => item.SizeBytes));
        Assert.Equal(4, legend.Sum(item => item.FileCount));
    }

    [Fact]
    public void Apply_UsesTheSameScanMapForSubsets_WithoutChangingGeometryOrLabels()
    {
        var root = Directory("根");
        var a = File(root, "主文件.weird", 1000);
        var b = File(root, "次文件.weird", 100);
        var context = TreemapColorContext.Create(root);
        var first = new TreemapNode { Entry = a, X = 10, Y = 20, Width = 30, Height = 40, Label = "名称", SizeLabel = "容量", Depth = 3, IsVisible = false };
        context.Apply([first]);
        string color = first.ColorHex;
        var second = new TreemapNode { Entry = b };
        context.Apply([second]);
        Assert.Equal(color, second.ColorHex);
        context.Apply([first]);
        Assert.Equal(color, first.ColorHex);
        Assert.Equal((10f, 20f, 30f, 40f, 3, false, "名称", "容量"),
            (first.X, first.Y, first.Width, first.Height, first.Depth, first.IsVisible, first.Label, first.SizeLabel));
    }

    [Fact]
    public void DirectoryNamesDoNotChangeFileTypeColors_AndTitlesStayNeutral()
    {
        var root = Directory("src"); var cache = Directory("cache", root);
        var a = File(root, "程序.dll", 10); var b = File(cache, "其他.dll", 20);
        var context = TreemapColorContext.Create(root);
        var nodes = new[] { root, cache, a, b }.Select(entry => new TreemapNode { Entry = entry }).ToArray();
        context.Apply(nodes);
        Assert.All(nodes.Take(2), node => Assert.Equal("#303030", node.ColorHex));
        Assert.Equal(nodes[2].ColorHex, nodes[3].ColorHex);
        Assert.Null(typeof(TreemapNode).GetProperty("AccentColorHex"));
    }

    [Fact]
    public void AggregatedTailIsContent_NotAnArtificialDirectoryTitle()
    {
        var root = Directory("cache");
        File(root, "缓存.bin", 100);
        var tail = new TreemapNode { Entry = root, AggregatedCount = 100, AggregatedSize = 50 };
        var context = TreemapColorContext.Create(root);
        context.Apply([tail]);
        Assert.NotEqual("#303030", tail.ColorHex);
        Assert.Equal(100, tail.AggregatedCount);
        Assert.Equal(50, tail.AggregatedSize);
    }

    [Fact]
    public void EmptyAndSingleFileRoots_HaveConsistentLegends_AndFixedColorsAcrossScans()
    {
        var empty = TreemapColorContext.Create(Directory("空根"));
        Assert.Empty(empty.FileTypes);
        Assert.Empty(empty.FileTypes);
        var file = new FileEntry { Name = "程序.dll", Size = 73 };
        var single = TreemapColorContext.Create(file);
        Assert.Equal(73, Assert.Single(single.FileTypes).SizeBytes);
        var otherRoot = Directory("另一次扫描");
        File(otherRoot, "主类型.exe", 1000);
        var same = File(otherRoot, "程序.dll", 1);
        var different = TreemapColorContext.Create(otherRoot);
        var a = new TreemapNode { Entry = file }; var b = new TreemapNode { Entry = same };
        single.Apply([a]); different.Apply([b]);
        Assert.Equal(a.ColorHex, b.ColorHex);
    }

    [Fact]
    public void DirectoriesUseObjectIdentity_NotPaths()
    {
        var root = Directory("任意根");
        var code = Directory("src", root);
        File(code, "程序.cs", 100);
        var context = TreemapColorContext.Create(root);
        var real = new TreemapNode { Entry = code };
        var samePath = new TreemapNode { Entry = new FileEntry { Name = "src", Path = code.Path, IsDirectory = true } };
        context.Apply([real, samePath]);
        Assert.NotEqual(real.ContentColorHex, samePath.ContentColorHex);
        var parent = new TreemapNode { Entry = root };
        context.Apply([parent, real]);
        Assert.Equal(parent.ContentColorHex, real.ContentColorHex);
    }

    [Fact]
    public void RefreshAfterCleanup_UpdatesTotalsButDoesNotPromoteOrRecolorTypes()
    {
        var root = Directory("根");
        for (int i = 0; i < 20; i++) File(root, $"文件{i}.custom{i:D2}", 100 + i);
        var context = TreemapColorContext.Create(root);
        var original = context.FileTypes.ToDictionary(item => item.Key, item => item.ColorHex);
        root.Children.RemoveAt(19);
        var refreshed = context.Refresh(root);
        var legend = refreshed.FileTypes;
        Assert.DoesNotContain(legend, item => item.Key == ".custom07");
        Assert.DoesNotContain(legend, item => item.Key == ".custom19");
        Assert.All(legend, item => Assert.Equal(original[item.Key], item.ColorHex));
        Assert.Equal(19, legend.Sum(item => item.FileCount));
        Assert.Equal(root.Children.Sum(entry => entry.Size), legend.Sum(item => item.SizeBytes));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AggregatedLayoutTail_KeepsItsRealDominantExtensionColor(bool mixed)
    {
        var root = Directory("根");
        for (int i = 0; i < 1024; i++)
            File(root, $"文件{i}" + (mixed && i < 128 ? ".exe" : ".dll"), mixed && i < 128 ? 1000 : 100);
        root.Size = root.Children.Sum(entry => entry.Size);
        var context = TreemapColorContext.Create(root);
        var nodes = new SquarifiedTreemapLayout().ComputeLayout(root, 1200, 800, 64);
        context.Apply(nodes);
        var tail = Assert.Single(nodes.Where(node => node.AggregatedCount > 0));
        Assert.Equal(896, tail.AggregatedCount);
        Assert.Equal(context.FileTypes.Single(item => item.Key == ".dll").ColorHex, tail.ColorHex);
        if (mixed) Assert.NotEqual(nodes[0].ContentColorHex, tail.ColorHex);
    }

    [Fact]
    public void AggregatedLayoutTail_RespectsOriginalOrderWhenSizesTie()
    {
        var root = Directory("根");
        for (int i = 0; i < 128; i++) File(root, $"文件{i}.dll", 100);
        File(root, "最后文件.exe", 100); root.Size = 12900;
        var context = TreemapColorContext.Create(root);
        var nodes = new SquarifiedTreemapLayout().ComputeLayout(root, 1200, 800, 64);
        context.Apply(nodes);
        Assert.Equal(context.FileTypes.Single(item => item.Key == ".exe").ColorHex,
            Assert.Single(nodes.Where(node => node.AggregatedCount > 0)).ColorHex);
    }

    private static FileEntry Directory(string name, FileEntry? parent = null)
    {
        var entry = new FileEntry { Name = name, Path = (parent?.Path ?? "D:") + "/" + name, IsDirectory = true, Parent = parent };
        parent?.Children.Add(entry);
        return entry;
    }

    private static FileEntry File(FileEntry parent, string name, long size)
    {
        var entry = new FileEntry { Name = name, Path = parent.Path + "/" + name, Size = size, Parent = parent };
        parent.Children.Add(entry);
        return entry;
    }
}
