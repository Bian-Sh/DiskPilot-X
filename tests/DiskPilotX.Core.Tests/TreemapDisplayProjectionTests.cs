using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Treemap;

namespace DiskPilotX.Core.Tests;

public class TreemapDisplayProjectionTests
{
    [Theory]
    [InlineData(1, 1f)]
    [InlineData(3, 1f)]
    [InlineData(6, 1f)]
    [InlineData(1, 8f)]
    [InlineData(3, 8f)]
    [InlineData(6, 8f)]
    [InlineData(64, 1f)]
    [InlineData(64, 8f)]
    public void 各上限与缩放均保留真实容量和层级且不裁剪源树(int depth, float zoom)
    {
        var root = Tree();
        var all = Descendants(root).ToArray();
        var snapshot = all.Select(e => (e, e.Parent, e.Size, e.Children.Count)).ToArray();
        var nodes = new SquarifiedTreemapLayout().ComputeLayout(root, 1000, 650, depth, zoom);
        Assert.Equal(root.Size, nodes.Where(n => n.IsContentLeaf).Sum(n => n.AggregatedSize ?? n.Entry.Size));
        Assert.All(nodes, n =>
        {
            Assert.InRange(n.Depth, 0, depth);
            Assert.True(n.Width > 0 && n.Height > 0);
            if (n.AggregatedCount == 0)
            {
                int actual = 0; var e = n.Entry;
                while (e != root) { e = e.Parent!; actual++; }
                Assert.Equal(actual, n.Depth);
            }
            if (!n.Entry.IsDirectory) Assert.Null(n.HeaderHeight);
            if (n.Entry.IsDirectory && !n.ShowDirectoryFrame) Assert.Equal(0f, n.HeaderHeight);
            if (n.Depth == depth) Assert.True(n.IsContentLeaf);
        });
        Assert.Equal(snapshot, all.Select(e => (e, e.Parent, e.Size, e.Children.Count)));
        Assert.Equal(depth < 8, nodes.Any(n => n.Entry.IsDirectory && n.IsContentLeaf && n.AggregatedCount == 0));
    }

    [Fact]
    public void 手动深度相对新根重新计层且终止块不是聚合父目录()
    {
        var root = Tree().Children[0];
        var nodes = new SquarifiedTreemapLayout().ComputeLayout(root, 1000, 650, 1, 8);
        Assert.Same(root, nodes[0].Entry); Assert.Equal(0, nodes[0].Depth);
        Assert.All(nodes.Where(n => n.Depth == 1), n => Assert.Same(root, n.Entry.Parent));
        Assert.All(nodes.Where(n => n.IsContentLeaf && n.Entry.IsDirectory), n =>
        { Assert.Equal(0, n.AggregatedCount); Assert.Null(n.AggregatedSize); Assert.Contains(n.Entry, root.Children); });
    }

    private static IEnumerable<FileEntry> Descendants(FileEntry e)
    {
        yield return e;
        foreach (var child in e.Children) foreach (var descendant in Descendants(child)) yield return descendant;
    }

    private static FileEntry Tree()
    {
        var root = new FileEntry { Name="冻结根", Path=@"D:\冻结根", IsDirectory=true };
        void Populate(FileEntry parent, int depth)
        {
            if (depth < 8)
            {
                var folder = new FileEntry { Name="目录"+depth, Path=parent.Path+"/目录"+depth, IsDirectory=true, Parent=parent };
                parent.Children.Add(folder); Populate(folder, depth+1);
            }
            for (int i=0;i<20;i++) parent.Children.Add(new FileEntry { Name=$"文件{i}.dll", Path=parent.Path+$"/文件{i}.dll", Parent=parent, Size=i==0?100000:1 });
            parent.Size=parent.Children.Sum(e=>e.Size);
        }
        Populate(root,0);return root;
    }
}
