using System.Collections;
using System.Windows;
using System.Windows.Media;
using DiskPilotX.App.ViewModels;

namespace DiskPilotX.App.Controls;

public sealed class TreeGuideControl : FrameworkElement
{
    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.Register(
        nameof(Segments),
        typeof(IEnumerable),
        typeof(TreeGuideControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HasParentProperty = DependencyProperty.Register(
        nameof(HasParent),
        typeof(bool),
        typeof(TreeGuideControl),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsLastChildProperty = DependencyProperty.Register(
        nameof(IsLastChild),
        typeof(bool),
        typeof(TreeGuideControl),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HasChildrenProperty = DependencyProperty.Register(
        nameof(HasChildren),
        typeof(bool),
        typeof(TreeGuideControl),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsDebugColoredProperty = DependencyProperty.Register(
        nameof(IsDebugColored),
        typeof(bool),
        typeof(TreeGuideControl),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double IndentSize = 16.0;
    private const double StemX = 9.0;
    private const double PreferredRowHeight = 24.0;
    private const double JunctionY = 12.0;
    private const double ToggleSlotWidth = 18.0;
    private const double IconLeadWidth = 4.0;

    public IEnumerable? Segments
    {
        get => (IEnumerable?)GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public bool HasParent
    {
        get => (bool)GetValue(HasParentProperty);
        set => SetValue(HasParentProperty, value);
    }

    public bool IsLastChild
    {
        get => (bool)GetValue(IsLastChildProperty);
        set => SetValue(IsLastChildProperty, value);
    }

    public bool HasChildren
    {
        get => (bool)GetValue(HasChildrenProperty);
        set => SetValue(HasChildrenProperty, value);
    }

    public bool IsDebugColored
    {
        get => (bool)GetValue(IsDebugColoredProperty);
        set => SetValue(IsDebugColoredProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(GetIconStartX(), PreferredRowHeight);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var rowHeight = ActualHeight > 0 ? ActualHeight : PreferredRowHeight;
        var segments = GetSegmentValues();
        var verticalPen = CreatePen(IsDebugColored ? Color.FromRgb(0x00, 0xE6, 0x76) : Colors.White, IsDebugColored ? 2.6 : 1.0);
        var branchPen = CreatePen(IsDebugColored ? Color.FromRgb(0xFF, 0xD5, 0x00) : Colors.White, IsDebugColored ? 2.6 : 1.0);

        for (var index = 0; index < segments.Count; index++)
        {
            if (!segments[index])
            {
                continue;
            }

            var x = Align(index * IndentSize + StemX);
            drawingContext.DrawLine(verticalPen, new Point(x, 0), new Point(x, rowHeight));
        }

        if (!HasParent)
        {
            return;
        }

        var currentX = Align(segments.Count * IndentSize + StemX);
        var junctionY = Align(Math.Min(JunctionY, rowHeight));
        var verticalEndY = IsLastChild ? junctionY : rowHeight;
        drawingContext.DrawLine(branchPen, new Point(currentX, 0), new Point(currentX, verticalEndY));

        var horizontalEndX = HasChildren
            ? (segments.Count + 1) * IndentSize
            : GetIconStartX();
        drawingContext.DrawLine(branchPen, new Point(currentX, junctionY), new Point(horizontalEndX, junctionY));
    }

    private double GetIconStartX()
    {
        var visualDepth = HasParent ? GetSegmentValues().Count + 1 : 0;
        return visualDepth * IndentSize + ToggleSlotWidth + IconLeadWidth;
    }

    private static Pen CreatePen(Color color, double thickness)
    {
        var brush = new SolidColorBrush(Color.FromArgb(230, color.R, color.G, color.B));
        brush.Freeze();
        var pen = new Pen(brush, thickness)
        {
            StartLineCap = PenLineCap.Flat,
            EndLineCap = PenLineCap.Flat,
            LineJoin = PenLineJoin.Miter
        };
        pen.Freeze();
        return pen;
    }

    private List<bool> GetSegmentValues()
    {
        var values = new List<bool>();
        if (Segments is null)
        {
            return values;
        }

        foreach (var segment in Segments)
        {
            values.Add(segment switch
            {
                TreeGuideSegment guideSegment => guideSegment.Continues,
                bool continues => continues,
                _ => false
            });
        }

        return values;
    }

    private static double Align(double value)
    {
        return Math.Floor(value) + 0.5;
    }
}
