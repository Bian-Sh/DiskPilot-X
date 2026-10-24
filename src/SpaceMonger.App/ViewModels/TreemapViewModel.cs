using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using SpaceMonger.App.Localization;
using SpaceMonger.Core.Enums;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Treemap;
using SpaceMonger.Core.Services.FileTree;

namespace SpaceMonger.App.ViewModels;

public partial class TreemapViewModel : ObservableObject
{
    private readonly ITreemapLayoutEngine _layoutEngine;
    private readonly Stack<FileEntry> _navigationStack = new();
    private readonly Stack<FileEntry> _forwardStack = new();
    private FileEntry? _scanRoot;

    public FileEntry? ScanRoot => _scanRoot;
    private ScanSession? _session;

    [ObservableProperty]
    private FileEntry? _currentRoot;

    [ObservableProperty]
    private TreemapNode? _selectedNode;

    [ObservableProperty]
    private TreemapNode? _hoveredNode;

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

    public TreemapViewModel(ITreemapLayoutEngine layoutEngine)
    {
        _layoutEngine = layoutEngine;
    }

    public void SetRoot(FileEntry root, ScanSession? session = null)
    {
        _scanRoot = root;
        _session = session;
        _navigationStack.Clear();
        _forwardStack.Clear();
        CurrentRoot = root;
        UpdateBreadcrumb();
        RecomputeLayout();
    }

    public void DrillDown(FileEntry folder)
    {
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

        if (CurrentRoot?.Path is not null && string.Equals(CurrentRoot.Path, path, StringComparison.OrdinalIgnoreCase))
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
        var normalized = Path.GetFullPath(path.Trim());
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
            var normalized = Path.GetFullPath(path);
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
        ViewWidth = width;
        ViewHeight = height;
        RecomputeLayout();
    }

    public void ApplyCleanupResults(List<CleanupAction> results, ScanSession session)
    {
        var query = new FileTreeQueryService();
        var removed = new HashSet<FileEntry>();

        foreach (var action in results)
        {
            if (action.Result is not (CleanupResult.Success or CleanupResult.AlreadyRemoved))
                continue;

            // 推荐可能引用过滤副本；只从当前扫描树解析，已移除路径自然不会重复扣减。
            var entry = query.FindByPath(session, action.Recommendation.TargetPath);
            if (entry is null)
                continue;

            var files = 0;
            var folders = 0;
            var pending = new Stack<FileEntry>();
            pending.Push(entry);
            while (pending.TryPop(out var current))
            {
                removed.Add(current);
                if (current.IsDirectory)
                    folders++;
                else
                    files++;

                foreach (var child in current.Children)
                    pending.Push(child);
            }

            var size = entry.Size;
            var parent = entry.Parent;
            parent?.RemoveChild(entry);
            entry.Parent = null;
            session.TotalFiles -= files;
            session.TotalFolders -= folders;
            session.TotalSize -= size;

            if (entry == session.RootEntry)
            {
                session.RootEntry = null;
                _scanRoot = null;
            }

            // 当前目录或其祖先被删除时，返回仍然存在的上级。
            if (CurrentRoot is not null && removed.Contains(CurrentRoot))
                CurrentRoot = parent ?? CreateExternalParent(entry.Path);
        }

        if (removed.Count == 0)
            return;

        if (SelectedNode is not null && removed.Contains(SelectedNode.Entry))
            SelectedNode = null;
        if (HoveredNode is not null && removed.Contains(HoveredNode.Entry))
            HoveredNode = null;

        PruneHistory(_navigationStack);
        PruneHistory(_forwardStack);
        UpdateBreadcrumb();
        RecomputeLayout();

        void PruneHistory(Stack<FileEntry> history)
        {
            var surviving = history.Where(entry => !removed.Contains(entry) && entry != CurrentRoot).Reverse().ToList();
            history.Clear();
            foreach (var entry in surviving)
                history.Push(entry);
        }
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
        if (CurrentRoot is null || ViewWidth <= 0 || ViewHeight <= 0)
        {
            Nodes = null;
            return;
        }

        Nodes = _layoutEngine.ComputeLayout(CurrentRoot, ViewWidth, ViewHeight, maxDepth: 8);
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
