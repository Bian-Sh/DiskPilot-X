using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Treemap;
using SkiaSharp;

namespace DiskPilotX.App.Controls;

/// <summary>纯Skia底图生成，不访问WPF对象；后台使用冻结节点和视口参数。</summary>
internal static class TreemapRasterizer
{
    internal sealed record Frame(SKImage Image, int Nodes, int Labels);
    internal static Frame Render(TreemapNode[] nodes, int width, int height, float dpiX, float dpiY,
        float zoom, float panX, float panY, CancellationToken token, bool drawFileLabels = true)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas; canvas.Clear(SKColor.Parse("#1B2430"));
        canvas.Scale(dpiX, dpiY); canvas.Translate(panX, panY); canvas.Scale(zoom);
        int labels = Draw(canvas, nodes, zoom, token, true, drawFileLabels);
        return new(surface.Snapshot(), nodes.Length, labels);
    }

    /// <summary>在当前画布直接补几何，不申请位图或排版文字；精细底图继续由后台生成。</summary>
    internal static void DrawGeometry(SKCanvas canvas, TreemapNode[] nodes, float zoom) =>
        Draw(canvas, nodes, zoom, CancellationToken.None, false);

    private static int Draw(SKCanvas canvas, TreemapNode[] nodes, float zoom, CancellationToken token, bool drawLabels, bool drawFileLabels = true)
    {
        using var fill = new SKPaint { IsAntialias = false };
        using var border = new SKPaint { Style = SKPaintStyle.Stroke, IsAntialias = false };
        using var text = new SKPaint { IsAntialias = true };
        using var fileFont = drawLabels ? TreemapControl.CreateTreemapFont(TreemapLabelPolicy.FileLabelFontSize / zoom) : null;
        using var directoryFont = drawLabels ? TreemapControl.CreateTreemapFont(TreemapLabelPolicy.DirectoryTitleFontSize / zoom) : null;
        int labels = 0;
        float Spec(float x) => x / zoom;
        void Label(TreemapNode n, float x, float y, float maxWidth, SKColor color)
        {
            var font = n.Entry.IsDirectory && n.AggregatedCount == 0 ? directoryFont : fileFont;
            if (font is null) return;
            string? label = TreemapControl.FormatLabel(n.Label!, n.SizeLabel ?? Converters.FileSizeConverter.FormatSize(n.AggregatedSize ?? n.Entry.Size), font, maxWidth);
            if (label is null) return;
            text.Color = .299f * color.Red + .587f * color.Green + .114f * color.Blue >= 150
                ? new SKColor(26, 30, 40) : new SKColor(245, 247, 250);
            canvas.DrawText(label, x, y, SKTextAlign.Left, font, text); labels++;
        }
        for (int i = 0; i < nodes.Length; i++)
        {
            if ((i & 255) == 0) token.ThrowIfCancellationRequested();
            var n = nodes[i];var rect = new SKRect(n.X, n.Y, n.X + n.Width, n.Y + n.Height);
            if (n.Entry.IsDirectory && n.AggregatedCount == 0)
            {
                if (!n.ShowDirectoryFrame && !n.IsContentLeaf && !n.Entry.IsAccessDenied) continue;
                var color = SKColor.Parse(n.ColorHex);
                fill.Color = SKColor.Parse(n.ContentColorHex ?? n.ColorHex); canvas.DrawRect(rect, fill);
                border.Color = Lighten(color,.20f);border.StrokeWidth = 0;
                var m = canvas.TotalMatrix;
                float X(float x) => (MathF.Floor(x * m.ScaleX + m.TransX) + .5f - m.TransX) / m.ScaleX;
                float Y(float y) => (MathF.Floor(y * m.ScaleY + m.TransY) + .5f - m.TransY) / m.ScaleY;
                var frame = new SKRect(X(rect.Left), Y(rect.Top), X(rect.Right), Y(rect.Bottom));
                float header = n.Label is null ? 0 : n.HeaderHeight ?? TreemapLabelPolicy.DirectoryHeaderHeight(n.Width,n.Height,n.Depth,zoom);
                if (header >= Spec(14))
                {
                    fill.Color = color;canvas.DrawRect(new SKRect(rect.Left,rect.Top,rect.Right,rect.Top+header),fill);
                    Label(n,n.X+Spec(4),n.Y+header/2+Spec(3.5f),n.Width-Spec(8),color);
                    canvas.DrawLine(frame.Left,Y(rect.Top+header),frame.Right,Y(rect.Top+header),border);
                }
                // 展开容器只描标题条；内容轮廓交给子块，折叠的真实内容块仍保留完整外框。
                if (n.ShowDirectoryFrame)
                {
                    if (n.IsContentLeaf) canvas.DrawRect(frame,border);
                    else if (header >= Spec(14))
                        canvas.DrawRect(new SKRect(frame.Left,frame.Top,frame.Right,Y(rect.Top+header)),border);
                }
            }
            else
            {
                var color=SKColor.Parse(n.ColorHex);bool detailed=Math.Min(rect.Width,rect.Height)>=Spec(5);
                if(detailed)rect.Inflate(-Spec(.5f),-Spec(.5f));fill.Color=color;
                using var shader=drawLabels&&rect.Width>=Spec(32)&&rect.Height>=Spec(24)
                    ? SKShader.CreateLinearGradient(new(rect.MidX,rect.Top),new(rect.MidX,rect.Bottom),[Lighten(color,.035f),Darken(color,.035f)],[0f,1f],SKShaderTileMode.Clamp):null;
                fill.Shader=shader;canvas.DrawRect(rect,fill);fill.Shader=null;
                border.Color=Darken(color,.14f);border.StrokeWidth=Spec(.5f);
                var frame=rect;frame.Inflate(-Spec(.25f),-Spec(.25f));if(detailed)canvas.DrawRect(frame,border);
                if (n.Label is not null && drawLabels && drawFileLabels)
                {
                    // 文字属于文件自身的可见交集，不能因左上角移出视口就整行消失。
                    var clip = canvas.LocalClipBounds;
                    var labelRect = new SKRect(Math.Max(rect.Left, clip.Left), Math.Max(rect.Top, clip.Top),
                        Math.Min(rect.Right, clip.Right), Math.Min(rect.Bottom, clip.Bottom));
                    if (TreemapLabelPolicy.ShowFileLabel(labelRect.Width, labelRect.Height, zoom))
                        Label(n, labelRect.Left + Spec(3), labelRect.Top + Spec(11), labelRect.Width - Spec(6), color);
                }
            }
            if(n.Entry.IsAccessDenied)
            {
                canvas.Save();canvas.ClipRect(rect);
                using var hatch=new SKPaint {Style=SKPaintStyle.Stroke,Color=new SKColor(255,80,80,160),StrokeWidth=Spec(1.5f),IsAntialias=true};
                for(float offset=0;offset<rect.Width+rect.Height;offset+=Spec(8))
                    canvas.DrawLine(rect.Left+offset,rect.Bottom,rect.Left+offset-rect.Height,rect.Top,hatch);
                canvas.Restore();
            }
        }
        return labels;
    }
    private static SKColor Lighten(SKColor c,float f)=>new((byte)(c.Red+(255-c.Red)*f),(byte)(c.Green+(255-c.Green)*f),(byte)(c.Blue+(255-c.Blue)*f),c.Alpha);
    private static SKColor Darken(SKColor c,float f)=>new((byte)(c.Red*(1-f)),(byte)(c.Green*(1-f)),(byte)(c.Blue*(1-f)),c.Alpha);
}
