using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using DiskPilotX.App.Views;
using DiskPilotX.App.Controls;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Treemap;
using SkiaSharp;

namespace DiskPilotX.App.Tests;

public class TreemapSelectionPingTests
{
    [Fact]
    public void Treemap右键不显示Ping调试入口但保留手动推荐() => StaHost.Run(() =>
    {
        Application.Current.Resources["VP.Button"] = new Style(typeof(Button));
        var view = new TreemapView { AddRecommendationsAsync = _ => Task.CompletedTask };
        var menu = (ContextMenu)typeof(TreemapView).GetMethod("BuildContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(view, [new FileEntry { Name = "目标", Path = @"C:\Ping\目标" }])!;
        Assert.DoesNotContain(menu.Items.OfType<MenuItem>(), item => item.Header?.ToString()?.Contains("Ping") == true);
        Assert.Contains(menu.Items.OfType<MenuItem>(), item => Equals(item.Header, "加入推荐清理") && item.IsEnabled);
    });

    [Fact]
    public void Ctrl增加选择保留首次单选() => StaHost.Run(() =>
    {
        var (control, first, second) = Map();
        Call(control, "HandleClick", new Point(30, 30), 1, false);
        Call(control, "HandleClick", new Point(130, 30), 1, true);
        Assert.Equal(2, control.MultiSelection.Count);
        Assert.Contains(first, control.MultiSelection);
        Assert.Contains(second, control.MultiSelection);
    });

    [Fact]
    public void 右键未选项切换而不是沿用旧集合() => StaHost.Run(() =>
    {
        var (control, _, second) = Map();
        Call(control, "HandleClick", new Point(30, 30), 1, true);
        Call(control, "HandleContextMenu", new Point(130, 30));
        Assert.DoesNotContain(control.MultiSelection, n => n != second);
        Assert.Same(second, control.SelectedNode);
    });

    [Fact]
    public void 普通点击已经多选的项目仍回到单选() => StaHost.Run(() =>
    {
        var (control, first, _) = Map();
        Call(control, "HandleClick", new Point(30, 30), 1, true);
        Call(control, "HandleClick", new Point(130, 30), 1, true);
        Call(control, "HandleClick", new Point(30, 30), 1, false);
        Assert.True(control.MultiSelection.Count <= 1);
        Assert.Same(first, control.SelectedNode);
    });

    [Fact]
    public void 当前地图能展示目标就不钻取目录() => StaHost.Run(() =>
    {
        var root = new FileEntry { Name="根", Path=@"C:\Ping", IsDirectory=true, Size=100 };
        var child = new FileEntry { Name="目录", Path=@"C:\Ping\目录", IsDirectory=true, Size=100, Parent=root };
        var file = new FileEntry { Name="文件.bin", Path=@"C:\Ping\目录\文件.bin", Size=100, Parent=child };
        root.Children.Add(child); child.Children.Add(file);
        var vm = new TreemapViewModel(new SquarifiedTreemapLayout()); vm.UpdateSize(400, 300); vm.SetRoot(root);
        Assert.NotNull(vm.FocusPath(child.Path, true));
        Assert.Same(root, vm.CurrentRoot);
    });

    [Fact]
    public void Ping开始不会瞬间复位当前视口() => StaHost.Run(() =>
    {
        var (control, first, _) = Map();
        Field(control, "_zoom", 2f); Field(control, "_panX", -100f);
        control.FocusNode(first);
        Assert.Equal((2f, -100f, 0f), Viewport(control));
        Call(control, "CancelFocusAnimation");
    });

    [Fact]
    public void 已可见且合适的目标不强制居中() => StaHost.Run(() =>
    {
        var result = Target(new SKRect(50, 40, 110, 100), 2, -40, -20, 400, 300);
        Assert.Equal((2f, -40f, -20f), result);
    });

    [Fact]
    public void 小目标放大且离屏目标最少平移() => StaHost.Run(() =>
    {
        var result = Target(new SKRect(190, 120, 194, 124), 1, 0, 0, 400, 300);
        Assert.True(result.Zoom > 1);
        Assert.InRange(190 * result.Zoom + result.X, 0, 400);
        Assert.InRange(194 * result.Zoom + result.X, 0, 400);
        Assert.InRange(124 * result.Zoom + result.Y, 0, 300);
        var visible = Target(new SKRect(30, 30, 60, 60), 2, -10, -10, 400, 300);
        Assert.Equal((2f, -10f, -10f), visible);
    });

    [Fact]
    public void 大目标缩小且细长目标优先完整可见() => StaHost.Run(() =>
    {
        var result = Target(new SKRect(30, 30, 330, 250), 5, -100, -100, 400, 300);
        Assert.True(result.Zoom < 5);
        Assert.True(300 * result.Zoom <= 400);
        Assert.True(220 * result.Zoom <= 300);
        var thin = Target(new SKRect(10, 10, 390, 11), 1, 0, 0, 400, 300);
        Assert.True(380 * thin.Zoom <= 400);
    });

    [Theory]
    [InlineData(399.5f, 299.5f, 400f, 300f)]
    [InlineData(0f, 0f, 0.5f, 0.5f)]
    public void 角落极小目标可超过八倍但不越过地图边界(float left, float top, float right, float bottom) => StaHost.Run(() =>
    {
        var target = Target(new SKRect(left, top, right, bottom), 1, 0, 0, 400, 300);
        Assert.True(target.Zoom > 8);
        Assert.InRange((right - left) * target.Zoom, 31.9f, 32.1f);
        Assert.InRange(target.X, 400 * (1 - target.Zoom), 0);
        Assert.InRange(target.Y, 300 * (1 - target.Zoom), 0);
        Assert.Equal(left == 0 ? 0f : 400f, (left == 0 ? left : right) * target.Zoom + target.X);
        Assert.Equal(top == 0 ? 0f : 300f, (top == 0 ? top : bottom) * target.Zoom + target.Y);
    });

    [Fact]
    public void 高倍率Ping后滚轮缩小不硬切到八倍() => StaHost.Run(() =>
    {
        var (control, _, _) = Map();
        Field(control, "_zoom", 64f);
        Field(control, "_panX", -25000f); Field(control, "_panY", -18500f);
        Call(control, "ZoomAt", 200f, 150f, 0.8f);
        Assert.InRange(Viewport(control).Zoom, 51.19f, 51.21f);
    });

    [Fact]
    public void Ping缓动存在中间帧且用户点击可以取消()
    {
        TreemapControl? control = null;
        StaHost.Run(() =>
        {
            (control, _, _) = Map();
            var target = control.Nodes![1]; target.Width=4; target.Height=4;
            control.FocusNode(target);
        });
        Thread.Sleep(110);
        StaHost.Run(() =>
        {
            Assert.True(Viewport(control!).Zoom > 1);
            Assert.True((bool)Property(control!, "IsFocusAnimating")!);
            Call(control!, "HandleClick", new Point(20, 20), 1, false);
            Assert.False((bool)Property(control!, "IsFocusAnimating")!);
        });
    }

    [Fact]
    public async Task 右键已选子文件按父子去重后的完整集合加入推荐()
    {
        var clicked=Task.FromResult(false);IReadOnlyList<FileEntry>? added=null;
        StaHost.Run(() =>
        {
            Application.Current.Resources["VP.Button"]=new Style(typeof(Button));
            var parent=new FileEntry {Name="父目录",Path=@"C:\Ping\父目录",IsDirectory=true};
            var child=new FileEntry {Name="子文件",Path=parent.Path+@"\子文件",Parent=parent};
            var other=new FileEntry {Name="另一项",Path=@"C:\Ping\另一项"};parent.Children.Add(child);
            var nodes=new List<TreemapNode>
            {
                new(){Entry=parent,X=0,Y=0,Width=100,Height=100},
                new(){Entry=child,X=100,Y=0,Width=100,Height=100},
                new(){Entry=other,X=200,Y=0,Width=100,Height=100}
            };
            var view=new TreemapView();var control=(TreemapControl)view.FindName("Treemap");control.Nodes=nodes;
            control.Measure(new Size(400,300));control.Arrange(new Rect(0,0,400,300));
            view.AddRecommendationsAsync=entries=>{added=entries;return Task.CompletedTask;};
            Call(control,"HandleClick",new Point(30,30),1,true);
            Call(control,"HandleClick",new Point(130,30),1,true);
            Call(control,"HandleClick",new Point(230,30),1,true);
            var menu=(ContextMenu)typeof(TreemapView).GetMethod("BuildContextMenu",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(view,[child])!;
            var add=menu.Items.OfType<MenuItem>().First(item=>item.Header?.ToString()?.Contains("加入推荐清理")==true);
            Assert.Equal("将 2 项加入推荐清理",add.Header);
            add.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(new[]{parent,other},added);
            clicked=Task.FromResult(true);
        });
        Assert.True(await clicked);
    }

    private static (TreemapControl, TreemapNode, TreemapNode) Map()
    {
        var root = new FileEntry { Name="根", Path=@"C:\Ping", IsDirectory=true };
        var first = new TreemapNode { Entry=new FileEntry { Name="一.bin", Path=@"C:\Ping\一.bin", Parent=root }, X=0, Y=0, Width=100, Height=100, ColorHex="#557799" };
        var second = new TreemapNode { Entry=new FileEntry { Name="二.bin", Path=@"C:\Ping\二.bin", Parent=root }, X=100, Y=0, Width=100, Height=100, ColorHex="#557799" };
        var control = new TreemapControl { Nodes=[first, second] };
        control.Measure(new Size(400,300)); control.Arrange(new Rect(0,0,400,300));
        return (control, first, second);
    }
    private static (float Zoom,float X,float Y) Target(SKRect rect,float z,float x,float y,float w,float h)
    {
        var method=typeof(TreemapControl).GetMethod("CalculateFocusViewport",BindingFlags.Static|BindingFlags.NonPublic);
        Assert.NotNull(method);
        return ((float,float,float))method!.Invoke(null,[rect,z,x,y,w,h])!;
    }
    private static object? Call(object o,string name,params object[] args)
    {
        var method=o.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic);
        Assert.NotNull(method); return method!.Invoke(o,args);
    }
    private static void Field(object o,string name,object value) => o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(o,value);
    private static object? Property(object o,string name) => o.GetType().GetProperty(name,BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(o);
    private static (float Zoom,float X,float Y) Viewport(TreemapControl c) => ((float,float,float))Property(c,"Viewport")!;
}
