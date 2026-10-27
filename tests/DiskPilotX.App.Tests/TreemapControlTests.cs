using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using DiskPilotX.App.Controls;
using DiskPilotX.Core.Models;

namespace DiskPilotX.App.Tests;

public class TreemapControlTests
{
    [Theory]
    [InlineData(1, 10, 10, true, false)]
    [InlineData(2, 10, 10, true, true)]
    [InlineData(2, 10, 100, true, false)]
    [InlineData(2, 120, 80, false, false)]
    public void Click_SelectsHitNode_AndOnlyDirectoryTitleDoubleClickActivates(
        int count, double x, double y, bool directory, bool activates) => InSta(() =>
    {
        var folder = Node(true, 0, 0, 200, 200);
        var file = Node(false, 100, 30, 100, 170);
        file.Depth = 1;
        var control = new TreemapControl { Nodes = [folder, file] };
        TreemapNode? clicked = null;
        TreemapNode? activated = null;
        control.NodeClicked += (_, node) => clicked = node;
        var activation = typeof(TreemapControl).GetEvent("NodeActivated");
        Assert.NotNull(activation);
        activation.AddEventHandler(control, new EventHandler<TreemapNode>((_, node) => activated = node));

        Click(control, x, y, count);

        var expected = directory ? folder : file;
        Assert.Same(expected, clicked);
        Assert.Same(expected, Selected(control));
        Assert.Same(activates ? folder : null, activated);
    });

    [Fact]
    public void HiddenTitle_DoesNotActivateDirectory() => InSta(() =>
    {
        var node = Node(true, 0, 0, 100, 100);
        node.Label = null;
        var control = new TreemapControl { Nodes = [node] };
        int activations = 0;
        var activation = typeof(TreemapControl).GetEvent("NodeActivated");
        Assert.NotNull(activation);
        activation.AddEventHandler(control, new EventHandler<TreemapNode>((_, _) => activations++));
        Click(control, 10, 5, 2);
        Assert.Equal(0, activations);
        Assert.Same(node, Selected(control));
    });

    [Fact]
    public void LayoutReplacement_ClearsSelection() => InSta(() =>
    {
        var control = new TreemapControl { Nodes = [Node(true, 0, 0, 100, 100)] };
        Click(control, 5, 5, 1);
        Assert.NotNull(Selected(control));
        control.Nodes = [Node(true, 0, 0, 200, 200)];
        Assert.Null(Selected(control));
    });

    [Fact]
    public void SharedEdge_HitsOnlyRightHandTile() => InSta(() =>
    {
        var left = Node(false, 0, 0, 100, 100);
        var right = Node(false, 100, 0, 100, 100);
        var control = new TreemapControl { Nodes = [right, left] };
        Assert.Same(right, control.HitTest(100, 50));
        Assert.Null(control.HitTest(200, 50));
    });

    [Fact]
    public void Hover_UsesThinOutline_SelectionUsesThickerOutline_AndLeaveClearsHover() => InSta(() =>
    {
        var folder = Node(true, 0, 0, 400, 300);
        var file = Node(false, 1, 18, 200, 281);
        file.Depth = 1;
        var control = new TreemapControl { Nodes = [folder, file] };
        control.Measure(new Size(400, 300));
        control.Arrange(new Rect(0, 0, 400, 300));
        TreemapNode? hovered = null;
        control.NodeHovered += (_, node) => hovered = node;
        var hover = typeof(TreemapControl).GetMethod("HandleHover", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(hover);
        hover.Invoke(control, [new Point(100, 100)]);
        Assert.Same(file, hovered);
        using var thin = Render(control);
        Assert.Equal(SKColors.White, thin.GetPixel(1, 150));
        Assert.NotEqual(SKColors.White, thin.GetPixel(2, 150));

        Click(control, 100, 100, 1);
        using var selected = Render(control);
        // 选中框为纯白粗框（外延到 cell 外），悬停为细白框。
        Assert.Equal(SKColors.White, selected.GetPixel(0, 150));
        Assert.NotEqual(SKColors.White, selected.GetPixel(1, 150));
        Assert.NotEqual(SKColors.White, selected.GetPixel(2, 150));
        hover.Invoke(control, [new Point(-1, -1)]);
        Assert.Null(hovered);
        Assert.Same(file, Selected(control));

        Click(control, 10, 5, 1);
        using var directory = Render(control);
        Assert.NotEqual(SKColors.White, directory.GetPixel(0, 150));
        Assert.NotEqual(SKColors.White, directory.GetPixel(399, 150));
        Assert.NotEqual(SKColors.White, directory.GetPixel(2, 150));
    });

    [Fact]
    public void FlatTiles_LightSitsAtNodeCenter_AndClearsOnLeave() => InSta(() =>
    {
        var node = Node(false, 0, 0, 400, 300);
        node.Label = null;
        var control = new TreemapControl { Nodes = [node] };
        control.Measure(new Size(400, 300));
        control.Arrange(new Rect(0, 0, 400, 300));
        using var normal = Render(control);
        Assert.Equal(normal.GetPixel(100, 150), normal.GetPixel(300, 150));
        var hover = typeof(TreemapControl).GetMethod("HandleHover", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(hover);
        hover.Invoke(control, [new Point(100, 150)]);
        using var first = Render(control);
        // 光斑固定在悬停 cell 的中心（跨块光照），不跟随指针。
        Assert.True(first.GetPixel(200, 150).Red > first.GetPixel(20, 150).Red);
        hover.Invoke(control, [new Point(300, 150)]);
        using var second = Render(control);
        Assert.Equal(first.GetPixel(100, 150), second.GetPixel(100, 150));
        hover.Invoke(control, [new Point(-1, -1)]);
        using var cleared = Render(control);
        Assert.Equal(normal.GetPixel(100, 150), cleared.GetPixel(100, 150));
        Assert.Equal(normal.GetPixel(300, 150), cleared.GetPixel(300, 150));
    });

    [Fact]
    public void PointerLight_CrossesCellBoundary() => InSta(() =>
    {
        var left = Node(false, 0, 0, 200, 300);
        var right = Node(false, 200, 0, 200, 300);
        left.Label = right.Label = null;
        var control = new TreemapControl { Nodes = [left, right] };
        control.Measure(new Size(400, 300));
        control.Arrange(new Rect(0, 0, 400, 300));
        using var normal = Render(control);
        typeof(TreemapControl).GetMethod("HandleHover", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(control, [new Point(190, 150)]);
        using var lit = Render(control);
        Assert.True(lit.GetPixel(180, 150).Red > normal.GetPixel(180, 150).Red);
        Assert.True(lit.GetPixel(210, 150).Red > normal.GetPixel(210, 150).Red,
            "光斑应跨越相邻 cell，而不是裁切到命中块");
    });

    [Fact]
    public void SelectedOutline_ExpandsOutsideWithoutCoveringCell() => InSta(() =>
    {
        var node = Node(false, 50, 40, 200, 180);
        node.Label = null;
        var control = new TreemapControl { Nodes = [node] };
        control.Measure(new Size(400, 300));
        control.Arrange(new Rect(0, 0, 400, 300));
        using var normal = Render(control);
        Click(control, 100, 100, 1);
        using var selected = Render(control);
        Assert.Equal(SKColors.White, selected.GetPixel(48, 100));
        Assert.Equal(SKColors.White, selected.GetPixel(49, 100));
        Assert.Equal(normal.GetPixel(50, 100), selected.GetPixel(50, 100));
        Assert.Equal(normal.GetPixel(51, 100), selected.GetPixel(51, 100));
    });

    [Fact]
    public void PointerLight_ExcludesDirectoryTitles_AndIncludesFileTitles() => InSta(() =>
    {
        var folder = Node(true, 0, 0, 400, 300);
        var file = Node(false, 1, 18, 398, 281);
        file.Depth = 1;
        var control = new TreemapControl { Nodes = [folder, file] };
        control.Measure(new Size(400, 300));
        control.Arrange(new Rect(0, 0, 400, 300));
        using var normal = Render(control);
        var hover = typeof(TreemapControl).GetMethod("HandleHover", BindingFlags.Instance | BindingFlags.NonPublic)!;
        hover.Invoke(control, [new Point(100, 60)]);
        using var contentHover = Render(control);
        Assert.Equal(normal.GetPixel(150, 10), contentHover.GetPixel(150, 10));
        Assert.True(contentHover.GetPixel(150, 25).Red > normal.GetPixel(150, 25).Red);
        Assert.True(contentHover.GetPixel(150, 60).Red > normal.GetPixel(150, 60).Red);
        hover.Invoke(control, [new Point(100, 25)]);
        using var fileTitleHover = Render(control);
        Assert.True(fileTitleHover.GetPixel(150, 25).Red > normal.GetPixel(150, 25).Red);
        // 悬停目录标题：标题条不被光照覆盖，内容区随父节点光斑（中心在节点中心）变亮。
        hover.Invoke(control, [new Point(100, 5)]);
        using var titleHover = Render(control);
        Assert.Equal(normal.GetPixel(150, 10), titleHover.GetPixel(150, 10));
        Assert.True(titleHover.GetPixel(200, 140).Red > normal.GetPixel(200, 140).Red);
    });

    private static SKBitmap Render(TreemapControl control)
    {
        var info = new SKImageInfo(400, 300);
        using var surface = SKSurface.Create(info);
        typeof(TreemapControl).GetMethod("OnPaintSurface", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!
            .Invoke(control, [null, new SKPaintSurfaceEventArgs(surface, info)]);
        using var image = surface.Snapshot();
        return SKBitmap.FromImage(image);
    }

    private static object? Selected(TreemapControl control)
    {
        var property = typeof(TreemapControl).GetProperty("SelectedNode");
        Assert.NotNull(property);
        return property.GetValue(control);
    }

    private static void Click(TreemapControl control, double x, double y, int count)
    {
        var method = typeof(TreemapControl).GetMethod("HandleClick", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(control, [new Point(x, y), count, false]);
    }

    private static TreemapNode Node(bool directory, float x, float y, float width, float height) => new()
    {
        Entry = new FileEntry { IsDirectory = directory, Name = directory ? "目录" : "文件.bin" },
        X = x, Y = y, Width = width, Height = height, Label = "测试条目", ColorHex = "#9030D0"
    };

    private static void InSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "STA 测试未在规定时间内完成");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
