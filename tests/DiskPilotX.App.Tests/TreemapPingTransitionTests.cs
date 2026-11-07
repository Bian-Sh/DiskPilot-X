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

public class TreemapPingTransitionTests
{
    [Fact]
    public async Task 稳定布局过渡中外部取消不能遗留半透明截图()
    {
        TreemapControl? control = null; Task<bool>? pending = null;
        using var source = new CancellationTokenSource();
        StaHost.Run(() =>
        {
            var node = new TreemapNode { Entry = new FileEntry { Name = "目标", Path = @"C:\Ping\目标" },
                X = 399.5f, Y = 299.5f, Width = 0.5f, Height = 0.5f };
            control = new TreemapControl { Nodes = [node] }; Size(control);
            pending = (Task<bool>)Call(control, "FocusStableNodeAsync", node, true, source.Token,
                (Func<Func<TreemapNode, float>, CancellationToken, Task<TreemapNode?>>)((_, _) => Task.FromResult<TreemapNode?>(node)))!;
            Assert.NotNull(Field(control, "_focusTransitionImage"));
        });
        await Task.Delay(80); source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending!);
        StaHost.Run(() =>
        {
            Assert.Null(Field(control!, "_focusTransitionImage"));
            Assert.False((bool)Property(control!, "IsFocusAnimating")!);
            Assert.Equal(0f, Field(control!, "_focusTransitionOpacity"));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 稳定布局准备失败或取消必须释放过渡截图(bool cancel)
    {
        TreemapControl? control = null;
        using var source = new CancellationTokenSource();
        var ready = new TaskCompletionSource<TreemapNode?>();
        Task<bool>? pending = null;
        StaHost.Run(() =>
        {
            var node = new TreemapNode { Entry = new FileEntry { Name = "目标", Path = @"C:\Ping\目标" },
                X = 40, Y = 40, Width = 80, Height = 80 };
            control = new TreemapControl { Nodes = [node] }; Size(control);
            pending = (Task<bool>)Call(control, "FocusStableNodeAsync", node, true, source.Token,
                (Func<Func<TreemapNode, float>, CancellationToken, Task<TreemapNode?>>)((_, _) => ready.Task))!;
            Assert.NotNull(Field(control, "_focusTransitionImage"));
        });
        if (cancel) source.Cancel();
        ready.SetResult(null);
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending!);
        else Assert.False(await pending!);
        StaHost.Run(() =>
        {
            Assert.Null(Field(control!, "_focusTransitionImage"));
            Assert.False((bool)Property(control!, "IsFocusAnimating")!);
            Assert.Null(Property(control!, "ActivePingEntry"));
        });
    }

    [Fact]
    public async Task 真实小目标布局先稳定再开始一次定位不在到达后提交新几何()
    {
        TreemapControl? control = null; TreemapViewModel? vm = null; FileEntry? target = null;
        Task<bool> pending = Task.FromResult(false);
        var commits = new List<float>();
        var observations = new List<string>();
        var frames = new List<(float Zoom, float X, float Y)>();
        StaHost.Run(() =>
        {
            Application.Current.Resources["VP.Button"] = new Style(typeof(Button));
            var root = new FileEntry { Name = "根", Path = @"C:\Ping", IsDirectory = true };
            for (int i = 0; i < 80; i++)
            {
                var file = new FileEntry { Name = $"{i:D3}.bin", Path = root.Path + $@"\{i:D3}.bin", Parent = root,
                    Size = (long)(20 * 1024 * 1024 * Math.Pow(0.9, i)) };
                root.Children.Add(file); root.Size += file.Size;
            }
            target = root.Children[^1];
            vm = new TreemapViewModel(new SquarifiedTreemapLayout()); vm.UpdateSize(811, 294.5f); vm.SetRoot(root);
            var view = new TreemapView(); view.SetViewModel(vm); control = (TreemapControl)view.FindName("Treemap");
            control.Measure(new Size(811, 294.5)); control.Arrange(new Rect(0, 0, 811, 294.5));
            vm.PropertyChanged += (_, e) =>
            {
                observations.Add($"{e.PropertyName}: {vm.ViewWidth}x{vm.ViewHeight}, generation={Field(vm, "_layoutGeneration")}");
                if (e.PropertyName == nameof(vm.Nodes)) commits.Add(Viewport(control).Zoom);
            };
            observations.Add($"开始: generation={Field(vm, "_layoutGeneration")}");
            control.SizeChanged += (_, e) => observations.Add($"尺寸事件: {e.PreviousSize} -> {e.NewSize}");
            pending = view.FocusPathAsync(target.Path, true);
        });
        while (!pending.IsCompleted)
        {
            await Task.Delay(12);
            StaHost.Run(() =>
            {
                var v = Viewport(control!);
                var n = control!.Nodes!.First(n => ReferenceEquals(n.Entry, target) && n.AggregatedCount == 0);
                frames.Add((v.Zoom, (n.X + n.Width / 2) * v.Zoom + v.X, (n.Y + n.Height / 2) * v.Zoom + v.Y));
            });
        }
        Assert.True(await pending.WaitAsync(TimeSpan.FromSeconds(6)), string.Join(";", observations));
        await Task.Delay(150);
        StaHost.Run(() =>
        {
            Assert.NotEmpty(commits);
            Assert.All(commits, zoom => Assert.InRange(zoom, 0.999f, 1.001f));
            Assert.Contains(frames, f => f.Zoom > 1.1f && f.Zoom < 15f);
            var moving = frames.Where(f => f.Zoom > 1.01f).ToArray();
            for (int i = 1; i < moving.Length; i++)
            {
                Assert.True(moving[i].X <= moving[i - 1].X + 0.1f, "目标横向反向回摆");
                Assert.True(moving[i].Y <= moving[i - 1].Y + 0.1f, "目标纵向反向回摆");
            }
            Assert.Null(Field(control!, "_focusTransitionImage"));
            Assert.False((bool)Property(control!, "IsFocusAnimating")!);
            Assert.Same(target, Property(control!, "ActivePingEntry"));
            Call(control!, "CancelFocusAnimation");
        });
    }

    [Fact]
    public async Task Ping橙框与普通白色选择分离且后续点击撤销Ping()
    {
        TreemapControl? control=null; Task<bool> ping=Task.FromResult(false);
        StaHost.Run(() =>
        {
            var file = new FileEntry { Name="目标.bin", Path=@"C:\Ping\目标.bin" };
            var node = new TreemapNode { Entry=file, X=50,Y=50,Width=100,Height=100,ColorHex="#557799" };
            control=new TreemapControl { Nodes=[node] }; Size(control);
            ping=control.FocusNodeAsync(node);
        });
        Assert.True(await ping.WaitAsync(TimeSpan.FromSeconds(5)));
        StaHost.Run(() =>
        {
            using var surface=SKSurface.Create(new SKImageInfo(400,300));
            Paint(control!,surface); using var image=surface.Snapshot(); using var bitmap=SKBitmap.FromImage(image);
            Assert.Equal(new SKColor(254,164,0),bitmap.GetPixel(49,100));
            Call(control!,"HandleClick",new Point(100,100),1,false);
            Assert.Null(Property(control!,"ActivePingEntry"));
        });
    }

    [Fact]
    public async Task 层级切换保留旧场景并连续缩放交叠()
    {
        Task<bool> transition=Task.FromResult(false); TreemapControl? control=null;
        StaHost.Run(() =>
        {
            var root=new FileEntry { Name="根",Path=@"C:\Ping",IsDirectory=true };
            var child=new FileEntry { Name="目录",Path=@"C:\Ping\目录",IsDirectory=true,Parent=root };
            control=new TreemapControl();Size(control);Call(control,"SetViewRoot",root,root);
            control.Nodes=[new TreemapNode { Entry=root,X=0,Y=0,Width=400,Height=300,ColorHex="#445566" },
                new TreemapNode { Entry=child,X=100,Y=50,Width=100,Height=80,Depth=1,ColorHex="#557799" }];
            var method=typeof(TreemapControl).GetMethod("ChangeFocusRootAsync",BindingFlags.Instance|BindingFlags.NonPublic)!;
            transition=(Task<bool>)method.Invoke(control,[child,(Func<Task<bool>>)(() =>
            {
                Call(control,"SetViewRoot",child,root);
                control.Nodes=[new TreemapNode { Entry=child,X=0,Y=0,Width=400,Height=300,ColorHex="#889955" }];
                return Task.FromResult(true);
            }),CancellationToken.None])!;
            Assert.NotNull(Field(control,"_focusTransitionImage"));
            Assert.True((bool)Property(control,"IsFocusAnimating")!);
            Assert.InRange(Viewport(control).Zoom,0.01f,0.99f);
        });
        await Task.Delay(100);
        StaHost.Run(() => { Assert.InRange(Viewport(control!).Zoom,0.3f,0.999f); });
        Assert.True(await transition.WaitAsync(TimeSpan.FromSeconds(5)));
        StaHost.Run(() => { Assert.Equal((1f,0f,0f),Viewport(control!));Assert.Null(Field(control!,"_focusTransitionImage")); });
    }

    [Fact]
    public async Task 层级外目录Ping完成后仍指向真实目录而不是父层聚合块()
    {
        TreemapViewModel? vm=null;TreemapControl? control=null;Task<bool> pending=Task.FromResult(false);FileEntry? target=null;var observations=new List<string>();
        StaHost.Run(() =>
        {
            Application.Current.Resources["VP.Button"]=new Style(typeof(Button));
            var root=new FileEntry {Name="根",Path=@"C:\Ping",IsDirectory=true,Size=200};
            var left=new FileEntry {Name="左",Path=@"C:\Ping\左",IsDirectory=true,Parent=root,Size=100};
            var right=new FileEntry {Name="右",Path=@"C:\Ping\右",IsDirectory=true,Parent=root,Size=100};
            root.Children.Add(left);root.Children.Add(right);
            left.Children.Add(new FileEntry {Name="一.bin",Path=left.Path+@"\一.bin",Parent=left,Size=100});
            target=new FileEntry {Name="目标.bin",Path=right.Path+@"\目标.bin",Parent=right,Size=100};right.Children.Add(target);
            vm=new TreemapViewModel(new SquarifiedTreemapLayout());vm.UpdateSize(400,300);vm.SetRoot(root);vm.NavigateToEntry(left);
            var view=new TreemapView();view.SetViewModel(vm);control=(TreemapControl)view.FindName("Treemap");Size(control);
            vm.PropertyChanged+=(_,e)=>observations.Add($"{e.PropertyName}: {vm.CurrentRoot?.Path}, {vm.ViewWidth}x{vm.ViewHeight}");
            typeof(TreemapControl).GetEvent("FocusInterrupted",BindingFlags.Instance|BindingFlags.NonPublic)!.GetAddMethod(true)!.Invoke(control,[(EventHandler)((_,_)=>observations.Add("中断"))]);
            pending=view.FocusPathAsync(target.Path,true);
        });
        var success=await pending.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.True(success,string.Join(";",observations));
        StaHost.Run(() =>
        {
            Assert.Same(target,Property(control!,"ActivePingEntry"));
            Assert.Same(target,vm!.SelectedNode!.Entry);
            Assert.Contains(control!.Nodes!,n=>ReferenceEquals(n.Entry,target)&&n.AggregatedCount==0);
            Assert.InRange((float)Field(control, "_pingOpacity")!, 0.8f, 1f);
        });
    }

    [Fact]
    public async Task 不存在路径不会留下已释放取消源()
    {
        TreemapView? view=null;
        StaHost.Run(() =>
        {
            Application.Current.Resources["VP.Button"]=new Style(typeof(Button));
            view=new TreemapView();view.SetViewModel(new TreemapViewModel(new SquarifiedTreemapLayout()));
        });
        Task<bool>? pending=null;
        StaHost.Run(()=>pending=view!.FocusPathAsync(@"C:\不存在\目标",true));
        Assert.False(await pending!);
        StaHost.Run(() =>
        {
            Assert.Null(Field(view!,"_focusRequest"));
            Call((TreemapControl)view!.FindName("Treemap"),"HandleClick",new Point(0,0),1,false);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 新定位失败也立即清除旧Ping而保留普通选择(bool scanActivationFails)
    {
        TreemapView? view = null; TreemapControl? control = null; FileEntry? root = null;
        Task<bool> first = Task.FromResult(false), missing = Task.FromResult(true);
        StaHost.Run(() =>
        {
            Application.Current.Resources["VP.Button"] = new Style(typeof(Button));
            root = new FileEntry { Name = "根", Path = @"C:\Ping", IsDirectory = true, Size = 100 };
            var file = new FileEntry { Name = "目标", Path = @"C:\Ping\目标", Parent = root, Size = 100 };
            root.Children.Add(file);
            var vm = new TreemapViewModel(new SquarifiedTreemapLayout()); vm.UpdateSize(400, 300); vm.SetRoot(root);
            view = new TreemapView(); view.SetViewModel(vm); control = (TreemapControl)view.FindName("Treemap"); Size(control);
            first = view.FocusPathAsync(file.Path, true);
        });
        Assert.True(await first.WaitAsync(TimeSpan.FromSeconds(5)));
        StaHost.Run(() =>
        {
            Assert.NotNull(Property(control!, "ActivePingEntry"));
            Assert.Equal(1f, (float)Property(control!, "PingOpacity")!);
            var selected = control!.SelectedNode;
            var viewport = Viewport(control);
            missing = scanActivationFails
                ? view!.FocusScanPathAsync(root!, () => false, @"C:\Ping\不存在", true)
                : view!.FocusPathAsync(@"C:\Ping\不存在", true);
            Assert.Null(Property(control, "ActivePingEntry"));
            Assert.Equal(0f, (float)Property(control, "PingOpacity")!);
            Assert.Equal(0f, (float)Property(control, "PingWaveOpacity")!);
            Assert.False((bool)Property(control, "IsFocusAnimating")!);
            Assert.Same(selected, control.SelectedNode);
            Assert.Equal(viewport, Viewport(control));
        });
        Assert.False(await missing);
    }

    [Fact]
    public async Task 新Ping或用户操作取消旧动画不会弹错误或回弹()
    {
        TreemapView? view=null;TreemapControl? control=null;Task<bool> first=Task.FromResult(false),second=Task.FromResult(false);
        StaHost.Run(() =>
        {
            Application.Current.Resources["VP.Button"]=new Style(typeof(Button));
            var root=new FileEntry {Name="根",Path=@"C:\Ping",IsDirectory=true,Size=10004};
            var big=new FileEntry {Name="大",Path=root.Path+@"\大",Parent=root,Size=10000};
            var small=new FileEntry {Name="小",Path=root.Path+@"\小",Parent=root,Size=4};root.Children.Add(big);root.Children.Add(small);
            var vm=new TreemapViewModel(new SquarifiedTreemapLayout());vm.UpdateSize(400,300);vm.SetRoot(root);
            vm.Nodes=[new TreemapNode {Entry=big,X=0,Y=0,Width=80,Height=80,ColorHex="#557799"},
                new TreemapNode {Entry=small,X=100,Y=70,Width=4,Height=4,ColorHex="#557799"}];
            view=new TreemapView();view.SetViewModel(vm);control=(TreemapControl)view.FindName("Treemap");Size(control);
            first=view.FocusPathAsync(small.Path,true);
        });
        await Task.Delay(70);
        StaHost.Run(() => second=view!.FocusPathAsync(@"C:\Ping\大",true));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>first);
        Assert.True(await second.WaitAsync(TimeSpan.FromSeconds(5)));
        StaHost.Run(()=>Assert.Equal(@"C:\Ping\大",((FileEntry)Property(control!,"ActivePingEntry")!).Path));
        await Task.Delay(500);
        StaHost.Run(()=>Assert.Equal(@"C:\Ping\大",((FileEntry)Property(control!,"ActivePingEntry")!).Path));
    }

    [Fact]
    public async Task 聚合长尾中的真实文件仍可Ping而不是误标父目录()
    {
        Task<bool> pending=Task.FromResult(false);TreemapControl? control=null;FileEntry? target=null;
        StaHost.Run(() =>
        {
            Application.Current.Resources["VP.Button"]=new Style(typeof(Button));
            var root=new FileEntry {Name="根",Path=@"C:\Ping",IsDirectory=true,Size=1_000_000_004};
            root.Children.Add(new FileEntry {Name="大文件",Path=root.Path+@"\大文件",Parent=root,Size=1_000_000_000});
            for(int i=0;i<4;i++)root.Children.Add(new FileEntry {Name=$"长尾{i}",Path=root.Path+$@"\长尾{i}",Parent=root,Size=1});
            target=root.Children[2];
            var vm=new TreemapViewModel(new SquarifiedTreemapLayout());vm.UpdateSize(400,300);vm.SetRoot(root);
            Assert.DoesNotContain(vm.Nodes!,n=>ReferenceEquals(n.Entry,target));
            var view=new TreemapView();view.SetViewModel(vm);control=(TreemapControl)view.FindName("Treemap");Size(control);
            pending=view.FocusPathAsync(target.Path,true);
        });
        Assert.True(await pending.WaitAsync(TimeSpan.FromSeconds(6)));
        StaHost.Run(() =>
        {
            Assert.Same(target,Property(control!,"ActivePingEntry"));
            Assert.Contains(control!.Nodes!,n=>ReferenceEquals(n.Entry,target)&&n.AggregatedCount==0);
        });
    }

    [Fact]
    public async Task 新层级提交但过渡未接管时仍显示旧画面()
    {
        var gate=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        TreemapControl? control=null;Task<bool> transition=Task.FromResult(false);SKColor previous=default;
        StaHost.Run(() =>
        {
            var root=new FileEntry {Name="根",Path=@"C:\Ping",IsDirectory=true};
            var child=new FileEntry {Name="子目录",Path=@"C:\Ping\子目录",IsDirectory=true,Parent=root};
            control=new TreemapControl();Size(control);Call(control,"SetViewRoot",root,root);
            control.Nodes=[new TreemapNode {Entry=new FileEntry {Name="旧文件",Path=@"C:\Ping\旧文件"},X=0,Y=0,Width=400,Height=300,ColorHex="#AA3300"}];
            using(var surface=SKSurface.Create(new SKImageInfo(400,300)))
            {Paint(control,surface);using var image=surface.Snapshot();using var bitmap=SKBitmap.FromImage(image);previous=bitmap.GetPixel(100,100);}
            transition=(Task<bool>)typeof(TreemapControl).GetMethod("ChangeFocusRootAsync",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(control,[child,(Func<Task<bool>>)(() =>
            {
                Call(control,"SetViewRoot",child,root);
                control.Nodes=[new TreemapNode {Entry=new FileEntry {Name="新文件",Path=@"C:\Ping\子目录\新文件"},X=0,Y=0,Width=400,Height=300,ColorHex="#00AA00"}];
                return gate.Task;
            }),CancellationToken.None])!;
            Assert.NotNull(Field(control,"_focusTransitionImage"));
            using var current=SKSurface.Create(new SKImageInfo(400,300));Paint(control,current);using var currentImage=current.Snapshot();using var currentBitmap=SKBitmap.FromImage(currentImage);
            Assert.Equal(previous,currentBitmap.GetPixel(100,100));
            gate.SetResult(true);
        });
        Assert.True(await transition.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task 缩放中目标锚点不会先飘出视口再拉回()
    {
        TreemapControl? control=null;Task<bool> ping=Task.FromResult(false);
        var node=new TreemapNode { Entry=new FileEntry {Name="小目标",Path=@"C:\Ping\小目标"},X=100,Y=70,Width=4,Height=4,ColorHex="#557799"};
        StaHost.Run(() => {control=new TreemapControl {Nodes=[node]};Size(control);ping=control.FocusNodeAsync(node);});
        await Task.Delay(110);
        StaHost.Run(() =>
        {
            var v=Viewport(control!);
            Assert.InRange((node.X+2)*v.Zoom+v.X,101f,103f);
            Assert.InRange((node.Y+2)*v.Zoom+v.Y,71f,73f);
        });
        Assert.True(await ping.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void 用户打断缩入层级中间帧后仍能平移不抛异常() => StaHost.Run(() =>
    {
        var control=new TreemapControl();Size(control);
        typeof(TreemapControl).GetField("_zoom",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(control,0.4f);
        typeof(TreemapControl).GetField("_panX",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(control,120f);
        Call(control,"InterruptFocus");
        Call(control,"ClampPan");
        Assert.InRange(Viewport(control).X,0,240);
    });

    [Fact]
    public async Task Ping停稳前不出现冲击且八百毫秒结束保留普通选择()
    {
        TreemapControl? control = null; Task<bool> pending = Task.FromResult(false);
        var node = new TreemapNode { Entry = new FileEntry { Name = "角落小文件", Path = @"C:\Ping\角落小文件" },
            X = 399.5f, Y = 299.5f, Width = 0.5f, Height = 0.5f, ColorHex = "#557799" };
        StaHost.Run(() =>
        {
            control = new TreemapControl { Nodes = [node] }; Size(control);
            pending = control.FocusNodeAsync(node);
        });
        await Task.Delay(110);
        StaHost.Run(() => Assert.Equal(0f, Field(control!, "_pingOpacity")));
        Assert.True(await pending.WaitAsync(TimeSpan.FromSeconds(5)));
        StaHost.Run(() =>
        {
            Assert.Same(node.Entry, Property(control!, "ActivePingEntry"));
            Assert.InRange((float)Field(control!, "_pingOpacity")!, 0.8f, 1f);
            var viewport = Viewport(control!);
            Assert.InRange(node.X * viewport.Zoom + viewport.X, 11.9f, 388f);
            Call(control!, "ClampPan");
            Assert.Equal(viewport, Viewport(control!));
        });
        await Task.Delay(1000);
        StaHost.Run(() =>
        {
            Assert.Null(Property(control!, "ActivePingEntry"));
            Assert.Equal(0f, Field(control!, "_pingOpacity"));
            Assert.Same(node, control!.SelectedNode);
        });
    }

    [Theory]
    [InlineData(0, 1f)]
    [InlineData(80, 1f)]
    [InlineData(440, 0.3535534f)]
    [InlineData(800, 0f)]
    [InlineData(1000, 0f)]
    public void Ping冲击后只衰减且八百毫秒消失(int elapsed, float expected) => StaHost.Run(() =>
    {
        var method = typeof(TreemapControl).GetMethod("CalculatePingOpacity", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        Assert.InRange((float)method!.Invoke(null, [(double)elapsed])!, expected - 0.001f, expected + 0.001f);
    });

    [Fact]
    public void Ping边框一次压实后光晕向外扩散() => StaHost.Run(() =>
    {
        var method = typeof(TreemapControl).GetMethod("CalculatePingImpact", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        (float StrokeWidth, float WaveOffset, float WaveOpacity) At(double ms) =>
            ((float, float, float))method!.Invoke(null, [ms])!;
        var start = At(0); var hit = At(80); var spread = At(440); var end = At(800);
        Assert.True(hit.StrokeWidth > start.StrokeWidth);
        Assert.True(hit.WaveOffset < start.WaveOffset);
        Assert.True(spread.WaveOffset > hit.WaveOffset);
        Assert.True(spread.WaveOpacity < hit.WaveOpacity);
        Assert.Equal(0f, end.WaveOpacity);
        float previous = 1;
        var opacity = typeof(TreemapControl).GetMethod("CalculatePingOpacity", BindingFlags.Static | BindingFlags.NonPublic)!;
        for (int ms = 80; ms <= 800; ms += 16)
        {
            float current = (float)opacity.Invoke(null, [(double)ms])!;
            Assert.True(current <= previous);
            previous = current;
        }
    });

    [Fact]
    public void 冲击扩散渲染有外沿光晕但不移动Cell() => StaHost.Run(() =>
    {
        var node = new TreemapNode { Entry = new FileEntry { Name = "目标.bin", Path = @"C:\Ping\目标.bin" },
            X = 50, Y = 50, Width = 100, Height = 100, ColorHex = "#557799" };
        var control = new TreemapControl { Nodes = [node] }; Size(control);
        var method = typeof(TreemapControl).GetMethod("CalculatePingImpact", BindingFlags.Static | BindingFlags.NonPublic)!;
        var opacity = typeof(TreemapControl).GetMethod("CalculatePingOpacity", BindingFlags.Static | BindingFlags.NonPublic)!;
        var hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(TreemapControl).GetField("_pingNode", hidden)!.SetValue(control, node);
        var pixels = new Dictionary<int, SKColor>();
        var output = Environment.GetEnvironmentVariable("DISKPILOT_PING_FRAME_DIR");
        if (output is not null) System.IO.Directory.CreateDirectory(output);
        for (int elapsed = 0; elapsed <= 800; elapsed += 80)
        {
            var (width, offset, wave) = ((float, float, float))method.Invoke(null, [(double)elapsed])!;
            foreach (var (name, value) in new[] { ("_pingStrokeWidth", width), ("_pingWaveOffset", offset),
                ("_pingWaveOpacity", wave), ("_pingOpacity", (float)opacity.Invoke(null, [(double)elapsed])!) })
                typeof(TreemapControl).GetField(name, hidden)!.SetValue(control, value);
            using var surface = SKSurface.Create(new SKImageInfo(400, 300));
            Paint(control, surface); using var image = surface.Snapshot(); using var bitmap = SKBitmap.FromImage(image);
            pixels[elapsed] = bitmap.GetPixel(43, 100);
            if (output is not null)
            {
                using var png = image.Encode(SKEncodedImageFormat.Png, 100);
                using var stream = System.IO.File.Create(System.IO.Path.Combine(output, $"impact-{elapsed:D3}.png"));
                png.SaveTo(stream);
            }
            Assert.Equal((1f, 0f, 0f), Viewport(control));
            Assert.Equal(50f, node.X); Assert.Equal(100f, node.Width);
        }
        Assert.True(pixels[400].Red > pixels[80].Red);
        Assert.Equal(new SKColor(27, 36, 48), pixels[800]);
    });

    [Fact]
    public async Task 控件卸载停止Ping提示并释放目标()
    {
        TreemapControl? control = null; Task<bool> pending = Task.FromResult(false);
        StaHost.Run(() =>
        {
            var node = new TreemapNode { Entry = new FileEntry { Name = "文件", Path = @"C:\Ping\文件" },
                X = 40, Y = 40, Width = 80, Height = 80 };
            control = new TreemapControl { Nodes = [node] }; Size(control); pending = control.FocusNodeAsync(node);
        });
        Assert.True(await pending);
        StaHost.Run(() =>
        {
            control!.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            Assert.Null(Property(control, "ActivePingEntry"));
            Assert.False(((System.Windows.Threading.DispatcherTimer)Field(control, "_pingTimer")!).IsEnabled);
        });
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public async Task 右下角极小文件在不同DPI下放大但允许边界裁剪(float dpi)
    {
        TreemapControl? control = null; Task<bool> pending = Task.FromResult(false);
        var node = new TreemapNode { Entry = new FileEntry { Name = "角落", Path = @"C:\Ping\角落" },
            X = 399.5f, Y = 299.5f, Width = 0.5f, Height = 0.5f, ColorHex = "#557799" };
        StaHost.Run(() => { control = new TreemapControl { Nodes = [node] }; Size(control); pending = control.FocusNodeAsync(node); });
        Assert.True(await pending);
        StaHost.Run(() =>
        {
            var viewport = Viewport(control!);
            var rect = new SKRect(node.X * viewport.Zoom + viewport.X, node.Y * viewport.Zoom + viewport.Y,
                (node.X + node.Width) * viewport.Zoom + viewport.X, (node.Y + node.Height) * viewport.Zoom + viewport.Y);
            Assert.Equal(400f, rect.Right);
            Assert.Equal(300f, rect.Bottom);
            Assert.InRange(rect.Width, 31.9f, 32.1f);
            var info = new SKImageInfo((int)(400 * dpi), (int)(300 * dpi));
            using var surface = SKSurface.Create(info);
            typeof(TreemapControl).GetMethod("OnPaintSurface", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!
                .Invoke(control, [null, new SKPaintSurfaceEventArgs(surface, info)]);
            using var image = surface.Snapshot(); using var bitmap = SKBitmap.FromImage(image);
            // 内侧两边保留可见提示，贴地图边缘的外沿允许自然裁剪。
            foreach (var point in new[] { new SKPoint(rect.Left - 1.5f, rect.MidY), new SKPoint(rect.MidX, rect.Top - 1.5f) })
            {
                var color = bitmap.GetPixel((int)(point.X * dpi), (int)(point.Y * dpi));
                Assert.InRange(color.Red, (byte)230, (byte)254);
                Assert.InRange(color.Green, (byte)145, (byte)170);
                Assert.InRange(color.Blue, (byte)0, (byte)25);
            }
            typeof(TreemapControl).GetField("_panX", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(control, -100000f);
            typeof(TreemapControl).GetField("_panY", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(control, 100000f);
            Call(control!, "ClampPan");
            Assert.Equal(400 * (1 - viewport.Zoom), Viewport(control!).X);
            Assert.Equal(0f, Viewport(control!).Y);
            Call(control!, "CancelFocusAnimation");
        });
    }

    [Fact]
    public async Task 贴边目标的普通Ping缓动帧也不能越过地图边界()
    {
        TreemapControl? control = null; Task<bool> pending = Task.FromResult(false);
        var node = new TreemapNode { Entry = new FileEntry { Name = "角落", Path = @"C:\Ping\角落" },
            X = 399.5f, Y = 299.5f, Width = 0.5f, Height = 0.5f, ColorHex = "#557799" };
        var frames = new List<(float Zoom, float X, float Y)>();
        StaHost.Run(() => { control = new TreemapControl { Nodes = [node] }; Size(control); pending = control.FocusNodeAsync(node); });
        while (!pending.IsCompleted)
        {
            await Task.Delay(8);
            StaHost.Run(() => frames.Add(Viewport(control!)));
        }
        Assert.True(await pending);
        Assert.Contains(frames, frame => frame.Zoom > 1 && frame.Zoom < 63);
        foreach (var frame in frames)
        {
            Assert.InRange(frame.X, 400 * (1 - frame.Zoom) - 0.03f, 0.03f);
            Assert.InRange(frame.Y, 300 * (1 - frame.Zoom) - 0.03f, 0.03f);
        }
        StaHost.Run(() => Call(control!, "CancelFocusAnimation"));
    }

    [Fact]
    public async Task 新Ping重新计时不会被上一轮消失时间截断()
    {
        TreemapControl? control = null; Task<bool> pending = Task.FromResult(false);
        var first = new TreemapNode { Entry = new FileEntry { Name = "第一项", Path = @"C:\Ping\第一项" }, X = 40, Y = 40, Width = 80, Height = 80 };
        var second = new TreemapNode { Entry = new FileEntry { Name = "第二项", Path = @"C:\Ping\第二项" }, X = 150, Y = 40, Width = 80, Height = 80 };
        StaHost.Run(() => { control = new TreemapControl { Nodes = [first, second] }; Size(control); pending = control.FocusNodeAsync(first); });
        Assert.True(await pending);
        await Task.Delay(600);
        StaHost.Run(() => pending = control!.FocusNodeAsync(second));
        Assert.True(await pending);
        await Task.Delay(550);
        StaHost.Run(() =>
        {
            Assert.Same(second.Entry, Property(control!, "ActivePingEntry"));
            Assert.InRange((float)Field(control!, "_pingOpacity")!, 0.05f, 1f);
            Call(control!, "CancelFocusAnimation");
        });
    }

    private static void Size(TreemapControl control) { control.Measure(new Size(400,300));control.Arrange(new Rect(0,0,400,300)); }
    private static object? Property(object o,string name)=>o.GetType().GetProperty(name,BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(o);
    private static object? Field(object o,string name)=>o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(o);
    private static (float Zoom,float X,float Y) Viewport(TreemapControl c)=>((float,float,float))Property(c,"Viewport")!;
    private static object? Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(o,args);
    private static void Paint(TreemapControl control,SKSurface surface)=>typeof(TreemapControl).GetMethod("OnPaintSurface",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly)!.Invoke(control,[null,new SKPaintSurfaceEventArgs(surface,new SKImageInfo(400,300))]);
}
