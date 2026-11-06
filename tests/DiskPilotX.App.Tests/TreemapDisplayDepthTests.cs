using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using DiskPilotX.App.Controls;
using DiskPilotX.App.ViewModels;
using DiskPilotX.App.Views;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Treemap;

namespace DiskPilotX.App.Tests;

public class TreemapDisplayDepthTests
{
    [Fact]
    public void 深度设置默认自动且非法值回自动()
    {
        var property = typeof(AppSettings).GetProperty("TreemapDisplayDepth");
        Assert.NotNull(property);
        var settings = new AppSettings();
        Assert.Equal(0, property!.GetValue(settings));
        foreach (int invalid in new[] { -1, 7, 64 })
        {
            property.SetValue(settings, invalid);
            Assert.Equal(0, property.GetValue(settings));
        }
    }

    [Fact]
    public void 节点与空白均有互斥深度菜单() => StaHost.Run(() =>
    {
        Application.Current.Resources["VP.Button"] = new Style(typeof(Button));
        var view = new TreemapView();
        view.SetViewModel(new TreemapViewModel(new SquarifiedTreemapLayout()));
        var method = typeof(TreemapView).GetMethod("BuildContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (FileEntry? entry in new FileEntry?[] { Tree(), null })
        {
            var menu = (ContextMenu)method.Invoke(view, [entry])!;
            var depth = menu.Items.OfType<MenuItem>().SingleOrDefault(i => Equals(i.Header, "显示深度"));
            Assert.NotNull(depth);
            Assert.Equal(7, depth!.Items.Count);
            Assert.Single(depth.Items.OfType<MenuItem>().Where(i => i.IsChecked));
            Assert.All(depth.Items.OfType<MenuItem>(), i => Assert.True(i.IsCheckable));
        }
    });

    [Fact]
    public void 隐藏已选项不改成父目录且恢复可见后高亮真实对象() => StaHost.Run(() =>
    {
        var root = Tree(); var engine = new SquarifiedTreemapLayout();
        var full = engine.ComputeLayout(root, 600, 400, 64);
        var selected = full.First(n => !n.Entry.IsDirectory);
        var control = new TreemapControl { Nodes = full, SelectedNode = selected };
        control.Nodes = engine.ComputeLayout(root, 600, 400, 1);
        Assert.Same(selected.Entry, control.SelectedNode?.Entry);
        control.Nodes = engine.ComputeLayout(root, 600, 400, 64);
        Assert.Same(selected.Entry, control.SelectedNode?.Entry);
        Assert.Contains(control.SelectedNode!, control.Nodes!);
    });

    [Fact]
    public void 深度终止的单链目录仍有紧凑可命中标题()
    {
        var root = Tree();
        var node = new SquarifiedTreemapLayout().ComputeLayout(root, 600, 400, 1)
            .Single(n => n.Depth == 1);
        Assert.Same(root.Children[0], node.Entry);
        Assert.True(node.IsContentLeaf);
        Assert.NotNull(node.Label);
        Assert.True(node.HeaderHeight > 0);
    }

    [Theory]
    [InlineData("-2")]
    [InlineData("7")]
    [InlineData("\"无效旧值\"")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("{}")]
    public void 非法旧深度单独回自动不丢其他设置(string value)
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{\"Language\":\"zh-TW\",\"TreemapDisplayDepth\":" + value + "}");
        Assert.Equal(0, settings!.TreemapDisplayDepth);
        Assert.Equal("zh-TW", settings.Language);
    }

    [Fact]
    public async Task 手动上限随缩放不越界且进入目录相对重计并保存偏好()
    {
        var file = Path.Combine(Path.GetTempPath(), "DiskPilotX深度测试_" + Guid.NewGuid().ToString("N"), "settings.json");
        var settings = new DiskPilotX.Core.Services.Settings.SettingsService(file);
        TreemapViewModel? vm = null; Task pending = Task.CompletedTask;
        var root = Tree();
        StaHost.Run(() =>
        {
            vm = new TreemapViewModel(new SquarifiedTreemapLayout(), settings);
            vm.UpdateSize(600, 400); vm.SetRoot(root); vm.SelectedNode = vm.Nodes!.First(n => !n.Entry.IsDirectory);
            vm.SetZoom(2); pending = vm.SetDisplayDepthAsync(1);
        });
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        StaHost.Run(() =>
        {
            Assert.Equal(2f, vm!.Zoom); Assert.Equal(1, vm.DisplayDepth);
            Assert.Equal(1, vm.Nodes!.Max(n => n.Depth));
            Assert.Same(root.Children[0].Children[0].Children[0], vm.SelectedNode!.Entry);
            vm.SetZoom(8); Assert.Equal(1, vm.Nodes!.Max(n => n.Depth));
            vm.DrillDown(root.Children[0]);
            Assert.Equal(0, vm.Nodes!.First().Depth);
            Assert.Contains(vm.Nodes, n => ReferenceEquals(n.Entry, root.Children[0].Children[0]) && n.Depth == 1);
            Assert.Equal(1, new TreemapViewModel(new SquarifiedTreemapLayout(), settings).DisplayDepth);
            Assert.Equal(2, root.Children[0].Children[0].Children.Count);
        });
    }

    [Fact]
    public async Task 切深度不重新套用旧滚轮锚点或清空多选()
    {
        TreemapViewModel? vm=null; TreemapControl? control=null; Task pending=Task.CompletedTask;
        (float Zoom,float X,float Y) before=default;
        FileEntry? selected=null;var observations=new List<string>();
        StaHost.Run(() =>
        {
            Application.Current.Resources["VP.Button"]=new Style(typeof(Button));
            vm=new TreemapViewModel(new SquarifiedTreemapLayout()); vm.UpdateSize(600,400);vm.SetRoot(Tree());
            vm.PropertyChanged+=(_,e)=>observations.Add($"{e.PropertyName}: {vm.ViewWidth}×{vm.ViewHeight}, 选择={vm.SelectedNode?.Entry.Name}");
            var view=new TreemapView();view.SetViewModel(vm);control=(TreemapControl)view.FindName("Treemap");
            control.Measure(new Size(600,400));control.Arrange(new Rect(0,0,600,400));control.UpdateLayout();
            typeof(TreemapControl).GetField("_zoom",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(control,2f);
            typeof(TreemapControl).GetField("_panX",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(control,-100f);
            typeof(TreemapControl).GetField("_panY",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(control,-80f);
            vm.Zoom=2f;
            var node=vm.Nodes!.First(n=>!n.Entry.IsDirectory);selected=node.Entry;vm.SelectedNode=node;
            typeof(TreemapControl).GetMethod("HandleClick",BindingFlags.Instance|BindingFlags.NonPublic)!
                .Invoke(control,[new Point(node.X*2-100+10,node.Y*2-80+10),1,true]);
            // 旧锚点故意给出不同位置，切深度不能据此重定位画面。
            typeof(TreemapControl).GetField("_zoomAnchorEntry",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(control,vm.CurrentRoot);
            before=Viewport(control);pending=vm.SetDisplayDepthAsync(1);
        });
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        StaHost.Run(()=> {Assert.Equal(before,Viewport(control!));Assert.True(vm!.SelectedNode is not null,string.Join(";",observations));Assert.Same(selected,vm.SelectedNode!.Entry);Assert.Single(control!.MultiSelection);});
        StaHost.Run(()=> pending=vm!.SetDisplayDepthAsync(0));await pending.WaitAsync(TimeSpan.FromSeconds(5));
        StaHost.Run(()=> {Assert.Same(selected,control!.SelectedNode!.Entry);Assert.Contains(control.SelectedNode,control.Nodes!);Assert.Single(control.MultiSelection);});
    }

    [Fact]
    public async Task 旧深度后台结果不得覆盖后来的模式()
    {
        var layout=new DepthBlockingLayout();TreemapViewModel? vm=null;Task old=Task.CompletedTask,latest=Task.CompletedTask;
        StaHost.Run(()=> {vm=new TreemapViewModel(layout);vm.UpdateSize(600,400);vm.SetRoot(Tree());layout.Block=true;old=vm.SetDisplayDepthAsync(3);});
        Assert.True(layout.Entered.Wait(TimeSpan.FromSeconds(5)));
        StaHost.Run(()=> latest=vm!.SetDisplayDepthAsync(1));
        layout.Release.Set();await Task.WhenAll(old,latest).WaitAsync(TimeSpan.FromSeconds(8));
        StaHost.Run(()=> {Assert.Equal(1,vm!.DisplayDepth);Assert.Equal(1,vm.Nodes!.Max(n=>n.Depth));});
    }

    private sealed class DepthBlockingLayout : ITreemapLayoutEngine
    {
        internal bool Block;
        internal readonly ManualResetEventSlim Entered=new(false),Release=new(false);
        public List<TreemapNode> ComputeLayout(FileEntry root,float width,float height,int maxDepth,float zoom=1)
        {
            if(Block&&maxDepth==3) {Entered.Set();if(!Release.Wait(TimeSpan.FromSeconds(6)))throw new TimeoutException("旧深度夹具未释放");}
            return new SquarifiedTreemapLayout().ComputeLayout(root,width,height,maxDepth,zoom);
        }
    }

    private static (float Zoom,float X,float Y) Viewport(TreemapControl control) =>
        ((float,float,float))typeof(TreemapControl).GetProperty("Viewport",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(control)!;

    [Fact]
    public void 树图菜单键入口不依赖鼠标且不劫持其他控件() => StaHost.Run(() =>
    {
        var control=new TreemapControl();
        Assert.True(control.Focusable);
        int blank=0,node=0;control.BlankRightClicked+=(_,_)=>blank++;control.NodeRightClicked+=(_,_)=>node++;
        using var source=new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("树图键盘夹具") {Width=0,Height=0,WindowStyle=0});
        void MenuKey()
        {
            var args=new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,source,Environment.TickCount,System.Windows.Input.Key.Apps)
            {RoutedEvent=System.Windows.Input.Keyboard.KeyDownEvent};
            control.RaiseEvent(args);Assert.True(args.Handled);
        }
        MenuKey();Assert.Equal(1,blank);
        var nodes=new SquarifiedTreemapLayout().ComputeLayout(Tree(),600,400,1);
        control.Nodes=nodes;control.SelectedNode=nodes[1];MenuKey();Assert.Equal(1,node);
        Assert.Equal(1,blank);
    });

    [Fact]
    public void 子菜单局部宿主和勾选事件使用真实模板且不改变全局模板() => StaHost.Run(() =>
    {
        Application.Current.Resources["VP.Button"]=new Style(typeof(Button));
        var view=new TreemapView();var vm=new TreemapViewModel(new SquarifiedTreemapLayout());view.SetViewModel(vm);
        var method=typeof(TreemapView).GetMethod("BuildContextMenu",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var menu=(ContextMenu)method.Invoke(view,[Tree()])!;
        var theme=new ResourceDictionary {Source=new Uri("/DiskPilot-X;component/Themes/VisionProTheme.xaml",UriKind.RelativeOrAbsolute)};
        menu.Resources.MergedDictionaries.Add(theme);
        var global=(Style)theme[typeof(MenuItem)];
        var depth=menu.Items.OfType<MenuItem>().Single(i=>i.Items.Count==7);
        menu.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));menu.Arrange(new Rect(menu.DesiredSize));menu.UpdateLayout();
        depth.ApplyTemplate();
        var popup=Assert.IsType<System.Windows.Controls.Primitives.Popup>(depth.Template.FindName("PART_Popup",depth));
        Assert.Equal(System.Windows.Controls.Primitives.PlacementMode.Right,popup.Placement);
        Assert.False(popup.Focusable);
        var binding=System.Windows.Data.BindingOperations.GetBinding(popup,System.Windows.Controls.Primitives.Popup.IsOpenProperty);
        Assert.Equal("IsSubmenuOpen",binding!.Path.Path);
        Assert.Equal(13d,depth.FontSize);
        Assert.Same(global,theme[typeof(MenuItem)]);
        var third=(MenuItem)depth.Items[3];third.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal(3,vm.DisplayDepth);
        Assert.Single(depth.Items.OfType<MenuItem>().Where(i=>i.IsChecked));Assert.True(third.IsChecked);
        // 仅布局/模板自渲染，不弹出窗口；不是原生桌面或UAC后截图。
        if(Environment.GetEnvironmentVariable("DISKPILOT_X_DEPTH_MENU_EVIDENCE") is {Length:>0} output)
        {
            Directory.CreateDirectory(output);
            void Render(FrameworkElement element,string name)
            {
                element.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
                element.Arrange(new Rect(element.DesiredSize));element.UpdateLayout();
                var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(Math.Max(1,(int)Math.Ceiling(element.ActualWidth)),Math.Max(1,(int)Math.Ceiling(element.ActualHeight)),96,96,System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(element);var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using var file=File.Create(Path.Combine(output,name));png.Save(file);
            }
            Render(menu,"菜单_后台模板自渲染.png");
            Render((FrameworkElement)popup.Child,"子菜单_后台模板自渲染.png");
        }
    });

    internal static FileEntry Tree()
    {
        var root = new FileEntry { Name="根", Path=@"D:\夹具", IsDirectory=true, Size=200 };
        var folder = new FileEntry { Name="目录", Path=root.Path+@"\目录", IsDirectory=true, Size=200, Parent=root };
        var nested = new FileEntry { Name="深层", Path=folder.Path+@"\深层", IsDirectory=true, Size=200, Parent=folder };
        nested.Children.Add(new FileEntry { Name="一.txt", Path=nested.Path+@"\一.txt", Size=100, Parent=nested });
        nested.Children.Add(new FileEntry { Name="二.bin", Path=nested.Path+@"\二.bin", Size=100, Parent=nested });
        folder.Children.Add(nested); root.Children.Add(folder); return root;
    }
}
