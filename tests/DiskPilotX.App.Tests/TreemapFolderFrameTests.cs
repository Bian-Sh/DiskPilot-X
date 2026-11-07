using System.Reflection;
using System.Windows;
using DiskPilotX.App.Controls;
using DiskPilotX.Core.Models;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace DiskPilotX.App.Tests;

public sealed class TreemapFolderFrameTests
{
    [Theory]
    [InlineData(1f,false)]
    [InlineData(2f,false)]
    [InlineData(1f,true)]
    [InlineData(2f,true)]
    public void 展开目录只画标题边界折叠块才有内容外框(float dpi,bool leaf)=>StaHost.Run(()=>
    {
        var entry=new FileEntry{IsDirectory=true,Path=@"D:\共边",Name="目录",Size=100};
        var c=new TreemapControl{Nodes=[new(){Entry=entry,X=20,Y=20,Width=350,Height=250,Label="目录",HeaderHeight=14,ShowDirectoryFrame=true,IsContentLeaf=leaf,ColorHex="#303030",ContentColorHex="#808080"}]};
        c.Measure(new Size(400,300));c.Arrange(new Rect(0,0,400,300));var info=new SKImageInfo((int)(400*dpi),(int)(300*dpi));using var surface=SKSurface.Create(info);
        typeof(TreemapControl).GetMethod("OnPaintSurface",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly)!.Invoke(c,[null,new SKPaintSurfaceEventArgs(surface,info)]);
        using var image=surface.Snapshot();using var bitmap=SKBitmap.FromImage(image);
        Assert.Equal(SKColor.Parse("#595959"),bitmap.GetPixel((int)(300*dpi),(int)(34*dpi)));
        var body=bitmap.GetPixel((int)(300*dpi),(int)(150*dpi));var edge=bitmap.GetPixel((int)(20*dpi),(int)(150*dpi));
        if(leaf)Assert.NotEqual(body,edge);else Assert.Equal(body,edge);
        var bottom=bitmap.GetPixel((int)(300*dpi),(int)(270*dpi));if(leaf)Assert.NotEqual(body,bottom);
        else Assert.Equal(SKColor.Parse("#1B2430"),bottom);
    });
}
