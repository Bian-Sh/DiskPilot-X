using System.Reflection;
using System.Windows;
using DiskPilotX.App.Controls;
using DiskPilotX.Core.Models;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace DiskPilotX.App.Tests;

public class TreemapPanCoverageTests
{
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    private static readonly SKColor Background=SKColor.Parse("#1B2430");

    [Theory]
    [InlineData(60,30,5,140,1f)]
    [InlineData(60,30,5,140,1.5f)]
    [InlineData(60,30,5,140,2f)]
    [InlineData(-70,-40,390,140,1f)]
    [InlineData(0,40,180,5,1f)]
    [InlineData(0,-40,180,280,1f)]
    public void 平移首帧立即填充缓存外新暴露区域而非等后台补图(int dx,int dy,int x,int y,float dpi) => StaHost.Run(() =>
    {
        var file=new FileEntry {Name="文件.bin",Path="D:/平移夹具/文件.bin",Size=100};
        var control=new TreemapControl {Nodes=[new TreemapNode {Entry=file,X=0,Y=0,Width=400,Height=300,ColorHex="#7DA7CC"}]};
        control.Measure(new Size(400,300));control.Arrange(new Rect(0,0,400,300));control.UpdateLayout();
        Set(control,"_zoom",2f);Set(control,"_panX",-100f);Set(control,"_panY",-80f);
        using var initial=Paint(control,dpi);int count=Count(control);
        // 首帧走真实预览分支，后台开关已开，但本断言不泵消息也不等待任务或计时器。
        typeof(TreemapControl).GetProperty("EnableAsyncRaster",Hidden)!.SetValue(control,true);
        Invoke(control,"HandleLeftPress",new Point(200,140),1,false);
        Invoke(control,"HandlePointerMove",new Point(200+dx,140+dy));
        using var immediate=Paint(control,dpi);
        Assert.NotEqual(Background,immediate.GetPixel((int)(x*dpi),(int)(y*dpi)));
        Assert.Equal(count,Count(control));
        Invoke(control,"HandleLeftRelease",new Point(200+dx,140+dy));
    });

    [Fact]
    public void 大跨度平移与缩小均不能露出整条空白() => StaHost.Run(() =>
    {
        var root=new FileEntry {Name="根",Path="D:/平移夹具",Size=100,IsDirectory=true};
        var control=new TreemapControl {Nodes=[new TreemapNode {Entry=root,X=0,Y=0,Width=400,Height=300,IsContentLeaf=true,ColorHex="#506070",ContentColorHex="#7DA7CC",HeaderHeight=0,ShowDirectoryFrame=false}]};
        control.Measure(new Size(400,300));control.Arrange(new Rect(0,0,400,300));control.UpdateLayout();
        Set(control,"_zoom",4f);Set(control,"_panX",-600f);Set(control,"_panY",-400f);
        using var initial=Paint(control);int count=Count(control);
        typeof(TreemapControl).GetProperty("EnableAsyncRaster",Hidden)!.SetValue(control,true);
        Invoke(control,"HandleLeftPress",new Point(100,100),1,false);Invoke(control,"HandlePointerMove",new Point(600,400));
        using var panned=Paint(control);Assert.NotEqual(Background,panned.GetPixel(20,140));Assert.Equal(count,Count(control));
        Invoke(control,"HandleLeftRelease",new Point(600,400));
        Set(control,"_zoom",1f);Set(control,"_panX",0f);Set(control,"_panY",0f);
        Set(control,"_lastViewportInput",Environment.TickCount64);
        using var zoomed=Paint(control);Assert.NotEqual(Background,zoomed.GetPixel(380,270));
    });

    private static void Set(TreemapControl control,string name,object value)=>typeof(TreemapControl).GetField(name,Hidden)!.SetValue(control,value);
    private static object? Invoke(TreemapControl control,string name,params object[] args)=>typeof(TreemapControl).GetMethod(name,Hidden|BindingFlags.DeclaredOnly)!.Invoke(control,args);
    private static int Count(TreemapControl control)=>(int)typeof(TreemapControl).GetProperty("BaseRenderCount",Hidden)!.GetValue(control)!;
    private static SKBitmap Paint(TreemapControl control,float dpi=1f)
    {
        var info=new SKImageInfo((int)(400*dpi),(int)(300*dpi));using var surface=SKSurface.Create(info);
        Invoke(control,"OnPaintSurface",null!,new SKPaintSurfaceEventArgs(surface,info));
        var bitmap=new SKBitmap(info);surface.ReadPixels(info,bitmap.GetPixels(),bitmap.RowBytes,0,0);return bitmap;
    }
}
