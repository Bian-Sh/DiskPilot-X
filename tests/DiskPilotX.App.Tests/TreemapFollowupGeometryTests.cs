using System.Reflection;
using System.Windows;
using DiskPilotX.App.Controls;
using DiskPilotX.Core.Models;
using SkiaSharp;
namespace DiskPilotX.App.Tests;
public class TreemapFollowupGeometryTests
{
    private const BindingFlags Hidden=BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    [Fact] public void 文件标题被挤到五十五像素仍随可见区域浮动()
    {
        var node=new TreemapNode { Entry=new FileEntry { Name="文件.bin" }, Width=120,Height=80,Label="文件.bin" };
        var method=typeof(TreemapControl).GetMethod("GetFileTitleRect",Hidden); Assert.NotNull(method);
        var rect=Assert.IsType<SKRect>(method.Invoke(null,[node,1f,-65f,-2f,300f,200f]));
        Assert.Equal(0,rect.Left);Assert.Equal(0,rect.Top);Assert.Equal(55,rect.Width);
        Assert.Null(method.Invoke(null,[node,1f,-121f,0f,300f,200f]));
    }
    [Fact] public void 标题已经存在时文字不再独立消失()
    {
        using var font=new SKFont(SKTypeface.Default,10);
        var method=typeof(TreemapControl).GetMethod("FormatRetainedTitleLabel",Hidden);Assert.NotNull(method);
        Assert.Equal("目录",method.Invoke(null,["目录","100.0 GB",font,4f]));
    }
    [Fact] public void Deleted细框不改变Selected粗框()
    {
        var field=typeof(TreemapControl).GetField("DeletedOutlineScreenWidth",Hidden);Assert.NotNull(field);
        Assert.Equal(1f,field.GetRawConstantValue());
    }
    [Fact] public void Ping只临时提示且不修改选择或根引用()=>StaHost.Run(()=>
    {
        var root=new FileEntry {Name="根",IsDirectory=true};var child=new FileEntry {Name="文件",Parent=root};root.Children.Add(child);
        var parentNode=new TreemapNode {Entry=root,Width=200,Height=150};var childNode=new TreemapNode {Entry=child,X=10,Y=20,Width=100,Height=80};
        var nodes=new List<TreemapNode>{parentNode,childNode};var control=new TreemapControl {Nodes=nodes,SelectedNode=parentNode};
        control.Measure(new Size(200,150));control.Arrange(new Rect(0,0,200,150));
        var method=typeof(TreemapControl).GetMethod("PingEntry");Assert.NotNull(method);
        Assert.Equal(true,method.Invoke(control,[child]));Assert.Same(parentNode,control.SelectedNode);Assert.Same(nodes,control.Nodes);
        var ping=typeof(TreemapControl).GetProperty("PingedEntry",Hidden);Assert.NotNull(ping);Assert.Same(child,ping.GetValue(control));
        control.Nodes=null;Assert.Null(ping.GetValue(control));
    });
}
