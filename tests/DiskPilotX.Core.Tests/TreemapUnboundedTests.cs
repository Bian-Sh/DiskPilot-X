using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Treemap;

namespace DiskPilotX.Core.Tests;

public sealed class TreemapUnboundedTests
{
    [Theory]
    [InlineData(129)]
    [InlineData(161)]
    [InlineData(600)]
    [InlineData(4096)]
    [InlineData(20000)]
    public void 所有实际文件参与布局且不生成虚拟聚合块(int count)
    {
        var root = new FileEntry { Name = "outputs", Path = "D:/outputs", IsDirectory = true, Size = count * 1024L };
        for (var i = 0; i < count; i++)
            root.Children.Add(new FileEntry { Name = $"项目{i}.dll", Path = $"D:/outputs/项目{i}.dll", Parent = root, Size = 1024 });
        var nodes = new SquarifiedTreemapLayout().ComputeLayout(root, 1200, 800, 64);
        Assert.Equal(count + 1, nodes.Count);
        Assert.All(nodes, node => Assert.Equal(0, node.AggregatedCount));
        Assert.Single(nodes.Where(node => ReferenceEquals(node.Entry, root)));
        Assert.Equal(count, nodes.Select(node => node.Entry.Path).Where(path => path != root.Path).Distinct().Count());
        Assert.Equal(root.Size, nodes.Where(node => node.Depth == 1).Sum(node => node.Entry.Size));
    }

    [Fact]
    public void 多于128个目录仍保留各自路径和可展开内容()
    {
        var root = new FileEntry { Name = "outputs", Path = "D:/outputs", IsDirectory = true, Size = 161 * 1024L };
        for (var i = 0; i < 161; i++)
        {
            var child = new FileEntry { Name = $"发布包{i}", Path = $"D:/outputs/发布包{i}", Parent = root, IsDirectory = true, Size = 1024 };
            child.Children.Add(new FileEntry { Name = "实际文件.dll", Path = child.Path + "/实际文件.dll", Parent = child, Size = 1024 });
            root.Children.Add(child);
        }
        var nodes = new SquarifiedTreemapLayout().ComputeLayout(root, 1800, 1200, 64);
        var directories = nodes.Where(node => node.Depth == 1).ToArray();
        Assert.Equal(161, directories.Length);
        Assert.All(directories, node => Assert.Contains(node.Entry, root.Children));
        Assert.All(directories, node => Assert.NotEqual(root.Path, node.Entry.Path));
        Assert.Equal(161, nodes.Count(node => !node.Entry.IsDirectory));
        Assert.DoesNotContain(nodes, node => node.AggregatedCount > 0 || node.Label?.StartsWith("+") == true);
    }
}
