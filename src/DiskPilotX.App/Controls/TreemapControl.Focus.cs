using System.Diagnostics;
using System.Windows.Threading;
using DiskPilotX.Core.Models;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace DiskPilotX.App.Controls;

public partial class TreemapControl
{
    private static readonly SKColor PingColor = new(254, 164, 0);
    private const float MaxFocusZoom = 4096f;
    private readonly DispatcherTimer _pingTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _pingClock = new();
    private float _pingOpacity;
    private float _pingStrokeWidth = 3f;
    private float _pingWaveOffset;
    private float _pingWaveOpacity;
    private int _focusVersion;
    private CancellationTokenSource? _focusAnimation;
    private FileEntry? _pingEntry;
    private TreemapNode? _pingNode;
    private bool _focusLevelChanging;
    private int _focusLevelVersion;
    private bool _focusPreparing;
    private SKImage? _focusTransitionImage;
    private (float Zoom, float X, float Y) _focusTransitionStart;
    private float _focusTransitionOpacity;
    internal bool IsFocusAnimating => _focusAnimation is not null || _focusLevelChanging || _focusPreparing;
    internal FileEntry? ActivePingEntry => _pingEntry;
    internal float PingOpacity => _pingOpacity;
    internal float PingWaveOffset => _pingWaveOffset;
    internal float PingWaveOpacity => _pingWaveOpacity;
    internal event EventHandler? FocusInterrupted;

    /// <summary>维持目标当前屏幕位置，只补足进入视口所需平移，不强制居中。</summary>
    internal static (float Zoom, float X, float Y) CalculateFocusViewport(SKRect rect,
        float zoom, float panX, float panY, float width, float height)
    {
        if (rect.Width <= 0 || rect.Height <= 0 || width <= 0 || height <= 0)
            return (zoom, panX, panY);
        float margin = Math.Min(12f, Math.Min(width, height) / 10f);
        float fit = Math.Min((width - 2 * margin) / rect.Width, (height - 2 * margin) / rect.Height);
        float targetZoom = zoom;
        if (rect.Width * zoom > width - 2 * margin || rect.Height * zoom > height - 2 * margin)
            targetZoom = fit;
        else if (Math.Min(rect.Width, rect.Height) * zoom < 32f)
            targetZoom = Math.Min(32f / Math.Min(rect.Width, rect.Height), fit);
        // Ping 按真实目标大小揭示细节，不受普通滚轮的 8x 层级切换上限限制。
        targetZoom = Math.Clamp(targetZoom, MinZoom, Math.Max(zoom, MaxFocusZoom));
        float x = panX + rect.MidX * (zoom - targetZoom);
        float y = panY + rect.MidY * (zoom - targetZoom);
        x = Reveal(rect.Left * targetZoom + x, rect.Right * targetZoom + x, x, width, margin);
        y = Reveal(rect.Top * targetZoom + y, rect.Bottom * targetZoom + y, y, height, margin);
        // 只在地图本来的平移范围内定位；贴边提示允许自然裁剪，不额外越界留白。
        return (targetZoom, Math.Clamp(x, width * (1 - targetZoom), 0),
            Math.Clamp(y, height * (1 - targetZoom), 0));

        static float Reveal(float left, float right, float pan, float extent, float border)
        {
            if (right - left >= extent - 2 * border)
                return pan + Math.Max(0, border - left) - Math.Max(0, right - (extent - border));
            if (left < border) return pan + border - left;
            if (right > extent - border) return pan + extent - border - right;
            return pan;
        }
    }

    private void DrawPingOutline(SKCanvas canvas, TreemapNode node)
    {
        if (_pingOpacity <= 0) return;
        // 极端比例的真实 cell 可能短于一个像素；只扩大提示框，不伪造文件面积或命中范围。
        var rect = NodeRect(node);
        float minimum = Spec(3f);
        if (rect.Width < minimum) rect.Inflate((minimum - rect.Width) / 2, 0);
        if (rect.Height < minimum) rect.Inflate(0, (minimum - rect.Height) / 2);
        rect.Inflate(Spec(1.5f), Spec(1.5f));
        // 冲击只作用于叠加描边；真实 cell、命中范围和视口保持停稳。
        var wave = rect;
        wave.Inflate(Spec(_pingWaveOffset), Spec(_pingWaveOffset));
        using var blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Spec(1.5f));
        using var glow = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = Spec(2.5f),
            Color = PingColor.WithAlpha((byte)(255 * _pingWaveOpacity)), MaskFilter = blur, IsAntialias = true };
        canvas.DrawRect(wave, glow);
        using var wavePaint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = Spec(1f),
            Color = PingColor.WithAlpha((byte)(180 * _pingWaveOpacity)), IsAntialias = true };
        canvas.DrawRect(wave, wavePaint);
        using var paint = new SKPaint { Style=SKPaintStyle.Stroke, StrokeWidth=Spec(_pingStrokeWidth), Color=PingColor.WithAlpha((byte)(255 * _pingOpacity)), IsAntialias=false };
        canvas.DrawRect(rect, paint);
    }

    internal static float CalculatePingOpacity(double elapsedMilliseconds)
    {
        float remaining = Math.Clamp(1 - (float)(elapsedMilliseconds - 80) / 720, 0, 1);
        return MathF.Pow(remaining, 1.5f);
    }

    internal static (float StrokeWidth, float WaveOffset, float WaveOpacity) CalculatePingImpact(double elapsedMilliseconds)
    {
        if (elapsedMilliseconds < 80)
        {
            float approach = Math.Clamp((float)elapsedMilliseconds / 80, 0, 1);
            float impact = 1 - MathF.Pow(1 - approach, 3);
            return (3 + 3 * impact, 6 * (1 - impact), 0.35f + 0.45f * impact);
        }
        float progress = Math.Clamp((float)(elapsedMilliseconds - 80) / 720, 0, 1);
        float spread = 1 - MathF.Pow(1 - progress, 3);
        return (6 - 3 * spread, 5 * spread, 0.8f * (1 - progress) * (1 - progress));
    }

    internal void StartPingHighlight(FileEntry entry)
    {
        if (!ReferenceEquals(_pingEntry, entry) || _pingNode is null || IsFocusAnimating) return;
        _pingClock.Restart(); _pingOpacity = 1;
        (_pingStrokeWidth, _pingWaveOffset, _pingWaveOpacity) = CalculatePingImpact(0);
        _pingTimer.Start(); InvalidateVisual();
    }

    private void UpdatePingHighlight()
    {
        _pingOpacity = CalculatePingOpacity(_pingClock.Elapsed.TotalMilliseconds);
        (_pingStrokeWidth, _pingWaveOffset, _pingWaveOpacity) = CalculatePingImpact(_pingClock.Elapsed.TotalMilliseconds);
        if (_pingOpacity <= 0 || _pingEntry?.IsDeleted == true)
            ClearPingHighlight();
        InvalidateVisual();
    }

    private void ClearPingHighlight()
    {
        _pingTimer.Stop(); _pingClock.Reset(); _pingOpacity = 0;
        _pingWaveOpacity = 0; _pingWaveOffset = 0; _pingStrokeWidth = 3;
        _pingEntry = null; _pingNode = null;
    }

    internal void CancelFocusAnimation()
    {
        _focusVersion++;
        ClearPingHighlight();
        _focusAnimation?.Cancel();
        _focusAnimation = null;
        _focusTransitionImage?.Dispose(); _focusTransitionImage = null;
        _focusTransitionOpacity = 0;
        _focusPreparing = false; _focusLevelChanging = false;
    }

    private void InterruptFocus()
    {
        CancelFocusAnimation();
        _pingEntry = null; _pingNode = null;
        FocusInterrupted?.Invoke(this, EventArgs.Empty);
    }

    private void RemapPingNode()
    {
        _pingNode = _nodes?.FirstOrDefault(n =>
            ReferenceEquals(n.Entry, _pingEntry) && n.AggregatedCount == 0 && !n.Entry.IsDeleted);
        if (_pingEntry is not null && _pingNode is null) ClearPingHighlight();
    }

    public Task<bool> FocusNodeAsync(TreemapNode node, bool highlight = true, CancellationToken cancellationToken = default,
        bool deferHighlight = false) => FocusNodeCoreAsync(node, highlight, cancellationToken, deferHighlight, null);

    internal Task<bool> FocusStableNodeAsync(TreemapNode node, bool highlight, CancellationToken cancellationToken,
        Func<Func<TreemapNode, float>, CancellationToken, Task<TreemapNode?>> prepareLayout) =>
        FocusNodeCoreAsync(node, highlight, cancellationToken, false, prepareLayout);

    private async Task<bool> FocusNodeCoreAsync(TreemapNode node, bool highlight, CancellationToken cancellationToken,
        bool deferHighlight, Func<Func<TreemapNode, float>, CancellationToken, Task<TreemapNode?>>? prepareLayout)
    {
        if (_nodes?.Contains(node) != true || node.Entry.IsDeleted || node.AggregatedCount > 0) return false;
        CancelFocusAnimation();
        int version = _focusVersion;
        _snapActive = false; _zoomAnchorEntry = null; _zoomAnchorAncestors.Clear();
        bool blendScene = prepareLayout is not null;
        if (prepareLayout is not null)
        {
            var start = Viewport;
            float width = (float)ActualWidth, height = (float)ActualHeight;
            var screen = new SKPoint((node.X + node.Width / 2) * start.Zoom + start.X,
                (node.Y + node.Height / 2) * start.Zoom + start.Y);
            _focusTransitionImage = CaptureFocusFrame();
            _focusTransitionStart = start; _focusTransitionOpacity = 1; _focusPreparing = true;
            InvalidateVisual();
            bool preparedReady = false;
            try
            {
                var prepared = await prepareLayout(n => CalculateFocusViewport(new(n.X, n.Y, n.X + n.Width, n.Y + n.Height),
                    start.Zoom, start.X, start.Y, width, height).Zoom, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (version != _focusVersion || prepared is null) return false;
                node = prepared;
                // 新几何从原目标的屏幕位置接管，旧画面随镜头交叠，不硬切也不先走临时落点。
                _panX = screen.X - (node.X + node.Width / 2) * _zoom;
                _panY = screen.Y - (node.Y + node.Height / 2) * _zoom;
                ClampPan();
                _focusTransitionStart = Viewport;
                preparedReady = true;
            }
            finally
            {
                if (version == _focusVersion)
                {
                    _focusPreparing = false;
                    if (!preparedReady || cancellationToken.IsCancellationRequested || _nodes?.Contains(node) != true)
                    { _focusTransitionImage?.Dispose(); _focusTransitionImage = null; _focusTransitionOpacity = 0; }
                }
            }
        }
        if (highlight)
        {
            _pingEntry = node.Entry; _pingNode = node;
            SelectedNode = node; ClearMultiSelection();
            NodeClicked?.Invoke(this, node);
        }
        var target = CalculateFocusViewport(new(node.X, node.Y, node.X + node.Width, node.Y + node.Height),
            _zoom, _panX, _panY, (float)ActualWidth, (float)ActualHeight);
        InvalidateVisual();
        await AnimateFocusViewportAsync(target, cancellationToken, anchor: new SKPoint(node.X + node.Width / 2, node.Y + node.Height / 2),
            blendScene: blendScene, notifyZoom: !blendScene);
        cancellationToken.ThrowIfCancellationRequested();
        if (version != _focusVersion) return false;
        if (highlight && !deferHighlight) StartPingHighlight(node.Entry);
        return true;
    }

    /// <summary>新层级后台准备期间保留旧画面；提交后用同一空间锚点缩放并交叠过渡。</summary>
    internal async Task<bool> ChangeFocusRootAsync(FileEntry nextRoot, Func<Task<bool>> navigate, CancellationToken cancellationToken)
    {
        CancelFocusAnimation();
        var oldRoot = _viewRoot;
        var oldViewport = Viewport;
        var entering = _nodes?.FirstOrDefault(n => ReferenceEquals(n.Entry, nextRoot) && n.AggregatedCount == 0);
        SKRect? enteringRect = entering is null ? null : new(entering.X * _zoom + _panX, entering.Y * _zoom + _panY,
            (entering.X + entering.Width) * _zoom + _panX, (entering.Y + entering.Height) * _zoom + _panY);
        var image = CaptureFocusFrame();
        _focusLevelChanging = true;
        int version = ++_focusLevelVersion;
        _focusPreparing = true;
        _focusTransitionImage = image; image = null;
        _focusTransitionStart = oldViewport; _focusTransitionOpacity = 1;
        InvalidateVisual();
        try
        {
            if (!await navigate()) { cancellationToken.ThrowIfCancellationRequested(); return false; }
            cancellationToken.ThrowIfCancellationRequested();
            var exiting = _nodes?.FirstOrDefault(n => ReferenceEquals(n.Entry, oldRoot) && n.AggregatedCount == 0);
            float width = (float)ActualWidth, height = (float)ActualHeight;
            var anchor = new SKPoint(width / 2, height / 2);
            if (exiting is { Width: > 0, Height: > 0 })
            {
                // 拉回共同祖先时，旧根在新地图中仍占据原屏幕范围。
                _zoom = Math.Max(width / exiting.Width, height / exiting.Height) * oldViewport.Zoom;
                _panX = oldViewport.X - exiting.X * _zoom;
                _panY = oldViewport.Y - exiting.Y * _zoom;
                anchor = new SKPoint(exiting.X + exiting.Width / 2, exiting.Y + exiting.Height / 2);
            }
            else if (enteringRect is { Width: > 0, Height: > 0 } rect)
            {
                // 向子目录靠近时，新地图从原目录 cell 的屏幕范围连续展开。
                _zoom = Math.Max(0.02f, Math.Min(rect.Width / width, rect.Height / height));
                _panX = rect.MidX - width * _zoom / 2;
                _panY = rect.MidY - height * _zoom / 2;
            }
            else
            {
                // 两份扫描没有共享几何，使用轻微拉远及交叠，而不是瞬间换图。
                _zoom = 0.85f; _panX = width * 0.075f; _panY = height * 0.075f;
            }
            _focusPreparing = false;
            _focusTransitionStart = Viewport; _focusTransitionOpacity = 1;
            await AnimateFocusViewportAsync((1, 0, 0), cancellationToken, preserveTransition: true, anchor: anchor);
            cancellationToken.ThrowIfCancellationRequested();
            return true;
        }
        finally
        {
            image?.Dispose();
            if (version == _focusLevelVersion)
            {
                _focusLevelChanging = false; _focusPreparing = false;
                _focusTransitionImage?.Dispose(); _focusTransitionImage = null;
                _focusTransitionOpacity = 0;
            }
        }
    }

    private SKImage? CaptureFocusFrame()
    {
        if (_nodes is not { Count: > 0 } || ActualWidth <= 0 || ActualHeight <= 0) return null;
        int width = Math.Max(1, (int)Math.Ceiling(ActualWidth * _pixelScaleX));
        int height = Math.Max(1, (int)Math.Ceiling(ActualHeight * _pixelScaleY));
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        if (surface is null) return null;
        OnPaintSurface(this, new SKPaintSurfaceEventArgs(surface, new SKImageInfo(width, height)));
        return surface.Snapshot();
    }

    private void DrawFocusTransition(SKCanvas canvas)
    {
        if (_focusTransitionImage is null || _focusTransitionOpacity <= 0) return;
        if (_focusPreparing)
        {
            // 即使新根和新布局已提交，继续显示旧截图，直到同一视口过渡准备完毕。
            canvas.DrawImage(_focusTransitionImage, new SKRect(0, 0, (float)ActualWidth, (float)ActualHeight));
            return;
        }
        float ratio = _zoom / _focusTransitionStart.Zoom;
        var rect = new SKRect(_panX - _focusTransitionStart.X * ratio, _panY - _focusTransitionStart.Y * ratio,
            _panX - _focusTransitionStart.X * ratio + (float)ActualWidth * ratio,
            _panY - _focusTransitionStart.Y * ratio + (float)ActualHeight * ratio);
        using var paint = new SKPaint { Color = SKColors.White.WithAlpha((byte)(255 * _focusTransitionOpacity)), IsAntialias = true };
        canvas.DrawImage(_focusTransitionImage, rect, paint);
    }

    private async Task AnimateFocusViewportAsync((float Zoom, float X, float Y) target,
        CancellationToken cancellationToken, bool preserveTransition = false, SKPoint? anchor = null,
        bool blendScene = false, bool notifyZoom = true)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _focusAnimation = source;
        var start = Viewport;
        if (!preserveTransition && !blendScene && Math.Abs(start.Zoom - target.Zoom) < 0.0001f
            && Math.Abs(start.X - target.X) < 0.01f && Math.Abs(start.Y - target.Y) < 0.01f)
        {
            _focusAnimation = null; source.Dispose(); InvalidateVisual(); return;
        }
        var focus = anchor ?? new SKPoint((float)ActualWidth / 2, (float)ActualHeight / 2);
        var startScreen = new SKPoint(focus.X * start.Zoom + start.X, focus.Y * start.Zoom + start.Y);
        var endScreen = new SKPoint(focus.X * target.Zoom + target.X, focus.Y * target.Zoom + target.Y);
        var clock = Stopwatch.StartNew();
        try
        {
            await Task.Delay(16, source.Token);
            while (true)
            {
                source.Token.ThrowIfCancellationRequested();
                float t = Math.Min(1, (float)clock.Elapsed.TotalMilliseconds / 420f);
                float eased = 1 - MathF.Pow(1 - t, 3);
                _zoom = MathF.Exp(MathF.Log(Math.Max(0.001f, start.Zoom)) * (1 - eased) + MathF.Log(target.Zoom) * eased);
                // 锚点的屏幕位置先连续插值，再反算 pan；几何缩放不能与线性 pan 各走各的。
                _panX = startScreen.X + (endScreen.X - startScreen.X) * eased - focus.X * _zoom;
                _panY = startScreen.Y + (endScreen.Y - startScreen.Y) * eased - focus.Y * _zoom;
                // 普通定位逐帧遵循地图边界；层级交叠仍按原空间关系连续过渡。
                if (!preserveTransition) ClampPan();
                if (preserveTransition || blendScene) _focusTransitionOpacity = 1 - eased;
                MarkViewportInput(); InvalidateVisual();
                if (t >= 1) break;
                await Task.Delay(16, source.Token);
            }
            _zoom = target.Zoom; _panX = target.X; _panY = target.Y;
            _focusTransitionImage?.Dispose(); _focusTransitionImage = null;
            if (notifyZoom && Math.Abs(start.Zoom - _zoom) > 0.0001f) ZoomChanged?.Invoke(this, _zoom);
            InvalidateVisual();
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_focusAnimation, source))
            {
                _focusAnimation = null;
                _focusTransitionImage?.Dispose(); _focusTransitionImage = null;
                _focusTransitionOpacity = 0;
            }
            source.Dispose();
        }
    }
}
