using System.Reflection;
using System.Windows;
using DiskPilotX.App.Controls;
using DiskPilotX.Core.Models;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace DiskPilotX.App.Tests;

public sealed class TreemapHorizontalTitleTests
{
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly;
    private static List<TreemapNode> Rows()
    {
        var parent=new FileEntry{Path=@"D:\横向标题\父",Name="父目录",IsDirectory=true,Size=400};
        var child=new FileEntry{Path=parent.Path+@"\子",Name="子目录",IsDirectory=true,Parent=parent,Size=200};parent.Children.Add(child);
        return [new(){Entry=parent,X=20,Y=20,Width=420,Height=270,Depth=1,HeaderHeight=14,Label=parent.Name,ColorHex="#303030",ContentColorHex="#808080",ShowDirectoryFrame=true},
            new(){Entry=child,X=20.5f,Y=34,Width=419,Height=255.5f,Depth=2,HeaderHeight=14,Label=child.Name,ColorHex="#303030",ContentColorHex="#808080",ShowDirectoryFrame=true}];
    }
    private static TreemapControl Control(List<TreemapNode> nodes,float pan)
    {
        var c=new TreemapControl{Nodes=nodes};c.Measure(new Size(480,310));c.Arrange(new Rect(0,0,480,310));
        typeof(TreemapControl).GetField("_panX",Hidden)!.SetValue(c,pan);return c;
    }
    private static SKBitmap Paint(TreemapControl c,float dpi)
    {
        var info=new SKImageInfo((int)(480*dpi),(int)(310*dpi));using var surface=SKSurface.Create(info);
        typeof(TreemapControl).GetMethod("OnPaintSurface",Hidden)!.Invoke(c,[null,new SKPaintSurfaceEventArgs(surface,info)]);
        using var image=surface.Snapshot();return SKBitmap.FromImage(image);
    }
    [Theory]
    [InlineData(1f,-40f)]
    [InlineData(1.25f,-40f)]
    [InlineData(2f,-40.25f)]
    [InlineData(1f,100f)]
    public void 横向裁剪重定位后仍保留原标题单物理像素分隔(float dpi,float pan)=>StaHost.Run(()=>
    {
        using var normal=Paint(Control(Rows(),0),dpi);using var clipped=Paint(Control(Rows(),pan),dpi);
        var x=(int)(300*dpi);var y=(int)MathF.Floor(34*dpi);
        Assert.NotEqual(normal.GetPixel(x,y+3),normal.GetPixel(x,y));
        Assert.Equal(normal.GetPixel(x,y),clipped.GetPixel(x,y));
        Assert.NotEqual(clipped.GetPixel(x,y+3),clipped.GetPixel(x,y));
    });
    [Fact]
    public void 横向裁剪标题文字不能画到自己十四像素区域外()=>StaHost.Run(()=>
    {
        var directory=new FileEntry{Path=@"D:\横向标题\gjpq",Name="gjpqgjpqgjpqgjpq",IsDirectory=true,Size=200};
        var node=new TreemapNode{Entry=directory,X=-20,Y=20,Width=400,Height=260,Depth=1,HeaderHeight=14,Label=directory.Name,ShowDirectoryFrame=true,ColorHex="#303030"};
        var c=Control([node],0);using var initial=Paint(c,1);
        var info=new SKImageInfo(480,310);using var surface=SKSurface.Create(info);surface.Canvas.Clear(SKColors.Transparent);using var text=new SKPaint();
        typeof(TreemapControl).GetMethod("DrawSemanticTitles",Hidden)!.Invoke(c,[surface.Canvas,text]);using var image=surface.Snapshot();using var bitmap=SKBitmap.FromImage(image);
        var brightOutside=0;for(int y=34;y<42;y++)for(int x=2;x<350;x++){var p=bitmap.GetPixel(x,y);if(p.Alpha>0&&p.Red>150&&p.Green>150&&p.Blue>150)brightOutside++;}
        Assert.Equal(0,brightOutside);
    });
}