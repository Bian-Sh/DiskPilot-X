using DiskPilotX.Core.Models;

namespace DiskPilotX.Core.Services.Treemap;

public class SquarifiedTreemapLayout : ITreemapLayoutEngine
{
    private const float HeaderBaseHeight = 18f;
    private const float MinNodeDimension = 14f;

    // 逻辑坐标 = 屏幕像素 / zoom：cell 规格（标题栏、边框、最小尺寸）按屏幕像素恒定，
    // 缩放只改变内容区大小与 LOD 细节，title 区域在屏幕上始终不变。
    private static float Spec(float screenPx, float zoom) => screenPx / Math.Max(zoom, 1e-6f);

    // A directory only expands its children if the content area (after header
    // and border) meets this minimum.  Below this threshold the directory is
    // rendered as a solid colored block — matching DiskPilot-X's adaptive
    // depth behavior where small directories become leaf nodes.
    private const float MinContentArea = 800f;  // ~28×28 px equivalent

    // Maximum children to render per directory.
    private const int MaxChildrenPerDir = 20;

    // Keep whole-drive scans responsive even when several large directories
    // each have many visible descendants.
    private const int MaxVisibleNodes = 5000;

    // LOD 生长：zoom 越大，展开子节点所需的面积阈值越小（按 zoom² 缩放），
    // 于是更多目录跨过阈值、向下展开更多层级 —— 等价于地图加载更细瓦片。
    // zoom=1 时用基础阈值；zoom=2 时阈值降为 1/4，更多目录展开。
    private static float LodContentArea(float zoom) =>
        MinContentArea / Math.Max(zoom * zoom, 1f);

    // LOD 深度预算：zoom=1 展开 5 层，之后每放大 1 级多展开 2 层，zoom=3 起展开到底。
    // 每档滚轮都会重新锚定到指针下更深的节点，预算必须领先钻取速度 1 层以上，否则指针下的目录只显示纯色块。
    private static int LodMaxDepth(float zoom) =>
        (int)Math.Clamp(MathF.Round(3f + zoom * 2f), 2, 8);

    // 目录（含标题栏区域）使用中性深灰，不做彩色编码；仅文件 cell 按类型上色。
    private const string DirectoryColor = "#303030";

    // 文件按类型使用高饱和亮色。
    private static readonly string[] FilePalette =
    [
        "#EF5350", "#FF9800", "#CDDC39", "#2196F3",
        "#4CAF50", "#E91E63", "#00BCD4", "#9C27B0",
    ];

    private static string GetFileColor(FileEntry entry)
    {
        string extension = (entry.Extension ?? System.IO.Path.GetExtension(entry.Name)).ToLowerInvariant();
        return extension switch
        {
            ".nvph" or ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" => FilePalette[0],
            ".dll" or ".node" or ".lib" => FilePalette[1],
            ".exe" or ".pak" or ".pack" => FilePalette[2],
            ".js" or ".ts" or ".cs" or ".py" or ".css" or ".html" => FilePalette[3],
            ".asar" or ".vpk" or ".msi" => FilePalette[4],
            ".sys" or ".pdf" => FilePalette[5],
            ".zip" or ".7z" or ".rar" or ".mp4" or ".mp3" or ".wav" => FilePalette[6],
            "" or ".json" or ".log" or ".txt" or ".vhdx" or ".bin" => FilePalette[7],
            _ => FilePalette[GetExtensionColorIndex(extension)],
        };
    }

    private static int GetExtensionColorIndex(string extension)
    {
        // 使用固定散列，未知扩展名在不同进程、目录和扫描中保持同色。
        uint hash = 2166136261;
        foreach (char character in extension)
            hash = unchecked((hash ^ character) * 16777619);
        return (int)(hash % (uint)FilePalette.Length);
    }

    private struct Rect
    {
        public float X, Y, Width, Height;

        public Rect(float x, float y, float width, float height)
        {
            X = x; Y = y; Width = width; Height = height;
        }

        public float Area => Width * Height;
        public float ShorterSide => Math.Min(Width, Height);
    }

    public List<TreemapNode> ComputeLayout(FileEntry root, float width, float height, int maxDepth, float zoom = 1f)
    {
        var result = new List<TreemapNode>();

        if (root.Size <= 0 || width <= 4 || height <= 4)
            return result;

        // zoom 驱动 LOD：有效深度预算随 zoom 增大（但不突破调用方给定的上限）。
        int effectiveMaxDepth = Math.Min(maxDepth, LodMaxDepth(zoom));

        // 固定预留外描边空间（屏幕像素恒定），选中时无需缩小或重排任何 cell。
        float rootPad = Spec(2f, zoom);
        var rootRect = new Rect(rootPad, rootPad, width - rootPad * 2, height - rootPad * 2);
        EmitDirectoryNode(root, rootRect, 0, effectiveMaxDepth, zoom, result);

        return result;
    }

    private void EmitDirectoryNode(
        FileEntry dir, Rect rect, int depth, int maxDepth, float zoom,
        List<TreemapNode> result)
    {
        float headerHeight = GetHeaderHeight(rect.Height, zoom);

        string? label = rect.Width >= Spec(20f, zoom) && headerHeight >= Spec(14f, zoom)
            ? $"{dir.Name} ({FormatSize(dir.Size)})"
            : null;

        string color = DirectoryColor;

        var node = new TreemapNode
        {
            Entry = dir,
            X = rect.X,
            Y = rect.Y,
            Width = rect.Width,
            Height = rect.Height,
            ColorHex = color,
            Depth = depth,
            IsVisible = rect.Width >= Spec(MinNodeDimension, zoom) && rect.Height >= Spec(MinNodeDimension, zoom),
            Label = label
        };
        result.Add(node);

        if (depth >= maxDepth || result.Count >= MaxVisibleNodes)
            return;

        float borderPad = GetBorderPadding(rect.Width, rect.Height, zoom);
        float contentX = rect.X + borderPad;
        float contentY = rect.Y + headerHeight;
        float contentW = rect.Width - borderPad * 2;
        float contentH = rect.Height - headerHeight - borderPad;

        // LOD 面积阈值随 zoom 缩放：放大后更多目录跨过阈值、展开子节点。
        float lodArea = LodContentArea(zoom);
        if (contentW < Spec(MinNodeDimension, zoom) || contentH < Spec(MinNodeDimension, zoom)
            || contentW * contentH < lodArea)
            return;

        var allChildren = dir.Children
            .Where(c => c.Size > 0)
            .OrderByDescending(c => c.Size)
            .ToList();

        if (allChildren.Count == 0)
            return;

        var children = allChildren.Count > MaxChildrenPerDir
            ? allChildren.Take(MaxChildrenPerDir).ToList()
            : allChildren;

        var contentRect = new Rect(contentX, contentY, contentW, contentH);
        SquarifyChildren(children, contentRect, depth + 1, maxDepth, zoom, result);
    }

    private void SquarifyChildren(
        List<FileEntry> items, Rect rect, int depth, int maxDepth, float zoom,
        List<TreemapNode> result)
    {
        if (depth > maxDepth || items.Count == 0 || rect.Area <= 0 || result.Count >= MaxVisibleNodes)
            return;

        if (rect.Width < Spec(MinNodeDimension, zoom) || rect.Height < Spec(MinNodeDimension, zoom))
            return;

        if (items.Count == 1)
        {
            EmitNode(items[0], rect, depth, maxDepth, zoom, result);
            return;
        }

        long totalSize = items.Sum(i => i.Size);
        var normalizedAreas = items
            .Select(i => (float)((double)i.Size / totalSize * rect.Area))
            .ToList();

        LayoutItems(items, normalizedAreas, rect, depth, maxDepth, zoom, result);
    }

    private void LayoutItems(
        List<FileEntry> items, List<float> areas, Rect rect,
        int depth, int maxDepth, float zoom,
        List<TreemapNode> result)
    {
        var remaining = new Rect(rect.X, rect.Y, rect.Width, rect.Height);
        int start = 0;

        while (start < items.Count && result.Count < MaxVisibleNodes)
        {
            float shorterSide = remaining.ShorterSide;
            if (shorterSide <= 0)
                break;

            var rowAreas = new List<float> { areas[start] };
            int end = start + 1;

            while (end < items.Count)
            {
                float worstWithout = WorstAspectRatio(rowAreas, shorterSide);
                rowAreas.Add(areas[end]);
                float worstWith = WorstAspectRatio(rowAreas, shorterSide);

                if (worstWith > worstWithout)
                {
                    rowAreas.RemoveAt(rowAreas.Count - 1);
                    break;
                }
                end++;
            }

            int rowCount = end - start;
            remaining = LayoutRow(items, areas, start, rowCount, remaining,
                depth, maxDepth, zoom, result);
            start = end;
        }
    }

    private Rect LayoutRow(
        List<FileEntry> items, List<float> areas,
        int startIndex, int count, Rect rect,
        int depth, int maxDepth, float zoom,
        List<TreemapNode> result)
    {
        float rowTotal = 0;
        for (int i = startIndex; i < startIndex + count; i++)
            rowTotal += areas[i];

        bool layoutHorizontally = rect.Width >= rect.Height;

        float stripThickness;
        if (layoutHorizontally)
            stripThickness = rect.Area > 0 ? rowTotal / rect.Height : 0;
        else
            stripThickness = rect.Area > 0 ? rowTotal / rect.Width : 0;

        float offset = 0;

        for (int i = startIndex; i < startIndex + count; i++)
        {
            float itemLength = stripThickness > 0 ? areas[i] / stripThickness : 0;

            float x, y, w, h;
            if (layoutHorizontally)
            {
                x = rect.X;
                y = rect.Y + offset;
                w = stripThickness;
                h = itemLength;
            }
            else
            {
                x = rect.X + offset;
                y = rect.Y;
                w = itemLength;
                h = stripThickness;
            }

            if (result.Count >= MaxVisibleNodes)
                break;

            EmitNode(items[i], new Rect(x, y, w, h), depth, maxDepth, zoom, result);
            offset += itemLength;
        }

        if (layoutHorizontally)
            return new Rect(rect.X + stripThickness, rect.Y, rect.Width - stripThickness, rect.Height);
        else
            return new Rect(rect.X, rect.Y + stripThickness, rect.Width, rect.Height - stripThickness);
    }

    private void EmitNode(
        FileEntry entry, Rect rect, int depth, int maxDepth, float zoom,
        List<TreemapNode> result)
    {
        if (result.Count >= MaxVisibleNodes || rect.Width < Spec(MinNodeDimension, zoom) || rect.Height < Spec(MinNodeDimension, zoom))
            return;

        if (entry.IsDirectory)
        {
            EmitDirectoryNode(entry, rect, depth, maxDepth, zoom, result);
        }
        else
        {
            string fileColor = GetFileColor(entry);

            // Always provide a label if the rectangle has any visible area.
            // The renderer handles truncation and fitting.
            string? label = rect.Width >= Spec(20f, zoom) && rect.Height >= Spec(10f, zoom)
                ? $"{entry.Name} ({FormatSize(entry.Size)})"
                : null;

            result.Add(new TreemapNode
            {
                Entry = entry,
                X = rect.X,
                Y = rect.Y,
                Width = rect.Width,
                Height = rect.Height,
                ColorHex = fileColor,
                Depth = depth,
                IsVisible = true,
                Label = label
            });
        }
    }

    // cell 规格按屏幕像素恒定：标题栏始终 18 屏幕像素（逻辑值 = 18/zoom），不随深度变化。
    private static float GetHeaderHeight(float availableHeight, float zoom)
        => Math.Min(Spec(HeaderBaseHeight, zoom), availableHeight * 0.4f);

    private static float GetBorderPadding(float width, float height, float zoom)
    {
        float p = Spec(1f, zoom);
        // Don't let padding eat too much of small nodes.
        p = Math.Min(p, width * 0.08f);
        p = Math.Min(p, height * 0.08f);
        return p;
    }

    private static float WorstAspectRatio(List<float> rowAreas, float sideLength)
    {
        if (rowAreas.Count == 0 || sideLength <= 0)
            return float.MaxValue;

        float rowTotal = 0;
        float minArea = float.MaxValue;
        float maxArea = float.MinValue;

        for (int i = 0; i < rowAreas.Count; i++)
        {
            float a = rowAreas[i];
            rowTotal += a;
            if (a < minArea) minArea = a;
            if (a > maxArea) maxArea = a;
        }

        if (rowTotal <= 0)
            return float.MaxValue;

        float w2 = sideLength * sideLength;
        float r2 = rowTotal * rowTotal;

        float worstForMax = Math.Max(w2 * maxArea / r2, r2 / (w2 * maxArea));
        float worstForMin = Math.Max(w2 * minArea / r2, r2 / (w2 * minArea));

        return Math.Max(worstForMax, worstForMin);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024L) return $"{bytes} B";
        if (bytes < 1024L * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        if (bytes < 1024L * 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        return $"{bytes / (1024.0 * 1024 * 1024 * 1024):F1} TB";
    }
}
