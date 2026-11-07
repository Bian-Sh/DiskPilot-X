using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Treemap;

namespace DiskPilotX.Core.Tests;

public sealed class TreemapSharedFrameTests
{
    [Theory]
    [InlineData(1f)]
    [InlineData(1.25f)]
    [InlineData(2f)]
    [InlineData(8f)]
    public void 有标题的父子目录不逐层收缩内容侧边和底边(float zoom)
    {
        var root=new FileEntry{Name="根",Path=@"D:\共边夹具",IsDirectory=true};
        var current=root;
        for(int i=0;i<5;i++)
        {
            var child=new FileEntry{Name="目录"+i,Path=current.Path+"/目录"+i,IsDirectory=true,Parent=current};
            current.Children.Add(child);
            current.Children.Add(new FileEntry{Name="样本.dll",Path=current.Path+"/样本.dll",Parent=current,Size=100});
            current=child;
        }
        current.Children.Add(new FileEntry{Name="末级.dll",Path=current.Path+"/末级.dll",Parent=current,Size=400});current.RecalculateSize();
        var nodes=new SquarifiedTreemapLayout().ComputeLayout(root,1200,900,64,zoom);
        var expanded=nodes.Where(n=>n.Entry.IsDirectory&&!n.IsContentLeaf&&n.HeaderHeight>0).ToArray();Assert.NotEmpty(expanded);
        foreach(var parent in expanded)
        {
            var children=nodes.Where(n=>ReferenceEquals(n.Entry.Parent,parent.Entry)).ToArray();Assert.NotEmpty(children);
            Assert.Equal(parent.X,children.Min(n=>n.X),3);
            Assert.Equal(parent.X+parent.Width,children.Max(n=>n.X+n.Width),3);
            Assert.Equal(parent.Y+parent.Height,children.Max(n=>n.Y+n.Height),3);
            Assert.Equal(parent.Y+parent.HeaderHeight!.Value,children.Min(n=>n.Y),3);
        }
        Assert.Equal(root.Size,nodes.Where(n=>n.IsContentLeaf).Sum(n=>n.AggregatedSize??n.Entry.Size));
    }
}
