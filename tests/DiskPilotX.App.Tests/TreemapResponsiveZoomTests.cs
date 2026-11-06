using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using DiskPilotX.App.Controls;
using DiskPilotX.App.ViewModels;
using DiskPilotX.App.Views;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Treemap;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace DiskPilotX.App.Tests;

public class TreemapResponsiveZoomTests
{
    [Fact]
    public void 真实视图快速滚轮不在输入调用栈重排并合并请求()
    {
        CountingLayout? engine = null; TreemapViewModel? vm = null; TreemapControl? control = null;
        int before = 0;
        StaHost.Run(() =>
        {
            var style = new Style(typeof(Button)); style.Seal(); Application.Current.Resources["VP.Button"] = style;
            engine = new CountingLayout { UiThread = Environment.CurrentManagedThreadId }; vm = new TreemapViewModel(engine);
            var root = Tree(); vm.UpdateSize(400, 300); vm.SetRoot(root);
            var view = new TreemapView(); view.SetViewModel(vm); control = (TreemapControl)view.FindName("Treemap");
            control.Measure(new Size(400,300)); control.Arrange(new Rect(0,0,400,300));
            before = engine.UiCalls;
            var wheel = typeof(TreemapControl).GetMethod("RaiseWheelAt", BindingFlags.Instance | BindingFlags.NonPublic)!;
            for (int i=0;i<3;i++) wheel.Invoke(control,[90f,150f,12]);
            Assert.Equal(before, engine.UiCalls);
            Assert.True((float)typeof(TreemapControl).GetField("_zoom",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(control)! > 1);
        });
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref engine!.Calls) > before, TimeSpan.FromSeconds(5)));
        Thread.Sleep(200);
        Assert.Equal(before+1, engine!.Calls);
    }

    [Theory]
    [InlineData(2f)]
    [InlineData(4f)]
    public void 放大后深层透明目录恢复视口标题并命中真实目录(float zoom) => StaHost.Run(() =>
    {
        var root = Tree(); var folder = root.Children[0];
        var directory = new TreemapNode { Entry=folder,X=80/zoom,Y=50/zoom,Width=240/zoom,Height=180/zoom,
            Depth=12,Label=null,HeaderHeight=0,ShowDirectoryFrame=false,IsContentLeaf=false };
        var file = new TreemapNode { Entry=folder.Children[0],X=directory.X,Y=directory.Y,Width=directory.Width,Height=directory.Height,Depth=13,ColorHex="#7DA7CC" };
        var control = new TreemapControl { Nodes=[directory,file] };
        control.Measure(new Size(400,300));control.Arrange(new Rect(0,0,400,300));
        typeof(TreemapControl).GetField("_zoom",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(control,zoom);
        using var surface=SKSurface.Create(new SKImageInfo(400,300));
        typeof(TreemapControl).GetMethod("OnPaintSurface",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly)!
            .Invoke(control,[null,new SKPaintSurfaceEventArgs(surface,new SKImageInfo(400,300))]);
        Assert.True((int)typeof(TreemapControl).GetProperty("RenderedLabelCount",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(control)!>0);
        Assert.Same(folder,control.HitTest(100,58)!.Entry);
        Assert.Same(file.Entry,control.HitTest(100,110)!.Entry);
    });

    [Fact]
    public void 局部标题支持双击真实目录且缩回全景收回() => StaHost.Run(() =>
    {
        var folder=Tree().Children[0];
        var directory=new TreemapNode {Entry=folder,X=40,Y=25,Width=120,Height=90,Depth=12,HeaderHeight=0,ShowDirectoryFrame=false,IsContentLeaf=false};
        var file=new TreemapNode {Entry=folder.Children[0],X=40,Y=25,Width=120,Height=90,Depth=13,ColorHex="#7DA7CC"};
        var control=new TreemapControl {Nodes=[directory,file]};control.Measure(new Size(400,300));control.Arrange(new Rect(0,0,400,300));
        typeof(TreemapControl).GetField("_zoom",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(control,2f);
        Draw(control);TreemapNode? activated=null;control.NodeActivated+=(_,n)=>activated=n;
        typeof(TreemapControl).GetMethod("HandleClick",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(control,[new Point(100,58),2,false]);
        Assert.Same(directory,activated);
        typeof(TreemapControl).GetField("_zoom",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(control,1f);
        Draw(control);Assert.Same(file,control.HitTest(50,30));
    });

    [Fact]
    public void 同一场景重复绘制不重新生成底图() => StaHost.Run(() =>
    {
        var node=new TreemapNode {Entry=Tree().Children[0].Children[0],X=0,Y=0,Width=400,Height=300,ColorHex="#7DA7CC"};
        var control=new TreemapControl {Nodes=[node]};control.Measure(new Size(400,300));control.Arrange(new Rect(0,0,400,300));
        Draw(control);
        int Count() => (int)typeof(TreemapControl).GetProperty("BaseRenderCount",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(control)!;
        int before=Count();Draw(control);Draw(control);Assert.Equal(before,Count());
    });

    private static void Draw(TreemapControl control)
    {
        using var surface=SKSurface.Create(new SKImageInfo(400,300));
        typeof(TreemapControl).GetMethod("OnPaintSurface",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly)!
            .Invoke(control,[null,new SKPaintSurfaceEventArgs(surface,new SKImageInfo(400,300))]);
    }

    [Fact]
    public async Task 旧后台结果不能覆盖新扫描根且提交仍在UI线程()
    {
        var engine=new BlockingLayout(); TreemapViewModel? vm=null;Task pending=Task.CompletedTask;
        FileEntry? next=null;List<TreemapNode>? newest=null;int uiThread=0;bool offThread=false;
        StaHost.Run(()=>
        {
            uiThread=Environment.CurrentManagedThreadId;vm=new TreemapViewModel(engine);vm.UpdateSize(400,300);vm.SetRoot(Tree());
            vm.PropertyChanged+=(_,e)=> {if(e.PropertyName==nameof(vm.Nodes)&&Environment.CurrentManagedThreadId!=uiThread) offThread=true;};
            engine.Block=true;pending=vm.RequestZoomAsync(2);
        });
        Assert.True(engine.Entered.Wait(TimeSpan.FromSeconds(5)));
        StaHost.Run(()=> { next=Tree();next.Name="新扫描";vm!.SetRoot(next);newest=vm.Nodes; });
        engine.Release.Set();await pending.WaitAsync(TimeSpan.FromSeconds(5));
        StaHost.Run(()=> {Assert.Same(next,vm!.CurrentRoot);Assert.Same(newest,vm.Nodes);Assert.Equal(1f,vm.Zoom);Assert.False(offThread);});
    }

    [Fact]
    public async Task 后台布局失败保留已有可用节点并停止等待状态()
    {
        var engine=new BlockingLayout();TreemapViewModel? vm=null;Task pending=Task.CompletedTask;List<TreemapNode>? previous=null;
        StaHost.Run(()=> {vm=new TreemapViewModel(engine);vm.UpdateSize(400,300);vm.SetRoot(Tree());previous=vm.Nodes;engine.Fail=true;pending=vm.RequestZoomAsync(2);});
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        StaHost.Run(()=>
        {
            Assert.Same(previous,vm!.Nodes);
            Assert.False((bool)typeof(TreemapViewModel).GetProperty("IsZoomLayoutPending",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(vm)!);
            Assert.NotNull(typeof(TreemapViewModel).GetProperty("ZoomLayoutError",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(vm));
        });
    }

    private sealed class BlockingLayout : ITreemapLayoutEngine
    {
        public bool Block,Fail; private int _uiThread;public readonly ManualResetEventSlim Entered=new(false),Release=new(false);
        public List<TreemapNode> ComputeLayout(FileEntry root,float width,float height,int maxDepth,float zoom=1)
        {
            if(zoom==1)_uiThread=Environment.CurrentManagedThreadId;
            if(zoom>1&&Block&&Environment.CurrentManagedThreadId!=_uiThread){Entered.Set();if(!Release.Wait(TimeSpan.FromSeconds(5)))throw new TimeoutException();}
            if(zoom>1&&Fail)throw new InvalidOperationException("夹具模拟失败");
            return new SquarifiedTreemapLayout().ComputeLayout(root,width,height,maxDepth,zoom);
        }
    }

    [Fact]
    public void 目录标题跨阈值有滞回而不是逐帧闪烁() => StaHost.Run(() =>
    {
        var folder=Tree().Children[0];
        var type=typeof(TreemapControl).Assembly.GetType("DiskPilotX.App.Controls.TreemapSemanticTitles")!;
        var picker=Activator.CreateInstance(type,nonPublic:true)!;
        var update=type.GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var n=new TreemapNode {Entry=folder,X=20,Y=20,Width=70,Height=60,HeaderHeight=0,ShowDirectoryFrame=false,IsContentLeaf=false};
        int Pick(long now) => ((System.Collections.IEnumerable)update.Invoke(picker,[new[]{n},2f,0f,0f,400f,300f,now,false])!).Cast<object>().Count();
        Assert.Equal(1,Pick(0));
        n.Width=58;Assert.Equal(1,Pick(200));
        n.Width=45;Assert.Equal(0,Pick(400));
    });

    [Fact]
    public void 旧尺寸后台结果不能覆盖新尺寸()
    {
        var engine=new BlockingLayout();TreemapViewModel? vm=null;Task old=Task.CompletedTask;List<TreemapNode>? resized=null;
        StaHost.Run(()=>{vm=new TreemapViewModel(engine);vm.UpdateSize(400,300);vm.SetRoot(Tree());engine.Block=true;old=vm.RequestZoomAsync(2);});
        Assert.True(engine.Entered.Wait(TimeSpan.FromSeconds(5)));
        StaHost.Run(()=>{vm!.UpdateSize(600,400);resized=vm.Nodes;});
        engine.Release.Set();Assert.True(SpinWait.SpinUntil(()=>old.IsCompleted,TimeSpan.FromSeconds(5)));
        StaHost.Run(()=>{Assert.Same(resized,vm!.Nodes);Assert.Equal(600,vm.ViewWidth);});
    }

    [Fact]
    public async Task 后台底图不能把旧节点画面提交到新场景()
    {
        TreemapControl? control=null;TreemapNode? blue=null;
        StaHost.Run(()=>
        {
            var red=new TreemapNode {Entry=Tree().Children[0].Children[0],X=0,Y=0,Width=400,Height=300,ColorHex="#AA2222"};
            blue=new TreemapNode {Entry=Tree().Children[1].Children[0],X=0,Y=0,Width=400,Height=300,ColorHex="#2233AA"};
            control=new TreemapControl {Nodes=[red]};control.Measure(new Size(400,300));control.Arrange(new Rect(0,0,400,300));
            typeof(TreemapControl).GetProperty("EnableAsyncRaster",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(control,true);
            Draw(control);control.Nodes=[blue];Draw(control);
        });
        await Task.Delay(300);
        StaHost.Run(()=>
        {
            using var surface=SKSurface.Create(new SKImageInfo(400,300));
            typeof(TreemapControl).GetMethod("OnPaintSurface",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly)!
                .Invoke(control,[null,new SKPaintSurfaceEventArgs(surface,new SKImageInfo(400,300))]);
            using var image=surface.Snapshot();using var bitmap=SKBitmap.FromImage(image);
            Assert.True(bitmap.GetPixel(200,150).Blue>bitmap.GetPixel(200,150).Red);
            Assert.Same(blue,control!.HitTest(200,150));
        });
    }

    [Fact]
    public void 局部标题进退具有短透明度过渡() => StaHost.Run(()=>
    {
        var type=typeof(TreemapControl).Assembly.GetType("DiskPilotX.App.Controls.TreemapSemanticTitles")!;
        var picker=Activator.CreateInstance(type,nonPublic:true)!;var update=type.GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var n=new TreemapNode {Entry=Tree().Children[0],X=20,Y=20,Width=70,Height=60,HeaderHeight=0,ShowDirectoryFrame=false,IsContentLeaf=false};
        float? Pick(long now)
        {
            var list=((System.Collections.IEnumerable)update.Invoke(picker,[new[]{n},2f,0f,0f,400f,300f,now,true])!).Cast<object>().ToArray();
            return list.Length==0?null:(float)list[0].GetType().GetProperty("Opacity")!.GetValue(list[0])!;
        }
        Assert.InRange(Pick(0)!.Value,.05f,.15f);
        Assert.Equal(1f,Pick(150));
        n.Width=52;Assert.Equal(1f,Pick(200));
        Assert.InRange(Pick(250)!.Value,.1f,.9f);
        Assert.Null(Pick(400));
    });

    [Fact]
    public void 后台布局提交不撤销用户随后拖动的平移() => StaHost.Run(()=>
    {
        var entry=Tree().Children[0].Children[0];
        var first=new TreemapNode {Entry=entry,X=0,Y=0,Width=400,Height=300};
        var control=new TreemapControl {Nodes=[first]};control.Measure(new Size(400,300));control.Arrange(new Rect(0,0,400,300));
        void Call(string name,params object[] args)=>typeof(TreemapControl).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(control,args);
        Call("ZoomAt",200f,150f,2f);
        Call("HandleLeftPress",new Point(200,150),1,false);
        Call("HandlePointerMove",new Point(230,170));
        float Pan(string field)=>(float)typeof(TreemapControl).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(control)!;
        float x=Pan("_panX"),y=Pan("_panY");
        control.Nodes=[new TreemapNode {Entry=entry,X=0,Y=0,Width=400,Height=300}];
        Assert.Equal(x,Pan("_panX"));Assert.Equal(y,Pan("_panY"));
        Call("HandlePointerMove",new Point(240,180));
        Assert.Equal(x+10,Pan("_panX"));Assert.Equal(y+10,Pan("_panY"));
        Call("HandleLeftRelease",new Point(240,180));
    });

    internal static FileEntry Tree()
    {
        var root=new FileEntry {Name="根",Path="D:/根",IsDirectory=true,Size=2000};
        for(int i=0;i<2;i++)
        {
            var d=new FileEntry {Name="目录"+i,Path=root.Path+"/目录"+i,Parent=root,IsDirectory=true,Size=1000};root.Children.Add(d);
            for(int j=0;j<40;j++)d.Children.Add(new FileEntry {Name="文件"+j,Path=d.Path+"/文件"+j,Parent=d,Size=25});
        }
        return root;
    }
    private sealed class CountingLayout : ITreemapLayoutEngine
    {
        public int Calls, UiCalls, UiThread;
        public List<TreemapNode> ComputeLayout(FileEntry root,float width,float height,int maxDepth,float zoom=1)
        { Interlocked.Increment(ref Calls); if (Environment.CurrentManagedThreadId == UiThread) Interlocked.Increment(ref UiCalls); return new SquarifiedTreemapLayout().ComputeLayout(root,width,height,maxDepth,zoom); }
    }
}
