using DiskPilotX.Core.Models;

namespace DiskPilotX.Core.Services.Treemap;

public class SquarifiedTreemapLayout : ITreemapLayoutEngine
{
    private const float HeaderBaseHeight = 18f;
    private const float MinNodeDimension = 1.25f;

    // 逻辑坐标 = 屏幕像素 / zoom：cell 规格（标题栏、边框、最小尺寸）按屏幕像素恒定，
    // 缩放只改变内容区大小与 LOD 细节，title 区域在屏幕上始终不变。
    private static float Spec(float screenPx, float zoom) => screenPx / Math.Max(zoom, 1e-6f);

    // A directory only expands its children if the content area (after header
    // and border) meets this minimum.  Below this threshold the directory is
    // rendered as a solid colored block — matching DiskPilot-X's adaptive
    // depth behavior where small directories become leaf nodes.
    private const float MinContentArea = 800f;  // ~28×28 px equivalent

    // Maximum children to render per directory.
    private const int MaxChildrenPerDir = 128;

    // Keep whole-drive scans responsive even when several large directories
    // each have many visible descendants.
    private const int MaxVisibleNodes = 5000;

    // LOD 生长：zoom 越大，展开子节点所需的面积阈值越小（按 zoom² 缩放），
    // 于是更多目录跨过阈值、向下展开更多层级 —— 等价于地图加载更细瓦片。
    // zoom=1 时用基础阈值；zoom=2 时阈值降为 1/4，更多目录展开。
    private static float LodContentArea(float zoom) =>
        MinContentArea / Math.Max(zoom * zoom, 1f);


    // 目录（含标题栏区域）使用中性深灰，不做彩色编码；仅文件 cell 按类型上色。
    private const string DirectoryColor = "#303030";

    // 参考 Disktree：统一低饱和与接近的明度，按内容类别区分柔和色相。
    private static readonly string[] FilePalette =
    [
        "#B8C3D0", // 源码：蓝灰
        "#B5CCBF", // 工具与运行库：灰绿
        "#C7BCD6", // 媒体：淡紫
        "#D2C9AA", // 缓存：沙色
        "#B6CDCB", // 归档：灰青
        "#C4C7CC", // 文档：中性灰蓝
        "#BDC3C2", // 其它：低彩度灰
        "#D0BFAF", // 构建产物：暖灰
    ];

    private static string GetFileColor(FileEntry entry)
    {
        string extension = (entry.Extension ?? System.IO.Path.GetExtension(entry.Name)).ToLowerInvariant();
        return extension switch
        {
            ".js" or ".ts" or ".cs" or ".py" or ".css" or ".html" or ".rs" or ".cpp" or ".h" or ".xaml" or ".json" => FilePalette[0],
            ".dll" or ".node" or ".lib" or ".exe" or ".sys" or ".pdb" or ".ilk" => FilePalette[1],
            ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".mp4" or ".mp3" or ".wav" or ".mkv" or ".svg" => FilePalette[2],
            ".nvph" or ".cache" or ".tmp" or ".log" or ".bak" => FilePalette[3],
            ".zip" or ".7z" or ".rar" or ".tar" or ".gz" => FilePalette[4],
            ".pdf" or ".txt" or ".md" or ".docx" or ".xlsx" or ".csv" => FilePalette[5],
            "" or ".vhdx" or ".bin" or ".dat" => FilePalette[6],
            ".asar" or ".vpk" or ".msi" or ".pak" or ".pack" or ".obj" => FilePalette[7],
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

    private sealed record LayoutItem(FileEntry Entry, long Size, int AggregatedCount = 0);

    // 折叠目录用最大占用后代的类型色表示内容；层级很深或无读取权限时用稳定的路径色。
    private static string GetDirectoryContentColor(FileEntry directory)
    {
        var current = directory;
        for (int depth = 0; depth < 64; depth++)
        {
            if (!current.IsDirectory) return GetFileColor(current);
            FileEntry? largest = null;
            foreach (var child in current.Children)
                if (child.Size > 0 && (largest is null || child.Size > largest.Size)) largest = child;
            if (largest is null) break;
            current = largest;
        }
        return FilePalette[GetExtensionColorIndex(directory.Path)];
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

        // 深度只作为安全上限；真实 LOD 由屏幕面积决定，避免大目录被硬性截成灰块。
        int effectiveMaxDepth = Math.Clamp(maxDepth, 0, 64);

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
            ContentColorHex = GetDirectoryContentColor(dir),
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

        var children = allChildren.Take(MaxChildrenPerDir)
            .Select(c => new LayoutItem(c, c.Size)).ToList();
        if (allChildren.Count > MaxChildrenPerDir)
        {
            long tailSize = allChildren.Skip(MaxChildrenPerDir).Sum(c => c.Size);
            children.Add(new LayoutItem(dir, tailSize, allChildren.Count - MaxChildrenPerDir));
        }

        var contentRect = new Rect(contentX, contentY, contentW, contentH);
        SquarifyChildren(children, contentRect, depth + 1, maxDepth, zoom, result);
    }

    private void SquarifyChildren(
        List<LayoutItem> items, Rect rect, int depth, int maxDepth, float zoom,
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
        List<LayoutItem> items, List<float> areas, Rect rect,
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
        List<LayoutItem> items, List<float> areas,
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
        LayoutItem item, Rect rect, int depth, int maxDepth, float zoom,
        List<TreemapNode> result)
    {
        if (result.Count >= MaxVisibleNodes || rect.Width < Spec(MinNodeDimension, zoom) || rect.Height < Spec(MinNodeDimension, zoom))
            return;

        var entry = item.Entry;
        if (item.AggregatedCount > 0)
        {
            result.Add(new TreemapNode
            {
                Entry = entry, X = rect.X, Y = rect.Y, Width = rect.Width, Height = rect.Height,
                Depth = depth, ColorHex = GetDirectoryContentColor(entry),
                AggregatedCount = item.AggregatedCount, AggregatedSize = item.Size,
                Label = $"+{item.AggregatedCount} 项 ({FormatSize(item.Size)})",
            });
        }
        else if (entry.IsDirectory)
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
