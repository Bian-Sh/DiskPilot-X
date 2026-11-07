using System.Collections;
using System.Reflection;
using DiskPilotX.App.Controls;
using DiskPilotX.Core.Models;
using SkiaSharp;

namespace DiskPilotX.App.Tests;

public sealed class TreemapTitleCollisionTests
{
    private static readonly Type Semantic = typeof(TreemapControl).Assembly.GetType("DiskPilotX.App.Controls.TreemapSemanticTitles")!;
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private sealed record Frame(TreemapNode Node, SKRect Rect, float Opacity);
    private static Frame[] Update(object state, List<TreemapNode> nodes, float panY, long now, bool animate) =>
        ((IEnumerable)Semantic.GetMethod("Update", Hidden)!.Invoke(state, [nodes, 1f, 0f, panY, 400f, 300f, now, animate])!).Cast<object>()
            .Select(title => new Frame((TreemapNode)title.GetType().GetProperty("Node")!.GetValue(title)!,
                (SKRect)title.GetType().GetProperty("Rect")!.GetValue(title)!,
                (float)title.GetType().GetProperty("Opacity")!.GetValue(title)!)).ToArray();

    private static (TreemapNode Parent, TreemapNode Child) Nodes()
    {
        var parent = new FileEntry { Name="父目录", Path=@"D:\标题夹具\父目录", IsDirectory=true, Size=400 };
        var child = new FileEntry { Name="子目录", Path=parent.Path+@"\子目录", IsDirectory=true, Size=200, Parent=parent };
        parent.Children.Add(child);
        return (new TreemapNode { Entry=parent, X=-30, Y=0, Width=280, Height=270, Depth=1, HeaderHeight=14, Label=parent.Name, ShowDirectoryFrame=true },
            new TreemapNode { Entry=child, X=-20, Y=100, Width=250, Height=160, Depth=2, HeaderHeight=14, Label=child.Name, ShowDirectoryFrame=true });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(55)]
    [InlineData(109)]
    public void 祖先与后代标题压到同一顶边时淡出不能叠字(int elapsed)
    {
        var (parent, child)=Nodes(); var state=Activator.CreateInstance(Semantic,true)!;
        Assert.Contains(Update(state,[parent,child],-30,1000,false), t=>ReferenceEquals(t.Node,parent));
        Update(state,[parent,child],-102,1100,true);
        var titles=Update(state,[parent,child],-102,1100+elapsed,true);
        Assert.Contains(titles,t=>ReferenceEquals(t.Node,child));
        Assert.False(titles.SelectMany((a,i)=>titles.Skip(i+1).Select(b=>(a,b)))
            .Any(pair=>pair.a.Opacity>0 && pair.b.Opacity>0 && pair.a.Rect.IntersectsWith(pair.b.Rect)),
            "旧祖先标题仍在淡出，新子目录标题已经压入同一区域，不能同时绘制。");
    }

    [Fact]
    public void 淡出标题不能盖住已预留的固定标题()
    {
        var (parent, child)=Nodes(); var state=Activator.CreateInstance(Semantic,true)!;
        Update(state,[parent,child],-30,1000,false);
        var titles=Update(state,[parent,child],-99,1100,true);
        Assert.Contains(titles,t=>ReferenceEquals(t.Node,child));
        Assert.DoesNotContain(titles,t=>ReferenceEquals(t.Node,parent));
    }

    [Theory]
    [InlineData(-.01f)]
    [InlineData(-.25f)]
    [InlineData(-1f)]
    [InlineData(-2.509857f)]
    [InlineData(-4.25f)]
    public void 顶边临界完整标题空间不足时临时父标题让位(float parentScreenY)
    {
        var (parent, child)=Nodes(); parent.Y=20; child.Y=34;
        var state=Activator.CreateInstance(Semantic,true)!;
        var titles=Update(state,[parent,child],parentScreenY-20,1000,false);
        Assert.Contains(titles,t=>ReferenceEquals(t.Node,child));
        Assert.DoesNotContain(titles,t=>ReferenceEquals(t.Node,parent));
        Assert.False(titles.SelectMany((a,i)=>titles.Skip(i+1).Select(b=>(a,b)))
            .Any(pair=>Math.Min(pair.a.Rect.Right,pair.b.Rect.Right)-Math.Max(pair.a.Rect.Left,pair.b.Rect.Left)>.001f
                && Math.Min(pair.a.Rect.Bottom,pair.b.Rect.Bottom)-Math.Max(pair.a.Rect.Top,pair.b.Rect.Top)>.001f));
    }
}