using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Input;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using DiskPilotX.App.Controls;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Services.Treemap;
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
    public void FlatTiles_LightFollowsPointer_AndClearsOnLeave() => InSta(() =>
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
        Assert.True(first.GetPixel(100, 150).Red > first.GetPixel(300, 150).Red);
        hover.Invoke(control, [new Point(300, 150)]);
        using var second = Render(control);
        Assert.True(second.GetPixel(300, 150).Red > second.GetPixel(100, 150).Red);
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
        // 悬停目录标题不点亮内容区。
        hover.Invoke(control, [new Point(100, 5)]);
        using var titleHover = Render(control);
        Assert.Equal(normal.GetPixel(150, 10), titleHover.GetPixel(150, 10));
        Assert.Equal(normal.GetPixel(100, 60), titleHover.GetPixel(100, 60));
    });

    [Fact]
    public void WheelZoom_KeepsAnchorNodeUnderPointer_AndResetsAtFullView() => InSta(() =>
    {
        var left = Node(false, 0, 0, 200, 300);
        var right = Node(false, 200, 0, 200, 300);
        var control = new TreemapControl { Nodes = [left, right] };
        control.Measure(new Size(400, 300));
        control.Arrange(new Rect(0, 0, 400, 300));
        var wheel = typeof(TreemapControl).GetMethod("RaiseWheelAt", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(wheel);

        Assert.Same(left, control.HitTest(150, 150));
        wheel.Invoke(control, [150f, 150f, 120]);
        // 放大后锚点不动：同一屏幕点仍命中 left。
        Assert.Same(left, control.HitTest(150, 150));

        // 缩回 1x 视图复位，整图铺满无偏移。
        wheel.Invoke(control, [150f, 150f, -120]);
        Assert.Same(left, control.HitTest(150, 150));
        Assert.Same(right, control.HitTest(350, 150));
    });

    [Fact]
    public void LeftDrag_PansFromInsideCell_WithoutClicking() => InSta(() =>
    {
        var left = Node(false, 0, 0, 200, 300);
        var right = Node(false, 200, 0, 200, 300);
        var control = Sized([left, right]);
        Call(control, "ZoomAt", 200f, 150f, 2f);
        Assert.Same(right, control.HitTest(230, 150));
        int clicks = 0;
        control.NodeClicked += (_, _) => clicks++;
        Call(control, "HandleLeftPress", new Point(200, 150), 1, false);
        Call(control, "HandlePointerMove", new Point(260, 150));
        Call(control, "HandleLeftRelease", new Point(260, 150));
        Assert.Equal(0, clicks);
        Assert.Same(left, control.HitTest(230, 150));
        Call(control, "HandleLeftPress", new Point(100, 150), 1, false);
        Call(control, "HandleLeftRelease", new Point(101, 151));
        Assert.Equal(1, clicks);
        Assert.Same(left, control.SelectedNode);
    });

    [Fact]
    public void LeftClick_CtrlSelectionAndTitleDoubleClickArePreserved() => InSta(() =>
    {
        var directory = Node(true, 0, 0, 400, 300);
        var file = Node(false, 1, 18, 398, 281);
        file.Depth = 1;
        var control = Sized([directory, file]);
        int entered = 0;
        control.NodeActivated += (_, _) => entered++;
        Call(control, "HandleLeftPress", new Point(100, 100), 1, true);
        Call(control, "HandleLeftRelease", new Point(100, 100));
        Assert.Contains(file, control.MultiSelection);
        Call(control, "HandleLeftPress", new Point(100, 5), 2, false);
        Call(control, "HandleLeftRelease", new Point(100, 5));
        Assert.Equal(1, entered);
    });

    [Fact]
    public void FoldedDirectory_HasColoredBodyAndNeutralTitle() => InSta(() =>
    {
        var node = Node(true, 0, 0, 400, 300);
        node.ColorHex = "#303030";
        node.ContentColorHex = "#2196F3";
        var control = Sized([node]);
        using var image = Render(control);
        Assert.Equal(SKColor.Parse("#303030"), image.GetPixel(300, 10));
        Assert.Equal(SKColor.Parse("#2196F3"), image.GetPixel(300, 150));
    });

    [Fact]
    public void PointerLight_FollowsScreenPositionAfterZoomAndPan() => InSta(() =>
    {
        var node = Node(false, 0, 0, 400, 300);
        node.Label = null;
        var control = Sized([node]);
        Call(control, "ZoomAt", 200f, 150f, 2f);
        Call(control, "HandleHover", new Point(80, 150));
        using var image = Render(control);
        Assert.True(image.GetPixel(80, 150).Red > image.GetPixel(300, 150).Red);
    });

    [Fact]
    public void Wheel_EntersPointedDirectoryAndReturnsToSavedParentViewport() => InSta(() =>
    {
        var (control, vm, root, child) = NavigationFixture();
        // 扫描根本身有父目录，也不允许滚轮退出到扫描范围外。
        root.Parent = new FileEntry { Name = "扫描范围外", IsDirectory = true };
        float savedZoom = 1f, savedX = 0f, savedY = 0f;
        for (int i = 0; i < 20 && ReferenceEquals(root, vm.CurrentRoot); i++)
        {
            savedZoom = vm.Zoom; savedX = Field(control, "_panX"); savedY = Field(control, "_panY");
            Call(control, "RaiseWheelAt", 90f, 150f, 120);
        }
        Assert.Same(child, vm.CurrentRoot);
        Assert.Equal(1f, vm.Zoom);
        Assert.Equal(2, vm.BreadcrumbPath.Count);
        Call(control, "RaiseWheelAt", 90f, 150f, -120);
        Assert.Same(root, vm.CurrentRoot);
        Assert.Equal(savedZoom, vm.Zoom);
        Assert.Equal(savedX, Field(control, "_panX"));
        Assert.Equal(savedY, Field(control, "_panY"));
        for (int i = 0; i < 30; i++) Call(control, "RaiseWheelAt", 90f, 150f, -120);
        Assert.Same(root, vm.CurrentRoot);
        Assert.Equal(1f, vm.Zoom);
        Assert.Equal(0f, Field(control, "_panX"));
        Assert.Equal(0f, Field(control, "_panY"));
    });



    private static (TreemapControl, TreemapViewModel, FileEntry, FileEntry) NavigationFixture(bool tiny = false)
    {
        var root = new FileEntry { Name = "根", Path = "C:/根", IsDirectory = true, Size = 1000 };
        FileEntry Folder(string name)
        {
            var dir = new FileEntry { Name = name, Path = root.Path + "/" + name, IsDirectory = true, Size = tiny ? (name == "甲" ? 20 : 980) : 500, Parent = root };
            root.Children.Add(dir);
            for (int i = 0; i < 5; i++) dir.Children.Add(new FileEntry { Name = $"文件{i}.js", Path = dir.Path + $"/文件{i}.js", Size = dir.Size / 5, Parent = dir });
            return dir;
        }
        var child = Folder("甲");
        Folder("乙");
        var vm = new TreemapViewModel(new SquarifiedTreemapLayout());
        var control = Sized([]);
        SetWheelClock(control, new TestWheelClock());
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.CurrentRoot)) Call(control, "SetViewRoot", vm.CurrentRoot, vm.ScanRoot);
            if (e.PropertyName == nameof(vm.Nodes)) control.Nodes = vm.Nodes;
        };
        control.RootNavigationRequested += (_, dir) => vm.NavigateToEntry(dir);
        control.ZoomChanged += (_, zoom) => vm.SetZoom(zoom);
        vm.UpdateSize(400, 300);
        vm.SetRoot(root);
        return (control, vm, root, child);
    }

    [Fact]
    public void ZoomRelayout_KeepsLightUnderStationaryPointer() => InSta(() =>
    {
        var (control, _, _, _) = NavigationFixture();
        Call(control, "HandleHover", new Point(90, 150));
        Call(control, "RaiseWheelAt", 90f, 150f, 120);
        var hovered = typeof(TreemapControl).GetField("_lastHoveredNode", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(control);
        Assert.NotNull(hovered);
        Assert.Same(control.HitTest(90, 150), hovered);
    });



    [Fact]
    public void AggregateZoom_PreservesItsOwnAnchorInsteadOfParent_AndHasNoDirectoryTitle() => InSta(() =>
    {
        var parent = new FileEntry { Name = "目录", Path = "C:/目录", IsDirectory = true, Size = 1000 };
        var folder = Node(true, 0, 0, 400, 300);
        folder.Entry = parent;
        var tail = Node(true, 200, 30, 200, 270);
        tail.Entry = parent; tail.Depth = 1; tail.AggregatedCount = 500;
        var control = Sized([folder, tail]);
        int activations = 0;
        control.NodeActivated += (_, _) => activations++;
        Call(control, "HandleClick", new Point(250, 35), 2, false);
        Assert.Equal(0, activations);
        control.ZoomChanged += (_, _) => control.Nodes = [folder, tail];
        Call(control, "RaiseWheelAt", 250f, 100f, 120);
        Assert.Same(tail, control.HitTest(250, 100));
        Assert.Equal(-62.5f, Field(control, "_panX"), 2);
        Assert.Equal(-25f, Field(control, "_panY"), 2);
    });

    [Fact]
    public void MultiSelection_UsesPureWhiteOutline() => InSta(() =>
    {
        var node = Node(false, 50, 40, 200, 180);
        var control = Sized([node]);
        Call(control, "HandleClick", new Point(100, 100), 1, true);
        using var image = Render(control);
        Assert.Equal(SKColors.White, image.GetPixel(49, 100));
    });

    [Fact]
    public void WheelExit_VisitsImmediateParentWhenEnteringSeveralLevelsAtOnce() => InSta(() =>
    {
        var (control, vm, root, parent) = NavigationFixture();
        var nested = new FileEntry { Name = "内层", Path = parent.Path + "/内层", IsDirectory = true, Size = parent.Size, Parent = parent };
        foreach (var file in parent.Children) { file.Parent = nested; nested.Children.Add(file); }
        parent.Children.Clear();
        parent.Children.Add(nested);
        vm.UpdateSize(400, 300);
        for (int i = 0; i < 25 && ReferenceEquals(root, vm.CurrentRoot); i++) Call(control, "RaiseWheelAt", 90f, 150f, 120);
        Assert.Same(nested, vm.CurrentRoot);
        Call(control, "RaiseWheelAt", 90f, 150f, -120);
        Assert.Same(parent, vm.CurrentRoot);
        Call(control, "RaiseWheelAt", 90f, 150f, -120);
        Assert.Same(root, vm.CurrentRoot);
    });



    [Fact]
    public void MiddleButton_DoesNothing_AndNormalDragCannotExposeMargins() => InSta(() =>
    {
        var (control, vm, root, _) = NavigationFixture();
        var before = control.Nodes;
        control.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Middle) { RoutedEvent = Mouse.MouseDownEvent });
        control.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 1, MouseButton.Middle) { RoutedEvent = Mouse.MouseUpEvent });
        Assert.Same(root, vm.CurrentRoot);
        Assert.Same(before, control.Nodes);
        Assert.Equal(1f, vm.Zoom);
        Assert.Equal(0f, Field(control, "_panX"));
        Assert.Equal(0f, Field(control, "_panY"));
        Call(control, "HandleLeftPress", new Point(100, 100), 1, false);
        Call(control, "HandlePointerMove", new Point(350, 250));
        Call(control, "HandleLeftRelease", new Point(350, 250));
        Assert.Equal(0f, Field(control, "_panX"));
        Assert.Equal(0f, Field(control, "_panY"));
    });

    [Fact]
    public void WheelSnap_ShowsFullNewLevelAndConsumesWheelBurstBeforeContinuing() => InSta(() =>
    {
        var (control, vm, root, child) = NavigationFixture();
        // 新层仍有更深目录可进入，才能区分 Snap 滞留与末级目录停止机制。
        var deeper = new FileEntry { Name = "更深目录", Path = child.Path + "/更深目录", IsDirectory = true, Size = 10, Parent = child };
        deeper.Children.Add(new FileEntry { Name = "细节.cs", Path = deeper.Path + "/细节.cs", Size = 10, Parent = deeper });
        child.Children.Add(deeper); child.Size += 10; root.Size += 10;
        vm.UpdateSize(400, 300);
        var clock = new TestWheelClock { AutoAdvance = false };
        SetWheelClock(control, clock);
        for (int i = 0; i < 25 && ReferenceEquals(vm.CurrentRoot, root); i++)
            Call(control, "RaiseWheelAt", 90f, 150f, 120);
        Assert.Same(child, vm.CurrentRoot);
        Assert.Equal(1f, vm.Zoom);
        var next = control.Nodes!.Single(n => ReferenceEquals(n.Entry, deeper));
        float nextX = next.X + next.Width / 2, nextY = next.Y + next.Height / 2;
        for (int i = 0; i < 10; i++)
        {
            clock.Advance(100);
            Call(control, "RaiseWheelAt", nextX, nextY, 120);
            Assert.Equal(1f, vm.Zoom);
            Assert.Equal(0f, Field(control, "_panY"));
            Assert.NotNull(control.Nodes![0].Label);
        }
        clock.Advance(200);
        Call(control, "RaiseWheelAt", nextX, nextY, 120);
        Assert.True(vm.Zoom > 1f);
    });

    [Fact]
    public void WheelSnap_AlsoPausesAfterReturningToParent() => InSta(() =>
    {
        var (control, vm, root, child) = NavigationFixture();
        var clock = new TestWheelClock { AutoAdvance = false };
        SetWheelClock(control, clock);
        for (int i = 0; i < 25 && ReferenceEquals(vm.CurrentRoot, root); i++) Call(control, "RaiseWheelAt", 90f, 150f, 120);
        clock.Advance(600);
        Call(control, "RaiseWheelAt", 90f, 150f, -120);
        Assert.Same(root, vm.CurrentRoot);
        float restoredZoom = vm.Zoom;
        Call(control, "RaiseWheelAt", 90f, 150f, -120);
        Assert.Equal(restoredZoom, vm.Zoom);
        clock.Advance(600);
        Call(control, "RaiseWheelAt", 90f, 150f, -120);
        Assert.True(vm.Zoom < restoredZoom);
    });

    private sealed class TestWheelClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 27, 0, 0, 0, TimeSpan.Zero);
        public bool AutoAdvance { get; set; } = true;
        public void Advance(int milliseconds) => _now += TimeSpan.FromMilliseconds(milliseconds);
        public override DateTimeOffset GetUtcNow()
        {
            if (AutoAdvance) Advance(1000);
            return _now;
        }
    }

    private static void SetWheelClock(TreemapControl control, TimeProvider clock)
    {
        var property = typeof(TreemapControl).GetProperty("WheelClock", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(property);
        property.SetValue(control, clock);
    }

    [Fact]
    public void FileFill_HasSubtleSameHueTransition_WithoutStrongShading() => InSta(() =>
    {
        var node = Node(false, 0, 0, 400, 300);
        node.Label = null; node.ColorHex = "#B8C3D0";
        var control = Sized([node]);
        using var image = Render(control);
        var top = image.GetPixel(100, 40);
        var bottom = image.GetPixel(100, 260);
        Assert.InRange(top.Red - bottom.Red, 1, 15);
        Assert.InRange(top.Green - bottom.Green, 1, 15);
        Assert.InRange(top.Blue - bottom.Blue, 1, 15);
        Assert.Equal(image.GetPixel(100, 150), image.GetPixel(300, 150));
    });

    [Fact]
    public void WheelZoom_StopsAtLastDirectoryEvenAfterSnapExpires() => InSta(() =>
    {
        var (control, vm, root, child) = NavigationFixture();
        for (int i = 0; i < 25 && ReferenceEquals(vm.CurrentRoot, root); i++)
            Call(control, "RaiseWheelAt", 90f, 150f, 120);
        Assert.Same(child, vm.CurrentRoot);
        var finalLayout = control.Nodes;
        // 测试时钟每次前进一秒，确保终止并非仅由换层 Snap 暂停造成。
        for (int i = 0; i < 40; i++) Call(control, "RaiseWheelAt", 90f, 150f, 120);
        Assert.Same(child, vm.CurrentRoot);
        Assert.Equal(1f, vm.Zoom);
        Assert.Equal(0f, Field(control, "_panX"));
        Assert.Equal(0f, Field(control, "_panY"));
        Assert.Same(finalLayout, control.Nodes);
        Assert.NotNull(control.Nodes![0].Label);
        Assert.InRange(control.Nodes[0].Y, 0f, 3f);
        Call(control, "RaiseWheelAt", 90f, 150f, -120);
        Assert.Same(root, vm.CurrentRoot);
    });

    [Fact]
    public void WheelZoom_FileOnlyScanRootStaysAtFullView() => InSta(() =>
    {
        var (control, vm, _, child) = NavigationFixture();
        vm.SetRoot(child);
        for (int i = 0; i < 20; i++) Call(control, "RaiseWheelAt", 200f, 150f, 120);
        Assert.Same(child, vm.CurrentRoot);
        Assert.Equal(1f, vm.Zoom);
        Call(control, "RaiseWheelAt", 200f, 150f, -120);
        Assert.Same(child, vm.CurrentRoot);
        Assert.Equal(1f, vm.Zoom);
    });

    [Fact]
    public void WheelZoom_StopsOnRootFileButStillAllowsPointedSubdirectory() => InSta(() =>
    {
        var (control, vm, root, child) = NavigationFixture();
        var largeFile = new FileEntry { Name = "大文件.bin", Path = root.Path + "/大文件.bin", Parent = root, Size = 1500 };
        root.Children.Add(largeFile); root.Size += largeFile.Size;
        vm.UpdateSize(400, 300);
        var file = control.Nodes!.Single(n => ReferenceEquals(n.Entry, largeFile));
        Call(control, "RaiseWheelAt", file.X + file.Width / 2, file.Y + file.Height / 2, 120);
        Assert.Equal(1f, vm.Zoom);
        var directory = control.Nodes!.Single(n => ReferenceEquals(n.Entry, child));
        Call(control, "RaiseWheelAt", directory.X + directory.Width / 2, directory.Y + directory.Height / 2, 120);
        Assert.True(vm.Zoom > 1f || !ReferenceEquals(vm.CurrentRoot, root));
    });

    [Fact]
    public void ViewportCulling_FiltersBeforeDrawing_AndRestoresTilesAfterPan() => InSta(() =>
    {
        var left = Node(false, 10, 20, 100, 100);
        var crossing = Node(false, 390, 20, 40, 100);
        var right = Node(false, 500, 20, 100, 100);
        var hidden = Node(false, 10, 20, 100, 100);
        hidden.IsVisible = false;
        var control = Sized([left, crossing, right, hidden]);
        var method = typeof(TreemapControl).GetMethod("GetVisibleNodes", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        TreemapNode[] Visible() => ((IEnumerable<TreemapNode>)method.Invoke(control, null)!).ToArray();

        Assert.Equal(new[] { left, crossing }, Visible());
        // 视口从[0,400]移动到逻辑区间[400,600]；部分相交节点仍必须保留。
        typeof(TreemapControl).GetField("_zoom", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(control, 2f);
        typeof(TreemapControl).GetField("_panX", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(control, -800f);
        Assert.Equal(new[] { crossing, right }, Visible());
        using var image = Render(control);
        Assert.NotEqual(SKColor.Parse("#1B2430"), image.GetPixel(250, 100));
        Assert.Equal(4, control.Nodes!.Count);
        control.ResetView();
        Assert.Equal(new[] { left, crossing }, Visible());
    });

    [Fact]
    public void ViewportCulling_AlsoLimitsHoveredTitleClipToVisibleDirectories() => InSta(() =>
    {
        var directory = Node(true, 0, 0, 400, 300);
        directory.Label = "可见目录";
        var offscreen = Node(true, 500, 0, 400, 300);
        offscreen.Label = "视口外目录";
        var file = Node(false, 1, 18, 398, 281);
        file.Depth = 1;
        var control = Sized([directory, offscreen, file]);
        Call(control, "HandleHover", new Point(200, 100));
        using var image = Render(control);
        var count = typeof(TreemapControl).GetProperty("TitleClipNodeCount", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(count);
        Assert.Equal(1, count.GetValue(control));
    });

    [Fact]
    public void TextLod_RendererDoesNotFloodSmallCellsWithLabels() => InSta(() =>
    {
        var small = Node(false, 10, 20, 90, 80);
        var large = Node(false, 110, 20, 280, 260);
        var control = Sized([small, large]);
        using var image = Render(control);
        var count = typeof(TreemapControl).GetProperty("RenderedLabelCount", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(count);
        Assert.Equal(1, count.GetValue(control));
    });

    [Fact]
    public void DirectoryTitle_HasNoPurposeColorOrAccentLine() => InSta(() =>
    {
        var directory = Node(true, 0, 0, 400, 300);
        directory.ColorHex = "#303030"; directory.ContentColorHex = "#808080";
        var control = Sized([directory]);
        using var image = Render(control);
        Assert.Equal(SKColor.Parse("#303030"), image.GetPixel(300, 1));
        Assert.Equal(SKColor.Parse("#303030"), image.GetPixel(300, 3));
        Assert.Equal(SKColor.Parse("#808080"), image.GetPixel(300, 100));
        Assert.Null(typeof(TreemapNode).GetProperty("AccentColorHex"));
    });

    private static TreemapControl Sized(List<TreemapNode> nodes)
    {
        var control = new TreemapControl { Nodes = nodes };
        control.Measure(new Size(400, 300));
        control.Arrange(new Rect(0, 0, 400, 300));
        return control;
    }

    private static float Field(TreemapControl control, string name) =>
        (float)typeof(TreemapControl).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(control)!;

    private static void Call(TreemapControl control, string name, params object?[] args)
    {
        var method = typeof(TreemapControl).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        method.Invoke(control, args);
    }

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
