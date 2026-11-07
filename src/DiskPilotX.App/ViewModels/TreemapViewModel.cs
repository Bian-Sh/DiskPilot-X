using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using DiskPilotX.App.Localization;
using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Treemap;
using DiskPilotX.Core.Services.FileTree;
using DiskPilotX.Core.Services.Settings;

namespace DiskPilotX.App.ViewModels;

public partial class TreemapViewModel : ObservableObject
{
    private readonly ITreemapLayoutEngine _layoutEngine;
    private readonly ISettingsService? _settingsService;
    public int DisplayDepth { get; private set; }
    private int LayoutDepth => DisplayDepth == 0 ? 64 : DisplayDepth;

    /// <summary>只切换显示投影；保留真实选择、缩放和平移，合并后台布局并隔离旧模式结果。</summary>
    public Task SetDisplayDepthAsync(int depth)
    {
        int next = depth is >= 1 and <= 6 ? depth : 0;
        if (next == DisplayDepth) return PendingZoomUpdate;
        if (_settingsService is not null)
        {
            var settings = _settingsService.LoadSettings();
            settings.TreemapDisplayDepth = next;
            _settingsService.SaveSettings(settings);
        }
        DisplayDepth = next;
        OnPropertyChanged(nameof(DisplayDepth));
        return RequestZoomAsync(Zoom);
    }

    private CancellationTokenSource? _zoomRequest;
    private readonly SemaphoreSlim _zoomWorker = new(1, 1);
    private int _layoutGeneration;
    internal Task PendingZoomUpdate { get; private set; } = Task.CompletedTask;
    internal bool IsZoomLayoutPending { get; private set; }
    internal int ZoomLayoutCommits { get; private set; }
    internal string? ZoomLayoutError { get; private set; }

    /// <summary>滚轮先更新几何视口，短延迟合并请求；后台细节只允许最新根、尺寸和缩放提交。</summary>
    public Task RequestZoomAsync(float zoom)
    {
        CancelZoomUpdate();
        Zoom = Math.Clamp(zoom, 1f, 8f);
        var root = CurrentRoot;
        if (root is null || ViewWidth <= 0 || ViewHeight <= 0) return Task.CompletedTask;
        var source = _zoomRequest = new CancellationTokenSource();
        int generation = _layoutGeneration;
        float width = ViewWidth, height = ViewHeight, target = Zoom;
        int mode = DisplayDepth, maxDepth = LayoutDepth;
        var colors = _colorContext;
        var focus = _focusLayoutEntry;
        var dispatcher = System.Windows.Threading.Dispatcher.FromThread(Thread.CurrentThread);
        IsZoomLayoutPending = true; ZoomLayoutError = null;
        return PendingZoomUpdate = Update();

        async Task Update()
        {
            try
            {
                await Task.Delay(65, source.Token);
                await _zoomWorker.WaitAsync(source.Token);
                List<TreemapNode> nodes;
                try
                {
                    nodes = await Task.Run(() =>
                    {
                        source.Token.ThrowIfCancellationRequested();
                        var result = ComputeFocusLayout(root, width, height, maxDepth, target, focus);
                        source.Token.ThrowIfCancellationRequested();
                        colors?.Apply(result);
                        Controls.TreemapSceneIndex.Prepare(result);
                        return result;
                    }, source.Token);
                }
                finally { _zoomWorker.Release(); }
                void Commit()
                {
                    if (source.IsCancellationRequested || generation != _layoutGeneration || !ReferenceEquals(root, CurrentRoot)
                        || width != ViewWidth || height != ViewHeight || target != Zoom || mode != DisplayDepth) return;
                    Nodes = nodes; ZoomLayoutCommits++;
                }
                if (dispatcher is not null && !dispatcher.CheckAccess()) await dispatcher.InvokeAsync(Commit);
                else Commit();
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                // 失败时保留原场景和安全的交互；不以空图覆盖可用结果。
                ZoomLayoutError = error.Message;
                System.Diagnostics.Trace.TraceError("树图后台细节更新失败：{0}", error);
            }
            finally
            {
                void Finish()
                {
                    if (ReferenceEquals(_zoomRequest, source)) { IsZoomLayoutPending = false; _zoomRequest = null; }
                    source.Dispose();
                }
                if (dispatcher is not null && !dispatcher.CheckAccess()) await dispatcher.InvokeAsync(Finish);
                else Finish();
            }
        }
    }

    public void CancelZoomUpdate()
    {
        _layoutGeneration++;
        var old = _zoomRequest; _zoomRequest = null;
        old?.Cancel();
        IsZoomLayoutPending = false;
    }
    private readonly Stack<FileEntry> _navigationStack = new();
    private readonly Stack<FileEntry> _forwardStack = new();
    private FileEntry? _scanRoot;
    private TreemapColorContext? _colorContext;
    internal IReadOnlyList<TreemapTypeColor> TypeColors => _colorContext?.FileTypes ?? Array.Empty<TreemapTypeColor>();

    public FileEntry? ScanRoot => _scanRoot;
    private ScanSession? _session;

    [ObservableProperty]
    private FileEntry? _currentRoot;

    [ObservableProperty]
    private TreemapNode? _selectedNode;

    [ObservableProperty]
    private TreemapNode? _hoveredNode;

    [ObservableProperty]
    private IReadOnlyList<TreemapNode> _multiSelection = Array.Empty<TreemapNode>();

    [ObservableProperty]
    private List<TreemapNode>? _nodes;

    [ObservableProperty]
    private List<string> _breadcrumbPath = new();

    [ObservableProperty]
    private bool _canNavigateUp;

    [ObservableProperty]
    private bool _canGoBack;

    [ObservableProperty]
    private bool _canGoForward;

    [ObservableProperty]
    private bool _canGoUp;

    [ObservableProperty]
    private float _viewWidth;

    [ObservableProperty]
    private float _viewHeight;

    /// <summary>缩放级别（1x 起）。驱动 LOD：越大，布局展开的层级越深（更多子节点生长）。</summary>
    [ObservableProperty]
    private float _zoom = 1f;

    public TreemapViewModel(ITreemapLayoutEngine layoutEngine) : this(layoutEngine, null) { }

    public TreemapViewModel(ITreemapLayoutEngine layoutEngine, ISettingsService? settingsService)
    {
        _layoutEngine = layoutEngine;
        _settingsService = settingsService;
        DisplayDepth = settingsService?.LoadSettings().TreemapDisplayDepth ?? 0;
    }

    partial void OnCurrentRootChanged(FileEntry? value)
    {
        SelectedNode = null;
        MultiSelection = Array.Empty<TreemapNode>();
        // 每次进入新层级从完整视图起步，控制器负责恢复滚轮返回时的父级视口。
        Zoom = 1f;
    }

    public void SetRoot(FileEntry root, ScanSession? session = null)
    {
        _focusLayoutEntry = null;
        _scanRoot = root;
        _colorContext = TreemapColorContext.Create(root);
        _session = session;
        _navigationStack.Clear();
        _forwardStack.Clear();
        CurrentRoot = root;
        UpdateBreadcrumb();
        RecomputeLayout();
    }

    public void DrillDown(FileEntry folder)
    {
        if (folder.IsDeleted) return;
        if (folder == CurrentRoot)
            return;

        if (CurrentRoot is not null)
        {
            PushBack(CurrentRoot);
        }

        _forwardStack.Clear();
        CurrentRoot = folder;
        UpdateBreadcrumb();
        RecomputeLayout();
    }

    public bool NavigateToEntry(FileEntry entry)
    {
        if (entry.IsDeleted) return false;
        var viewRoot = entry.IsDirectory ? entry : entry.Parent;
        if (viewRoot is null || viewRoot == CurrentRoot)
            return viewRoot is not null;

        if (!IsInCurrentScan(viewRoot))
            return false;

        if (CurrentRoot is not null)
        {
            PushBack(CurrentRoot);
        }

        _forwardStack.Clear();
        CurrentRoot = viewRoot;
        UpdateBreadcrumb();
        RecomputeLayout();
        return true;
    }

    [RelayCommand(CanExecute = nameof(CanNavigateUp))]
    public void NavigateUp()
    {
        if (_navigationStack.Count == 0)
            return;

        if (CurrentRoot is not null)
            _forwardStack.Push(CurrentRoot);

        CurrentRoot = _navigationStack.Pop();
        UpdateBreadcrumb();
        RecomputeLayout();
    }

    public void NavigateBack()
    {
        if (_navigationStack.Count == 0)
            return;

        if (CurrentRoot is not null)
            _forwardStack.Push(CurrentRoot);

        CurrentRoot = _navigationStack.Pop();
        UpdateBreadcrumb();
        RecomputeLayout();
    }

    public void NavigateForward()
    {
        if (_forwardStack.Count == 0)
            return;

        if (CurrentRoot is not null)
            PushBack(CurrentRoot);

        CurrentRoot = _forwardStack.Pop();
        UpdateBreadcrumb();
        RecomputeLayout();
    }

    public void NavigateToParent()
    {
        if (CurrentRoot is null)
            return;

        var parent = CurrentRoot.Parent ?? CreateExternalParent(CurrentRoot.Path);
        if (parent is null)
            return;

        PushBack(CurrentRoot);
        _forwardStack.Clear();
        CurrentRoot = parent;
        UpdateBreadcrumb();
        RecomputeLayout();
    }

    public bool NavigateToPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || _scanRoot is null)
            return false;

        // Search the tree for an entry matching this path
        var entry = FindEntryByPath(_scanRoot, path.Trim());
        if (entry is null)
            return false;

        if (entry == CurrentRoot)
            return true;

        if (CurrentRoot is not null)
        {
            PushBack(CurrentRoot);
        }

        _forwardStack.Clear();
        CurrentRoot = entry;
        UpdateBreadcrumb();
        RecomputeLayout();
        return true;
    }

    public void NavigateToExternalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        if (CurrentRoot?.Path is not null && PathsEqual(CurrentRoot.Path, path))
            return;

        if (CurrentRoot is not null)
            PushBack(CurrentRoot);

        _forwardStack.Clear();
        CurrentRoot = CreateExternalEntry(path.Trim());
        UpdateBreadcrumb();
        RecomputeLayoutForCurrentRoot();
    }

    private void PushBack(FileEntry entry)
    {
        if (_navigationStack.Count == 0 || _navigationStack.Peek() != entry)
            _navigationStack.Push(entry);
    }

    private static FileEntry CreateExternalEntry(string path)
    {
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
        var parentPath = Directory.GetParent(normalized)?.FullName;
        var parent = parentPath is null ? null : CreateExternalEntry(parentPath);
        var trimmed = normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        if (string.IsNullOrEmpty(name))
            name = Path.GetPathRoot(normalized)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) ?? normalized;

        var entry = new FileEntry
        {
            Name = name,
            Path = normalized,
            IsDirectory = true,
            Parent = parent,
            Depth = parent is null ? 0 : parent.Depth + 1,
        };

        parent?.Children.Add(entry);
        return entry;
    }

    private static FileEntry? CreateExternalParent(string path)
    {
        try
        {
            var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            var parentPath = Directory.GetParent(normalized)?.FullName;
            return parentPath is null ? null : CreateExternalEntry(parentPath);
        }
        catch
        {
            return null;
        }
    }

    private static FileEntry? FindEntryByPath(FileEntry root, string targetPath)
    {
        if (PathsEqual(root.Path, targetPath))
            return root;

        foreach (var child in root.Children)
        {
            if (child.IsDirectory)
            {
                var found = FindEntryByPath(child, targetPath);
                if (found is not null)
                    return found;
            }
        }

        return null;
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(NormalizeComparablePath(left), NormalizeComparablePath(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeComparablePath(string path)
    {
        try
        {
            var normalized = Path.GetFullPath(path.Trim());
            var root = Path.GetPathRoot(normalized);
            if (!string.IsNullOrEmpty(root) && string.Equals(normalized, root, StringComparison.OrdinalIgnoreCase))
                return root;

            return normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    public void NavigateTo(int breadcrumbIndex)
    {
        if (breadcrumbIndex < 0 || breadcrumbIndex >= BreadcrumbPath.Count - 1)
            return;

        // The breadcrumb list is ordered from the scan root (index 0) to the current root (last index).
        // To navigate to breadcrumbIndex, we pop the navigation stack until we reach
        // the desired level.
        int levelsToGoUp = BreadcrumbPath.Count - 1 - breadcrumbIndex;

        for (int i = 0; i < levelsToGoUp; i++)
        {
            if (_navigationStack.Count == 0)
                break;

            CurrentRoot = _navigationStack.Pop();
        }

        UpdateBreadcrumb();
        RecomputeLayout();
    }

    public void UpdateSize(float width, float height)
    {
        SelectedNode = null;
        ViewWidth = width;
        ViewHeight = height;
        RecomputeLayout();
    }

    /// <summary>同步多选集合（由 TreemapControl 交互驱动）。</summary>
    public void SetMultiSelection(IReadOnlyList<TreemapNode> selection)
    {
        MultiSelection = selection;
    }

    /// <summary>
    /// 定位到指定节点（目录或文件）并返回对应节点，供 focus_path 调用。
    /// 当前地图已有真实 cell 时保留层级；否则进入可展示目标的父层级。交互入口使用异步缓动版本。
    /// </summary>
    public TreemapNode? FocusPath(string path, bool highlight)
    {
        if (string.IsNullOrWhiteSpace(path) || _scanRoot is null) return null;
        var entry = FindFocusEntry(path);
        if (entry is null) return null;

        var visible = Nodes?.FirstOrDefault(n => ReferenceEquals(n.Entry, entry) && n.AggregatedCount == 0 && !entry.IsDeleted);
        if (visible is not null)
        {
            if (highlight) SelectedNode = visible;
            return visible;
        }
        var viewRoot = entry.Parent ?? (entry.IsDirectory ? entry : null);
        if (viewRoot is null || entry.IsDeleted) return null;

        if (viewRoot != CurrentRoot)
        {
            NavigateToEntry(viewRoot);
        }

        // 在重算后的布局里定位目标节点。
        var target = Nodes?.FirstOrDefault(n => ReferenceEquals(n.Entry, entry) && n.AggregatedCount == 0);
        if (target is not null && highlight)
            SelectedNode = target;
        return target;
    }

    public void ApplyCleanupResults(List<CleanupAction> results, ScanSession session)
    {
        if (Services.DeletedEntryState.Apply(session, results))
            OnPropertyChanged(nameof(DeletionRevision));
    }

    // 标记只触发重绘，不替换节点集合，避免选中态、缩放和平移被重置。
    public int DeletionRevision => 0;

    public void ClearDeletionMarks()
    {
        Services.DeletedEntryState.Clear(_scanRoot);
        OnPropertyChanged(nameof(DeletionRevision));
    }

    partial void OnNodesChanged(List<TreemapNode>? value)
    {
        // 隐藏只影响高亮，不能把真实选择偷换为父目录；重新可见时绑定到新几何。
        TreemapNode Remap(TreemapNode selected) => value?.FirstOrDefault(n => ReferenceEquals(n.Entry, selected.Entry)
            && (n.AggregatedCount > 0) == (selected.AggregatedCount > 0)) ?? selected;
        if (SelectedNode is { } selected) SelectedNode = Remap(selected);
        MultiSelection = MultiSelection.Select(Remap).ToArray();
        HoveredNode = null;
    }

    private void RecomputeLayout()
    {
        if (CurrentRoot is not null && !IsInCurrentScan(CurrentRoot))
        {
            Nodes = null;
            return;
        }

        RecomputeLayoutForCurrentRoot();
    }

    private void RecomputeLayoutForCurrentRoot()
    {
        CancelZoomUpdate();
        if (CurrentRoot is null || ViewWidth <= 0 || ViewHeight <= 0)
        {
            Nodes = null;
            return;
        }

        var nodes = ComputeFocusLayout(CurrentRoot, ViewWidth, ViewHeight, LayoutDepth, Zoom, _focusLayoutEntry);
        _colorContext?.Apply(nodes);
        Nodes = nodes;
    }

    /// <summary>更新缩放级别并触发 LOD 重布局（zoom 增大展开更多层级）。</summary>
    public void SetZoom(float zoom)
    {
        var clamped = Math.Clamp(zoom, 1f, 8f);
        if (Math.Abs(clamped - Zoom) < 1e-4f && !IsZoomLayoutPending) return;
        Zoom = clamped;
        RecomputeLayoutForCurrentRoot();
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024L) return $"{bytes} B";
        if (bytes < 1024L * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        if (bytes < 1024L * 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        return $"{bytes / (1024.0 * 1024 * 1024 * 1024):F1} TB";
    }

    private bool IsInCurrentScan(FileEntry entry)
    {
        if (_scanRoot is null)
            return false;

        var current = entry;
        while (current is not null)
        {
            if (current == _scanRoot)
                return true;

            current = current.Parent;
        }

        return false;
    }

    private void UpdateBreadcrumb()
    {
        var path = new List<string>();

        var entry = CurrentRoot;
        while (entry is not null)
        {
            path.Add(entry.Name);
            if (entry == _scanRoot)
                break;
            entry = entry.Parent;
        }

        path.Reverse();
        BreadcrumbPath = path;

        CanNavigateUp = _navigationStack.Count > 0;
        CanGoBack = _navigationStack.Count > 0;
        CanGoForward = _forwardStack.Count > 0;
        CanGoUp = CanNavigateToParentPath(CurrentRoot?.Path);
        NavigateUpCommand.NotifyCanExecuteChanged();
    }

    private static bool CanNavigateToParentPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            var normalized = Path.GetFullPath(path);
            var root = Path.GetPathRoot(normalized);
            return !string.IsNullOrEmpty(root)
                   && !string.Equals(
                       normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                       root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                       StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
