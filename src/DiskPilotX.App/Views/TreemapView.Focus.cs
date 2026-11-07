using DiskPilotX.Core.Models;

namespace DiskPilotX.App.Views;

public partial class TreemapView
{
    private CancellationTokenSource? _focusRequest;

    /// <summary>切换推荐的真实扫描版本也保留旧画面，禁止先硬切扫描再启动 Ping。</summary>
    public async Task<bool> FocusScanPathAsync(FileEntry root, Func<bool> activateScan, string path, bool highlight = true)
    {
        _focusRequest?.Cancel();
        Treemap.CancelFocusAnimation();
        Treemap.InvalidateVisual();
        if (ReferenceEquals(_viewModel?.ScanRoot, root))
            return activateScan() && await FocusPathAsync(path, highlight);
        using var request = new CancellationTokenSource();
        _focusRequest = request;
        try
        {
            if (!await Treemap.ChangeFocusRootAsync(root, () => Task.FromResult(activateScan()), request.Token)) return false;
            request.Token.ThrowIfCancellationRequested();
            _focusRequest = null;
            return await FocusPathAsync(path, highlight);
        }
        finally { if (ReferenceEquals(_focusRequest, request)) _focusRequest = null; }
    }

    /// <summary>Ping 当前扫描内真实实体；地图层级、大小和位置均以可取消的视野追踪呈现。</summary>
    public async Task<bool> FocusPathAsync(string path, bool highlight = true)
    {
        _focusRequest?.Cancel();
        // 新请求即使找不到目标，也不能继续提示上一目标。
        Treemap.CancelFocusAnimation();
        Treemap.InvalidateVisual();
        using var request = new CancellationTokenSource();
        _focusRequest = request;
        var vm = _viewModel;
        var entry = vm?.FindFocusEntry(path);
        if (vm is null || entry is null) { _focusRequest = null; return false; }
        try
        {
            vm.SetFocusLayoutEntry(entry);
            TreemapNode? Target() => vm.Nodes?.FirstOrDefault(n => ReferenceEquals(n.Entry, entry) && n.AggregatedCount == 0);
            var node = Target();
            if (node is null && await vm.RevealFocusEntryAsync(entry, request.Token)) node = Target();
            if (node is null)
            {
                // 当前层级之外先拉回共同祖先，避免隔空跳到另一支目录。
                var ancestors = new HashSet<FileEntry>();
                for (var parent = entry.IsDirectory ? entry : entry.Parent; parent is not null; parent = parent.Parent)
                    ancestors.Add(parent);
                var common = vm.CurrentRoot;
                while (common is not null && !ancestors.Contains(common)) common = common.Parent;
                if (common is not null && !ReferenceEquals(common, vm.CurrentRoot))
                {
                    if (!await Treemap.ChangeFocusRootAsync(common, () => vm.NavigateToFocusRootAsync(common, request.Token), request.Token)) return false;
                    node = Target();
                }
                if (node is null)
                {
                    var root = entry.Parent ?? (entry.IsDirectory ? entry : null);
                    if (root is null || !await Treemap.ChangeFocusRootAsync(root,
                            () => vm.NavigateToFocusRootAsync(root, request.Token), request.Token)) return false;
                    node = Target();
                }
            }
            if (node is null || request.IsCancellationRequested || !ReferenceEquals(vm, _viewModel)) return false;
            // 先稳定最终倍率的布局再一次追踪，不能到达旧落点后提交 LOD 并二次拉回。
            return await Treemap.FocusStableNodeAsync(node, highlight, request.Token,
                (chooseZoom, token) => vm.PrepareFocusLayoutAsync(entry, chooseZoom, token));
        }
        finally { if (ReferenceEquals(_focusRequest, request)) _focusRequest = null; }
    }
}
