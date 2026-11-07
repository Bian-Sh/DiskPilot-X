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

    private const float DeletedOutlineScreenWidth = 1f;
    private TreemapNode? _pingNode;
    private readonly DispatcherTimer _pingTimer = new() { Interval = TimeSpan.FromMilliseconds(900) };
    internal FileEntry? PingedEntry => _pingNode?.Entry;

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
    private TreemapSceneIndex? _scene;
    private readonly TreemapSemanticTitles _semanticTitles = new();
    private SKImage? _baseImage;
    internal bool EnableAsyncRaster { get; set; }
    private CancellationTokenSource? _rasterRequest;
    private readonly SemaphoreSlim _rasterWorker = new(1,1);
    private int _rasterRevision = -1;
    private float _rasterZoom, _rasterPanX, _rasterPanY;
    private int _rasterWidth, _rasterHeight;
    private SKRect _visibleViewport;
    private TreemapNode[] _visibleNodes = [];
    private int _visibleRevision = -1;
    private SKRect _semanticViewport;
    private float _semanticZoom;
    private int _semanticRevision = -1;
    private int _sceneRevision, _cachedRevision = -1;
    private float _cachedZoom, _cachedPanX, _cachedPanY;
    private int _cachedWidth, _cachedHeight;
    private int _cachedNodeCount, _cachedLabelCount;
    private long _lastViewportInput;
    private readonly DispatcherTimer _detailTimer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly DispatcherTimer _titleTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    internal bool RasterPending => _rasterRequest is not null;
    internal string? RasterError { get; private set; }
    internal int BaseRenderCount { get; private set; }
    internal int PreviewRenderCount { get; private set; }
    internal int GeometryFallbackNodeCount { get; private set; }
    internal IReadOnlyList<TreemapSemanticTitles.Title> SemanticTitles => _semanticTitles.Current;
    private List<FileEntry>? _deletedEntries;
    private HashSet<FileEntry>? _deletionHintEntries;
    private static readonly SKColor DeletedColor = new(229, 72, 77);
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
    private float _pixelScaleX = 1f;
    private float _pixelScaleY = 1f;
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
    private readonly List<(FileEntry Entry, float Fx, float Fy)> _zoomAnchorAncestors = new();
    private float _zoomAnchorFx;
    private float _zoomAnchorFy;
    private Point _zoomAnchorScreen;

    /// <summary>显示深度变化不继承上次滚轮的待重定位锚点，保持当前zoom/pan原值。</summary>
    internal void PreserveViewportForDepthChange()
    {
        _zoomAnchorEntry = null;
        _zoomAnchorAncestors.Clear();
    }

    public List<TreemapNode>? Nodes
    {
        get => _nodes;
        set
        {
            bool sameTree = _nodes?.FirstOrDefault()?.Entry is { } oldRoot && ReferenceEquals(oldRoot, value?.FirstOrDefault()?.Entry);
            _pingNode = null; _pingTimer.Stop();
            if (!sameTree) { SelectedNode = null; ClearMultiSelection(); }
            _nodes = value;
            _scene = value is null ? null : TreemapSceneIndex.Prepare(value);
            _sceneRevision++;
            // 新场景立即断开上一帧节点引用，空视图不再等下一次绘制才释放旧树。
            _visibleNodes = [];
            if (value is null || value.Count == 0) { _baseImage?.Dispose(); _baseImage = null; }
            _rasterRequest?.Cancel();
            _deletionHintEntries = null;
            if (_scanRoot is null) _deletedEntries = null;
            var selectedEntry = SelectedNode?.Entry;
            var selectedAggregate = SelectedNode?.AggregatedCount > 0;
            SelectedNode = value?.FirstOrDefault(n => ReferenceEquals(n.Entry, selectedEntry) && (n.AggregatedCount > 0) == selectedAggregate) ?? SelectedNode;
            if (value is not null && _multiSelection.Count > 0)
            {
                var replacements = value.ToDictionary(n => (n.Entry, n.AggregatedCount > 0));
                var selection = _multiSelection.Select(n => replacements.GetValueOrDefault((n.Entry, n.AggregatedCount > 0)) ?? n).ToArray();
                _multiSelection.Clear();
                foreach (var node in selection) _multiSelection.Add(node);
            }
            ClearHover();
            ApplyZoomAnchor();
            UpdateSemanticTitles();
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
    public event EventHandler? BlankRightClicked;
    public event EventHandler? MultiSelectionChanged;

    /// <summary>缩放级别改变时触发（参数为新 zoom），供 LOD 重布局。</summary>
    public event EventHandler<float>? ZoomChanged;
    public event EventHandler<FileEntry>? RootNavigationRequested;

    /// <summary>切换层级时同步视口；普通导航清除滚轮历史，滚轮导航保留返回位置。</summary>
    internal void SetViewRoot(FileEntry? root, FileEntry? scanRoot)
    {
        bool scanChanged = !ReferenceEquals(_scanRoot, scanRoot);
        _scanRoot = scanRoot;
        if (scanChanged) RefreshDeletionMarks();
        if (ReferenceEquals(root, _viewRoot)) return;
        _viewRoot = root;
        SelectedNode = null;
        ClearMultiSelection();
        _semanticTitles.Clear();
        _zoom = MinZoom;
        _panX = _panY = 0;
        _zoomAnchorEntry = null;
        _zoomAnchorAncestors.Clear();
        if (!_wheelNavigating) { _wheelHistory.Clear(); _snapActive = false; }
        if (_isPanning) EndPan();
        ClearHover();
        InvalidateVisual();
    }

    public TreemapControl()
    {
        Focusable = true;
        PaintSurface += OnPaintSurface;
        _pingTimer.Tick += (_, _) => { _pingTimer.Stop(); _pingNode = null; InvalidateVisual(); };
        _detailTimer.Tick += (_, _) => { _detailTimer.Stop(); InvalidateVisual(); };
        _titleTimer.Tick += (_, _) => { if (!_semanticTitles.Animating) _titleTimer.Stop(); InvalidateVisual(); };

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
        Unloaded += (_, _) =>
        {
            ClearHover(); _detailTimer.Stop(); _titleTimer.Stop(); _pingTimer.Stop(); _pingNode = null;
            _rasterRequest?.Cancel();
            _baseImage?.Dispose(); _baseImage = null;
        };
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        RenderedNodeCount = TitleClipNodeCount = RenderedLabelCount = GeometryFallbackNodeCount = 0;
        SKCanvas canvas = e.Surface.Canvas;
        canvas.Clear(SKColor.Parse("#1B2430"));
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        // 布局和命中使用逻辑坐标，绘制时统一映射到当前 DPI。
        float dpiScaleX = e.Info.Width / (float)ActualWidth;
        float dpiScaleY = e.Info.Height / (float)ActualHeight;
        if (_pixelScaleX != dpiScaleX || _pixelScaleY != dpiScaleY) _deletionHintEntries = null;
        _pixelScaleX = dpiScaleX; _pixelScaleY = dpiScaleY;
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

        using var textPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
        };

        var sortedNodes = GetVisibleNodes();
        UpdateSemanticTitles(sortedNodes);
        bool sameScene = _baseImage is not null && _cachedRevision == _sceneRevision
            && _cachedWidth == e.Info.Width && _cachedHeight == e.Info.Height;
        bool sameViewport = sameScene && _cachedZoom == _zoom && _cachedPanX == _panX && _cachedPanY == _panY;
        bool preview = sameScene && !sameViewport && Environment.TickCount64 - _lastViewportInput < 80;
        if (!sameViewport && !preview)
        {
            if (EnableAsyncRaster)
                RequestRaster(sortedNodes, e.Info.Width, e.Info.Height, dpiScaleX, dpiScaleY);
            else
                InstallRaster(TreemapRasterizer.Render(sortedNodes, e.Info.Width, e.Info.Height, dpiScaleX, dpiScaleY,
                    _zoom, _panX, _panY, CancellationToken.None, false), e.Info.Width, e.Info.Height, _sceneRevision, _zoom, _panX, _panY);
        }
        // 同步安装可能刚替换底图；异步未完成时，旧根/旧场景底图不能充当新场景内容。
        bool usableRaster = _baseImage is not null && _cachedRevision == _sceneRevision
            && _cachedWidth == e.Info.Width && _cachedHeight == e.Info.Height;
        if (usableRaster)
        {
            // 复用已有清晰像素；缓存外新暴露的区域在本帧补几何，不能空等后台图。
            canvas.Save();
            canvas.ResetMatrix(); canvas.Scale(dpiScaleX, dpiScaleY);
            float ratio = _zoom / Math.Max(_cachedZoom, .000001f);
            canvas.Translate(_panX - _cachedPanX * ratio, _panY - _cachedPanY * ratio);
            canvas.Scale(ratio / dpiScaleX, ratio / dpiScaleY);
            canvas.DrawImage(_baseImage, 0, 0);
            canvas.Restore();
            if (preview) { PreviewRenderCount++; _detailTimer.Stop(); _detailTimer.Start(); }
        }
        FillUncoveredGeometry(canvas, usableRaster);
        RenderedNodeCount = usableRaster ? _cachedNodeCount : GeometryFallbackNodeCount;
        RenderedLabelCount = usableRaster ? _cachedLabelCount : 0;
        DrawPointerLight(canvas, sortedNodes);
        // 按用户要求恢复状态框置顶；标题先画，Selected粗框/Deleted细框再覆盖其边界。
        DrawFileTitles(canvas, sortedNodes);
        DrawSemanticTitles(canvas, textPaint);


        // 多选集合先画纯白描边（除当前选中焦点外）。
        foreach (var node in _multiSelection)
        {
            if (node != SelectedNode && _nodes?.Contains(node) == true) DrawOutline(canvas, node, Spec(2f), SKColors.White);
        }

        // 最后叠加选中态纯白粗框 + 悬停细白框，目录选择框不会被子块覆盖。
        if (_lastHoveredNode != SelectedNode) DrawOutline(canvas, _lastHoveredNode, Spec(1f), SKColors.White);
        if (_nodes?.Contains(SelectedNode!) == true) DrawOutline(canvas, SelectedNode, Spec(2.5f), SKColors.White);
        // 删除标记最后绘制，选中和悬停不能覆盖红框。
        var deletionHints = GetDeletionHintEntries();
        if (_deletedEntries is { Count: > 0 }) foreach (var node in sortedNodes)
        {
            if (node.Entry.IsDeleted) DrawOutline(canvas, node, Spec(DeletedOutlineScreenWidth), DeletedColor);
            else if (node.AggregatedCount == 0 && deletionHints.Contains(node.Entry))
                DrawDeletionHint(canvas, node);
        }

        if (_pingNode is not null) DrawOutline(canvas, _pingNode, Spec(2f), new SKColor(129, 193, 229));

    }

    private void FillUncoveredGeometry(SKCanvas canvas, bool usableRaster)
    {
        var viewport = _visibleViewport;
        if (!usableRaster)
        {
            Fill(viewport);
            return;
        }
        // 底图覆盖范围以逻辑坐标保存，平移或缩小后的缺口拆成互不重叠的最多四条带。
        var coverage = new SKRect(-_cachedPanX / _cachedZoom, -_cachedPanY / _cachedZoom,
            ((float)ActualWidth - _cachedPanX) / _cachedZoom, ((float)ActualHeight - _cachedPanY) / _cachedZoom);
        var covered = new SKRect(Math.Max(viewport.Left, coverage.Left), Math.Max(viewport.Top, coverage.Top),
            Math.Min(viewport.Right, coverage.Right), Math.Min(viewport.Bottom, coverage.Bottom));
        if (covered.Width <= 0 || covered.Height <= 0)
        {
            Fill(viewport);
            return;
        }
        Fill(new(viewport.Left, viewport.Top, covered.Left, viewport.Bottom));
        Fill(new(covered.Right, viewport.Top, viewport.Right, viewport.Bottom));
        Fill(new(covered.Left, viewport.Top, covered.Right, covered.Top));
        Fill(new(covered.Left, covered.Bottom, covered.Right, viewport.Bottom));

        void Fill(SKRect strip)
        {
            if (strip.Width <= 0 || strip.Height <= 0) return;
            var nodes = _scene?.Visible(strip) ?? [];
            GeometryFallbackNodeCount += nodes.Length;
            canvas.Save();
            canvas.ClipRect(strip, SKClipOperation.Intersect, false);
            TreemapRasterizer.DrawGeometry(canvas, nodes, _zoom);
            canvas.Restore();
        }
    }

    private void InstallRaster(TreemapRasterizer.Frame frame, int width, int height, int revision, float zoom, float panX, float panY)
    {
        _baseImage?.Dispose(); _baseImage = frame.Image;
        _cachedRevision = revision; _cachedZoom = zoom; _cachedPanX = panX; _cachedPanY = panY;
        _cachedWidth = width; _cachedHeight = height; _cachedNodeCount = frame.Nodes; _cachedLabelCount = frame.Labels;
        BaseRenderCount++;
    }

    private void RequestRaster(TreemapNode[] nodes, int width, int height, float dpiX, float dpiY)
    {
        if (_rasterRequest is not null && _rasterRevision == _sceneRevision && _rasterZoom == _zoom
            && _rasterPanX == _panX && _rasterPanY == _panY && _rasterWidth == width && _rasterHeight == height) return;
        _rasterRequest?.Cancel();
        var source = _rasterRequest = new CancellationTokenSource();
        int revision = _rasterRevision = _sceneRevision;
        float logicalWidth = (float)ActualWidth, logicalHeight = (float)ActualHeight;
        RasterError = null;
        float zoom = _rasterZoom = _zoom, panX = _rasterPanX = _panX, panY = _rasterPanY = _panY;
        _rasterWidth = width; _rasterHeight = height;
        _ = Run();
        async Task Run()
        {
            TreemapRasterizer.Frame? frame = null;
            try
            {
                await _rasterWorker.WaitAsync(source.Token);
                try { frame = await Task.Run(() => TreemapRasterizer.Render(nodes,width,height,dpiX,dpiY,zoom,panX,panY,source.Token,false),source.Token); }
                finally { _rasterWorker.Release(); }
                await Dispatcher.InvokeAsync(() =>
                {
                    if (source.IsCancellationRequested || revision != _sceneRevision || zoom != _zoom || panX != _panX || panY != _panY
                        || logicalWidth != (float)ActualWidth || logicalHeight != (float)ActualHeight) return;
                    InstallRaster(frame!,width,height,revision,zoom,panX,panY);frame=null;InvalidateVisual();
                });
            }
            catch(OperationCanceledException) { }
            catch(Exception error)
            {
                await Dispatcher.InvokeAsync(() => RasterError = error.Message);
                System.Diagnostics.Trace.TraceError("树图底图更新失败：{0}",error);
            }
            finally
            {
                frame?.Image.Dispose();
                await Dispatcher.InvokeAsync(() => { if(ReferenceEquals(_rasterRequest,source))_rasterRequest=null;source.Dispose(); });
            }
        }
    }

    // 在排序、颜色解析、渐变和文字测量之前剔除视口外节点。
    // 保留完整布局，平移时可直接恢复细节，不把不可见等同于不存在。
    private TreemapNode[] GetVisibleNodes()
    {
        var viewport = new SKRect(-_panX / _zoom, -_panY / _zoom,
            ((float)ActualWidth - _panX) / _zoom, ((float)ActualHeight - _panY) / _zoom);
        if (_visibleRevision == _sceneRevision && _visibleViewport == viewport) return _visibleNodes;
        _visibleViewport = viewport; _visibleRevision = _sceneRevision;
        return _visibleNodes = _scene?.Visible(viewport) ?? [];
    }

    private void UpdateSemanticTitles(IEnumerable<TreemapNode>? visible = null)
    {
        var key = new SKRect(_panX, _panY, (float)ActualWidth, (float)ActualHeight);
        if (_semanticRevision == _sceneRevision && _semanticZoom == _zoom && _semanticViewport == key && !_semanticTitles.Animating) return;
        _semanticRevision = _sceneRevision; _semanticZoom = _zoom; _semanticViewport = key;
        // 输入阶段只检查目录索引，不为每次滚轮枚举和排序全部文件。
        _semanticTitles.Update(visible ?? _scene?.Directories.Values ?? Enumerable.Empty<TreemapNode>(), _zoom, _panX, _panY,
            (float)ActualWidth, (float)ActualHeight, Environment.TickCount64, SystemParameters.ClientAreaAnimation);
        if (_semanticTitles.Animating && !_titleTimer.IsEnabled) _titleTimer.Start();
    }

    private void DrawSemanticTitles(SKCanvas canvas, SKPaint textPaint)
    {
        if (_semanticTitles.Current.Count == 0) return;
        canvas.Save(); canvas.ResetMatrix(); canvas.Scale(_pixelScaleX, _pixelScaleY);
        using var font = CreateTreemapFont(TreemapLabelPolicy.DirectoryTitleFontSize);
        using var paint = new SKPaint { IsAntialias = false };
        foreach (var title in _semanticTitles.Current)
        {
            byte alpha = (byte)Math.Clamp(title.Opacity * 255, 0, 255);
            paint.Color = new SKColor(48, 48, 48, alpha); canvas.DrawRect(title.Rect, paint);
            var label = FormatRetainedTitleLabel(title.Node.Entry.Name, title.Node.SizeLabel ?? FileSizeConverter.FormatSize(title.Node.Entry.Size), font, title.Rect.Width - 8);
            paint.IsAntialias = true; paint.Color = new SKColor(245, 247, 250, alpha);
            // 文字与底图采用相同基线，并严格限制在自身标题条，避免下伸字形跨行。
            canvas.Save(); canvas.ClipRect(title.Rect, SKClipOperation.Intersect, false);
            canvas.DrawText(label, title.Rect.Left + 4, title.Rect.MidY + 3.5f, SKTextAlign.Left, font, paint);
            canvas.Restore();
            paint.IsAntialias = false; RenderedLabelCount++;
        }
        // 横向重定位会覆盖底图的标题边界；所有背景完成后统一补回同色系单物理像素线。
        // 邻接标题共边对齐到同一像素中心，不叠成双线，也不随缩放/DPI加粗。
        using var border = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 0, IsAntialias = false };
        float SnapX(float x) => (MathF.Floor(x * _pixelScaleX) + .5f) / _pixelScaleX;
        float SnapY(float y) => (MathF.Floor(y * _pixelScaleY) + .5f) / _pixelScaleY;
        foreach (var title in _semanticTitles.Current)
        {
            var color = SKColor.Parse(title.Node.ColorHex);
            border.Color = new SKColor((byte)(color.Red + (255 - color.Red) * .20f),
                (byte)(color.Green + (255 - color.Green) * .20f), (byte)(color.Blue + (255 - color.Blue) * .20f),
                (byte)Math.Clamp(title.Opacity * 255, 0, 255));
            canvas.DrawRect(new SKRect(SnapX(title.Rect.Left), SnapY(title.Rect.Top),
                SnapX(title.Rect.Right), SnapY(title.Rect.Bottom)), border);
        }
        canvas.Restore();
    }

    private void MarkViewportInput()
    {
        _lastViewportInput = Environment.TickCount64;
        _detailTimer.Stop(); _detailTimer.Start();
        _deletionHintEntries = null;
        UpdateSemanticTitles();
    }

    /// <summary>屏幕像素规格 → 逻辑坐标（除以 zoom 后绘制，屏幕上保持恒定）。</summary>
    private float Spec(float screenPx) => screenPx / _zoom;

    private static SKRect NodeRect(TreemapNode node) =>
        new(node.X, node.Y, node.X + node.Width, node.Y + node.Height);

    private void DrawPointerLight(SKCanvas canvas, IEnumerable<TreemapNode> visibleNodes)
    {
        if (_lastHoveredNode is null || _semanticTitles.Hit((float)_hoverPosition.X, (float)_hoverPosition.Y) is not null) return;
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
        return node.HeaderHeight ?? TreemapLabelPolicy.DirectoryHeaderHeight(node.Width, node.Height, node.Depth, _zoom);
    }

    /// <summary>删除或清除标记后刷新索引，不重置选中、缩放和平移。</summary>
    internal void RefreshDeletionMarks()
    {
        _deletedEntries = null;
        _deletionHintEntries = null;
        if (_toolTip.IsOpen && _lastHoveredNode is not null) UpdateToolTipContent(_lastHoveredNode);
        InvalidateVisual();
    }

    private HashSet<FileEntry> GetDeletionHintEntries()
    {
        if (_deletionHintEntries is not null) return _deletionHintEntries;
        // 文件树只在扫描或删除状态变化时遍历，悬停重绘和缩放复用删除项索引。
        if (_deletedEntries is null)
        {
            _deletedEntries = new List<FileEntry>();
            var roots = new HashSet<FileEntry>();
            if (_scanRoot is not null) roots.Add(_scanRoot);
            else if (_nodes is not null)
                foreach (var node in _nodes)
                {
                    var root = node.Entry;
                    while (root.Parent is not null) root = root.Parent;
                    roots.Add(root);
                }
            var pending = new Stack<FileEntry>(roots);
            while (pending.TryPop(out var entry))
            {
                if (entry.IsDeleted)
                {
                    _deletedEntries.Add(entry);
                    // 整个目录已删除时，自身红框或承接角标已能代表其全部后代。
                    continue;
                }
                foreach (var child in entry.Children) pending.Push(child);
            }
        }

        // 使用完整布局而非视口：子项移出屏幕，不应被误判为折叠而点亮父块。
        // 保留面积归属的亚像素节点并不一定能画出；删除提示仍须交给最近可辨认的真实祖先。
        var drawnEntries = _nodes?.Where(n => n.IsVisible && n.AggregatedCount == 0
            && n.Width * _zoom * _pixelScaleX >= 1f && n.Height * _zoom * _pixelScaleY >= 1f)
            .Select(n => n.Entry).ToHashSet() ?? new HashSet<FileEntry>();
        var hints = new HashSet<FileEntry>();
        foreach (var deleted in _deletedEntries)
        {
            if (drawnEntries.Contains(deleted)) continue;
            var parent = deleted.Parent;
            while (parent is not null && !drawnEntries.Contains(parent)) parent = parent.Parent;
            if (parent is not null && !parent.IsDeleted) hints.Add(parent);
        }
        return _deletionHintEntries = hints;
    }

    private void DrawDeletionHint(SKCanvas canvas, TreemapNode node)
    {
        var rect = NodeRect(node);
        // 承接角标属于内容状态，避开当前标题区域，不被标题盖住也不压住标题文字。
        var title = _semanticTitles.Current.LastOrDefault(t => ReferenceEquals(t.Node.Entry, node.Entry));
        if (title is not null) rect.Top = Math.Max(rect.Top, (title.Rect.Bottom - _panY) / _zoom);
        if (rect.Height <= 0) return;
        float side = Math.Min(Spec(8f), Math.Min(rect.Width, rect.Height));
        using var corner = new SKPath();
        corner.MoveTo(rect.Right - side, rect.Top);
        corner.LineTo(rect.Right, rect.Top);
        corner.LineTo(rect.Right, rect.Top + side);
        corner.Close();
        using var paint = new SKPaint { Color = DeletedColor, IsAntialias = false };
        canvas.DrawPath(corner, paint);
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
        var title = _semanticTitles.Hit((float)x, (float)y);
        if (title is not null) return title;
        return _scene?.Hit((float)((x - _panX) / _zoom), (float)((y - _panY) / _zoom));
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
        bool semanticHeader = _semanticTitles.Contains(node, (float)position.X, (float)position.Y);
        if (clickCount == 2 && node.Entry.IsDirectory && node.AggregatedCount == 0
            && (semanticHeader || node.Label is not null && headerHeight >= Spec(14f) && logicalY < node.Y + headerHeight))
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
        float previousPanX = _panX, previousPanY = _panY;
        _panX = _panStartPanX + (float)dx;
        _panY = _panStartPanY + (float)dy;
        ClampPan();
        if (_zoomAnchorEntry is not null)
            _zoomAnchorScreen = new Point(_zoomAnchorScreen.X + _panX - previousPanX,
                _zoomAnchorScreen.Y + _panY - previousPanY);
        MarkViewportInput();
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
            var node = _scene?.Directories.GetValueOrDefault(entry);
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
        // 已完整呈现的末级目录仍停在全景；有密集文件或聚合长尾时必须允许放大揭示细节。
        bool hasDetail = _scene?.HasZoomDetail ?? false;
        if (factor > 1f && target is null && _viewRoot is not null && _zoom <= MinZoom && !hasDetail) return;
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
        _zoomAnchorAncestors.Clear();
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
            // 聚合解开或文件重新并入长尾后，用原真实祖先内的相对位置续接锚点。
            var ancestor = _zoomAnchorAggregate ? anchorNode.Entry : anchorNode.Entry.Parent;
            while (ancestor is not null)
            {
                var parentNode = _scene?.Directories.GetValueOrDefault(ancestor);
                if (parentNode is not null && parentNode.Width > 0 && parentNode.Height > 0)
                    _zoomAnchorAncestors.Add((ancestor, (anchorLogicalX - parentNode.X) / parentNode.Width,
                        (anchorLogicalY - parentNode.Y) / parentNode.Height));
                ancestor = ancestor.Parent;
            }
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
        MarkViewportInput();
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
        if (node is null)
        {
            foreach (var ancestor in _zoomAnchorAncestors)
            {
                node = _nodes?.FirstOrDefault(n => n.IsVisible && n.AggregatedCount == 0
                    && ReferenceEquals(n.Entry, ancestor.Entry));
                if (node is null) continue;
                _zoomAnchorFx = ancestor.Fx; _zoomAnchorFy = ancestor.Fy;
                break;
            }
        }
        _zoomAnchorAncestors.Clear();
        if (node is null || node.Width <= 0 || node.Height <= 0) return;
        float anchorX = node.X + _zoomAnchorFx * node.Width;
        float anchorY = node.Y + _zoomAnchorFy * node.Height;
        float previousX = _panX, previousY = _panY;
        _panX = (float)_zoomAnchorScreen.X - anchorX * _zoom;
        _panY = (float)_zoomAnchorScreen.Y - anchorY * _zoom;
        ClampPan();
        if (_isPanning)
        {
            _panStartPanX += _panX - previousX;
            _panStartPanY += _panY - previousY;
        }
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
            case Key.Apps:
            case Key.F10 when Keyboard.Modifiers.HasFlag(ModifierKeys.Shift):
                if (SelectedNode is { } selected && _nodes?.Contains(selected) == true)
                    NodeRightClicked?.Invoke(this, selected);
                else BlankRightClicked?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
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
        string size = FileSizeConverter.FormatSize(node.AggregatedSize ?? entry.Size);
        string modified = entry.LastModified.ToString("g");

        var panel = new StackPanel { Margin = new Thickness(2) };

        panel.Children.Add(new TextBlock
        {
            Text = entry.Path,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 400,
        });

        if (node.AggregatedCount > 0)
            panel.Children.Add(new TextBlock
            {
                Text = node.Label ?? $"+{node.AggregatedCount} 项",
                Margin = new Thickness(0, 2, 0, 0),
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

        if (!entry.IsDeleted && node.AggregatedCount == 0 && GetDeletionHintEntries().Contains(entry))
        {
            var deletionText = new TextBlock
            {
                Text = L.Text("TreemapContainsDeletedItems"),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 2, 0, 0),
            };
            deletionText.SetResourceReference(TextBlock.ForegroundProperty, "VP.DangerTextBrush");
            panel.Children.Add(deletionText);
        }

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
        HandleContextMenu(e.GetPosition(this));
        e.Handled = true;
    }

    /// <summary>真实右键与后台验收共用，空白不改变选择。</summary>
    internal void HandleContextMenu(Point position)
    {
        var node = HitTest(position.X, position.Y);
        _toolTipTimer.Stop();
        _toolTip.IsOpen = false;
        if (node is not null)
        {
            SelectedNode = node;
            NodeClicked?.Invoke(this, node);
            NodeRightClicked?.Invoke(this, node);
        }
        else BlankRightClicked?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>容量紧接名称且完整保留；不足一个完整名称字符和省略号时整行隐藏。</summary>
    internal static string? FormatLabel(string name, string size, SKFont font, float maxWidth)
    {
        if (string.IsNullOrEmpty(name) || maxWidth <= 0)
            return null;

        string suffix = $" ({size})";
        string full = name + suffix;
        if (font.MeasureText(full) <= maxWidth)
            return full;

        // 按文本元素截断，避免切开中文、代理对、组合音标或 emoji 序列。
        int[] starts = System.Globalization.StringInfo.ParseCombiningCharacters(name);
        int lo = 0, hi = starts.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            string candidate = string.Concat(name.AsSpan(0, starts[mid]), "...", suffix);
            if (font.MeasureText(candidate) <= maxWidth)
                lo = mid;
            else
                hi = mid - 1;
        }

        return lo == 0 ? null : string.Concat(name.AsSpan(0, starts[lo]), "...", suffix);
    }

    /// <summary>
    /// 将视图定位（focus）到指定节点并高亮。供 AI focus_path 与外部定位调用。
    /// 布局已在 ViewModel 层完成导航/钻取，这里负责：清空视口缩放并高亮目标。
    /// </summary>
    internal static string FormatRetainedTitleLabel(string name, string size, SKFont font, float maxWidth) =>
        FormatLabel(name, size, font, maxWidth) ?? (string.IsNullOrEmpty(name) ? "…" : name);

    internal static SKRect? GetFileTitleRect(TreemapNode node, float zoom, float panX, float panY, float width, float height)
    {
        if (node.Label is null || node.Entry.IsDirectory && node.AggregatedCount == 0
            || !TreemapLabelPolicy.ShowFileLabel(node.Width, node.Height, zoom)) return null;
        float left = Math.Max(0, node.X * zoom + panX), top = Math.Max(0, node.Y * zoom + panY);
        float right = Math.Min(width, (node.X + node.Width) * zoom + panX);
        float bottom = Math.Min(height, (node.Y + node.Height) * zoom + panY);
        return right > left && bottom > top ? new SKRect(left, top, right, Math.Min(bottom, top + 16)) : null;
    }

    private void DrawFileTitles(SKCanvas canvas, IEnumerable<TreemapNode> nodes)
    {
        canvas.Save(); canvas.ResetMatrix(); canvas.Scale(_pixelScaleX, _pixelScaleY);
        using var font = CreateTreemapFont(TreemapLabelPolicy.FileLabelFontSize);
        using var paint = new SKPaint { IsAntialias = true };
        foreach (var node in nodes)
        {
            if (GetFileTitleRect(node, _zoom, _panX, _panY, (float)ActualWidth, (float)ActualHeight) is not { } rect) continue;
            var color = SKColor.Parse(node.ColorHex);
            paint.Color = .299f * color.Red + .587f * color.Green + .114f * color.Blue >= 150
                ? new SKColor(26, 30, 40) : new SKColor(245, 247, 250);
            var label = FormatRetainedTitleLabel(node.Label!, node.SizeLabel ?? FileSizeConverter.FormatSize(node.AggregatedSize ?? node.Entry.Size), font, rect.Width - 6);
            canvas.Save(); canvas.ClipRect(rect, SKClipOperation.Intersect, false);
            canvas.DrawText(label, rect.Left + 3, rect.Top + 11, SKTextAlign.Left, font, paint);
            canvas.Restore(); RenderedLabelCount++;
        }
        canvas.Restore();
    }

    /// <summary>只提示已有 cell；不钻取目录、不改选择，不用旧树节点替代当前节点。</summary>
    public bool PingEntry(FileEntry entry)
    {
        var node = _nodes?.FirstOrDefault(n => n.IsVisible && n.AggregatedCount == 0 && ReferenceEquals(n.Entry, entry));
        if (node is null) return false;
        // 只平移到目标可见位置，保持当前缩放、目录根和选择集合。
        float left = node.X * _zoom + _panX, top = node.Y * _zoom + _panY;
        float right = left + node.Width * _zoom, bottom = top + node.Height * _zoom;
        if (right <= 0 || left >= ActualWidth) _panX += (float)ActualWidth / 2 - (left + right) / 2;
        if (bottom <= 0 || top >= ActualHeight) _panY += (float)ActualHeight / 2 - (top + bottom) / 2;
        ClampPan(); MarkViewportInput();
        _pingNode = node; _pingTimer.Stop(); _pingTimer.Start(); InvalidateVisual();
        return true;
    }

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
