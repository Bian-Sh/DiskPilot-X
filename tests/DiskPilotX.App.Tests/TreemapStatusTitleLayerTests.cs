using System.Reflection;
using System.Windows;
using DiskPilotX.App.Controls;
using DiskPilotX.Core.Models;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace DiskPilotX.App.Tests;

public class TreemapStatusTitleLayerTests
{
    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void Selected粗框覆盖titlebar边线(float dpi) => StaHost.Run(() =>
    {
        var (control,node)=Create(false);
        control.SelectedNode=node;
        using var bitmap=Paint(control,dpi);
        // 标题右边线与粗框相交处必须保留 Selected 的白色，而非标题灰色。
        Assert.Equal(SKColors.White,bitmap.GetPixel((int)Math.Floor((node.X+node.Width)*dpi),(int)Math.Floor((node.Y+7)*dpi)));
    });

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void Deleted细框覆盖titlebar背景(float dpi) => StaHost.Run(() =>
    {
        var (control,node)=Create(true);
        using var bitmap=Paint(control,dpi);
        // Deleted 仍为原来的细框，但不能再被标题背景涂掉。
        Assert.Equal(new SKColor(229,72,77),bitmap.GetPixel((int)Math.Floor((node.X+node.Width)*dpi-1),(int)Math.Floor((node.Y+7)*dpi)));
    });

    private static (TreemapControl Control,TreemapNode Node) Create(bool deleted)
    {
        var entry=new FileEntry {Name="状态框层级夹具",Path=@"D:\本任务层级夹具",IsDirectory=true,IsDeleted=deleted,Size=1024};
        var node=new TreemapNode {Entry=entry,X=20,Y=20,Width=240,Height=180,Depth=1,
            HeaderHeight=14,Label=entry.Name,SizeLabel="1.0 KB",ShowDirectoryFrame=true,ColorHex="#303030"};
        var control=new TreemapControl {Nodes=[node]};
        control.Measure(new Size(400,300));control.Arrange(new Rect(0,0,400,300));
        return (control,node);
    }
    private static SKBitmap Paint(TreemapControl control,float dpi)
    {
        var info=new SKImageInfo((int)(400*dpi),(int)(300*dpi));using var surface=SKSurface.Create(info);
        typeof(TreemapControl).GetMethod("OnPaintSurface",BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly)!
            .Invoke(control,[null,new SKPaintSurfaceEventArgs(surface,info)]);
        using var image=surface.Snapshot();return SKBitmap.FromImage(image);
    }
}