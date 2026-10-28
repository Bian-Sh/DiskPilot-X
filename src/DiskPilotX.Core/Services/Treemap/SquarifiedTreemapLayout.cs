using DiskPilotX.Core.Models;

namespace DiskPilotX.Core.Services.Treemap;

public class SquarifiedTreemapLayout : ITreemapLayoutEngine
{
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
    internal const int MaxChildrenPerDir = 128;

    // 不按全图节点总数截断：深度优先遍历耗尽预算会丢失后续同级区域。
    // 布局规模由屏幕像素 LOD、目录长尾聚合与深度上限约束；视口外节点由绘制层剔除。

    // LOD 生长：zoom 越大，展开子节点所需的面积阈值越小（按 zoom² 缩放），
    // 于是更多目录跨过阈值、向下展开更多层级 —— 等价于地图加载更细瓦片。
    // zoom=1 时用基础阈值；zoom=2 时阈值降为 1/4，更多目录展开。
    private static float LodContentArea(float zoom) =>
        MinContentArea / Math.Max(zoom * zoom, 1f);


    // 目录（含标题栏区域）使用中性深灰，不做彩色编码；仅文件 cell 按类型上色。
    private const string DirectoryColor = "#303030";

    // 布局引擎的独立调用使用稳定回退色；App随后用整次扫描的缓存映射赋予完整语义。
    private static string GetFileColor(FileEntry entry) => TreemapColorContext.GetFallbackFileColor(entry);

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
        return "#858C96";
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
        float headerHeight = TreemapLabelPolicy.DirectoryHeaderHeight(rect.Width, rect.Height, depth, zoom);

        string? label = headerHeight > 0
            ? dir.Name
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
            Label = label,
            SizeLabel = FormatSize(dir.Size)
        };
        result.Add(node);

        if (depth >= maxDepth)
            return;

        float borderPad = GetBorderPadding(rect.Width, rect.Height, zoom);
        float contentX = rect.X + borderPad;
        float contentY = rect.Y + Math.Max(headerHeight, borderPad);
        float contentW = rect.Width - borderPad * 2;
        float contentH = rect.Height - Math.Max(headerHeight, borderPad) - borderPad;

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
        if (depth > maxDepth || items.Count == 0 || rect.Area <= 0)
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

        while (start < items.Count)
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
        if (rect.Width < Spec(MinNodeDimension, zoom) || rect.Height < Spec(MinNodeDimension, zoom))
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
            string? label = TreemapLabelPolicy.ShowFileLabel(rect.Width, rect.Height, zoom)
                ? entry.Name
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
