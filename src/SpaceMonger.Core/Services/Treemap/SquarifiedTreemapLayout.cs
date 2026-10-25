using SpaceMonger.Core.Models;

namespace SpaceMonger.Core.Services.Treemap;

public class SquarifiedTreemapLayout : ITreemapLayoutEngine
{
    private const float HeaderBaseHeight = 18f;
    private const float HeaderMinHeight = 14f;
    private const float BorderBasePadding = 1f;
    private const float BorderMinPadding = 1f;
    private const float MinNodeDimension = 14f;

    // A directory only expands its children if the content area (after header
    // and border) meets this minimum.  Below this threshold the directory is
    // rendered as a solid colored block — matching SpaceMonger's adaptive
    // depth behavior where small directories become leaf nodes.
    private const float MinContentArea = 800f;  // ~28×28 px equivalent

    // Maximum children to render per directory.
    private const int MaxChildrenPerDir = 20;

    // Keep whole-drive scans responsive even when several large directories
    // each have many visible descendants.
    private const int MaxVisibleNodes = 2000;

    private const string DirectoryColor = "#303030";

    private static readonly string[] FilePalette =
    [
        "#7137A0", "#B2762B", "#87912F", "#246EAA",
        "#26834B", "#A53A3A", "#258D8B", "#66666D",
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

    public List<TreemapNode> ComputeLayout(FileEntry root, float width, float height, int maxDepth)
    {
        var result = new List<TreemapNode>();

        if (root.Size <= 0 || width <= 4 || height <= 4)
            return result;

        // 固定预留外描边空间，选中时无需缩小或重排任何 cell。
        var rootRect = new Rect(2, 2, width - 4, height - 4);
        EmitDirectoryNode(root, rootRect, 0, maxDepth, result);

        return result;
    }

    private void EmitDirectoryNode(
        FileEntry dir, Rect rect, int depth, int maxDepth,
        List<TreemapNode> result)
    {
        float headerHeight = GetHeaderHeight(depth, rect.Height);

        string? label = rect.Width >= 20 && headerHeight >= HeaderMinHeight
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
            IsVisible = rect.Width >= MinNodeDimension && rect.Height >= MinNodeDimension,
            Label = label
        };
        result.Add(node);

        if (depth >= maxDepth || result.Count >= MaxVisibleNodes)
            return;

        float borderPad = GetBorderPadding(depth, rect.Width, rect.Height);
        float contentX = rect.X + borderPad;
        float contentY = rect.Y + headerHeight;
        float contentW = rect.Width - borderPad * 2;
        float contentH = rect.Height - headerHeight - borderPad;

        // Area-based pruning: if the content area is too small to show
        // meaningful children, treat this directory as a leaf (solid block).
        if (contentW < MinNodeDimension || contentH < MinNodeDimension
            || contentW * contentH < MinContentArea)
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
        SquarifyChildren(children, contentRect, depth + 1, maxDepth, result);
    }

    private void SquarifyChildren(
        List<FileEntry> items, Rect rect, int depth, int maxDepth,
        List<TreemapNode> result)
    {
        if (depth > maxDepth || items.Count == 0 || rect.Area <= 0 || result.Count >= MaxVisibleNodes)
            return;

        if (rect.Width < MinNodeDimension || rect.Height < MinNodeDimension)
            return;

        if (items.Count == 1)
        {
            EmitNode(items[0], rect, depth, maxDepth, result);
            return;
        }

        long totalSize = items.Sum(i => i.Size);
        var normalizedAreas = items
            .Select(i => (float)((double)i.Size / totalSize * rect.Area))
            .ToList();

        LayoutItems(items, normalizedAreas, rect, depth, maxDepth, result);
    }

    private void LayoutItems(
        List<FileEntry> items, List<float> areas, Rect rect,
        int depth, int maxDepth,
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
                depth, maxDepth, result);
            start = end;
        }
    }

    private Rect LayoutRow(
        List<FileEntry> items, List<float> areas,
        int startIndex, int count, Rect rect,
        int depth, int maxDepth,
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

            EmitNode(items[i], new Rect(x, y, w, h), depth, maxDepth, result);
            offset += itemLength;
        }

        if (layoutHorizontally)
            return new Rect(rect.X + stripThickness, rect.Y, rect.Width - stripThickness, rect.Height);
        else
            return new Rect(rect.X, rect.Y + stripThickness, rect.Width, rect.Height - stripThickness);
    }

    private void EmitNode(
        FileEntry entry, Rect rect, int depth, int maxDepth,
        List<TreemapNode> result)
    {
        if (result.Count >= MaxVisibleNodes || rect.Width < MinNodeDimension || rect.Height < MinNodeDimension)
            return;

        if (entry.IsDirectory)
        {
            EmitDirectoryNode(entry, rect, depth, maxDepth, result);
        }
        else
        {
            string fileColor = GetFileColor(entry);

            // Always provide a label if the rectangle has any visible area.
            // The renderer handles truncation and fitting.
            string? label = rect.Width >= 20 && rect.Height >= 10
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

    private static float GetHeaderHeight(int depth, float availableHeight)
    {
        float h = HeaderBaseHeight - depth * 1.5f;
        h = Math.Max(h, HeaderMinHeight);
        h = Math.Min(h, availableHeight * 0.4f);
        return h;
    }

    private static float GetBorderPadding(int depth, float width, float height)
    {
        float p = BorderBasePadding - depth * 0.5f;
        p = Math.Max(p, BorderMinPadding);
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
