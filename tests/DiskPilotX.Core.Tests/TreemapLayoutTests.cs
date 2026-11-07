using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Treemap;

namespace DiskPilotX.Core.Tests;

public class TreemapLayoutTests
{
    [Fact]
    public void 透明目录复用内容矩形而不重复预留边框()
    {
        var root = Directory("根");
        var child = Directory("目录", root);
        File("缓存.nvph", child, 100);
        child.Size = root.Size = 100;
        var nodes = new SquarifiedTreemapLayout().ComputeLayout(root, 800, 600, 8);
        var rootNode = nodes.Single(n => n.Entry == root);
        var childNode = nodes.Single(n => n.Entry == child);
        var file = nodes.Single(n => !n.Entry.IsDirectory);
        Assert.Equal(0f, childNode.X - rootNode.X);
        Assert.Equal(0f, file.X - childNode.X);
        Assert.Equal(0f, childNode.X + childNode.Width - file.X - file.Width);
        Assert.False(childNode.ShowDirectoryFrame);
        Assert.Null(childNode.Label);
        Assert.All(nodes.Where(n => n.Entry.IsDirectory), n => Assert.Equal("#303030", n.ColorHex));
    }

    [Fact]
    public void FileColors_DependOnExtension_InsteadOfDirectoryDepth()
    {
        var root = Directory("根");
        var child = Directory("目录", root);
        var first = File("一.NVPH", root, 100);
        var second = File("二.nvph", child, 100);
        var script = File("脚本.js", child, 100);
        child.Size = 200;
        root.Size = 300;
        var nodes = new SquarifiedTreemapLayout().ComputeLayout(root, 1000, 700, 8);
        var color = nodes.Single(n => n.Entry == first).ColorHex;
        Assert.Equal(color, nodes.Single(n => n.Entry == second).ColorHex);
        Assert.NotEqual(color, nodes.Single(n => n.Entry == script).ColorHex);
        var drilled = new SquarifiedTreemapLayout().ComputeLayout(child, 1000, 700, 8);
        Assert.Equal(color, drilled.Single(n => n.Entry == second).ColorHex);
    }

    [Fact]
    public void EmptyDirectories_AreExcluded()
    {
        var root = Directory("根");
        var empty = Directory("空目录", root);
        empty.Size = 0;
        File("a.js", root, 100);
        root.Size = 100;

        var nodes = new SquarifiedTreemapLayout().ComputeLayout(root, 800, 600, 8);

        Assert.DoesNotContain(nodes, n => n.Entry.Name == "空目录");
        Assert.Contains(nodes, n => n.Entry.Name == "a.js");
    }

    [Fact]
    public void Zoom_HeaderHeightIsScreenConstant()
    {
        var root = Directory("根");
        var child = Directory("子", root);
        File("a.js", child, 100);
        child.Size = root.Size = 100;

        var engine = new SquarifiedTreemapLayout();
        var z1 = engine.ComputeLayout(root, 800, 600, 8, zoom: 1f);
        var z2 = engine.ComputeLayout(root, 800, 600, 8, zoom: 2f);

        // 紧凑标题的屏幕高度恒定，缩放仅折算逻辑坐标，不按层级扩大。
        float h1 = z1.Single(n => n.Entry.Name == "子").Y - z1[0].Y;
        float h2 = z2.Single(n => n.Entry.Name == "子").Y - z2[0].Y;
        Assert.Equal(TreemapLabelPolicy.DirectoryTitleScreenHeight, h1, 1);
        Assert.Equal(TreemapLabelPolicy.DirectoryTitleScreenHeight / 2, h2, 1);
    }

    [Fact]
    public void 大量小文件全部保留真实节点而不伪造父目录()
    {
        var root = Directory("根");
        for (int i = 0; i < 600; i++) File($"文件{i}.js", root, 100);
        root.Size = 60000;
        var nodes = new SquarifiedTreemapLayout().ComputeLayout(root, 1200, 800, 64);
        Assert.Equal(601, nodes.Count);
        Assert.DoesNotContain(nodes, node => node.AggregatedCount > 0);
        Assert.Equal(60000, nodes.Where(node => !node.Entry.IsDirectory).Sum(node => node.Entry.Size));
        Assert.Single(nodes.Where(node => ReferenceEquals(node.Entry, root)));
        var first = nodes.First(n => n.Entry.Name == "文件0.js");
        Assert.True(first.Width * first.Height < 1200 * 800 / 300f);
    }

    [Fact]
    public void FilePalette_UsesDistinctMeaningfulTypesAndKeepsDirectoryTitlesNeutral()
    {
        var root = Directory("根");
        foreach (string ext in new[] { ".png", ".mp4", ".dll", ".pdb", ".exe", ".js", ".pdf", ".zip", ".tmp", ".bin" })
            File("文件" + ext, root, 100);
        root.Size = 1000;
        var nodes = new SquarifiedTreemapLayout().ComputeLayout(root, 1000, 600, 64);
        TreemapColorContext.Create(root).Apply(nodes);
        Assert.Equal(10, nodes.Where(n => !n.Entry.IsDirectory).Select(n => n.ColorHex).Distinct().Count());
        Assert.All(nodes.Where(n => n.Entry.IsDirectory), n => Assert.Equal("#303030", n.ColorHex));
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.25f)]
    [InlineData(2f)]
    [InlineData(4f)]
    [InlineData(8f)]
    public void LargeLayout_KeepsEverySiblingAndItsContentsPastFiveThousandNodes(float zoom)
    {
        var root = Directory("根");
        for (int i = 0; i < 128; i++)
        {
            var directory = Directory($"目录{i}", root);
            for (int j = 0; j < 64; j++) File($"文件{j}.dll", directory, 100);
            directory.Size = 6400;
        }
        root.Size = 128 * 6400;

        var nodes = new SquarifiedTreemapLayout().ComputeLayout(root, 1600, 900, 64, zoom);

        Assert.True(nodes.Count > 5000, $"节点数不应截断为{nodes.Count}");
        foreach (var directory in root.Children)
        {
            Assert.Single(nodes.Where(n => ReferenceEquals(n.Entry, directory)));
            Assert.Equal(64, nodes.Count(n => ReferenceEquals(n.Entry.Parent, directory)));
        }
        // 一级目录必须铺满根内容区，不能以父目录底色掩盖丢失的同级区域。
        double area = nodes.Where(n => n.Depth == 1).Sum(n => (double)n.Width * n.Height);
        var rootNode = nodes.Single(n => ReferenceEquals(n.Entry, root));
        double expected = rootNode.Width * (rootNode.Height - (rootNode.HeaderHeight ?? 0));
        Assert.InRange(area / expected, 0.99999, 1.00001);
    }

    [Fact]
    public void 小目录透明且放大优先展示文件名称()
    {
        var root = Directory("根");
        for (int i = 0; i < 128; i++)
        {
            var child = Directory($"目录{i}", root);
            File("文件.dll", child, 100);
            child.Size = 100;
        }
        root.Size = 12800;
        var engine = new SquarifiedTreemapLayout();
        var overview = engine.ComputeLayout(root, 1200, 800, 64, 1);
        var zoomed = engine.ComputeLayout(root, 1200, 800, 64, 2);
        var small = overview.First(n => n.Depth == 1 && n.Width < 104 && n.Height > 30);
        Assert.Null(small.Label);
        var file = overview.Single(n => ReferenceEquals(n.Entry.Parent, small.Entry));
        Assert.Equal(small.Y, file.Y);
        Assert.False(small.ShowDirectoryFrame);
        Assert.True(zoomed.Count(n => !n.Entry.IsDirectory && n.Label is not null)
            >= overview.Count(n => !n.Entry.IsDirectory && n.Label is not null));
        Assert.Contains(overview, n => !n.Entry.IsDirectory && n.Label is not null);
        Assert.Equal(128, overview.Count(n => !n.Entry.IsDirectory));
    }

    [Fact]
    public void 文字可读时全景文件也有名称而非强制隐藏()
    {
        var root = Directory("根");
        for (int i = 0; i < 64; i++) File($"文件{i}.dll", root, 100);
        root.Size = 6400;
        var engine = new SquarifiedTreemapLayout();
        var nodes = engine.ComputeLayout(root, 800, 600, 64);
        Assert.All(nodes.Where(n => !n.Entry.IsDirectory), n => Assert.Equal(n.Entry.Name, n.Label));
        // 单文件目录用于验证大块仍有直接标注。
        var single = Directory("单文件目录");
        File("大文件.dll", single, 100);
        single.Size = 100;
        Assert.Equal("大文件.dll", engine.ComputeLayout(single, 800, 600, 64).Single(n => !n.Entry.IsDirectory).Label);
    }

    private static FileEntry Directory(string name, FileEntry? parent = null)
    {
        var entry = new FileEntry { Name = name, Path = name, IsDirectory = true, Parent = parent };
        parent?.Children.Add(entry);
        return entry;
    }

    private static FileEntry File(string name, FileEntry parent, long size)
    {
        var entry = new FileEntry { Name = name, Path = name, Extension = Path.GetExtension(name), Size = size, Parent = parent };
        parent.Children.Add(entry);
        return entry;
    }

    [Fact]
    public void LargeDirectories_AreNotCutOffByZoomDepthBudget()
    {
        // 有足够屏幕空间的目录应立即展开，不依赖硬性 zoom 深度预算。
        var root = Directory("根");
        var current = root;
        for (int i = 0; i < 8; i++)
        {
            var dir = Directory($"层{i}", current);
            File($"f{i}.js", dir, 100);
            dir.Size = 100;
            current = dir;
        }
        root.Size = 800;

        var engine = new SquarifiedTreemapLayout();
        var lowZoom = engine.ComputeLayout(root, 800, 600, 8, zoom: 1f);
        var highZoom = engine.ComputeLayout(root, 800, 600, 8, zoom: 8f);

        Assert.Equal(8, lowZoom.Max(n => n.Depth));
        Assert.True(highZoom.Count >= lowZoom.Count);
    }
}
