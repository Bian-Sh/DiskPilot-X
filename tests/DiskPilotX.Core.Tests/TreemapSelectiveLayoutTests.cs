using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Treemap;

namespace DiskPilotX.Core.Tests;

public class TreemapSelectiveLayoutTests
{
    [Fact]
    public void 六层单子目录不再逐层消耗文件内容面积()
    {
        var direct = Chain(0); var nested = Chain(6);
        var engine = new SquarifiedTreemapLayout();
        var first = engine.ComputeLayout(direct, 800, 600, 64);
        var second = engine.ComputeLayout(nested, 800, 600, 64);
        double Area(TreemapNode node) => (double)node.Width * node.Height;
        Assert.InRange(Area(second.Single(n => !n.Entry.IsDirectory)) / Area(first.Single(n => !n.Entry.IsDirectory)), .999, 1.001);
        Assert.InRange(second.Count(n => n.Entry.IsDirectory && n.Label is not null), 1, 3);
        Assert.Equal(8, second.Count);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(2f)]
    public void 小目录仍布局两个可分辨文件(float zoom)
    {
        var root = Folder("小目录");
        AddFile(root, "一.dll", 100); AddFile(root, "二.png", 100); root.Size = 200;
        var nodes = new SquarifiedTreemapLayout().ComputeLayout(root, 30, 30, 64, zoom);
        Assert.Equal(2, nodes.Count(n => !n.Entry.IsDirectory));
        Assert.Equal(200, nodes.Where(n => !n.Entry.IsDirectory).Sum(n => n.Entry.Size));
    }

    [Fact]
    public void 小文件长尾面积和字节保留且放大后恢复细节()
    {
        var root = Folder("根"); AddFile(root, "大文件.bin", 99000);
        for (int i = 0; i < 1000; i++) AddFile(root, $"小文件{i}.txt", 1);
        root.Size = 100000;
        var engine = new SquarifiedTreemapLayout();
        var low = engine.ComputeLayout(root, 100, 100, 64, 1);
        var high = engine.ComputeLayout(root, 100, 100, 64, 8);
        long Bytes(IEnumerable<TreemapNode> nodes) => nodes.Where(n => !n.Entry.IsDirectory || n.AggregatedCount > 0)
            .Sum(n => n.AggregatedSize ?? n.Entry.Size);
        Assert.Equal(root.Size, Bytes(low)); Assert.Equal(root.Size, Bytes(high));
        var tail = Assert.Single(low.Where(n => n.AggregatedCount > 0));
        Assert.Same(root, tail.Entry); Assert.Equal(1000, tail.AggregatedCount); Assert.Equal(1000, tail.AggregatedSize);
        Assert.True(high.Count(n => !n.Entry.IsDirectory) > low.Count(n => !n.Entry.IsDirectory));
        var content = low.Where(n => !n.Entry.IsDirectory || n.AggregatedCount > 0).ToArray();
        Assert.InRange(content.Sum(n => (double)n.Width * n.Height) / (96 * 96), .9999, 1.0001);
        Assert.Equal(1001, root.Children.Count);
    }

    [Fact]
    public void 放大缩小往返恢复相同布局且路径从未改挂()
    {
        var root = Folder("根");
        for (int i = 0; i < 80; i++)
        {
            var directory = Folder($"目录{i}", root);
            for (int j = 0; j < 8; j++) AddFile(directory, $"文件{j}.dll", 100);
            directory.Size = 800;
        }
        root.Size = 64000; var engine = new SquarifiedTreemapLayout();
        var before = engine.ComputeLayout(root, 800, 600, 64, 1);
        var expanded = engine.ComputeLayout(root, 800, 600, 64, 8);
        var after = engine.ComputeLayout(root, 800, 600, 64, 1);
        Assert.Equal(before.Select(n => (n.Entry, n.X, n.Y, n.Width, n.Height, n.Label, n.AggregatedCount)),
            after.Select(n => (n.Entry, n.X, n.Y, n.Width, n.Height, n.Label, n.AggregatedCount)));
        Assert.True(expanded.Count(n => n.Label is not null) > before.Count(n => n.Label is not null));
        foreach (var child in root.Children)
            Assert.All(child.Children, file => Assert.Same(child, file.Parent));
    }

    [Fact]
    public void 大分支保留标题小兄弟透明但保留文件()
    {
        var root = Folder(".worktrees");
        var large = Folder("add-s", root);
        for (int j = 0; j < 10; j++) AddFile(large, $"大{j}.dll", 115);
        large.Size = 1150;
        for (int i = 0; i < 40; i++)
        {
            var small = Folder($"兄弟{i}", root); AddFile(small, "文件.png", 26); small.Size = 26;
        }
        root.Size = 2190;
        var nodes = new SquarifiedTreemapLayout().ComputeLayout(root, 800, 400, 64);
        Assert.NotNull(nodes.Single(n => ReferenceEquals(n.Entry, large)).Label);
        Assert.True(nodes.Count(n => n.Entry.IsDirectory && n.Label is not null) <= 3);
        Assert.Equal(50, nodes.Count(n => !n.Entry.IsDirectory));
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(2f)]
    [InlineData(8f)]
    public void 不可读细枝的深链不逐层收费且所有叶子保持归属(float zoom)
    {
        var root = Folder("根"); var current = root;
        for (int i = 0; i < 12; i++)
        {
            AddFile(current, $"旁支{i}.txt", 10);
            current = Folder($"主分支{i}", current);
        }
        AddFile(current, "末级.dll", 10000);
        for (var d = current; d is not null; d = d.Parent) d.Size = d.Children.Sum(c => c.Size);
        var nodes = new SquarifiedTreemapLayout().ComputeLayout(root, 1000, 800, 64, zoom);
        // 每级旁支仅10字节，屏幕窄边不足8像素；不因名义上有两个孩子就重复收费。
        Assert.All(nodes.Where(n => n.HeaderHeight > 0), n => Assert.Same(root, n.Entry));
        Assert.All(nodes.Where(n => n.Entry.IsDirectory && n.Label is null), n =>
        {
            Assert.False(n.ShowDirectoryFrame); Assert.Equal(0f, n.HeaderHeight);
        });
        Assert.Equal(root.Size, nodes.Where(n => !n.Entry.IsDirectory || n.AggregatedCount > 0).Sum(n => n.AggregatedSize ?? n.Entry.Size));
    }

    [Theory]
    [InlineData(1000000000000L, 1, 1f)]
    [InlineData(1000000000000L, 3, 1f)]
    [InlineData(1000000000000L, 1000, 2f)]
    [InlineData(1099511627776L, 3, 8f)]
    [InlineData(1099511627776L, 1000, 8f)]
    [InlineData(9000000000000000000L, 1, 1f)]
    [InlineData(9000000000000000000L, 3, 8f)]
    public void 极端比例的一字节尾部仍有正面积归属(long bigSize, int count, float zoom)
    {
        var root = Folder("数值边界"); AddFile(root, "大文件.bin", bigSize);
        for (int i = 0; i < count; i++) AddFile(root, $"细小{i}.txt", 1);
        root.Size = bigSize + count;
        var nodes = new SquarifiedTreemapLayout().ComputeLayout(root, 1000, 650, 64, zoom);
        var leaves = nodes.Where(n => !n.Entry.IsDirectory || n.AggregatedCount > 0).ToArray();
        Assert.Equal(root.Size, leaves.Sum(n => n.AggregatedSize ?? n.Entry.Size));
        Assert.All(leaves, n => { Assert.True(n.Width > 0); Assert.True(n.Height > 0); });
        Assert.InRange(leaves.Sum(n => (double)n.Width * n.Height), 620000, 650000);
        if (count > 1)
        {
            var tail = Assert.Single(leaves.Where(n => n.AggregatedCount > 0));
            Assert.Same(root, tail.Entry); Assert.Equal(count, tail.AggregatedSize);
        }
        else Assert.Contains(leaves, n => n.Entry.Name == "细小0.txt");
    }

    internal static FileEntry Chain(int depth)
    {
        var root = Folder("根"); var current = root;
        for (int i = 0; i < depth; i++) current = Folder($"层{i}", current);
        AddFile(current, "文件.dll", 1000);
        for (var item = current; item is not null; item = item.Parent) item.Size = 1000;
        return root;
    }

    internal static FileEntry Folder(string name, FileEntry? parent = null)
    {
        var entry = new FileEntry { Name = name, Path = (parent?.Path ?? "D:") + "/" + name, IsDirectory = true, Parent = parent };
        parent?.Children.Add(entry); return entry;
    }

    internal static FileEntry AddFile(FileEntry parent, string name, long size)
    {
        var entry = new FileEntry { Name = name, Path = parent.Path + "/" + name, Parent = parent, Size = size, Extension = Path.GetExtension(name) };
        parent.Children.Add(entry); return entry;
    }
}
