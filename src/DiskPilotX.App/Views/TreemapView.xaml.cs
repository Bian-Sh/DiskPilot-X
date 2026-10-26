using System;
using System.ComponentModel;
using System.IO;
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

    public event Action<FileEntry>? AskAiRequested;

    public TreemapView()
    {
        InitializeComponent();
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
        Treemap.Nodes = _viewModel.Nodes;
        Treemap.SelectedNode = _viewModel.SelectedNode;
        UpdateEmptyState();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
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
        var contextMenu = BuildContextMenu(entry);
        contextMenu.PlacementTarget = Treemap;
        contextMenu.IsOpen = true;
    }

    private ContextMenu BuildContextMenu(FileEntry entry)
    {
        var contextMenu = new ContextMenu();

        var askAiItem = new MenuItem { Header = "询问 AI" };
        askAiItem.Click += (_, _) => AskAiRequested?.Invoke(entry);
        contextMenu.Items.Add(askAiItem);
        contextMenu.Items.Add(new Separator());

        var openItem = new MenuItem { Header = entry.IsDirectory ? "打开文件夹" : "打开" };
        openItem.Click += async (_, _) => await RunShellActionAsync(() => FileEntryShellService.Open(entry));
        contextMenu.Items.Add(openItem);

        var openInExplorerItem = new MenuItem { Header = L.Text("OpenInExplorerMenu") };
        openInExplorerItem.Click += async (_, _) => await RunShellActionAsync(() => FileEntryShellService.ShowInExplorer(entry));
        contextMenu.Items.Add(openInExplorerItem);

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

        var propertiesItem = new MenuItem { Header = L.Text("PropertiesMenu") };
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









