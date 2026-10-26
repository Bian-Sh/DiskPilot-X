using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DiskPilotX.App.Localization;
using DiskPilotX.App.Services;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Models;

namespace DiskPilotX.App.Views;

public partial class TreeViewControl : UserControl
{
    private const double TreeRowHeight = 24.0;

    public event Action<FileEntry>? AskAiRequested;
    public event Action<FileEntry>? EntrySelected;
    private ScrollViewer? _headerScrollViewer;
    private bool _suppressEntrySelected;

    public TreeViewControl()
    {
        InitializeComponent();
        Loaded += TreeViewControl_Loaded;
    }

    private void TreeViewControl_Loaded(object? sender, System.Windows.RoutedEventArgs e)
    {
        // subscribe to any ScrollViewer.ScrollChanged routed events from within the TreeView
        // this is more robust than finding a descendant ScrollViewer directly
        FileTreeView.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(TreeScroll_ScrollChanged));

        // avoid depending on generated field from XAML; resolve by name
        _headerScrollViewer = FindName("HeaderScrollViewer") as ScrollViewer;
    }

    private bool _syncing;

    private void TreeHeaderSplitter_DragDelta(object sender, DragDeltaEventArgs e)
    {
        SyncHeaderToTreeScroll();
    }

    private void TreeHeaderSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        SyncHeaderToTreeScroll();
    }

    private void TreeScroll_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_syncing) return;

        SyncHeaderToTreeScroll();
    }

    private void SyncHeaderToTreeScroll()
    {
        if (_syncing) return;

        try
        {
            if (_headerScrollViewer is null || HeaderContentGrid is null || HeaderSpacerColumn is null || HeaderScrollbarGutterColumn is null) return;

            var treeSv = FindDescendant<ScrollViewer>(FileTreeView);
            if (treeSv is null) return;

            double treeExtent = treeSv.ExtentWidth;
            if (treeExtent <= 0) return;

            double rightViewportInset = treeSv.ComputedVerticalScrollBarVisibility == Visibility.Visible
                ? Math.Max(0, treeSv.ActualWidth - treeSv.ViewportWidth)
                : 0;
            if (Math.Abs(HeaderScrollbarGutterColumn.ActualWidth - rightViewportInset) > 0.1)
            {
                HeaderScrollbarGutterColumn.Width = new GridLength(rightViewportInset);
            }

            // Sum the ActualWidth of the 9 content columns (col0–col8)
            double contentWidth = 0;
            int dataColCount = HeaderContentGrid.ColumnDefinitions.Count - 1; // exclude spacer
            for (int i = 0; i < dataColCount; i++)
            {
                contentWidth += HeaderContentGrid.ColumnDefinitions[i].ActualWidth;
            }

            // Set spacer to fill the gap so header content matches tree extent width
            double spacerWidth = Math.Max(0, treeExtent - contentWidth);
            HeaderSpacerColumn.Width = new GridLength(spacerWidth);

            // Defer offset sync to after layout processes the spacer change
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (_headerScrollViewer is null) return;

                    _syncing = true;
                    _headerScrollViewer.ScrollToHorizontalOffset(treeSv.HorizontalOffset);
                    _syncing = false;
                }
                catch { _syncing = false; }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
        catch { }
    }

    private void TreeViewItem_Selected(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem item && item.DataContext is TreeViewItemViewModel vm)
        {
            if (DataContext is TreeViewModel dataContext)
            {
                dataContext.SelectedItem = vm;
            }

            if (!_suppressEntrySelected)
            {
                EntrySelected?.Invoke(vm.Entry);
            }

            e.Handled = true;
        }
    }

    public void SelectEntry(FileEntry entry)
    {
        if (DataContext is not TreeViewModel viewModel)
            return;

        _suppressEntrySelected = true;
        try
        {
            viewModel.SelectEntry(entry);
        }
        finally
        {
            _suppressEntrySelected = false;
        }

        Dispatcher.BeginInvoke(new Action(() => SnapSelectedItemToTop(viewModel.SelectedItem)), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void SnapSelectedItemToTop(TreeViewItemViewModel? selectedItem, bool retry = true)
    {
        if (selectedItem is null)
            return;

        var scrollViewer = FindDescendant<ScrollViewer>(FileTreeView);
        if (scrollViewer is null)
            return;

        var visibleIndex = GetVisibleIndex(selectedItem);
        if (visibleIndex >= 0)
        {
            scrollViewer.ScrollToVerticalOffset(visibleIndex * TreeRowHeight);
        }

        FileTreeView.UpdateLayout();
        var item = FindTreeViewItem(FileTreeView, selectedItem);
        if (item is null)
        {
            if (retry)
            {
                Dispatcher.BeginInvoke(new Action(() => SnapSelectedItemToTop(selectedItem, retry: false)), System.Windows.Threading.DispatcherPriority.ContextIdle);
            }

            return;
        }

        var top = item.TransformToAncestor(scrollViewer).Transform(new Point(0, 0)).Y;
        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + top);
    }

    internal object GetAcceptanceState()
    {
        var selectedItem = (DataContext as TreeViewModel)?.SelectedItem;
        var scrollViewer = FindDescendant<ScrollViewer>(FileTreeView);
        var container = selectedItem is null ? null : FindTreeViewItem(FileTreeView, selectedItem);
        double? selectedTop = null;

        if (container is not null && scrollViewer is not null)
        {
            selectedTop = container.TransformToAncestor(scrollViewer).Transform(new Point(0, 0)).Y;
        }

        return new
        {
            SelectedPath = selectedItem?.Entry.Path,
            VerticalOffset = scrollViewer?.VerticalOffset,
            SelectedTop = selectedTop,
        };
    }

    private static int GetVisibleIndex(TreeViewItemViewModel selectedItem)
    {
        var root = selectedItem;
        while (root.Parent is not null)
        {
            root = root.Parent;
        }

        var index = 0;
        return FindVisibleIndex(root, selectedItem, ref index);
    }

    private static int FindVisibleIndex(TreeViewItemViewModel current, TreeViewItemViewModel selectedItem, ref int index)
    {
        if (ReferenceEquals(current, selectedItem))
            return index;

        index++;

        if (!current.IsExpanded)
            return -1;

        foreach (var child in current.Children)
        {
            var found = FindVisibleIndex(child, selectedItem, ref index);
            if (found >= 0)
                return found;
        }

        return -1;
    }

    private void TreeViewItem_MouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var item = FindParent<TreeViewItem>((DependencyObject)e.OriginalSource);
        if (item?.DataContext is not TreeViewItemViewModel vm)
        {
            return;
        }

        item.Focus();
        item.IsSelected = true;
        e.Handled = true;

        if (DataContext is TreeViewModel dataContext)
        {
            dataContext.SelectedItem = vm;
        }

        var menu = BuildContextMenu(vm.Entry);
        // Defer opening until after the mouse event completes to avoid
        // the menu closing immediately (opening on MouseDown can be
        // followed by mouse-up processing that dismisses it).
        menu.PlacementTarget = item;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        Dispatcher.BeginInvoke(() => menu.IsOpen = true);
    }
    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        var current = child;
        while (current is not null)
        {
            if (current is T typed)
            {
                return typed;
            }

            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }

        return null;
    }
    private static T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent == null) return null;

        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) return typed;
            var result = FindDescendant<T>(child);
            if (result != null) return result;
        }

        return null;
    }
    private static TreeViewItem? FindTreeViewItem(ItemsControl parent, object item)
    {
        var container = parent.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
        if (container is not null)
            return container;

        foreach (var childItem in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(childItem) is not TreeViewItem childContainer)
                continue;

            childContainer.ApplyTemplate();
            childContainer.UpdateLayout();
            var found = FindTreeViewItem(childContainer, item);
            if (found is not null)
                return found;
        }

        return null;
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
        await Dispatcher.InvokeAsync(() => MessageBox.Show(Window.GetWindow(this), message, "DiskPilot-X", MessageBoxButton.OK, MessageBoxImage.Error));
    }
}








