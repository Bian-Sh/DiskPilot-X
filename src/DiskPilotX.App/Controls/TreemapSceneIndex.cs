using System.Runtime.CompilerServices;
using DiskPilotX.Core.Models;
using SkiaSharp;

namespace DiskPilotX.App.Controls;

/// <summary>同一节点集合只排序和建立空间索引一次，后台预热后由UI只读查询。</summary>
internal sealed class TreemapSceneIndex
{
    private const int Cells = 32;
    private static readonly ConditionalWeakTable<List<TreemapNode>, TreemapSceneIndex> Cache = new();
    private readonly List<int>?[] _grid = new List<int>?[Cells * Cells];
    private readonly float _width, _height;
    private readonly int[] _seen;
    private int _stamp;
    internal TreemapNode[] Ordered { get; }
    internal Dictionary<FileEntry, TreemapNode> Directories { get; } = new();
    internal bool HasZoomDetail { get; }

    internal static TreemapSceneIndex Prepare(List<TreemapNode> nodes) => Cache.GetValue(nodes, static n => new(n));

    private TreemapSceneIndex(List<TreemapNode> nodes)
    {
        Ordered = nodes.Where(n => n.IsVisible).OrderBy(n => n.Depth).ThenBy(n => n.Entry.IsDirectory ? 0 : 1).ToArray();
        _width = Math.Max(1f, Ordered.Length == 0 ? 1f : Ordered.Max(n => n.X + n.Width));
        _height = Math.Max(1f, Ordered.Length == 0 ? 1f : Ordered.Max(n => n.Y + n.Height));
        _seen = new int[Ordered.Length];
        HasZoomDetail = nodes.Any(n => n.AggregatedCount > 0) || nodes.Count(n => !n.Entry.IsDirectory) > 32;
        for (int i = 0; i < Ordered.Length; i++)
        {
            var n = Ordered[i];
            if (n.Entry.IsDirectory && n.AggregatedCount == 0) Directories[n.Entry] = n;
            int left = Cell(n.X, _width), right = Cell(n.X + n.Width, _width);
            int top = Cell(n.Y, _height), bottom = Cell(n.Y + n.Height, _height);
            for (int y = top; y <= bottom; y++)
                for (int x = left; x <= right; x++) (_grid[y * Cells + x] ??= new()).Add(i);
        }
    }

    private static int Cell(float value, float extent) => Math.Clamp((int)(value / extent * Cells), 0, Cells - 1);

    internal TreemapNode? Hit(float x, float y)
    {
        if (x < 0 || y < 0 || x > _width || y > _height) return null;
        var bucket = _grid[Cell(y, _height) * Cells + Cell(x, _width)];
        if (bucket is null) return null;
        for (int i = bucket.Count - 1; i >= 0; i--)
        {
            var n = Ordered[bucket[i]];
            if (x >= n.X && x < n.X + n.Width && y >= n.Y && y < n.Y + n.Height) return n;
        }
        return null;
    }

    internal TreemapNode[] Visible(SKRect rect)
    {
        if (rect.Left <= 0 && rect.Top <= 0 && rect.Right >= _width && rect.Bottom >= _height) return Ordered;
        // 视口覆盖较大比例时，顺序扫描比收集数十万索引再排序更便宜。
        if (rect.Width * rect.Height >= _width * _height * .15f)
            return Ordered.Where(n => n.X < rect.Right && n.Y < rect.Bottom
                && n.X + n.Width > rect.Left && n.Y + n.Height > rect.Top).ToArray();
        if (++_stamp == int.MaxValue) { Array.Clear(_seen); _stamp = 1; }
        var found = new List<int>();
        for (int y = Cell(rect.Top, _height); y <= Cell(rect.Bottom, _height); y++)
            for (int x = Cell(rect.Left, _width); x <= Cell(rect.Right, _width); x++)
                if (_grid[y * Cells + x] is { } bucket)
                    foreach (int i in bucket)
                    {
                        if (_seen[i] == _stamp) continue;
                        _seen[i] = _stamp; var n = Ordered[i];
                        if (n.X < rect.Right && n.Y < rect.Bottom && n.X + n.Width > rect.Left && n.Y + n.Height > rect.Top) found.Add(i);
                    }
        found.Sort();
        return found.Select(i => Ordered[i]).ToArray();
    }
}
