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
}
