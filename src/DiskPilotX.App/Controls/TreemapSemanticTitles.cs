using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Treemap;
using SkiaSharp;

namespace DiskPilotX.App.Controls;

/// <summary>局部语义标题不重排文件；仅在当前可见画面竞争面积预算并用滞回稳定选择。</summary>
internal sealed class TreemapSemanticTitles
{
    internal sealed record Title(TreemapNode Node, SKRect Rect, float Opacity);
    private readonly Dictionary<FileEntry, (bool Target, long Changed)> _states = new();
    private Title[] _titles = [];
    internal IReadOnlyList<Title> Current => _titles;
    internal bool Animating { get; private set; }

    internal IReadOnlyList<Title> Update(IEnumerable<TreemapNode> nodes, float zoom, float panX, float panY,
        float width, float height, long now, bool animate)
    {
        float area = width * height;
        const float titleHeight = TreemapLabelPolicy.DirectoryTitleScreenHeight;
        var candidates = new List<(TreemapNode Node, SKRect Rect, float Score, bool Pinned)>();
        var geometry = new Dictionary<FileEntry, (TreemapNode Node, SKRect Rect)>();
        var fixedHeaders = new List<SKRect>();
        var fixedCaptions = new List<Title>();
        foreach (var n in nodes)
        {
            if (!n.Entry.IsDirectory || n.AggregatedCount > 0) continue;
            float x = n.X * zoom + panX, y = n.Y * zoom + panY;
            float w = n.Width * zoom, h = n.Height * zoom;
            float left = Math.Max(0, x), right = Math.Min(width, x + w);
            float top = Math.Max(0, y), bottom = Math.Min(height, y + h);
            if (right <= left || bottom <= top) continue;
            bool hasHeader = n.HeaderHeight > 0 || n.Label is not null && n.ShowDirectoryFrame;
            float titleH = (n.HeaderHeight ?? titleHeight / zoom) * zoom;
            if (hasHeader && y >= 0 && y + titleH <= height)
            {
                var headerRect = new SKRect(left, y, right, y + titleH);
                fixedHeaders.Add(headerRect);
                // 已预留的标题不重复收取覆盖预算；横向裁剪只重排文字，不隐藏整行。
                if (x < 0 || x + w > width) fixedCaptions.Add(new(n, headerRect, 1));
                continue;
            }
            // 当前根的离屏标题由面包屑承担；局部目录仍可读时锚定到自身与视口的交集。
            if (n.Depth == 0 && y < 0) continue;
            float visibleH = bottom - top;
            if (right - left < 96 || visibleH < 30) continue;
            if (!hasHeader && (zoom < 1.15f || !n.IsContentLeaf && !HasReadableBranch(n.Entry, w, h))) continue;
            var titleRect = new SKRect(left, top, right, top + titleHeight);
            if (right - left >= 96 && visibleH >= 30) geometry[n.Entry] = (n, titleRect);
            bool retained = _states.TryGetValue(n.Entry, out var state) && state.Target;
            float minWidth = hasHeader ? (retained ? 96f : 104f) : (retained ? 106f : 130f);
            float minHeight = hasHeader ? 30f : (retained ? 58f : 74f);
            if (right - left < minWidth || visibleH < minHeight) continue;
            float share = (right - left) * visibleH / Math.Max(1, area);
            if (share < (retained ? .018f : .028f)) continue;
            float weight = n.Entry.Parent?.Size > 0 ? (float)((double)n.Entry.Size / n.Entry.Parent.Size) : 1f;
            float visibleFraction = (right - left) * visibleH / Math.Max(1, w * h);
            float score = share * (1 + MathF.Sqrt(Math.Max(0, weight))) * (.6f + .4f * visibleFraction);
            candidates.Add((n, titleRect, score, y < 0));
        }
        // 多层祖先一起越过顶边时，让仍可读的更深局部目录承担定位，不把祖先逐个钉满顶边。
        var pinned = candidates.Where(c => c.Pinned).ToArray();
        candidates.RemoveAll(c => c.Pinned && pinned.Any(d => IsAncestor(c.Node.Entry, d.Node.Entry)));
        var selected = new List<(TreemapNode Node, SKRect Rect)>();
        float spent = 0;
        foreach (var c in candidates.OrderByDescending(c => c.Score))
        {
            if (spent + c.Rect.Width * titleHeight > area * .10f) continue;
            if (fixedHeaders.Any(r => Overlaps(r, c.Rect)) || selected.Any(t => Overlaps(t.Rect, c.Rect))) continue;
            selected.Add((c.Node, c.Rect)); spent += c.Rect.Width * titleHeight;
        }
        var selectedEntries = selected.Select(t => t.Node.Entry).ToHashSet();
        var currentNodes = geometry;
        foreach (var chosen in selected)
            if (!_states.TryGetValue(chosen.Node.Entry, out var old) || !old.Target) _states[chosen.Node.Entry] = (true, now);
        foreach (var entry in _states.Keys.ToArray())
            if (!selectedEntries.Contains(entry) && _states[entry].Target) _states[entry] = (false, now);
        var output = new List<Title>(fixedCaptions); Animating = false;
        foreach (var entry in _states.Keys.ToArray())
        {
            var state = _states[entry]; float t = animate ? Math.Clamp((now - state.Changed) / 110f, 0, 1) : 1;
            if (t < 1) Animating = true;
            float opacity = state.Target ? .08f + .92f * t : 1 - t;
            if (opacity <= 0 || !currentNodes.TryGetValue(entry, out var c))
            { if (!state.Target || !currentNodes.ContainsKey(entry)) _states.Remove(entry); continue; }
            // 新定位标题和固定标题优先；旧标题淡出不能在同一可见区域继续叠字。
            // 保留原有压入自身cell、面积预算与滞回，仅取消会遮挡当前标题的淡出帧。
            if (!state.Target && (fixedHeaders.Any(r => Overlaps(r, c.Rect))
                || selected.Any(title => Overlaps(title.Rect, c.Rect))
                || output.Any(title => Overlaps(title.Rect, c.Rect)))) continue;
            output.Add(new(c.Node, c.Rect, opacity));
        }
        return _titles = output.ToArray();
    }

    private static bool HasReadableBranch(FileEntry directory, float width, float height)
    {
        double total = 0, largest = 0;
        foreach (var child in directory.Children)
        {
            if (child.Size <= 0) continue;
            total += child.Size; largest = Math.Max(largest, child.Size);
        }
        double secondaryShare = total > 0 ? 1d - largest / total : 0d;
        return secondaryShare * Math.Min(width, height) >= 8d
            && TreemapLabelPolicy.HasDirectoryTitleValue(secondaryShare, height);
    }

    // 完整标题条不能相交；只容忍浮点共边舍入，不以文字留白放行临界区叠压。
    private static bool Overlaps(SKRect a, SKRect b) => Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left) > .001f
        && Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top) > .001f;

    private static bool IsAncestor(FileEntry parent, FileEntry entry)
    {
        for (var p = entry.Parent; p is not null; p = p.Parent) if (ReferenceEquals(p, parent)) return true;
        return false;
    }

    internal TreemapNode? Hit(float x, float y) => _titles.LastOrDefault(t => t.Opacity >= .05f && t.Rect.Contains(x, y))?.Node;
    internal bool Contains(TreemapNode node, float x, float y) => _titles.Any(t => ReferenceEquals(t.Node.Entry, node.Entry) && t.Rect.Contains(x, y));
    internal void Clear() { _states.Clear(); _titles = []; Animating = false; }
}
