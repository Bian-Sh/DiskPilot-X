using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using DiskPilotX.App.Controls;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Models;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace DiskPilotX.App.Tests;

public sealed class TreemapLifetimeTests
{
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly;
    private static object? Call(object target,string name,params object?[] args)=>target.GetType().GetMethod(name,Hidden)!.Invoke(target,args);
    private static FileEntry Root(string name)
    {
        var root=new FileEntry{Path=@"D:\生命周期夹具\"+name,Name=name,IsDirectory=true,Size=200};
        var directory=new FileEntry{Path=root.Path+@"\缓存",Name="缓存",IsDirectory=true,Parent=root,Size=200};root.Children.Add(directory);
        directory.Children.Add(new FileEntry{Path=directory.Path+@"\样本.dll",Name="样本.dll",Parent=directory,Size=200});return root;
    }
    private static List<TreemapNode> Nodes(FileEntry root)=>[
        new(){Entry=root,X=0,Y=0,Width=400,Height=300,HeaderHeight=14,Label=root.Name,ShowDirectoryFrame=true},
        new(){Entry=root.Children[0],X=1,Y=14,Width=200,Height=250,HeaderHeight=14,Label="缓存",ShowDirectoryFrame=true,Depth=1},
        new(){Entry=root.Children[0].Children[0],X=1,Y=28,Width=200,Height=230,Label="文件",Depth=2}];
    [MethodImpl(MethodImplOptions.NoInlining)] private static bool Alive(WeakReference<FileEntry> reference)=>reference.TryGetTarget(out _);
    private static void Collect(){GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();}
    [MethodImpl(MethodImplOptions.NoInlining)] private static WeakReference<FileEntry> InstallOld(TreemapControl control,bool zoom)
    {
        var root=Root("旧扫描");Call(control,"SetViewRoot",root,root);control.Nodes=Nodes(root);
        var info=new SKImageInfo(400,300);using var surface=SKSurface.Create(info);Call(control,"OnPaintSurface",null,new SKPaintSurfaceEventArgs(surface,info));
        if(zoom)Call(control,"RaiseWheelAt",80f,100f,12);return new(root);
    }
    [Fact] public void 切换扫描清空未提交缩放的祖先锚点()=>StaHost.Run(()=>
    {
        var control=new TreemapControl();control.Measure(new Size(400,300));control.Arrange(new Rect(0,0,400,300));
        var old=InstallOld(control,true);var next=Root("新扫描");Call(control,"SetViewRoot",next,next);control.Nodes=Nodes(next);
        var anchors=(System.Collections.IList)typeof(TreemapControl).GetField("_zoomAnchorAncestors",Hidden)!.GetValue(control)!;
        Assert.Empty(anchors.Cast<object>());Collect();Assert.False(Alive(old),"缩放锚点或可见缓存仍保留整棵旧扫描树。");GC.KeepAlive(control);
    });
    [Fact] public void 清空树图立即释放旧可见缓存与底图()=>StaHost.Run(()=>
    {
        var control=new TreemapControl();control.Measure(new Size(400,300));control.Arrange(new Rect(0,0,400,300));
        var old=InstallOld(control,false);Call(control,"SetViewRoot",null,null);control.Nodes=null;
        Assert.Null(typeof(TreemapControl).GetField("_baseImage",Hidden)!.GetValue(control));
        Collect();Assert.False(Alive(old),"空视图仍通过上一帧可见节点持有扫描树。");GC.KeepAlive(control);
    });
    [MethodImpl(MethodImplOptions.NoInlining)] private static WeakReference<FileEntry> SelectOld(TreeViewModel vm)
    {
        var root=Root("旧列表");vm.SetRoot(root);vm.SelectEntry(root.Children[0]);return new(root);
    }
    [Fact] public void 切换扫描树列表不能保留旧选中对象()=>StaHost.Run(()=>
    {
        var vm=new TreeViewModel();var old=SelectOld(vm);vm.SetRoot(Root("新列表"));
        Assert.Null(vm.SelectedItem);Collect();Assert.False(Alive(old),"旧选中项仍引用已经替换的扫描树。");GC.KeepAlive(vm);
    });
}