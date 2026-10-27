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

namespace DiskPilotX.App.Controls;

public class TreemapControl : SKElement
{
    private const string TreemapGlyphProbe = "中文";

    private const float MinZoom = 1f;
    private const float MaxZoom = 8f;
    private const float DragThreshold = 4f;
    private const float ZoomStep = 1.25f;
    private const float KeyboardZoomStep = 1.5f; // 键盘缩放步进

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
    private readonly DispatcherTimer _toolTipTimer;

    // ── 视口变换（逻辑坐标 → 屏幕坐标：screen = logical * zoom + pan）──
    // zoom 同时驱动 LOD 重布局；cell 规格（标题栏/字号/边框）按屏幕像素恒定。
    private float _zoom = 1f;
    private float _panX;
    private float _panY;

    // ── 拖拽平移状态 ──
    private bool _isPanning;
    private Point _panStartScreen;
    private float _panStartPanX;
    private float _panStartPanY;
    private bool _panDragExceededThreshold;

    // ── 缩放锚点：LOD 重布局后保持指针下的节点相对位置不变 ──
    private FileEntry? _zoomAnchorEntry;
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

    public event EventHandler<TreemapNode>? NodeClicked;
    public event EventHandler<TreemapNode>? NodeActivated;
    public event EventHandler<TreemapNode?>? NodeHovered;
    public event EventHandler<TreemapNode>? NodeRightClicked;
    public event EventHandler? MultiSelectionChanged;

    /// <summary>缩放级别改变时触发（参数为新 zoom），供 LOD 重布局。</summary>
    public event EventHandler<float>? ZoomChanged;

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
        var sortedNodes = _nodes
            .Where(n => n.IsVisible)
            .OrderBy(n => n.Depth)
            .ThenBy(n => n.Entry.IsDirectory ? 0 : 1)
            .ToList();

        foreach (var node in sortedNodes)
        {
            if (node.Entry.IsDirectory)
            {
                DrawDirectoryNode(canvas, node, fillPaint, borderPaint, textPaint, labelFont);
            }
            else
            {
                DrawFileNode(canvas, node, fillPaint, borderPaint, textPaint, labelFont);
            }
        }

        DrawPointerLight(canvas);

        // 多选集合先画蓝色描边（除当前选中焦点外）。
        foreach (var node in _multiSelection)
        {
            if (node != SelectedNode) DrawOutline(canvas, node, Spec(2f), new SKColor(66, 165, 245));
        }

        // 最后叠加选中态纯白粗框 + 悬停细白框，目录选择框不会被子块覆盖。
        if (_lastHoveredNode != SelectedNode) DrawOutline(canvas, _lastHoveredNode, Spec(1f), SKColors.White);
        DrawOutline(canvas, SelectedNode, Spec(2.5f), SKColors.White);

    }

    private void DrawDirectoryNode(
        SKCanvas canvas, TreemapNode node,
        SKPaint fillPaint, SKPaint borderPaint, SKPaint textPaint, SKFont font)
    {
        var rect = NodeRect(node);
        // 目录（含标题栏）使用中性色，颜色由布局引擎统一分配。
        fillPaint.Color = SKColor.Parse(string.IsNullOrWhiteSpace(node.ColorHex) ? "#303030" : node.ColorHex);
        canvas.DrawRect(rect, fillPaint);
        borderPaint.Color = DarkenColor(fillPaint.Color, 0.20f);
        borderPaint.StrokeWidth = Spec(1f);
        var frame = rect;
        frame.Inflate(-Spec(0.5f), -Spec(0.5f));
        canvas.DrawRect(frame, borderPaint);

        float headerHeight = GetHeaderHeight(node.Height);
        if (node.Label is not null && headerHeight >= Spec(14f))
            DrawLabel(canvas, node.Label, node.X + Spec(3f), node.Y + headerHeight / 2f + Spec(3.5f),
                node.Width - Spec(6f), textPaint, font, fillPaint.Color);

        if (node.Entry.IsAccessDenied)
            DrawAccessDeniedOverlay(canvas, rect);
    }

    private void DrawFileNode(
        SKCanvas canvas, TreemapNode node,
        SKPaint fillPaint, SKPaint borderPaint, SKPaint textPaint, SKFont font)
    {
        var rect = NodeRect(node);
        rect.Inflate(-Spec(0.5f), -Spec(0.5f)); // cell 间留 1px 细缝（屏幕像素恒定），边界更清晰
        var color = SKColor.Parse(node.ColorHex);
        fillPaint.Color = color;
        canvas.DrawRect(rect, fillPaint);

        borderPaint.Color = DarkenColor(color, 0.65f);
        borderPaint.StrokeWidth = Spec(0.5f);
        var frame = rect;
        frame.Inflate(-Spec(0.25f), -Spec(0.25f));
        canvas.DrawRect(frame, borderPaint);

        if (node.Label is not null && rect.Height >= Spec(14f))
            DrawLabel(canvas, node.Label, rect.Left + Spec(3f), rect.Top + Spec(11f),
                rect.Width - Spec(6f), textPaint, font, color);

        if (node.Entry.IsAccessDenied)
            DrawAccessDeniedOverlay(canvas, rect);
    }

    /// <summary>屏幕像素规格 → 逻辑坐标（除以 zoom 后绘制，屏幕上保持恒定）。</summary>
    private float Spec(float screenPx) => screenPx / _zoom;

    private static SKRect NodeRect(TreemapNode node) =>
        new(node.X, node.Y, node.X + node.Width, node.Y + node.Height);

    private void DrawPointerLight(SKCanvas canvas)
    {
        if (_lastHoveredNode is null) return;
        // 用悬停节点在逻辑坐标里的中心做光斑，避免与视口缩放/平移错位。
        var node = _lastHoveredNode;
        var center = new SKPoint(node.X + node.Width / 2f, node.Y + node.Height / 2f);
        using var light = SKShader.CreateRadialGradient(center, Spec(150f),
            [new SKColor(255, 255, 255, 42), new SKColor(255, 255, 255, 0)],
            [0f, 1f], SKShaderTileMode.Clamp);
        using var paint = new SKPaint { Shader = light, IsAntialias = true };
        using var titleAreas = new SKPath();
        foreach (var n in _nodes!)
        {
            float titleHeight = GetDirectoryTitleHeight(n);
            if (n.IsVisible && titleHeight > 0)
                titleAreas.AddRect(new SKRect(n.X, n.Y, n.X + n.Width, n.Y + titleHeight));
        }
        canvas.Save();
        canvas.ClipPath(titleAreas, SKClipOperation.Difference, false);
        canvas.DrawCircle(center, Spec(150f), paint);
        canvas.Restore();
    }

    private float GetDirectoryTitleHeight(TreemapNode node)
    {
        if (!node.Entry.IsDirectory || node.Label is null) return 0f;
        float height = GetHeaderHeight(node.Height);
        return height >= Spec(14f) && node.Height >= height ? height : 0f;
    }

    private static void DrawOutline(SKCanvas canvas, TreemapNode? node, float width, SKColor color)
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
        float outset = width > 1f ? width / 2f : -width / 2f;
        rect.Inflate(outset, outset);
        canvas.DrawRect(rect, paint);
    }

    private static void DrawLabel(SKCanvas canvas, string text, float x, float y,
        float maxWidth, SKPaint paint, SKFont font, SKColor background)
    {
        var label = TruncateLabel(text, font, maxWidth);
        if (label is null) return;
        // 按底色亮度自适应文字色（亮底深字、暗底白字），不加阴影保持清晰。
        paint.Color = IsLightColor(background)
            ? new SKColor(26, 30, 40)
            : new SKColor(245, 247, 250);
        canvas.DrawText(label, x, y, SKTextAlign.Left, font, paint);
    }

    private static bool IsLightColor(SKColor color)
    {
        // Rec.601 相对亮度。
        float luminance = 0.299f * color.Red + 0.587f * color.Green + 0.114f * color.Blue;
        return luminance >= 150f;
    }

    // cell 规格按屏幕像素恒定：标题栏始终 18 屏幕像素（与布局引擎 SquarifiedTreemapLayout 一致）。
    private float GetHeaderHeight(float availableHeight)
        => Math.Min(Spec(18f), availableHeight * 0.4f);

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

    // ── 左键：单击选中 / Ctrl 多选 / 双击钻取 / 空白拖拽平移 ──
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        var pos = e.GetPosition(this);
        var node = HitTest(pos.X, pos.Y);

        if (node is null)
        {
            // 空白处开始拖拽平移。
            BeginPan(pos);
            e.Handled = true;
            return;
        }

        HandleClick(pos, e.ClickCount, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_isPanning)
            EndPan();
    }

    private void BeginPan(Point pos)
    {
        _isPanning = true;
        _panStartScreen = pos;
        _panStartPanX = _panX;
        _panStartPanY = _panY;
        _panDragExceededThreshold = false;
        CaptureMouse();
        Cursor = Cursors.SizeAll;
    }

    private void EndPan()
    {
        _isPanning = false;
        ReleaseMouseCapture();
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

        float headerHeight = GetHeaderHeight(node.Height);
        double logicalY = (position.Y - _panY) / _zoom;
        if (clickCount == 2 && node.Entry.IsDirectory && node.Label is not null
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

    // ── 中键：按住拖拽平移（按需平移）──
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.ChangedButton == MouseButton.Middle)
        {
            BeginPan(e.GetPosition(this));
            e.Handled = true;
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.ChangedButton == MouseButton.Middle && _isPanning)
        {
            EndPan();
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var pos = e.GetPosition(this);

        if (_isPanning)
        {
            double dx = pos.X - _panStartScreen.X;
            double dy = pos.Y - _panStartScreen.Y;
            if (!_panDragExceededThreshold &&
                Math.Abs(dx) < DragThreshold && Math.Abs(dy) < DragThreshold)
                return;
            _panDragExceededThreshold = true;
            _panX = _panStartPanX + (float)dx;
            _panY = _panStartPanY + (float)dy;
            ClampPan();
            Cursor = Cursors.SizeAll;
            _toolTipTimer.Stop();
            _toolTip.IsOpen = false;
            InvalidateVisual();
            return;
        }

        HandleHover(pos);
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
            float factor = e.Delta > 0 ? ZoomStep : 1f / ZoomStep;
            ZoomAt((float)pos.X, (float)pos.Y, factor);
        }
        e.Handled = true;
    }

    /// <summary>验收/测试注入：在指定控件坐标处执行一次滚轮缩放。</summary>
    internal void RaiseWheelAt(float x, float y, int delta)
    {
        float factor = delta > 0 ? ZoomStep : 1f / ZoomStep;
        ZoomAt(x, y, factor);
    }

    private void ZoomAt(float screenX, float screenY, float factor)
    {
        float newZoom = Math.Clamp(_zoom * factor, MinZoom, MaxZoom);
        if (Math.Abs(newZoom - _zoom) < 1e-6f) return;

        // 记录锚点：指针下的节点及其内部相对位置，LOD 重布局后对齐回来。
        _zoomAnchorEntry = null;
        var anchorNode = HitTest(screenX, screenY);
        if (anchorNode is not null && anchorNode.Width > 0 && anchorNode.Height > 0)
        {
            float anchorLogicalX = ((float)screenX - _panX) / _zoom;
            float anchorLogicalY = ((float)screenY - _panY) / _zoom;
            _zoomAnchorEntry = anchorNode.Entry;
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

    /// <summary>LOD 重布局后，把锚定节点对齐回指针下的相对位置。</summary>
    private void ApplyZoomAnchor()
    {
        if (_zoomAnchorEntry is null)
            return;
        var entry = _zoomAnchorEntry;
        _zoomAnchorEntry = null;
        var node = _nodes?.FirstOrDefault(n => n.IsVisible
            && string.Equals(n.Entry.Path, entry.Path, StringComparison.OrdinalIgnoreCase));
        if (node is null || node.Width <= 0 || node.Height <= 0)
            return;
        float anchorX = node.X + _zoomAnchorFx * node.Width;
        float anchorY = node.Y + _zoomAnchorFy * node.Height;
        _panX = (float)_zoomAnchorScreen.X - anchorX * _zoom;
        _panY = (float)_zoomAnchorScreen.Y - anchorY * _zoom;
        ClampPan();
    }

    /// <summary>钳制平移量：地图始终铺满视口，任何 zoom 下都不露出空白。</summary>
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
        string fileType = entry.IsDirectory ? L.Text("FolderType") : (entry.Extension ?? L.Text("PropertiesUnknown"));
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

        // Require at least 3 visible characters before the ellipsis.
        if (lo < 3)
            return null;

        return string.Concat(label.AsSpan(0, lo), "...");
    }

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
