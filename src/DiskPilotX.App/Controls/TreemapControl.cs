using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media;
using SkiaSharp;
using DiskPilotX.App.Localization;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;
using DiskPilotX.App.Converters;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Treemap;

namespace DiskPilotX.App.Controls;

public class TreemapControl : SKElement
{
    private const string TreemapGlyphProbe = "中文";

    private const float MinZoom = 1f;
    private const float MaxZoom = 8f;
    private const float DragThreshold = 4f;
    private const float ZoomStep = 1.25f;
    private const float KeyboardZoomStep = 1.5f; // 键盘缩放步进
    private const int SnapPauseMilliseconds = 450;
    private const int SnapWheelIdleMilliseconds = 140;

    private static readonly string[] TreemapFontFamilies =
    [
        "Microsoft YaHei UI",
        "Microsoft YaHei",
        "SimSun",
        "NSimSun",
        "Segoe UI",
    ];

    private static readonly Lazy<SKTypeface> TreemapTypeface = new(CreateTreemapTypeface);

    private List<TreemapNode>? _nodes;
    private readonly ToolTip _toolTip;
    private TreemapNode? _lastHoveredNode;
    private TreemapNode? _selectedNode;
    private readonly HashSet<TreemapNode> _multiSelection = new();
    private Point _hoverPosition;
    private bool _pointerInside;
    private readonly DispatcherTimer _toolTipTimer;

    // ── 视口变换（逻辑坐标 → 屏幕坐标：screen = logical * zoom + pan）──
    // zoom 同时驱动 LOD 重布局；cell 规格（标题栏/字号/边框）按屏幕像素恒定。
    private float _zoom = 1f;
    private float _panX;
    private float _panY;

    // ── 拖拽平移状态 ──
    private bool _isPanning;
    private int _pressClickCount;
    private bool _pressCtrlHeld;
    private FileEntry? _viewRoot;
    private FileEntry? _scanRoot;
    private bool _wheelNavigating;
    private bool _snapActive;
    private long _snapUntil;
    private long _lastSnapWheel;
    internal TimeProvider WheelClock { get; set; } = TimeProvider.System;
    private readonly Stack<ViewportFrame> _wheelHistory = new();
    private sealed record ViewportFrame(FileEntry Root, float Zoom, float PanX, float PanY);
    private Point _panStartScreen;
    private float _panStartPanX;
    private float _panStartPanY;
    private bool _panDragExceededThreshold;

    // ── 缩放锚点：LOD 重布局后保持指针下的节点相对位置不变 ──
    private FileEntry? _zoomAnchorEntry;
    private bool _zoomAnchorAggregate;
    private float _zoomAnchorFx;
    private float _zoomAnchorFy;
    private Point _zoomAnchorScreen;

    public List<TreemapNode>? Nodes
    {
        get => _nodes;
        set
        {
            _nodes = value;
            SelectedNode = null;
            ClearMultiSelection();
            ClearHover();
            ApplyZoomAnchor();
            if (_pointerInside && !_isPanning) HandleHover(_hoverPosition);
            InvalidateVisual();
        }
    }

    public TreemapNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (_selectedNode == value) return;
            _selectedNode = value;
            InvalidateVisual();
        }
    }

    public IReadOnlyCollection<TreemapNode> MultiSelection => _multiSelection;
    internal (float Zoom, float X, float Y) Viewport => (_zoom, _panX, _panY);
    internal int RenderedNodeCount { get; private set; }
    internal int RenderedLabelCount { get; private set; }
    internal int TitleClipNodeCount { get; private set; }

    public event EventHandler<TreemapNode>? NodeClicked;
    public event EventHandler<TreemapNode>? NodeActivated;
    public event EventHandler<TreemapNode?>? NodeHovered;
    public event EventHandler<TreemapNode>? NodeRightClicked;
    public event EventHandler? MultiSelectionChanged;

    /// <summary>缩放级别改变时触发（参数为新 zoom），供 LOD 重布局。</summary>
    public event EventHandler<float>? ZoomChanged;
    public event EventHandler<FileEntry>? RootNavigationRequested;

    /// <summary>切换层级时同步视口；普通导航清除滚轮历史，滚轮导航保留返回位置。</summary>
    internal void SetViewRoot(FileEntry? root, FileEntry? scanRoot)
    {
        _scanRoot = scanRoot;
        if (ReferenceEquals(root, _viewRoot)) return;
        _viewRoot = root;
        _zoom = MinZoom;
        _panX = _panY = 0;
        _zoomAnchorEntry = null;
        if (!_wheelNavigating) { _wheelHistory.Clear(); _snapActive = false; }
        if (_isPanning) EndPan();
        ClearHover();
        InvalidateVisual();
    }

    public TreemapControl()
    {
        PaintSurface += OnPaintSurface;

        _toolTip = new ToolTip
        {
            Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse,
            PlacementTarget = this,
            IsHitTestVisible = false,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(5),
        };
        _toolTip.SetResourceReference(Control.BackgroundProperty, "VP.BackgroundAltBrush");
        _toolTip.SetResourceReference(Control.ForegroundProperty, "VP.TextPrimaryBrush");
        _toolTip.SetResourceReference(Control.BorderBrushProperty, "VP.BorderLightBrush");
        _toolTip.SetResourceReference(Control.FontFamilyProperty, "VP.FontFamily");
        _toolTipTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _toolTipTimer.Tick += (_, _) =>
        {
            _toolTipTimer.Stop();
            if (_lastHoveredNode is not null && IsMouseOver)
                _toolTip.IsOpen = true;
        };
        Unloaded += (_, _) => ClearHover();
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        RenderedNodeCount = TitleClipNodeCount = RenderedLabelCount = 0;
        SKCanvas canvas = e.Surface.Canvas;
        canvas.Clear(SKColor.Parse("#1B2430"));
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        // 布局和命中使用逻辑坐标，绘制时统一映射到当前 DPI。
        float dpiScaleX = e.Info.Width / (float)ActualWidth;
        float dpiScaleY = e.Info.Height / (float)ActualHeight;
        canvas.Scale(dpiScaleX, dpiScaleY);

        // 圆角裁剪：在屏幕坐标（DPI 之后、视口变换之前）裁出可视区域，
        // 这样缩放/平移后内容不会溢出圆角。
        const float r = 10f;
        float w = (float)ActualWidth;
        float h = (float)ActualHeight;
        using var clipPath = new SKPath();
        clipPath.MoveTo(0, 0);
        clipPath.LineTo(w, 0);
        clipPath.LineTo(w, h - r);
        clipPath.ArcTo(r, r, 0, SKPathArcSize.Small, SKPathDirection.Clockwise, w - r, h);
        clipPath.LineTo(r, h);
        clipPath.ArcTo(r, r, 0, SKPathArcSize.Small, SKPathDirection.Clockwise, 0, h - r);
        clipPath.Close();
        canvas.ClipPath(clipPath);

        // 视口变换：screen = logical * zoom + pan。cell 规格（标题栏/字号/边框）由布局按 zoom 折算，屏幕上恒定。
        canvas.Translate(_panX, _panY);
        canvas.Scale(_zoom);

        if (_nodes is null || _nodes.Count == 0)
            return;

        using var fillPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            IsAntialias = false,
        };

        using var borderPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
            IsAntialias = false,
        };

        using var textPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
        };

        using var labelFont = CreateTreemapFont(Spec(11f));

        // Render in depth order: shallow first, deep on top.
        // Directories first at each depth, then files on top.
        var sortedNodes = GetVisibleNodes()
            .OrderBy(n => n.Depth)
            .ThenBy(n => n.Entry.IsDirectory ? 0 : 1)
            .ToList();

        RenderedNodeCount = sortedNodes.Count;
        foreach (var node in sortedNodes)
        {
            if (node.Entry.IsDirectory && node.AggregatedCount == 0)
            {
                DrawDirectoryNode(canvas, node, fillPaint, borderPaint, textPaint, labelFont);
            }
            else
            {
                DrawFileNode(canvas, node, fillPaint, borderPaint, textPaint, labelFont);
            }
        }

        DrawPointerLight(canvas, sortedNodes);

        // 多选集合先画纯白描边（除当前选中焦点外）。
        foreach (var node in _multiSelection)
        {
            if (node != SelectedNode) DrawOutline(canvas, node, Spec(2f), SKColors.White);
        }

        // 最后叠加选中态纯白粗框 + 悬停细白框，目录选择框不会被子块覆盖。
        if (_lastHoveredNode != SelectedNode) DrawOutline(canvas, _lastHoveredNode, Spec(1f), SKColors.White);
        DrawOutline(canvas, SelectedNode, Spec(2.5f), SKColors.White);

    }

    // 在排序、颜色解析、渐变和文字测量之前剔除视口外节点。
    // 保留完整布局，平移时可直接恢复细节，不把不可见等同于不存在。
    private IEnumerable<TreemapNode> GetVisibleNodes()
    {
        var viewport = new SKRect(-_panX / _zoom, -_panY / _zoom,
            ((float)ActualWidth - _panX) / _zoom, ((float)ActualHeight - _panY) / _zoom);
        return _nodes?.Where(node => node.IsVisible && NodeRect(node).IntersectsWith(viewport))
            ?? Enumerable.Empty<TreemapNode>();
    }

    private void DrawDirectoryNode(
        SKCanvas canvas, TreemapNode node,
        SKPaint fillPaint, SKPaint borderPaint, SKPaint textPaint, SKFont font)
    {
        var rect = NodeRect(node);
        // 目录（含标题栏）使用中性色，颜色由布局引擎统一分配。
        var titleColor = SKColor.Parse(string.IsNullOrWhiteSpace(node.ColorHex) ? "#303030" : node.ColorHex);
        fillPaint.Color = SKColor.Parse(node.ContentColorHex ?? node.ColorHex);
        canvas.DrawRect(rect, fillPaint);
        borderPaint.Color = DarkenColor(titleColor, 0.20f);
        borderPaint.StrokeWidth = Spec(1f);
        var frame = rect;
        frame.Inflate(-Spec(0.5f), -Spec(0.5f));
        canvas.DrawRect(frame, borderPaint);

        float headerHeight = GetDirectoryTitleHeight(node);
        if (node.Label is not null && headerHeight >= Spec(14f))
        {
            fillPaint.Color = titleColor;
            canvas.DrawRect(new SKRect(rect.Left, rect.Top, rect.Right, rect.Top + headerHeight), fillPaint);
            float nameWidth = node.Width - Spec(8f);
            if (node.SizeLabel is { } size && node.Width * _zoom >= 240f)
            {
                float sizeWidth = font.MeasureText(size);
                // 容量与名称分开，只有留有充分名称空间时才显示弱化的容量。
                if (nameWidth - sizeWidth >= Spec(120f))
                {
                    textPaint.Color = new SKColor(170, 175, 182);
                    canvas.DrawText(size, rect.Right - Spec(5f), node.Y + headerHeight / 2f + Spec(3.5f),
                        SKTextAlign.Right, font, textPaint);
                    nameWidth -= sizeWidth + Spec(12f);
                }
            }
            DrawLabel(canvas, node.Label, node.X + Spec(4f), node.Y + headerHeight / 2f + Spec(3.5f),
                nameWidth, textPaint, font, titleColor);
        }

        if (node.Entry.IsAccessDenied)
            DrawAccessDeniedOverlay(canvas, rect);
    }

    private void DrawFileNode(
        SKCanvas canvas, TreemapNode node,
        SKPaint fillPaint, SKPaint borderPaint, SKPaint textPaint, SKFont font)
    {
        var rect = NodeRect(node);
        bool detailed = Math.Min(rect.Width, rect.Height) >= Spec(5f);
        if (detailed) rect.Inflate(-Spec(0.5f), -Spec(0.5f)); // 小于 5px 时优先保留占用色
        var color = SKColor.Parse(node.ColorHex);
        fillPaint.Color = color;
        // 只给可读的大块添加轻微同色系过渡，细碎像素保留纯色，避免阴影和过度绘制。
        using SKShader? gradient = rect.Width >= Spec(32f) && rect.Height >= Spec(24f)
            ? SKShader.CreateLinearGradient(new SKPoint(rect.MidX, rect.Top), new SKPoint(rect.MidX, rect.Bottom),
                [LightenColor(color, 0.035f), DarkenColor(color, 0.035f)], [0f, 1f], SKShaderTileMode.Clamp)
            : null;
        fillPaint.Shader = gradient;
        canvas.DrawRect(rect, fillPaint);
        fillPaint.Shader = null;

        borderPaint.Color = DarkenColor(color, 0.14f);
        borderPaint.StrokeWidth = Spec(0.5f);
        var frame = rect;
        frame.Inflate(-Spec(0.25f), -Spec(0.25f));
        if (detailed) canvas.DrawRect(frame, borderPaint);

        if (node.Label is not null && TreemapLabelPolicy.ShowFileLabel(node.Width, node.Height, _zoom))
            DrawLabel(canvas, node.Label, rect.Left + Spec(3f), rect.Top + Spec(11f),
                rect.Width - Spec(6f), textPaint, font, color);

        if (node.Entry.IsAccessDenied)
            DrawAccessDeniedOverlay(canvas, rect);
    }

    /// <summary>屏幕像素规格 → 逻辑坐标（除以 zoom 后绘制，屏幕上保持恒定）。</summary>
    private float Spec(float screenPx) => screenPx / _zoom;

    private static SKRect NodeRect(TreemapNode node) =>
        new(node.X, node.Y, node.X + node.Width, node.Y + node.Height);

    private void DrawPointerLight(SKCanvas canvas, IEnumerable<TreemapNode> visibleNodes)
    {
        if (_lastHoveredNode is null) return;
        // 指针先逆变换为布局坐标，缩放、平移后光晕仍跟随鼠标。
        var center = new SKPoint((float)(_hoverPosition.X - _panX) / _zoom,
            (float)(_hoverPosition.Y - _panY) / _zoom);
        if (_lastHoveredNode.Entry.IsDirectory && _lastHoveredNode.AggregatedCount == 0
            && center.Y < _lastHoveredNode.Y + GetDirectoryTitleHeight(_lastHoveredNode)) return;
        using var light = SKShader.CreateRadialGradient(center, Spec(150f),
            [new SKColor(255, 255, 255, 42), new SKColor(255, 255, 255, 0)],
            [0f, 1f], SKShaderTileMode.Clamp);
        using var paint = new SKPaint { Shader = light, IsAntialias = true };
        using var titleAreas = new SKPath();
        foreach (var n in visibleNodes)
        {
            float titleHeight = GetDirectoryTitleHeight(n);
            if (titleHeight > 0)
            {
                titleAreas.AddRect(new SKRect(n.X, n.Y, n.X + n.Width, n.Y + titleHeight));
                TitleClipNodeCount++;
            }
        }
        canvas.Save();
        canvas.ClipPath(titleAreas, SKClipOperation.Difference, false);
        canvas.DrawCircle(center, Spec(150f), paint);
        canvas.Restore();
    }

    private float GetDirectoryTitleHeight(TreemapNode node)
    {
        if (!node.Entry.IsDirectory || node.AggregatedCount > 0 || node.Label is null) return 0f;
        return TreemapLabelPolicy.DirectoryHeaderHeight(node.Width, node.Height, node.Depth, _zoom);
    }

    private void DrawOutline(SKCanvas canvas, TreemapNode? node, float width, SKColor color)
    {
        if (node is null || !node.IsVisible) return;
        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = width,
            Color = color,
            IsAntialias = false,
        };
        var rect = NodeRect(node);
        float outset = width > Spec(1f) ? width / 2f : -width / 2f;
        rect.Inflate(outset, outset);
        canvas.DrawRect(rect, paint);
    }

    private void DrawLabel(SKCanvas canvas, string text, float x, float y,
        float maxWidth, SKPaint paint, SKFont font, SKColor background)
    {
        var label = TruncateLabel(text, font, maxWidth);
        if (label is null) return;
        // 按底色亮度自适应文字色（亮底深字、暗底白字），不加阴影保持清晰。
        paint.Color = IsLightColor(background)
            ? new SKColor(26, 30, 40)
            : new SKColor(245, 247, 250);
        canvas.DrawText(label, x, y, SKTextAlign.Left, font, paint);
        RenderedLabelCount++;
    }

    private static bool IsLightColor(SKColor color)
    {
        // Rec.601 相对亮度。
        float luminance = 0.299f * color.Red + 0.587f * color.Green + 0.114f * color.Blue;
        return luminance >= 150f;
    }

    // cell 规格按屏幕像素恒定：标题栏始终 18 屏幕像素（与布局引擎 SquarifiedTreemapLayout 一致）。
    internal static SKFont CreateTreemapFont(float fontSize) => new(TreemapTypeface.Value, fontSize);

    private static SKTypeface CreateTreemapTypeface()
    {
        foreach (var family in TreemapFontFamilies)
        {
            var typeface = SKTypeface.FromFamilyName(family);
            if (typeface is not null && SupportsTreemapGlyphs(typeface))
            {
                return typeface;
            }

            typeface?.Dispose();
        }

        var matchedTypeface = SKFontManager.Default.MatchCharacter('中');
        if (matchedTypeface is not null && SupportsTreemapGlyphs(matchedTypeface))
        {
            return matchedTypeface;
        }

        return SKTypeface.Default;
    }

    private static bool SupportsTreemapGlyphs(SKTypeface typeface)
    {
        using var font = new SKFont(typeface, 11f);
        return font.ContainsGlyphs(TreemapGlyphProbe);
    }

    public TreemapNode? HitTest(double x, double y)
    {
        if (_nodes is null || _nodes.Count == 0)
            return null;

        // 屏幕坐标 → 逻辑坐标（逆视口变换：screen = logical * zoom + pan）。
        double lx = (x - _panX) / _zoom;
        double ly = (y - _panY) / _zoom;

        TreemapNode? deepest = null;

        for (int i = _nodes.Count - 1; i >= 0; i--)
        {
            var node = _nodes[i];
            if (!node.IsVisible)
                continue;

            if (lx >= node.X && lx < node.X + node.Width &&
                ly >= node.Y && ly < node.Y + node.Height)
            {
                if (deepest is null || node.Depth > deepest.Depth)
                {
                    deepest = node;
                }
            }
        }

        return deepest;
    }

    // 左键按下后先捕获，释放时才区分点击与拖动，避免平移误触选择或钻取。
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        HandleLeftPress(e.GetPosition(this), e.ClickCount, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        HandleLeftRelease(e.GetPosition(this));
        e.Handled = true;
    }

    internal void HandleLeftPress(Point pos, int clickCount, bool ctrlHeld)
    {
        _isPanning = true;
        _panStartScreen = pos;
        _panStartPanX = _panX;
        _panStartPanY = _panY;
        _panDragExceededThreshold = false;
        _pressClickCount = clickCount;
        _pressCtrlHeld = ctrlHeld;
    }

    internal void HandleLeftRelease(Point pos)
    {
        if (!_isPanning) return;
        HandlePointerMove(pos);
        bool click = !_panDragExceededThreshold;
        EndPan();
        if (click) HandleClick(pos, _pressClickCount, _pressCtrlHeld);
        HandleHover(pos);
    }

    private void EndPan()
    {
        _isPanning = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        Cursor = Cursors.Arrow;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _isPanning = false;
        Cursor = Cursors.Arrow;
    }

    internal void HandleClick(Point position, int clickCount, bool ctrlHeld)
    {
        _toolTipTimer.Stop();
        _toolTip.IsOpen = false;
        var node = HitTest(position.X, position.Y);
        if (node is null) return;

        if (ctrlHeld)
        {
            // Ctrl 多选：toggle，不改变单选焦点钻取语义。
            ToggleMultiSelection(node);
            return;
        }

        // 普通单击：选中（不钻取）。
        if (!_multiSelection.Contains(node))
            ClearMultiSelection();
        SelectedNode = node;
        NodeClicked?.Invoke(this, node);

        float headerHeight = GetDirectoryTitleHeight(node);
        double logicalY = (position.Y - _panY) / _zoom;
        if (clickCount == 2 && node.Entry.IsDirectory && node.AggregatedCount == 0 && node.Label is not null
            && headerHeight >= Spec(14f) && logicalY < node.Y + headerHeight)
        {
            // 双击钻取目录。
            ClearHover();
            ClearMultiSelection();
            NodeActivated?.Invoke(this, node);
        }
    }

    private void ToggleMultiSelection(TreemapNode node)
    {
        if (_multiSelection.Contains(node))
            _multiSelection.Remove(node);
        else
            _multiSelection.Add(node);
        MultiSelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private void ClearMultiSelection()
    {
        if (_multiSelection.Count == 0) return;
        _multiSelection.Clear();
        MultiSelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        HandlePointerMove(e.GetPosition(this));
    }

    internal void HandlePointerMove(Point pos)
    {
        if (!_isPanning) { HandleHover(pos); return; }
        double dx = pos.X - _panStartScreen.X;
        double dy = pos.Y - _panStartScreen.Y;
        if (!_panDragExceededThreshold && Math.Abs(dx) < DragThreshold && Math.Abs(dy) < DragThreshold) return;
        _panDragExceededThreshold = true;
        _panX = _panStartPanX + (float)dx;
        _panY = _panStartPanY + (float)dy;
        ClampPan();
        Cursor = Cursors.SizeAll;
        ClearHover();
        InvalidateVisual();
    }

    private TreemapNode? DirectoryAt(Point position)
    {
        var hit = HitTest(position.X, position.Y);
        var entry = hit?.Entry.IsDirectory == true ? hit.Entry : hit?.Entry.Parent;
        while (entry is not null)
        {
            if (ReferenceEquals(entry, _viewRoot)) return null;
            var node = _nodes?.FirstOrDefault(n => n.IsVisible && n.AggregatedCount == 0
                && ReferenceEquals(n.Entry, entry));
            if (node is not null && entry.Size > 0 && entry.Children.Any(c => c.Size > 0)) return node;
            entry = entry.Parent;
        }
        return null;
    }

    // ── 滚轮缩放（指针锚定）+ 键盘缩放/平移 ──
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        var pos = e.GetPosition(this);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            // Shift + 滚轮：水平平移。
            _panX += e.Delta > 0 ? 40 : -40;
            ClampPan();
        }
        else
        {
            RaiseWheelAt((float)pos.X, (float)pos.Y, e.Delta);
        }
        e.Handled = true;
    }

    /// <summary>验收/测试注入：在指定控件坐标处执行一次滚轮缩放。</summary>
    internal void RaiseWheelAt(float x, float y, int delta)
    {
        if (delta == 0) return;
        if (ConsumeSnapWheel()) return;
        float factor = MathF.Pow(ZoomStep, delta / 120f);
        ZoomAt(x, y, factor);
        if (!_isPanning) HandleHover(new Point(x, y));
    }

    private void ZoomAt(float screenX, float screenY, float factor)
    {
        var position = new Point(screenX, screenY);
        if (factor < 1f && _zoom <= MinZoom + 0.0001f)
        {
            ExitWheelLevel();
            return;
        }
        var target = DirectoryAt(position);
        // 没有更深的可进入目录时停止放大，末级目录保留完整标题与文件名。
        if (factor > 1f && target is null && _viewRoot is not null) return;
        float ceiling = MaxZoom;
        if (target is not null)
        {
            float bodyHeight = target.Height - GetDirectoryTitleHeight(target);
            ceiling = Math.Clamp(Math.Min((float)ActualWidth / target.Width,
                (float)ActualHeight / Math.Max(bodyHeight, Spec(1f))), MinZoom, MaxZoom);
        }
        if (factor > 1f && target is not null && _viewRoot is not null && _zoom >= ceiling - 0.0001f)
        {
            EnterWheelLevel(target.Entry);
            return;
        }
        // 缩小时不能因指针碰到另一目录而突然跳变到更小的适配上限。
        float newZoom = Math.Clamp(_zoom * factor, MinZoom, factor > 1f ? Math.Max(_zoom, ceiling) : MaxZoom);
        if (Math.Abs(newZoom - _zoom) < 1e-6f) return;

        // 记录锚点：指针下的节点及其内部相对位置，LOD 重布局后对齐回来。
        _zoomAnchorEntry = null;
        var anchorNode = HitTest(screenX, screenY);
        if (anchorNode is not null && anchorNode.Width > 0 && anchorNode.Height > 0)
        {
            float anchorLogicalX = ((float)screenX - _panX) / _zoom;
            float anchorLogicalY = ((float)screenY - _panY) / _zoom;
            _zoomAnchorEntry = anchorNode.Entry;
            _zoomAnchorAggregate = anchorNode.AggregatedCount > 0;
            _zoomAnchorFx = (anchorLogicalX - anchorNode.X) / anchorNode.Width;
            _zoomAnchorFy = (anchorLogicalY - anchorNode.Y) / anchorNode.Height;
            _zoomAnchorScreen = new Point(screenX, screenY);
        }

        // 先按几何比例保持锚点不动（重布局前画面平滑），LOD 重布局后再精确对齐。
        float ratio = newZoom / _zoom;
        _panX = screenX - (screenX - _panX) * ratio;
        _panY = screenY - (screenY - _panY) * ratio;
        _zoom = newZoom;
        if (_zoom <= MinZoom)
        {
            // 回到 1x：整图恰好铺满视口，复位平移。
            _panX = 0f;
            _panY = 0f;
            _zoomAnchorEntry = null;
        }
        ClampPan();
        ZoomChanged?.Invoke(this, _zoom);
        InvalidateVisual();
    }

    private void EnterWheelLevel(FileEntry directory)
    {
        if (_viewRoot is null || RootNavigationRequested is null) return;
        var frame = new ViewportFrame(_viewRoot, _zoom, _panX, _panY);
        _wheelNavigating = true;
        try
        {
            RootNavigationRequested.Invoke(this, directory);
            if (ReferenceEquals(_viewRoot, directory))
            {
                _wheelHistory.Push(frame);
                HoldWheelSnap();
            }
        }
        finally { _wheelNavigating = false; }
    }

    private void ExitWheelLevel()
    {
        if (_viewRoot is null || ReferenceEquals(_viewRoot, _scanRoot) || RootNavigationRequested is null) return;
        var parent = _viewRoot.Parent;
        if (parent is null) return;
        var saved = _wheelHistory.Count > 0 ? _wheelHistory.Peek() : null;
        // 进入时可能跨过多个已展开层级；退出仍按真实父目录逐层返回。
        var frame = saved is not null && ReferenceEquals(saved.Root, parent) ? _wheelHistory.Pop() : null;
        _wheelNavigating = true;
        try
        {
            RootNavigationRequested.Invoke(this, parent);
            if (frame is not null && ReferenceEquals(parent, _viewRoot))
            {
                _zoom = frame.Zoom;
                _panX = frame.PanX;
                _panY = frame.PanY;
                _zoomAnchorEntry = null;
                ZoomChanged?.Invoke(this, _zoom);
                ClampPan();
            }
            if (ReferenceEquals(parent, _viewRoot)) HoldWheelSnap();
        }
        finally { _wheelNavigating = false; }
        InvalidateVisual();
    }

    // 换层后先停在完整视图。连续滚轮手势也必须结束，防止滞留刚结束就被余量滚过标题。
    private void HoldWheelSnap()
    {
        long now = WheelClock.GetUtcNow().ToUnixTimeMilliseconds();
        _snapUntil = now + SnapPauseMilliseconds;
        _lastSnapWheel = now;
        _snapActive = true;
    }

    private bool ConsumeSnapWheel()
    {
        if (!_snapActive) return false;
        long now = WheelClock.GetUtcNow().ToUnixTimeMilliseconds();
        bool consume = now < _snapUntil || now - _lastSnapWheel < SnapWheelIdleMilliseconds;
        _lastSnapWheel = now;
        if (!consume) _snapActive = false;
        return consume;
    }

    /// <summary>LOD 重布局后，把锚定节点对齐回指针下的相对位置。</summary>
    private void ApplyZoomAnchor()
    {
        if (_zoomAnchorEntry is null)
            return;
        var entry = _zoomAnchorEntry;
        _zoomAnchorEntry = null;
        var node = _nodes?.FirstOrDefault(n => n.IsVisible
            && (n.AggregatedCount > 0) == _zoomAnchorAggregate
            && ReferenceEquals(n.Entry, entry));
        if (node is null || node.Width <= 0 || node.Height <= 0)
            return;
        float anchorX = node.X + _zoomAnchorFx * node.Width;
        float anchorY = node.Y + _zoomAnchorFy * node.Height;
        _panX = (float)_zoomAnchorScreen.X - anchorX * _zoom;
        _panY = (float)_zoomAnchorScreen.Y - anchorY * _zoom;
        ClampPan();
    }

    /// <summary>始终把视口约束在地图内，1x 时平移归零。</summary>
    private void ClampPan()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
            return;
        float minX = (float)ActualWidth * (1f - _zoom);
        float minY = (float)ActualHeight * (1f - _zoom);
        _panX = Math.Clamp(_panX, minX, 0f);
        _panY = Math.Clamp(_panY, minY, 0f);
    }

    public void ResetView()
    {
        _snapActive = false;
        if (_zoom != 1f || _panX != 0f || _panY != 0f)
        {
            _zoom = 1f;
            _panX = 0f;
            _panY = 0f;
            ZoomChanged?.Invoke(this, _zoom);
        }
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key)
        {
            case Key.Add:
            case Key.OemPlus:
                ZoomAt((float)ActualWidth / 2f, (float)ActualHeight / 2f, KeyboardZoomStep);
                e.Handled = true;
                break;
            case Key.Subtract:
            case Key.OemMinus:
                ZoomAt((float)ActualWidth / 2f, (float)ActualHeight / 2f, 1f / KeyboardZoomStep);
                e.Handled = true;
                break;
            case Key.D0:
            case Key.NumPad0:
                ResetView();
                e.Handled = true;
                break;
        }
    }

    internal void HandleHover(Point position)
    {
        _hoverPosition = position;
        _pointerInside = position.X >= 0 && position.Y >= 0
            && position.X < ActualWidth && position.Y < ActualHeight;
        var node = HitTest(position.X, position.Y);
        if (node == _lastHoveredNode)
        {
            if (node is not null) InvalidateVisual();
            return;
        }

        ClearHover();
        _lastHoveredNode = node;
        if (node is not null)
        {
            UpdateToolTipContent(node);
            _toolTipTimer.Start();
            NodeHovered?.Invoke(this, node);
        }
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _pointerInside = false;
        ClearHover();
    }

    private void ClearHover()
    {
        _toolTipTimer.Stop();
        _toolTip.IsOpen = false;
        if (_lastHoveredNode is null) return;
        _lastHoveredNode = null;
        NodeHovered?.Invoke(this, null);
        InvalidateVisual();
    }

    private void UpdateToolTipContent(TreemapNode node)
    {
        var entry = node.Entry;
        string extension = string.IsNullOrWhiteSpace(entry.Extension)
            ? System.IO.Path.GetExtension(entry.Name) : entry.Extension;
        string fileType = entry.IsDirectory ? L.Text("FolderType")
            : string.IsNullOrEmpty(extension) ? L.Text("TreemapNoExtension") : extension.ToLowerInvariant();
        string size = FileSizeConverter.FormatSize(entry.Size);
        string modified = entry.LastModified.ToString("g");

        var panel = new StackPanel { Margin = new Thickness(2) };

        panel.Children.Add(new TextBlock
        {
            Text = entry.Path,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 400,
        });

        panel.Children.Add(new TextBlock
        {
            Text = L.Format("TooltipSize", size),
            Margin = new Thickness(0, 2, 0, 0),
        });

        panel.Children.Add(new TextBlock
        {
            Text = L.Format("TooltipType", fileType),
            Margin = new Thickness(0, 2, 0, 0),
        });

        panel.Children.Add(new TextBlock
        {
            Text = L.Format("TooltipModified", modified),
            Margin = new Thickness(0, 2, 0, 0),
        });

        if (entry.IsAccessDenied)
        {
            var deniedText = new TextBlock
            {
                Text = L.Text("AccessDenied"),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 2, 0, 0),
            };
            deniedText.SetResourceReference(TextBlock.ForegroundProperty, "VP.DangerTextBrush");
            panel.Children.Add(deniedText);
        }

        _toolTip.Content = panel;
    }

    protected override void OnMouseRightButtonUp(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        var position = e.GetPosition(this);
        var node = HitTest(position.X, position.Y);
        if (node is not null)
        {
            _toolTipTimer.Stop();
            _toolTip.IsOpen = false;
            SelectedNode = node;
            NodeClicked?.Invoke(this, node);
            NodeRightClicked?.Invoke(this, node);
        }
    }

    private void DrawAccessDeniedOverlay(SKCanvas canvas, SKRect rect)
    {
        using var hatchPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(255, 80, 80, 160),
            StrokeWidth = Spec(1.5f),
            IsAntialias = true,
        };

        canvas.Save();
        canvas.ClipRect(rect);

        float step = Spec(8f);
        float totalSpan = rect.Width + rect.Height;
        for (float offset = 0; offset < totalSpan; offset += step)
        {
            float x0 = rect.Left + offset;
            float y0 = rect.Bottom;
            float x1 = rect.Left + offset - rect.Height;
            float y1 = rect.Top;
            canvas.DrawLine(x0, y0, x1, y1, hatchPaint);
        }

        canvas.Restore();
    }

    /// <summary>
    /// Truncates a label to fit within maxWidth, appending "..." if needed.
    /// Returns null if even "..." won't fit.
    /// </summary>
    private static string? TruncateLabel(string label, SKFont font, float maxWidth)
    {
        if (maxWidth <= 0)
            return null;

        float fullWidth = font.MeasureText(label);
        if (fullWidth <= maxWidth)
            return label;

        float ellipsisWidth = font.MeasureText("...");
        if (ellipsisWidth >= maxWidth)
            return null;

        float available = maxWidth - ellipsisWidth;

        // Binary search for the longest prefix that fits.
        int lo = 0, hi = label.Length;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (font.MeasureText(label.AsSpan(0, mid)) <= available)
                lo = mid;
            else
                hi = mid - 1;
        }

        // 不显示只有两三字加省略号的无效标注；完整短名称仍可直接显示。
        if (lo < Math.Min(6, label.Length))
            return null;

        return string.Concat(label.AsSpan(0, lo), "...");
    }

    private static SKColor LightenColor(SKColor color, float factor) => new(
        (byte)(color.Red + (255 - color.Red) * factor),
        (byte)(color.Green + (255 - color.Green) * factor),
        (byte)(color.Blue + (255 - color.Blue) * factor), color.Alpha);

    private static SKColor DarkenColor(SKColor color, float factor)
    {
        byte r = (byte)(color.Red * (1f - factor));
        byte g = (byte)(color.Green * (1f - factor));
        byte b = (byte)(color.Blue * (1f - factor));
        return new SKColor(r, g, b, color.Alpha);
    }

    /// <summary>
    /// 将视图定位（focus）到指定节点并高亮。供 AI focus_path 与外部定位调用。
    /// 布局已在 ViewModel 层完成导航/钻取，这里负责：清空视口缩放并高亮目标。
    /// </summary>
    public void FocusNode(TreemapNode node, bool highlight = true)
    {
        if (_nodes is null || !_nodes.Contains(node)) return;

        // 复位视口缩放/平移，确保目标以 1:1 可见，并同步 LOD 级别。
        _snapActive = false;
        _zoom = 1f;
        _panX = 0f;
        _panY = 0f;
        _zoomAnchorEntry = null;
        ZoomChanged?.Invoke(this, _zoom);

        if (highlight)
        {
            SelectedNode = node;
            ClearMultiSelection();
        }
        InvalidateVisual();
    }
}
