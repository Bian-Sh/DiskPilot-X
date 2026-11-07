using DiskPilotX.Core.Models;

namespace DiskPilotX.App.ViewModels;

public partial class TreemapViewModel
{
    private FileEntry? _focusLayoutEntry;
    internal void SetFocusLayoutEntry(FileEntry? entry) => _focusLayoutEntry = entry;

    /// <summary>后台收敛目标倍率的布局，只提交最终几何，避免镜头到达后再次重排。</summary>
    internal Task<TreemapNode?> PrepareFocusLayoutAsync(FileEntry entry, Func<TreemapNode, float> chooseZoom,
        CancellationToken cancellationToken)
    {
        CancelZoomUpdate();
        int generation = _layoutGeneration, mode = DisplayDepth, depth = LayoutDepth;
        var root = CurrentRoot; var scan = _scanRoot; var colors = _colorContext;
        float width = ViewWidth, height = ViewHeight, initialZoom = Zoom;
        IsZoomLayoutPending = true; ZoomLayoutError = null;
        var pending = Prepare();
        PendingZoomUpdate = pending;
        return pending;

        async Task<TreemapNode?> Prepare()
        {
            try
            {
                if (root is null || width <= 0 || height <= 0) return null;
                await _zoomWorker.WaitAsync(cancellationToken);
                (List<TreemapNode> Nodes, float Zoom)? prepared;
                try
                {
                    prepared = await Task.Run<(List<TreemapNode>, float)?>(() =>
                    {
                        float zoom = initialZoom;
                        for (int attempt = 0; attempt < 8; attempt++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var nodes = ComputeFocusLayout(root, width, height, depth, zoom, entry);
                            var target = nodes.FirstOrDefault(n => ReferenceEquals(n.Entry, entry) && n.AggregatedCount == 0);
                            if (target is null) return null;
                            float next = Math.Clamp(chooseZoom(target), 1, 8);
                            if (Math.Abs(next - zoom) < 0.0001f)
                            {
                                colors?.Apply(nodes); Controls.TreemapSceneIndex.Prepare(nodes);
                                return (nodes, zoom);
                            }
                            zoom = next;
                        }
                        return null;
                    }, cancellationToken);
                }
                finally { _zoomWorker.Release(); }
                cancellationToken.ThrowIfCancellationRequested();
                if (prepared is not { } result || generation != _layoutGeneration || !ReferenceEquals(root, CurrentRoot)
                    || !ReferenceEquals(scan, _scanRoot) || width != ViewWidth || height != ViewHeight || mode != DisplayDepth
                    || entry.IsDeleted) return null;
                Zoom = result.Zoom;
                Nodes = result.Nodes; ZoomLayoutCommits++;
                return Nodes.First(n => ReferenceEquals(n.Entry, entry) && n.AggregatedCount == 0);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                ZoomLayoutError = error.Message;
                throw;
            }
            finally { if (generation == _layoutGeneration) IsZoomLayoutPending = false; }
        }
    }

    private List<TreemapNode> ComputeFocusLayout(FileEntry root, float width, float height, int depth, float zoom, FileEntry? focus) =>
        focus is not null && _layoutEngine is DiskPilotX.Core.Services.Treemap.SquarifiedTreemapLayout engine
            ? engine.ComputeFocusedLayout(root, width, height, depth, zoom, focus)
            : _layoutEngine.ComputeLayout(root, width, height, depth, zoom);

    internal async Task<bool> RevealFocusEntryAsync(FileEntry entry, CancellationToken token)
    {
        _focusLayoutEntry = entry;
        await RequestZoomAsync(Zoom);
        token.ThrowIfCancellationRequested();
        return Nodes?.Any(n => ReferenceEquals(n.Entry, entry) && n.AggregatedCount == 0) == true;
    }

    internal FileEntry? FindFocusEntry(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || _scanRoot is null) return null;
        var entry = new DiskPilotX.Core.Services.FileTree.FileTreeQueryService().FindByPath(
            _session ?? new ScanSession { RootEntry = _scanRoot }, path.Trim());
        return entry is { IsDeleted: false } ? entry : null;
    }

    /// <summary>后台准备层级几何，提交前核对扫描、根、尺寸和模式；不阻塞镜头动画。</summary>
    internal async Task<bool> NavigateToFocusRootAsync(FileEntry root, CancellationToken cancellationToken)
    {
        if (!root.IsDirectory || root.IsDeleted || !IsInCurrentScan(root)) return false;
        if (ReferenceEquals(root, CurrentRoot)) return true;
        var scan = _scanRoot; var previous = CurrentRoot;
        float width = ViewWidth, height = ViewHeight;
        int depth = LayoutDepth, mode = DisplayDepth;
        if (width <= 0 || height <= 0) return false;
        var colors = _colorContext;
        var focus = _focusLayoutEntry;
        var nodes = await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var layout = ComputeFocusLayout(root, width, height, depth, 1, focus);
            colors?.Apply(layout);
            Controls.TreemapSceneIndex.Prepare(layout);
            return layout;
        }, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(scan, _scanRoot) || !ReferenceEquals(previous, CurrentRoot)
            || width != ViewWidth || height != ViewHeight || mode != DisplayDepth || root.IsDeleted) return false;
        CancelZoomUpdate();
        if (previous is not null) PushBack(previous);
        _forwardStack.Clear();
        CurrentRoot = root;
        UpdateBreadcrumb();
        Nodes = nodes;
        return true;
    }
}
