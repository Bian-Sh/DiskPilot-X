using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Treemap;

namespace DiskPilotX.Core.Tests;

public class TreemapLayoutTests
{
    [Fact]
    public void DirectoryChildren_UseOnePixelFrame()
    {
        var root = Directory("根");
        var child = Directory("目录", root);
        File("缓存.nvph", child, 100);
        child.Size = root.Size = 100;
        var nodes = new SquarifiedTreemapLayout().ComputeLayout(root, 800, 600, 8);
        var rootNode = nodes.Single(n => n.Entry == root);
        var childNode = nodes.Single(n => n.Entry == child);
        var file = nodes.Single(n => !n.Entry.IsDirectory);
        Assert.Equal(1f, childNode.X - rootNode.X);
        Assert.Equal(1f, file.X - childNode.X);
        Assert.Equal(1f, childNode.X + childNode.Width - file.X - file.Width);
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

        // 标题栏在屏幕上恒定 18px：逻辑高度 = 18/zoom。
        float h1 = z1.Single(n => n.Entry.Name == "子").Y - z1[0].Y;
        float h2 = z2.Single(n => n.Entry.Name == "子").Y - z2[0].Y;
        Assert.Equal(18f, h1, 1);
        Assert.Equal(9f, h2, 1);
    }

    [Fact]
    public void ManyTinyFiles_KeepVisibleColorAndAccountForTail()
    {
        var root = Directory("根");
        for (int i = 0; i < 600; i++) File($"文件{i}.js", root, 100);
        root.Size = 60000;
        var nodes = new SquarifiedTreemapLayout().ComputeLayout(root, 1200, 800, 64);
        // 不能只保留前 20 项并把它们错误放大为全部占用。
        Assert.True(nodes.Count > 21);
        var tail = Assert.Single(nodes.Where(n => n.AggregatedCount > 0));
        Assert.Equal(472, tail.AggregatedCount);
        Assert.Equal(47200, tail.AggregatedSize);
        Assert.Same(root, tail.Entry);
        var first = nodes.First(n => n.Entry.Name == "文件0.js");
        Assert.True(first.Width * first.Height < 1200 * 800 / 300f);
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
