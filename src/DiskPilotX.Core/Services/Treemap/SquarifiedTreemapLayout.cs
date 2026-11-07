using DiskPilotX.Core.Models;

namespace DiskPilotX.Core.Services.Treemap;

public class SquarifiedTreemapLayout : ITreemapLayoutEngine
{
    // 布局坐标保持真实层级；屏幕预算决定装饰和小于像素的同父尾部，而不是按目录面积截断文件。
    private static float Spec(float screenPx, float zoom) => screenPx / Math.Max(zoom, 1e-6f);
    private readonly record struct LayoutBudget(float ViewportArea, FileEntry? FocusEntry = null);

    // 目录（含标题栏区域）使用中性深灰，不做彩色编码；仅文件 cell 按类型上色。
    private const string DirectoryColor = "#303030";

    // 布局引擎的独立调用使用稳定回退色；App随后用整次扫描的缓存映射赋予完整语义。
    private static string GetFileColor(FileEntry entry) => TreemapColorContext.GetFallbackFileColor(entry);

    private sealed record LayoutItem(FileEntry Entry, long Size, int Count = 0);

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
        public double X, Y, Width, Height;

        public Rect(double x, double y, double width, double height)
        {
            X = x; Y = y; Width = width; Height = height;
        }

        public double Area => Width * Height;
        public double ShorterSide => Math.Min(Width, Height);
    }

    public List<TreemapNode> ComputeLayout(FileEntry root, float width, float height, int maxDepth, float zoom = 1f) =>
        ComputeLayoutCore(root, width, height, maxDepth, zoom, null);

    /// <summary>Ping 仅将指定真实实体从同父长尾中展开，不改字节比例，也不把聚合块冒充文件。</summary>
    public List<TreemapNode> ComputeFocusedLayout(FileEntry root, float width, float height, int maxDepth, float zoom, FileEntry focus) =>
        ComputeLayoutCore(root, width, height, maxDepth, zoom, focus);

    private List<TreemapNode> ComputeLayoutCore(FileEntry root, float width, float height, int maxDepth, float zoom, FileEntry? focus)
    {
        var result = new List<TreemapNode>();

        if (root.Size <= 0 || width <= 4 || height <= 4)
            return result;

        // 相对当前根0层；手动上限决定终止层，自动传入安全上限并由像素LOD渐进展开。
        int effectiveMaxDepth = Math.Clamp(maxDepth, 0, 64);

        // 固定预留外描边空间（屏幕像素恒定），选中时无需缩小或重排任何 cell。
        float rootPad = Spec(2f, zoom);
        var rootRect = new Rect(rootPad, rootPad, width - rootPad * 2, height - rootPad * 2);
        EmitDirectoryNode(root, rootRect, 0, effectiveMaxDepth, zoom, result, new(width * height, focus));

        return result;
    }

    private void EmitDirectoryNode(
        FileEntry dir, Rect rect, int depth, int maxDepth, float zoom,
        List<TreemapNode> result, LayoutBudget budget)
    {
        bool terminal = depth >= maxDepth;
        // 上限目录保留全部容量，不枚举或裁剪后代；标题是否可读与是否继续细分各自决定。
        var children = terminal ? new List<LayoutItem>() : dir.Children.Where(c => c.Size > 0).OrderByDescending(c => c.Size)
            .Select(c => new LayoutItem(c, c.Size)).ToList();
        // 根保留定位信息；单子路径不重复收费，有信息的分支按自身可读像素决定，不设标题个数配额。
        double childBytes = children.Sum(c => (double)c.Size);
        double secondaryShare = childBytes > 0 ? 1d - children[0].Size / childBytes : 0d;
        bool usefulBranch = children.Count > 1 && secondaryShare * Math.Min(rect.Width, rect.Height) * zoom >= 8d;
        bool significant = rect.Area * zoom * zoom >= budget.ViewportArea * 0.025f;
        float headerHeight = (depth == 0 || usefulBranch && significant
            && TreemapLabelPolicy.HasDirectoryTitleValue(secondaryShare, rect.Height * zoom))
            ? TreemapLabelPolicy.DirectoryHeaderHeight((float)rect.Width, (float)rect.Height, depth, zoom) : 0f;
        if (terminal)
            headerHeight = TreemapLabelPolicy.DirectoryHeaderHeight((float)rect.Width, (float)rect.Height, 0, zoom);
        string? label = headerHeight > 0 ? dir.Name : null;

        string color = DirectoryColor;

        var node = new TreemapNode
        {
            Entry = dir,
            X = (float)rect.X,
            Y = (float)rect.Y,
            Width = (float)rect.Width,
            Height = (float)rect.Height,
            ColorHex = color,
            ContentColorHex = GetDirectoryContentColor(dir),
            Depth = depth,
            IsVisible = rect.Width > 0 && rect.Height > 0,
            ShowDirectoryFrame = headerHeight > 0,
            HeaderHeight = headerHeight,
            Label = label,
            SizeLabel = FormatSize(dir.Size)
        };
        result.Add(node);

        if (depth >= maxDepth)
            return;

        // 展开目录只为标题预留高度；侧边和底边直接复用子内容，不逐层内缩累积框线。
        var contentRect = new Rect(rect.X, rect.Y + headerHeight,
            rect.Width, rect.Height - headerHeight);
        if (children.Count == 0 || contentRect.Width <= 0 || contentRect.Height <= 0) return;
        node.IsContentLeaf = false;
        SquarifyChildren(children, contentRect, depth + 1, maxDepth, zoom, result,
            budget);
    }

    private void SquarifyChildren(
        List<LayoutItem> items, Rect rect, int depth, int maxDepth, float zoom,
        List<TreemapNode> result, LayoutBudget budget)
    {
        if (depth > maxDepth || items.Count == 0 || rect.Area <= 0)
            return;

        // 同父长尾只合并小于一个屏幕像素的项目，面积和字节仍占据真实父目录内的连续分区。
        long totalSize = items.Sum(i => i.Size);
        double screenArea = rect.Area * zoom * zoom;
        int tailStart = items.FindIndex(i => (double)i.Size / totalSize * screenArea < 1d);
        if (tailStart >= 0 && items.Count - tailStart > 1)
        {
            var parent = items[tailStart].Entry.Parent;
            // 测试和外部构造的树可能未设置 Parent；不虚构路径或跨父目录合并。
            if (parent is not null && items.Skip(tailStart).All(i => ReferenceEquals(i.Entry.Parent, parent)))
            {
                var tail = items.Skip(tailStart).Where(item => !ReferenceEquals(item.Entry, budget.FocusEntry)).ToArray();
                if (tail.Length > 1)
                {
                    var focused = items.Skip(tailStart).Where(item => ReferenceEquals(item.Entry, budget.FocusEntry));
                    items = items.Take(tailStart).Concat(focused).Append(new LayoutItem(parent, tail.Sum(i => i.Size), tail.Length))
                        .OrderByDescending(i => i.Size).ToList();
                }
            }
        }

        if (items.Count == 1)
        {
            EmitNode(items[0], rect, depth, maxDepth, zoom, result, budget);
            return;
        }

        var normalizedAreas = items
            .Select(i => (double)i.Size / totalSize * rect.Area)
            .ToList();

        LayoutItems(items, normalizedAreas, rect, depth, maxDepth, zoom, result, budget);
    }

    private void LayoutItems(
        List<LayoutItem> items, List<double> areas, Rect rect,
        int depth, int maxDepth, float zoom,
        List<TreemapNode> result, LayoutBudget budget)
    {
        var remaining = new Rect(rect.X, rect.Y, rect.Width, rect.Height);
        int start = 0;

        while (start < items.Count)
        {
            double shorterSide = remaining.ShorterSide;
            if (shorterSide <= 0)
                break;

            var rowAreas = new List<double> { areas[start] };
            int end = start + 1;

            while (end < items.Count)
            {
                double worstWithout = WorstAspectRatio(rowAreas, shorterSide);
                rowAreas.Add(areas[end]);
                double worstWith = WorstAspectRatio(rowAreas, shorterSide);

                if (worstWith > worstWithout)
                {
                    rowAreas.RemoveAt(rowAreas.Count - 1);
                    break;
                }
                end++;
            }

            int rowCount = end - start;
            remaining = LayoutRow(items, areas, start, rowCount, remaining,
                depth, maxDepth, zoom, result, budget);
            start = end;
        }
    }

    private Rect LayoutRow(
        List<LayoutItem> items, List<double> areas,
        int startIndex, int count, Rect rect,
        int depth, int maxDepth, float zoom,
        List<TreemapNode> result, LayoutBudget budget)
    {
        double rowTotal = 0;
        for (int i = startIndex; i < startIndex + count; i++)
            rowTotal += areas[i];

        bool layoutHorizontally = rect.Width >= rect.Height;

        double stripThickness;
        if (layoutHorizontally)
            stripThickness = rect.Area > 0 ? rowTotal / rect.Height : 0;
        else
            stripThickness = rect.Area > 0 ? rowTotal / rect.Width : 0;

        // 数值预算和条带计算全程双精度；即使接近 long 上限，也不能把正字节尾部舍掉。
        double stripSide = layoutHorizontally ? rect.Width : rect.Height;
        bool finalStrip = startIndex + count == items.Count;
        stripThickness = finalStrip ? stripSide : Math.Min(stripThickness, Math.BitDecrement(stripSide));
        double longSide = layoutHorizontally ? rect.Height : rect.Width;
        double offset = 0;

        for (int i = startIndex; i < startIndex + count; i++)
        {
            double available = longSide - offset;
            double itemLength = i == startIndex + count - 1 ? available
                : Math.Min(areas[i] / rowTotal * longSide, Math.BitDecrement(available));

            double x, y, w, h;
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

            EmitNode(items[i], new Rect(x, y, w, h), depth, maxDepth, zoom, result, budget);
            offset += itemLength;
        }

        if (layoutHorizontally)
            return new Rect(rect.X + stripThickness, rect.Y, rect.Width - stripThickness, rect.Height);
        else
            return new Rect(rect.X, rect.Y + stripThickness, rect.Width, rect.Height - stripThickness);
    }

    private void EmitNode(
        LayoutItem item, Rect rect, int depth, int maxDepth, float zoom,
        List<TreemapNode> result, LayoutBudget budget)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;
        var entry = item.Entry;
        if (item.Count > 0)
        {
            string content = GetDirectoryContentColor(entry);
            result.Add(new TreemapNode
            {
                Entry = entry, X = (float)rect.X, Y = (float)rect.Y, Width = (float)rect.Width, Height = (float)rect.Height,
                Depth = depth, AggregatedCount = item.Count, AggregatedSize = item.Size,
                SizeLabel = FormatSize(item.Size), ColorHex = content, ContentColorHex = content,
                Label = TreemapLabelPolicy.ShowFileLabel((float)rect.Width, (float)rect.Height, zoom) ? $"+{item.Count} 项" : null,
                ShowDirectoryFrame = false, HeaderHeight = 0,
            });
        }
        else if (entry.IsDirectory)
        {
            EmitDirectoryNode(entry, rect, depth, maxDepth, zoom, result, budget);
        }
        else
        {
            string fileColor = GetFileColor(entry);

            // Always provide a label if the rectangle has any visible area.
            // The renderer handles truncation and fitting.
            string? label = TreemapLabelPolicy.ShowFileLabel((float)rect.Width, (float)rect.Height, zoom)
                ? entry.Name
                : null;

            result.Add(new TreemapNode
            {
                Entry = entry,
                X = (float)rect.X,
                Y = (float)rect.Y,
                Width = (float)rect.Width,
                Height = (float)rect.Height,
                ColorHex = fileColor,
                Depth = depth,
                IsVisible = true,
                Label = label,
                SizeLabel = FormatSize(entry.Size)
            });
        }
    }

    private static double WorstAspectRatio(List<double> rowAreas, double sideLength)
    {
        if (rowAreas.Count == 0 || sideLength <= 0)
            return double.MaxValue;

        double rowTotal = 0;
        double minArea = double.MaxValue;
        double maxArea = double.MinValue;

        for (int i = 0; i < rowAreas.Count; i++)
        {
            double a = rowAreas[i];
            rowTotal += a;
            if (a < minArea) minArea = a;
            if (a > maxArea) maxArea = a;
        }

        if (rowTotal <= 0)
            return double.MaxValue;

        double w2 = sideLength * sideLength;
        double r2 = rowTotal * rowTotal;

        double worstForMax = Math.Max(w2 * maxArea / r2, r2 / (w2 * maxArea));
        double worstForMin = Math.Max(w2 * minArea / r2, r2 / (w2 * minArea));

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
