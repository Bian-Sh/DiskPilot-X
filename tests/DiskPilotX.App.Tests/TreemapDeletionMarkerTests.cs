using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using DiskPilotX.App.Controls;
using DiskPilotX.App.Localization;
using DiskPilotX.App.ViewModels;
using DiskPilotX.App.Views;
using DiskPilotX.Core.Enums;
using System.Windows.Controls;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Treemap;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace DiskPilotX.App.Tests;

public class TreemapDeletionMarkerTests
{
    private static readonly SKColor DeletedColor = new(229, 72, 77);

    [Theory]
    [InlineData(1f, 1f)]
    [InlineData(2f, 1f)]
    [InlineData(1f, 1.5f)]
    [InlineData(2f, 2f)]
    public void 折叠块以固定屏幕尺寸角标提示隐藏删除项且保留底色(float zoom, float dpi) => InSta(() =>
    {
        var (folder, _, _) = Fixture();
        var node = Node(folder, 20 / zoom, 20 / zoom, 80 / zoom, 80 / zoom);
        var control = Sized([node], zoom);
        using var image = Render(control, dpi);
        Assert.Equal(DeletedColor, image.GetPixel((int)(98 * dpi), (int)(22 * dpi)));
        Assert.NotEqual(DeletedColor, image.GetPixel((int)(90 * dpi), (int)(22 * dpi)));
        Assert.NotEqual(DeletedColor, image.GetPixel((int)(60 * dpi), (int)(60 * dpi)));
        Assert.NotEqual(DeletedColor, image.GetPixel((int)(18 * dpi), (int)(60 * dpi)));
        Assert.False(folder.IsDeleted);
    });

    [Fact]
    public void 只给最近承接块提示而不沿祖先链重复亮标() => InSta(() =>
    {
        var (folder, _, _) = Fixture();
        var root = new FileEntry { IsDirectory = true, Name = "根目录", Children = [folder] };
        folder.Parent = root;
        var sibling = new FileEntry { IsDirectory = true, Name = "未删除目录", Parent = root };
        root.Children.Add(sibling);
        var control = Sized([Node(root, 0, 0, 400, 300), Node(folder, 20, 20, 80, 80),
            Node(sibling, 150, 20, 80, 80)]);
        using var image = Render(control);
        Assert.Equal(DeletedColor, image.GetPixel(98, 22));
        Assert.NotEqual(DeletedColor, image.GetPixel(398, 2));
        Assert.NotEqual(DeletedColor, image.GetPixel(228, 22));
    });

    [Fact]
    public void 展开后具体删除项接回红框而父块不重复显示角标() => InSta(() =>
    {
        var (folder, deleted, _) = Fixture();
        var control = Sized([Node(folder, 0, 0, 400, 300), Node(deleted, 20, 20, 80, 80)]);
        using var image = Render(control);
        Assert.Equal(DeletedColor, image.GetPixel(20, 60));
        Assert.NotEqual(DeletedColor, image.GetPixel(398, 2));
        Assert.NotEqual(DeletedColor, image.GetPixel(98, 22));
    });

    [Fact]
    public void 目录自身已删除时只保留完整红框() => InSta(() =>
    {
        var (folder, _, _) = Fixture();
        folder.IsDeleted = true;
        var control = Sized([Node(folder, 20, 20, 80, 80)]);
        using var image = Render(control);
        Assert.Equal(DeletedColor, image.GetPixel(20, 60));
        Assert.NotEqual(DeletedColor, image.GetPixel(98, 22));
    });

    [Fact]
    public void LOD缩小角标与放大子项红框可往返() => InSta(() =>
    {
        var (folder, deleted, _) = Fixture();
        var layout = new SquarifiedTreemapLayout();
        var control = Sized(layout.ComputeLayout(folder, 5, 5, 64, 1));
        typeof(TreemapControl).GetMethod("SetViewRoot", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(control, [folder, folder]);
        Assert.Contains(control.Nodes!, n => n.AggregatedCount == 2);
        HashSet<FileEntry> Hints() => (HashSet<FileEntry>)typeof(TreemapControl)
            .GetMethod("GetDeletionHintEntries", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, null)!;
        Assert.Contains(folder, Hints());

        typeof(TreemapControl).GetField("_zoom", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(control, 2f);
        control.Nodes = layout.ComputeLayout(folder, 5, 5, 64, 2);
        Assert.Contains(control.Nodes!, n => ReferenceEquals(n.Entry, deleted));
        Assert.DoesNotContain(folder, Hints());
        using (var image = Render(control))
            Assert.Contains(Enumerable.Range(0, 10).SelectMany(x => Enumerable.Range(0, 10).Select(y => image.GetPixel(x, y))), color => color == DeletedColor);

        typeof(TreemapControl).GetField("_zoom", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(control, 1f);
        control.Nodes = layout.ComputeLayout(folder, 5, 5, 64, 1);
        Assert.Contains(folder, Hints());
    });

    [Fact]
    public void 删除与清除经真实视图通知刷新且不重置选中和视口() => StaHost.Run(() =>
    {
        var (folder, deleted, _) = Fixture();
        deleted.IsDeleted = false;
        deleted.Path = folder.Path + "/已删除.bin";
        var vm = new TreemapViewModel(new SquarifiedTreemapLayout());
        var session = new ScanSession { RootEntry = folder, TotalFiles = 2, TotalSize = 200 };
        vm.UpdateSize(30, 30);
        vm.SetRoot(folder, session);
        var style = new Style(typeof(Button));
        style.Seal();
        Application.Current.Resources["VP.Button"] = style;
        var view = new TreemapView();
        view.SetViewModel(vm);
        var control = (TreemapControl)view.FindName("Treemap");
        control.Measure(new Size(400, 300));
        control.Arrange(new Rect(0, 0, 400, 300));
        var nodes = control.Nodes;
        control.SelectedNode = nodes![0];
        var deletedNode = nodes.Single(n => ReferenceEquals(n.Entry, deleted));
        int markX = (int)Math.Ceiling(deletedNode.X), markY = (int)(deletedNode.Y + deletedNode.Height / 2);
        using (var image = Render(control)) Assert.NotEqual(DeletedColor, image.GetPixel(markX, markY));

        vm.ApplyCleanupResults([new CleanupAction { Result = CleanupResult.Success,
            Recommendation = new() { TargetPath = deleted.Path } }], session);
        using (var image = Render(control)) Assert.Equal(DeletedColor, image.GetPixel(markX, markY));
        Assert.Same(nodes, control.Nodes);
        Assert.Same(nodes[0], control.SelectedNode);
        Assert.Equal(1f, typeof(TreemapControl).GetField("_zoom", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control));

        vm.ClearDeletionMarks();
        using (var image = Render(control)) Assert.NotEqual(DeletedColor, image.GetPixel(markX, markY));
        Assert.Same(nodes, control.Nodes);
        Assert.Same(nodes[0], control.SelectedNode);
    });

    [Fact]
    public void 未达到像素阈值的删除项由最近已展开父块提示() => InSta(() =>
    {
        var (folder, _, live) = Fixture();
        var control = Sized([Node(folder, 20, 20, 80, 80), Node(live, 21, 40, 60, 59)]);
        using var image = Render(control);
        Assert.Equal(DeletedColor, image.GetPixel(98, 22));
    });

    [Fact]
    public void 子项平移出视口不会误点亮父块() => InSta(() =>
    {
        var (folder, deleted, _) = Fixture();
        var control = Sized([Node(folder, 0, 0, 800, 300), Node(deleted, 100, 20, 80, 80)]);
        typeof(TreemapControl).GetField("_panX", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(control, -401f);
        using var image = Render(control);
        Assert.NotEqual(DeletedColor, image.GetPixel(397, 2));
    });

    [Theory]
    [InlineData(2f)]
    [InlineData(4f)]
    public void 细小块角标不超出自身或侵占邻块(float side) => InSta(() =>
    {
        var (folder, _, _) = Fixture();
        var control = Sized([Node(folder, 20, 20, side, side)]);
        using var image = Render(control);
        int redPixels = 0;
        for (int y = 18; y < 28; y++)
            for (int x = 18; x < 28; x++)
                if (image.GetPixel(x, y) == DeletedColor)
                {
                    redPixels++;
                    Assert.InRange(x, 20, 20 + (int)side - 1);
                    Assert.InRange(y, 20, 20 + (int)side - 1);
                }
        Assert.True(redPixels > 0);
    });

    [Fact]
    public void 角标提示说明只在存在隐藏删除项时出现() => InSta(() =>
    {
        var (folder, deleted, _) = Fixture();
        var node = Node(folder, 20, 20, 80, 80);
        var control = Sized([node]);
        var tooltipMethod = typeof(TreemapControl).GetMethod("UpdateToolTipContent", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var tooltip = (ToolTip)typeof(TreemapControl).GetField("_toolTip", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!;
        tooltipMethod.Invoke(control, [node]);
        Assert.Contains(((StackPanel)tooltip.Content).Children.OfType<TextBlock>(),
            text => text.Text == L.Text("TreemapContainsDeletedItems"));
        deleted.IsDeleted = false;
        typeof(TreemapControl).GetMethod("RefreshDeletionMarks", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, null);
        tooltipMethod.Invoke(control, [node]);
        Assert.DoesNotContain(((StackPanel)tooltip.Content).Children.OfType<TextBlock>(),
            text => text.Text == L.Text("TreemapContainsDeletedItems"));
    });

    [Fact]
    public void 保留质量的亚像素删除项仍由可见祖先承接提示() => InSta(() =>
    {
        var folder = new FileEntry { Name = "根", IsDirectory = true, Size = 1000000000001L };
        var big = new FileEntry { Name = "大文件.bin", Size = 1000000000000L, Parent = folder };
        var tiny = new FileEntry { Name = "一字节.txt", Size = 1, Parent = folder, IsDeleted = true };
        folder.Children = [big, tiny];
        var nodes = new SquarifiedTreemapLayout().ComputeLayout(folder, 400, 300, 64);
        Assert.Contains(nodes, n => ReferenceEquals(n.Entry, tiny));
        var control = Sized(nodes);
        typeof(TreemapControl).GetMethod("SetViewRoot", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, [folder, folder]);
        var hints = (HashSet<FileEntry>)typeof(TreemapControl).GetMethod("GetDeletionHintEntries", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, null)!;
        Assert.Contains(folder, hints);
        using var image = Render(control);
        var headerBottom = (int)Math.Ceiling(nodes[0].Y + (nodes[0].HeaderHeight ?? 0));
        var red = new List<(int X, int Y)>();
        for (int y = 0; y < 40; y++) for (int x = 388; x < 400; x++)
            if (image.GetPixel(x, y) == DeletedColor) red.Add((x, y));
        Assert.NotEmpty(red);
        Assert.All(red, pixel => Assert.True(pixel.Y >= headerBottom, "承接角标不能进入标题文字区域。"));
        Assert.NotEqual(DeletedColor, image.GetPixel(396, 4));
    });

    private static (FileEntry Folder, FileEntry Deleted, FileEntry Live) Fixture()
    {
        var folder = new FileEntry { IsDirectory = true, Name = "包含删除痕迹", Path = "D:/示例", Size = 200 };
        var deleted = new FileEntry { Name = "已删除.bin", Size = 100, Parent = folder, IsDeleted = true };
        var live = new FileEntry { Name = "仍保留.bin", Size = 100, Parent = folder };
        folder.Children = [deleted, live];
        return (folder, deleted, live);
    }

    private static TreemapNode Node(FileEntry entry, float x, float y, float width, float height) => new()
    {
        Entry = entry, X = x, Y = y, Width = width, Height = height,
        ColorHex = "#303030", ContentColorHex = "#9030D0"
    };

    private static TreemapControl Sized(List<TreemapNode> nodes, float zoom = 1)
    {
        var control = new TreemapControl { Nodes = nodes };
        control.Measure(new Size(400, 300));
        control.Arrange(new Rect(0, 0, 400, 300));
        typeof(TreemapControl).GetField("_zoom", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(control, zoom);
        return control;
    }

    private static SKBitmap Render(TreemapControl control, float dpi = 1)
    {
        var info = new SKImageInfo((int)(400 * dpi), (int)(300 * dpi));
        using var surface = SKSurface.Create(info);
        typeof(TreemapControl).GetMethod("OnPaintSurface", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!
            .Invoke(control, [null, new SKPaintSurfaceEventArgs(surface, info)]);
        using var image = surface.Snapshot();
        return SKBitmap.FromImage(image);
    }

    private static void InSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { failure = e; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "STA 测试超时");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
