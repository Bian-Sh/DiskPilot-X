using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media;
using SkiaSharp;
using SpaceMonger.App.Localization;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;
using SpaceMonger.App.Converters;
using SpaceMonger.Core.Models;

namespace SpaceMonger.App.Controls;

public class TreemapControl : SKElement
{
    private const string TreemapGlyphProbe = "中文";

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
    private Point _hoverPosition;
    private readonly DispatcherTimer _toolTipTimer;


    public List<TreemapNode>? Nodes
    {
        get => _nodes;
        set
        {
            _nodes = value;
            SelectedNode = null;
            ClearHover();
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

    public event EventHandler<TreemapNode>? NodeClicked;
    public event EventHandler<TreemapNode>? NodeActivated;
    public event EventHandler<TreemapNode?>? NodeHovered;
    public event EventHandler<TreemapNode>? NodeRightClicked;

    public TreemapControl()
    {
        PaintSurface += OnPaintSurface;

        _toolTip = new ToolTip
        {
            Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse,
            PlacementTarget = this,
            IsHitTestVisible = false,
            Background = Brushes.LightYellow,
            Foreground = Brushes.Black,
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(5),
        };
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
        canvas.Clear(SKColor.Parse("#202020"));
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        // 布局和命中使用逻辑坐标，绘制时统一映射到当前 DPI。
        canvas.Scale(e.Info.Width / (float)ActualWidth, e.Info.Height / (float)ActualHeight);

        // Clip to rounded corners — only bottom-left and bottom-right (matching container Border CornerRadius="0,0,10,10")
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

        using var labelFont = CreateTreemapFont(11f);

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

        // 最后叠加白框，目录选择框不会被子块覆盖。
        if (_lastHoveredNode != SelectedNode) DrawOutline(canvas, _lastHoveredNode, 1f);
        DrawOutline(canvas, SelectedNode, 2f);
    }

    private static void DrawDirectoryNode(
        SKCanvas canvas, TreemapNode node,
        SKPaint fillPaint, SKPaint borderPaint, SKPaint textPaint, SKFont font)
    {
        var rect = NodeRect(node);
        fillPaint.Color = SKColor.Parse("#303030");
        canvas.DrawRect(rect, fillPaint);
        borderPaint.Color = SKColor.Parse("#454545");
        borderPaint.StrokeWidth = 1f;
        var frame = rect;
        frame.Inflate(-0.5f, -0.5f);
        canvas.DrawRect(frame, borderPaint);

        float headerHeight = GetHeaderHeight(node.Depth, node.Height);
        if (node.Label is not null && headerHeight >= 14f)
            DrawLabel(canvas, node.Label, node.X + 3f, node.Y + headerHeight / 2f + 3.5f,
                node.Width - 6f, textPaint, font);

        if (node.Entry.IsAccessDenied)
            DrawAccessDeniedOverlay(canvas, rect);
    }

    private static void DrawFileNode(
        SKCanvas canvas, TreemapNode node,
        SKPaint fillPaint, SKPaint borderPaint, SKPaint textPaint, SKFont font)
    {
        var rect = NodeRect(node);
        var color = SKColor.Parse(node.ColorHex);
        fillPaint.Color = color;
        canvas.DrawRect(rect, fillPaint);

        borderPaint.Color = DarkenColor(color, 0.65f);
        borderPaint.StrokeWidth = 0.5f;
        var frame = rect;
        frame.Inflate(-0.25f, -0.25f);
        canvas.DrawRect(frame, borderPaint);

        if (node.Label is not null && rect.Height >= 14f)
            DrawLabel(canvas, node.Label, rect.Left + 3f, rect.Top + 11f,
                rect.Width - 6f, textPaint, font);

        if (node.Entry.IsAccessDenied)
            DrawAccessDeniedOverlay(canvas, rect);
    }

    private static SKRect NodeRect(TreemapNode node) =>
        new(node.X, node.Y, node.X + node.Width, node.Y + node.Height);

    private void DrawPointerLight(SKCanvas canvas)
    {
        if (_lastHoveredNode is null
            || _hoverPosition.Y < _lastHoveredNode.Y + GetDirectoryTitleHeight(_lastHoveredNode)) return;
        var center = new SKPoint((float)_hoverPosition.X, (float)_hoverPosition.Y);
        using var light = SKShader.CreateRadialGradient(center, 150f,
            [new SKColor(255, 255, 255, 42), new SKColor(255, 255, 255, 0)],
            [0f, 1f], SKShaderTileMode.Clamp);
        using var paint = new SKPaint { Shader = light, IsAntialias = true };
        using var titleAreas = new SKPath();
        foreach (var node in _nodes!)
        {
            float titleHeight = GetDirectoryTitleHeight(node);
            if (node.IsVisible && titleHeight > 0)
                titleAreas.AddRect(new SKRect(node.X, node.Y, node.X + node.Width, node.Y + titleHeight));
        }
        canvas.Save();
        canvas.ClipPath(titleAreas, SKClipOperation.Difference, false);
        canvas.DrawCircle(center, 150f, paint);
        canvas.Restore();
    }

    private static float GetDirectoryTitleHeight(TreemapNode node)
    {
        if (!node.Entry.IsDirectory || node.Label is null) return 0f;
        float height = GetHeaderHeight(node.Depth, node.Height);
        return height >= 14f && node.Height >= height ? height : 0f;
    }

    private static void DrawOutline(SKCanvas canvas, TreemapNode? node, float width)
    {
        if (node is null || !node.IsVisible) return;
        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = width,
            Color = SKColors.White,
            IsAntialias = false,
        };
        var rect = NodeRect(node);
        float outset = width > 1f ? width / 2f : -width / 2f;
        rect.Inflate(outset, outset);
        canvas.DrawRect(rect, paint);
    }

    private static void DrawLabel(SKCanvas canvas, string text, float x, float y,
        float maxWidth, SKPaint paint, SKFont font)
    {
        var label = TruncateLabel(text, font, maxWidth);
        if (label is null) return;
        paint.Color = new SKColor(0, 0, 0, 180);
        canvas.DrawText(label, x + 0.5f, y + 0.5f, SKTextAlign.Left, font, paint);
        paint.Color = SKColors.White;
        canvas.DrawText(label, x, y, SKTextAlign.Left, font, paint);
    }

    private static float GetHeaderHeight(int depth, float availableHeight)
    {
        float h = 18f - depth * 1.5f;
        h = Math.Max(h, 14f);
        h = Math.Min(h, availableHeight * 0.4f);
        return h;
    }

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

        TreemapNode? deepest = null;

        for (int i = _nodes.Count - 1; i >= 0; i--)
        {
            var node = _nodes[i];
            if (!node.IsVisible)
                continue;

            if (x >= node.X && x < node.X + node.Width &&
                y >= node.Y && y < node.Y + node.Height)
            {
                if (deepest is null || node.Depth > deepest.Depth)
                {
                    deepest = node;
                }
            }
        }

        return deepest;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        HandleClick(e.GetPosition(this), e.ClickCount);
        e.Handled = true;
    }

    internal void HandleClick(Point position, int clickCount)
    {
        _toolTipTimer.Stop();
        _toolTip.IsOpen = false;
        var node = HitTest(position.X, position.Y);
        if (node is null) return;

        SelectedNode = node;
        NodeClicked?.Invoke(this, node);
        float headerHeight = GetHeaderHeight(node.Depth, node.Height);
        if (clickCount == 2 && node.Entry.IsDirectory && node.Label is not null
            && headerHeight >= 14f && position.Y < node.Y + headerHeight)
        {
            // 在按下时导航，第二次抬起不会选中新布局的其他块。
            ClearHover();
            NodeActivated?.Invoke(this, node);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        HandleHover(e.GetPosition(this));
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
            panel.Children.Add(new TextBlock
            {
                Text = L.Text("AccessDenied"),
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 80, 80)),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 2, 0, 0),
            });
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

    private static void DrawAccessDeniedOverlay(SKCanvas canvas, SKRect rect)
    {
        using var hatchPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(255, 80, 80, 160),
            StrokeWidth = 1.5f,
            IsAntialias = true,
        };

        canvas.Save();
        canvas.ClipRect(rect);

        float step = 8f;
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
}
