using System.Collections;
using System.Reflection;
using System.Windows;
using DiskPilotX.App.Controls;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Treemap;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace DiskPilotX.App.Tests;

public class TreemapTitleReadabilityTests
{
    private static readonly Type Semantic=typeof(TreemapControl).Assembly.GetType("DiskPilotX.App.Controls.TreemapSemanticTitles")!;
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    private static object State()=>Activator.CreateInstance(Semantic,true)!;
    private static TreemapNode[] Titles(object state,List<TreemapNode> nodes,float zoom,float panY,float width=400,float height=300)=>
        ((IEnumerable)Semantic.GetMethod("Update",Hidden)!.Invoke(state,[nodes,zoom,0f,panY,width,height,1000L,false])!).Cast<object>()
            .Select(t=>(TreemapNode)t.GetType().GetProperty("Node")!.GetValue(t)!).ToArray();

    [Fact]
    public void 目录主体可读时上移两DIP越过视口不丢标题()
    {
        var folder=Folder("目录",null);AddFile(folder,"一.dll",100);AddFile(folder,"二.dll",100);
        var node=new TreemapNode {Entry=folder,X=0,Y=.21489716f/1.25f,Width=200,Height=246.9f,Depth=5,HeaderHeight=0,ShowDirectoryFrame=false,IsContentLeaf=false};
        var state=State();Assert.Contains(node,Titles(state,[node],1.25f,0));
        Assert.Contains(node,Titles(state,[node],1.25f,-2));
        Assert.Same(node,Semantic.GetMethod("Hit",Hidden)!.Invoke(state,[40f,9f]));
        Assert.Contains(node,Titles(state,[node],1.5f,-2));
    }

    [Fact]
    public void 已有标题被裁到顶部时在自身可见区域恢复可读标题()
    {
        var folder=Folder("目录",null);AddFile(folder,"一.dll",100);AddFile(folder,"二.dll",100);
        var node=new TreemapNode {Entry=folder,X=0,Y=0,Width=250,Height=260,Depth=4,HeaderHeight=18f/1.25f,ShowDirectoryFrame=true,Label=folder.Name,IsContentLeaf=false};
        Assert.Contains(node,Titles(State(),[node],1.25f,-3));
    }

    [Fact]
    public void 十三个互不相交且像素成本充足的目录不按十二个截断()
    {
        var root=Folder("根",null);var nodes=new List<TreemapNode>();
        for(int i=0;i<13;i++)
        {
            var folder=Folder("目录"+i,root);AddFile(folder,"一.dll",100);AddFile(folder,"二.dll",100);folder.RecalculateSize();
            nodes.Add(new TreemapNode {Entry=folder,X=i%4*80,Y=i/4*70+2,Width=70,Height=60,Depth=3,HeaderHeight=0,ShowDirectoryFrame=false,IsContentLeaf=false});
        }
        root.RecalculateSize();Assert.Equal(13,Titles(State(),nodes,2,0,640,560).Length);
    }

    [Fact]
    public void 大而有信息的深层分支不被前三个标题耗尽()
    {
        var root=Frozen();
        var nodes=new SquarifiedTreemapLayout().ComputeLayout(root,1200,900,64,2);
        Assert.Contains(nodes,n=>n.Depth>=3&&n.Entry.IsDirectory&&n.HeaderHeight>0);
        Assert.True(nodes.Count(n=>n.Entry.IsDirectory&&n.HeaderHeight>0)>3);
    }

    [Fact]
    public void 可读的窄文件块显示名称而非一律等待两万像素面积()
    {
        Assert.True(TreemapLabelPolicy.ShowFileLabel(100,26,1));
        Assert.False(TreemapLabelPolicy.ShowFileLabel(10,10,1));
        var root=Frozen();var nodes=new SquarifiedTreemapLayout().ComputeLayout(root,809,292,64,1.25f);
        var direct=nodes.Where(n=>!n.Entry.IsDirectory&&ReferenceEquals(n.Entry.Parent,root)).ToArray();
        Assert.Equal(4,direct.Length);Assert.Contains(direct,n=>n.Label is not null);
        var sub=root.Children.First(e=>e.IsDirectory);
        Assert.Contains(nodes,n=>!n.Entry.IsDirectory&&ReferenceEquals(n.Entry.Parent,sub)&&n.Label is not null);
    }

    [Fact]
    public void 文件标签真正进入渲染而不只修改节点标志() => StaHost.Run(() =>
    {
        var root=Folder("目录",null);AddFile(root,"一.dll",100);AddFile(root,"二.dll",100);root.RecalculateSize();
        var control=new TreemapControl {Nodes=new SquarifiedTreemapLayout().ComputeLayout(root,200,70,64)};
        control.Measure(new Size(200,70));control.Arrange(new Rect(0,0,200,70));control.UpdateLayout();
        using var surface=SKSurface.Create(new SKImageInfo(200,70));
        typeof(TreemapControl).GetMethod("OnPaintSurface",Hidden|BindingFlags.DeclaredOnly)!.Invoke(control,[null,new SKPaintSurfaceEventArgs(surface,new SKImageInfo(200,70))]);
        Assert.True((int)typeof(TreemapControl).GetProperty("RenderedLabelCount",Hidden)!.GetValue(control)!>=3);
    });

    [Fact]
    public void 文件左上角移出视口后文字仍在自身可见部分() => StaHost.Run(() =>
    {
        var file=new FileEntry {Name="示例.dll",Path="D:/文字夹具/示例.dll",Size=100};
        var control=new TreemapControl {Nodes=[new TreemapNode {Entry=file,X=0,Y=0,Width=400,Height=300,Label=file.Name,SizeLabel="100 B",ColorHex="#7DA7CC"}]};
        control.Measure(new Size(200,80));control.Arrange(new Rect(0,0,200,80));control.UpdateLayout();
        typeof(TreemapControl).GetField("_zoom",Hidden)!.SetValue(control,2f);
        typeof(TreemapControl).GetField("_panX",Hidden)!.SetValue(control,-150f);
        typeof(TreemapControl).GetField("_panY",Hidden)!.SetValue(control,-100f);
        var info=new SKImageInfo(200,80);using var surface=SKSurface.Create(info);
        typeof(TreemapControl).GetMethod("OnPaintSurface",Hidden|BindingFlags.DeclaredOnly)!.Invoke(control,[null,new SKPaintSurfaceEventArgs(surface,info)]);
        using var bitmap=new SKBitmap(info);surface.ReadPixels(info,bitmap.GetPixels(),bitmap.RowBytes,0,0);
        int textPixels=0;
        for(int y=1;y<20;y++)for(int x=2;x<198;x++) {var c=bitmap.GetPixel(x,y);if(c.Red<60&&c.Green<60&&c.Blue<70)textPixels++;}
        Assert.True(textPixels>0,"文件主体可读，但文字仍留在视口外。");
    });

    [Fact]
    public void 已预留的十三行目录标题不因横向裁剪再被面积预算隐藏()
    {
        var nodes=new List<TreemapNode>();
        for(int i=0;i<13;i++)
        {
            var folder=Folder("目录"+i,null);AddFile(folder,"一.dll",100);AddFile(folder,"二.dll",100);
            nodes.Add(new TreemapNode {Entry=folder,X=-40,Y=i*20,Width=500,Height=280,Depth=i+1,
                HeaderHeight=18,ShowDirectoryFrame=true,Label=folder.Name,IsContentLeaf=false});
        }
        Assert.Equal(13,Titles(State(),nodes,1,0,400,300).Length);
    }

    [Fact]
    public void 顶部临时标题不挤压固定子标题的完整区域()
    {
        var parent=Folder("父目录",null);AddFile(parent,"一.dll",100);AddFile(parent,"二.dll",100);
        var child=Folder("子目录",parent);AddFile(child,"三.dll",100);AddFile(child,"四.dll",100);
        var first=new TreemapNode {Entry=parent,X=-10,Y=0,Width=250,Height=260,Depth=1,HeaderHeight=14,ShowDirectoryFrame=true,Label=parent.Name,IsContentLeaf=false};
        var second=new TreemapNode {Entry=child,X=-10,Y=14,Width=240,Height=230,Depth=2,HeaderHeight=14,ShowDirectoryFrame=true,Label=child.Name,IsContentLeaf=false};
        Assert.DoesNotContain(first,Titles(State(),[first,second],1,-2));
        Assert.Contains(second,Titles(State(),[first,second],1,-2));
    }

    [Fact]
    public void 同视口深链标题成本受实际文件空间约束而不是个数()
    {
        var root=Frozen();const float width=932,height=295;
        var nodes=new SquarifiedTreemapLayout().ComputeLayout(root,width,height,64);
        double titleArea=nodes.Sum(n=>(double)n.Width*(n.HeaderHeight??0));
        Assert.InRange(titleArea/(width*height),0,.18);
        Assert.All(nodes.Where(n=>n.HeaderHeight>0),n=>Assert.InRange(n.HeaderHeight!.Value,0,14));
        Assert.Equal(root.Size,nodes.Where(n=>n.IsContentLeaf).Sum(n=>n.AggregatedSize??n.Entry.Size));
    }

    [Fact]
    public void 用户确认的标题压入自身可见区域且cell离开后消失()
    {
        var folder=Folder("保留的边界体验",null);AddFile(folder,"一.dll",100);AddFile(folder,"二.dll",100);
        var node=new TreemapNode {Entry=folder,X=0,Y=0,Width=250,Height=260,Depth=3,HeaderHeight=14,ShowDirectoryFrame=true,Label=folder.Name,IsContentLeaf=false};
        var state=State();Assert.Contains(node,Titles(state,[node],1,-2));
        Assert.Same(node,Semantic.GetMethod("Hit",Hidden)!.Invoke(state,[40f,7f]));
        Assert.Empty(Titles(state,[node],1,-261));
        Assert.Empty(Titles(state,[node],1,301));
        Assert.Contains(node,Titles(state,[node],1,-2));
    }

    private static FileEntry Folder(string name,FileEntry? parent)
    {
        var result=new FileEntry {Name=name,Path=(parent?.Path??"D:/标题夹具")+"/"+name,IsDirectory=true,Parent=parent};parent?.Children.Add(result);return result;
    }
    private static void AddFile(FileEntry parent,string name,long size)=>parent.Children.Add(new FileEntry {Name=name,Path=parent.Path+"/"+name,Size=size,Parent=parent});
    private static FileEntry Frozen()
    {
        var root=Folder("子目录2",null);
        void Populate(FileEntry parent,int depth)
        {
            if(depth<7)Populate(Folder("子目录"+(depth+3),parent),depth+1);
            for(int i=0;i<4;i++)AddFile(parent,$"第{depth+2}层_{i}.dll",16384*(i+1));
            parent.RecalculateSize();
        }
        Populate(root,0);return root;
    }
}
