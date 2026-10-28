using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using DiskPilotX.App.Services;
using System.Windows.Controls;
using System.Windows.Input;
using DiskPilotX.App.Localization;
using System.Windows.Media;
using DiskPilotX.App.Controls;
using DiskPilotX.App.Converters;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Models;

namespace DiskPilotX.App.Views;

public partial class TreemapView : UserControl
{
    private TreemapViewModel? _viewModel;
    private bool _isScanning;

    public Func<string, string, MessageBoxButton, MessageBoxImage, Task<MessageBoxResult>>? ShowMessageAsync { get; set; }

    public Func<FrameworkElement, double, double, Task<bool?>>? ShowContentAsync { get; set; }

    public Action<object?>? CloseContentModal { get; set; }

    public Func<FileEntry, Task>? DeleteEntryAsync { get; set; }

    public event Action<FileEntry>? AskAiRequested;

    /// <summary>用户请求把多选集合引用到 AI 对话框。参数为去重后的顶层条目。</summary>
    public event Action<IReadOnlyList<FileEntry>>? MultiSelectionReferenced;

    /// <summary>当前多选条目（父目录吸收去重后的顶层集合）。</summary>
    public IReadOnlyList<FileEntry> ReferencedEntries
    {
        get
        {
            var selected = _viewModel?.MultiSelection ?? Array.Empty<TreemapNode>();
            return CollapseToTopLevel(selected.Select(n => n.Entry).ToList());
        }
    }

    private static IReadOnlyList<FileEntry> CollapseToTopLevel(List<FileEntry> entries)
    {
        if (entries.Count == 0) return entries;
        var top = new List<FileEntry>();
        foreach (var e in entries)
        {
            bool covered = entries.Any(other => other != e && other.IsDirectory
                && IsUnderPath(e.Path, other.Path));
            if (!covered) top.Add(e);
        }
        return top;
    }

    public TreemapView()
    {
        InitializeComponent();
        // 事件只绑定一次，替换 ViewModel 不累积旧委托。
        Treemap.MultiSelectionChanged += (_, _) => _viewModel?.SetMultiSelection(Treemap.MultiSelection.ToList());
        Treemap.ZoomChanged += (_, zoom) => _viewModel?.SetZoom(zoom);
        Treemap.RootNavigationRequested += (_, entry) => _viewModel?.NavigateToEntry(entry);

    }

    public void SetViewModel(TreemapViewModel vm)
    {
        // Unsubscribe from previous view model if any
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }

        _viewModel = vm;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;

        // Sync initial state
        Treemap.SetViewRoot(_viewModel.CurrentRoot, _viewModel.ScanRoot);
        Treemap.Nodes = _viewModel.Nodes;
        Treemap.SelectedNode = _viewModel.SelectedNode;
        UpdateEmptyState();


    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TreemapViewModel.DeletionRevision):
                Treemap.InvalidateVisual();
                break;
            case nameof(TreemapViewModel.CurrentRoot):
                Treemap.SetViewRoot(_viewModel?.CurrentRoot, _viewModel?.ScanRoot);
                break;
            case nameof(TreemapViewModel.SelectedNode):
                Treemap.SelectedNode = _viewModel?.SelectedNode;
                break;
            case nameof(TreemapViewModel.Nodes):
                Treemap.Nodes = _viewModel?.Nodes;
                UpdateEmptyState();
                break;
        }
    }

    public void SetScanningState(bool isScanning, string? titleText, string? progressText)
    {
        _isScanning = isScanning;
        ScanningOverlay.Visibility = isScanning ? Visibility.Visible : Visibility.Collapsed;
        ScanTitleText.Text = string.IsNullOrWhiteSpace(titleText) ? L.Text("ScanningStatus") : titleText;
        ScanProgressText.Text = progressText ?? string.Empty;
        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        bool hasData = _viewModel?.Nodes is { Count: > 0 };
        bool showEmpty = !_isScanning && !hasData;

        EmptyStatePanel.Visibility = showEmpty ? Visibility.Visible : Visibility.Collapsed;
        if (showEmpty)
        {
            var path = _viewModel?.CurrentRoot?.Path;
            bool hasScan = _viewModel?.ScanRoot is not null;
            bool outsideScan = hasScan && !string.IsNullOrWhiteSpace(path) && !IsUnderPath(path, _viewModel!.ScanRoot!.Path);
            bool insideScan = hasScan && !outsideScan;

            EmptyStateTitle.Text = outsideScan
                ? L.Text("TreemapAnalysisRequiredTitle")
                : insideScan
                    ? L.Text("TreemapNoChildDataTitle")
                    : L.Text("TreemapEmptyTitle");

        }

        // Hide opaque SkiaSharp canvas when empty (Opacity=0 keeps element in visual tree)
        Treemap.Opacity = hasData ? 1.0 : 0.0;
    }

    private static bool IsUnderPath(string path, string rootPath)
    {
        try
        {
            var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullRoot = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase)
                   || fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public bool NavigateToPath(string path)
    {
        return _viewModel?.NavigateToPath(path) == true;
    }

    /// <summary>定位到指定路径并高亮，供 AI focus_path 调用。返回是否命中。</summary>
    public bool FocusPath(string path, bool highlight)
    {
        var node = _viewModel?.FocusPath(path, highlight);
        if (node is null) return false;
        Treemap.FocusNode(node, highlight);
        return true;
    }

    private void Treemap_NodeClicked(object? sender, TreemapNode node)
    {
        if (_viewModel is not null) _viewModel.SelectedNode = node;
    }

    private void Treemap_NodeActivated(object? sender, TreemapNode node)
    {
        if (node.Entry.IsDirectory)
        {
            _viewModel?.DrillDown(node.Entry);
        }
    }

    private void Treemap_NodeHovered(object? sender, TreemapNode? node)
    {
        if (_viewModel is not null)
        {
            _viewModel.HoveredNode = node;
        }
    }

    private void Treemap_NodeRightClicked(object? sender, TreemapNode node)
    {
        var entry = node.Entry;
        var contextMenu = BuildNodeContextMenu(node);
        contextMenu.PlacementTarget = Treemap;
        contextMenu.IsOpen = true;
    }

    private ContextMenu BuildNodeContextMenu(TreemapNode node)
    {
        var menu = BuildContextMenu(node.Entry);
        if (node.AggregatedCount > 0)
        {
            var deletion = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, L.Text("DeleteEntryMenu")));
            deletion.IsEnabled = false;
            deletion.ToolTip = "聚合块不是独立文件或文件夹，请进入目录后选择具体项目。";
            ToolTipService.SetShowOnDisabled(deletion, true);
        }
        return menu;
    }

    internal ContextMenu BuildContextMenu(FileEntry entry)
    {
        var contextMenu = new ContextMenu();

        // 多选非空时提供「引用到对话框」。
        if (ReferencedEntries.Count > 1)
        {
            var refItem = new MenuItem { Header = $"引用 {ReferencedEntries.Count} 项到对话框" };
            refItem.Click += (_, _) => MultiSelectionReferenced?.Invoke(ReferencedEntries);
            contextMenu.Items.Add(refItem);
            contextMenu.Items.Add(new Separator());
        }

        var askAiItem = new MenuItem { Header = "询问 AI" };
        askAiItem.Click += (_, _) => AskAiRequested?.Invoke(entry);
        contextMenu.Items.Add(askAiItem);
        contextMenu.Items.Add(new Separator());

        if (!entry.IsDirectory)
        {
            var openItem = new MenuItem { Header = L.Text("OpenFileMenu"), IsEnabled = !entry.IsDeleted };
            openItem.Click += async (_, _) => await RunShellActionAsync(() => FileEntryShellService.Open(entry));
            contextMenu.Items.Add(openItem);
        }

        var openInExplorerItem = new MenuItem { Header = L.Text(entry.IsDirectory ? "ShowFolderInExplorerMenu" : "ShowFileInExplorerMenu"), IsEnabled = !entry.IsDeleted };
        openInExplorerItem.Click += async (_, _) => await RunShellActionAsync(() => FileEntryShellService.ShowInExplorer(entry));
        contextMenu.Items.Add(openInExplorerItem);

        var deleteItem = new MenuItem { Header = L.Text("DeleteEntryMenu"), IsEnabled = !entry.IsDeleted && DeleteEntryAsync is not null };
        deleteItem.Click += async (_, _) =>
        {
            if (!entry.IsDeleted && DeleteEntryAsync is not null) await DeleteEntryAsync(entry);
        };
        contextMenu.Items.Add(deleteItem);

        var copyPathItem = new MenuItem { Header = L.Text("CopyPathMenu") };
        copyPathItem.Click += async (_, _) =>
        {
            try
            {
                Clipboard.SetText(entry.Path);
            }
            catch (Exception ex)
            {
                await ShowErrorAsync(L.Format("CopyPathFailedMessage", ex.Message));
            }
        };
        contextMenu.Items.Add(copyPathItem);
        contextMenu.Items.Add(new Separator());

        var propertiesItem = new MenuItem { Header = L.Text("PropertiesMenu"), IsEnabled = !entry.IsDeleted };
        propertiesItem.Click += async (_, _) => await RunShellActionAsync(() => FileEntryShellService.ShowProperties(entry));
        contextMenu.Items.Add(propertiesItem);

        return contextMenu;
    }

    private async Task RunShellActionAsync(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(L.Format("OpenExplorerFailedMessage", ex.Message));
        }
    }

    private async Task ShowErrorAsync(string message)
    {
        if (ShowMessageAsync is not null)
        {
            await ShowMessageAsync(message, "DiskPilot-X", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        await Dispatcher.InvokeAsync(() => MessageBox.Show(Window.GetWindow(this), message, "DiskPilot-X", MessageBoxButton.OK, MessageBoxImage.Error));
    }
    private void TreemapContainer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // WPF Border.ClipToBounds clips to rectangle, not CornerRadius.
        // Apply explicit Clip matching CornerRadius="0,0,10,10" (bottom corners only).
        var w = TreemapContainer.ActualWidth;
        var h = TreemapContainer.ActualHeight;
        if (w > 0 && h > 0)
        {
            const double r = 10.0;
            var figure = new PathFigure
            {
                StartPoint = new Point(0, 0),
                IsClosed = true,
                IsFilled = true,
            };
            figure.Segments.Add(new LineSegment(new Point(w, 0), true));
            figure.Segments.Add(new LineSegment(new Point(w, h - r), true));
            figure.Segments.Add(new ArcSegment(new Point(w - r, h), new Size(r, r), 0, false, SweepDirection.Clockwise, true));
            figure.Segments.Add(new LineSegment(new Point(r, h), true));
            figure.Segments.Add(new ArcSegment(new Point(0, h - r), new Size(r, r), 0, false, SweepDirection.Clockwise, true));

            var geometry = new PathGeometry(new[] { figure });
            geometry.Freeze();
            TreemapContainer.Clip = geometry;
        }
    }

    private void Treemap_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        _viewModel?.UpdateSize((float)e.NewSize.Width, (float)e.NewSize.Height);
    }
}









