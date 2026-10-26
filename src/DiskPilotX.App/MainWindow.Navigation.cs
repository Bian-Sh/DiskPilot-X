using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Data;
using Microsoft.Extensions.DependencyInjection;
using DiskPilotX.App.Diagnostics;
using DiskPilotX.App.Helpers;
using DiskPilotX.App.Localization;
using DiskPilotX.App.Converters;
using DiskPilotX.App.ViewModels;
using DiskPilotX.App.Views;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Scanning;
using DiskPilotX.Core.Services.Cleanup;

namespace DiskPilotX.App;

public partial class MainWindow
{
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _treemapViewModel?.NavigateUp();
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            CommitPathEditText();
            if (DataContext is MainViewModel vm && vm.ScanCommand.CanExecute(null))
                vm.ScanCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.A && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            OnAnalyzeRequested();
            e.Handled = true;
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        _treemapViewModel?.NavigateBack();
    }

    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        _treemapViewModel?.NavigateForward();
    }

    private void UpButton_Click(object sender, RoutedEventArgs e)
    {
        _treemapViewModel?.NavigateToParent();
    }

    private void PathEditTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            CommitPathEditText();
            e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.Escape)
        {
            SwitchToBreadcrumbMode();
            e.Handled = true;
        }
    }

    private void ScanButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        CommitPathEditText();
    }

    private void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        CommitPathEditText();
    }

    private void CommitPathEditText()
    {
        if (PathEditTextBox.Visibility != Visibility.Visible)
            return;

        var path = PathEditTextBox.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(path))
        {
            NavigateToPathOrSelect(path);
        }
        SwitchToBreadcrumbMode();
    }

    private void PathEditTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        ExitPathEditMode();
    }

    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (PathEditTextBox.Visibility != Visibility.Visible)
            return;

        if (IsOriginalSourceWithin(e.OriginalSource, PathEditTextBox)
            || IsOriginalSourceWithin(e.OriginalSource, BrowseButton))
        {
            return;
        }

        if (IsOriginalSourceWithin(e.OriginalSource, ScanButton))
        {
            CommitPathEditText();
            if (DataContext is MainViewModel vm && vm.ScanCommand.CanExecute(null))
                vm.ScanCommand.Execute(null);
            e.Handled = true;
            return;
        }

        ExitPathEditMode();
        Keyboard.ClearFocus();
    }

    private void AddressBar_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (PathEditTextBox.Visibility == Visibility.Visible)
            return;

        // Only switch to edit mode if the click was on the container itself, not on breadcrumb buttons
        var source = e.OriginalSource as DependencyObject;
        while (source is not null)
        {
            if (source is Button)
                return; // Click was on a breadcrumb segment button
            if (source == sender)
                break;
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }

        if (BreadcrumbBar.Visibility == Visibility.Visible)
        {
            SwitchToEditMode();
        }
    }

    private static bool IsOriginalSourceWithin(object originalSource, DependencyObject target)
    {
        var source = originalSource as DependencyObject;
        while (source is not null)
        {
            if (source == target)
                return true;
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }
        return false;
    }
    private void SwitchToEditMode()
    {
        var path = ResolveDisplayPath(_treemapViewModel?.CurrentRoot?.Path
                                      ?? (DataContext as MainViewModel)?.SelectedPath);
        if (path is not null)
        {
            PathEditTextBox.Text = path;
        }
        BreadcrumbBar.Visibility = Visibility.Collapsed;
        PathEditTextBox.Visibility = Visibility.Visible;
        PathEditTextBox.Focus();
        PathEditTextBox.SelectAll();
    }

    private void SwitchToBreadcrumbMode()
    {
        ExitPathEditMode();
    }

    private void ExitPathEditMode()
    {
        if (PathEditTextBox.Visibility != Visibility.Visible)
            return;

        PathEditTextBox.Visibility = Visibility.Collapsed;
        BreadcrumbBar.Visibility = Visibility.Visible;
        RebuildBreadcrumbBar();
    }

    private bool _rebuildingBreadcrumbs;


    private void OnAppLanguageChanged()
    {
        Dispatcher.InvokeAsync(() => RebuildBreadcrumbBar());
    }

    private void RebuildBreadcrumbBar()
    {
        if (_rebuildingBreadcrumbs)
            return;

        _rebuildingBreadcrumbs = true;
        try
        {
            BreadcrumbBar.Children.Clear();

            var currentPath = ResolveDisplayPath(_displayPathOverride ?? _treemapViewModel?.CurrentRoot?.Path);
            if (string.IsNullOrEmpty(currentPath))
            {
                currentPath = ResolveDisplayPath((DataContext as MainViewModel)?.SelectedPath);
            }
            if (string.IsNullOrEmpty(currentPath))
                return;

            var segments = BuildBreadcrumbSegments(_treemapViewModel?.CurrentRoot, currentPath);

            for (int i = 0; i < segments.Count; i++)
            {
                if (i > 0)
                {
                    var owner = segments[i - 1];
                    var sepBtn = new Button
                    {
                        Content = new TextBlock
                        {
                            Text = "›",
                            FontSize = 18,
                            FontWeight = FontWeights.SemiBold,
                            Opacity = 0.95,
                            VerticalAlignment = VerticalAlignment.Center,
                            TextAlignment = TextAlignment.Center,
                            RenderTransform = new TranslateTransform(0, -2),
                        },
                        Background = Brushes.Transparent,
                        BorderThickness = new Thickness(0),
                        Foreground = (SolidColorBrush)FindResource("VP.TextPrimaryBrush"),
                        Style = (Style)FindResource("VP.BreadcrumbButton"),
                        Padding = new Thickness(5, 0, 5, 0),
                        Cursor = Cursors.Hand,
                        Tag = owner,
                        VerticalContentAlignment = VerticalAlignment.Center,
                    };
                    var sepMenu = new ContextMenu();
                    sepMenu.Opened += BreadcrumbDropdown_Opened;
                    sepBtn.ContextMenu = sepMenu;
                    sepBtn.Click += BreadcrumbChevron_Click;
                    // style handles hover visuals
                    BreadcrumbBar.Children.Add(sepBtn);
                }

                var segment = segments[i];
                string segPath = segment.path;
                string segName = segment.name;

                // ── Name button: click to navigate ──
                var nameButton = new Button
                {
                    Content = new TextBlock
                    {
                        Text = segName,
                        FontSize = 13,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Foreground = (SolidColorBrush)FindResource("VP.TextPrimaryBrush"),
                    Padding = new Thickness(4, 2, 2, 2),
                    Style = (Style)FindResource("VP.BreadcrumbButton"),
                    Cursor = Cursors.Hand,
                    Tag = segment,
                };
                nameButton.Click += BreadcrumbSegment_Click;
                // style handles hover visuals
                BreadcrumbBar.Children.Add(nameButton);
            }

            // Trailing › chevron (shows children of current folder)
            if (segments.Count > 0)
            {
                var trailBtn = new Button
                {
                    Content = new TextBlock
                    {
                        Text = "›",
                        FontSize = 18,
                        FontWeight = FontWeights.SemiBold,
                        Opacity = 0.95,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextAlignment = TextAlignment.Center,
                            RenderTransform = new TranslateTransform(0, -2),
                    },
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Foreground = (SolidColorBrush)FindResource("VP.TextPrimaryBrush"),
                    Padding = new Thickness(5, 0, 5, 0),
                    Style = (Style)FindResource("VP.BreadcrumbButton"),
                    Cursor = Cursors.Hand,
                    Tag = segments[^1],
                    VerticalContentAlignment = VerticalAlignment.Center,
                };
                var trailMenu = new ContextMenu();
                trailMenu.Opened += BreadcrumbDropdown_Opened;
                trailBtn.ContextMenu = trailMenu;
                trailBtn.Click += BreadcrumbChevron_Click;
                // style handles hover visuals
                BreadcrumbBar.Children.Add(trailBtn);
            }
        }
        finally
        {
            _rebuildingBreadcrumbs = false;
        }
    }

    private const string ThisPCSentinel = "::thispc::";

    private static string ThisPC => L.Text("ThisPCLabel");

    private static string? ResolveDisplayPath(string? path)
    {
        return ScanPathResolver.TryResolve(path, out var resolvedPath, out _)
            ? resolvedPath
            : path?.Trim();
    }

    private List<(string path, string name)> ParsePathSegments(string fullPath)
    {
        var result = new List<(string path, string name)>();
        if (string.IsNullOrEmpty(fullPath))
            return result;

        // Always start with "此电脑" (This PC) — Windows 11 Explorer style
        result.Add((ThisPCSentinel, ThisPC));

        var parts = fullPath.Split(System.IO.Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return result;

        // Drive root (e.g., "C:")
        string rootPart = parts[0];
        result.Add(($"{rootPart}{System.IO.Path.DirectorySeparatorChar}", rootPart));

        var accumulated = $"{rootPart}{System.IO.Path.DirectorySeparatorChar}";
        for (int i = 1; i < parts.Length; i++)
        {
            accumulated += parts[i];
            if (i < parts.Length - 1)
                accumulated += System.IO.Path.DirectorySeparatorChar;
            result.Add((accumulated, parts[i]));
        }

        return result;
    }


    private List<(string path, string name, FileEntry? entry)> BuildBreadcrumbSegments(FileEntry? currentRoot, string fullPath)
    {
        if (currentRoot is null || !string.Equals(currentRoot.Path, fullPath, StringComparison.OrdinalIgnoreCase))
            return ParsePathSegments(fullPath).Select(s => (s.path, s.name, (FileEntry?)null)).ToList();

        var parsedSegments = ParsePathSegments(fullPath);
        var entries = new Stack<FileEntry>();
        for (var entry = currentRoot; entry is not null; entry = entry.Parent)
            entries.Push(entry);

        if (entries.Count < parsedSegments.Count - 1)
        {
            return parsedSegments
                .Select(s => (s.path, s.name, BreadcrumbPathsEqual(s.path, currentRoot.Path) ? currentRoot : null))
                .ToList();
        }

        var result = new List<(string path, string name, FileEntry? entry)> { (ThisPCSentinel, ThisPC, null) };
        foreach (var entry in entries)
            result.Add((entry.Path, string.IsNullOrWhiteSpace(entry.Name) ? entry.Path : entry.Name, entry));

        return result;
    }

    private static bool BreadcrumbPathsEqual(string left, string right)
    {
        return string.Equals(
            left.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar),
            right.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }
    private void BreadcrumbSegment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ValueTuple<string, string, FileEntry?> segment)
        {
            var path = segment.Item1;
            var entry = segment.Item3;

            if (path == ThisPCSentinel)
            {
                e.Handled = true;
                return;
            }

            if (_treemapViewModel?.CurrentRoot?.Path == path)
            {
                e.Handled = true;
                return;
            }

            NavigateToEntryOrPath(entry, path);
        }
        e.Handled = true;
    }

    private void BreadcrumbChevron_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu is ContextMenu cm)
        {
            cm.PlacementTarget = btn;
            cm.IsOpen = true;
        }
        e.Handled = true;
    }

    /// <summary>
    /// Populates a breadcrumb chevron ContextMenu on open.
    /// Uses ItemsSource + VirtualizingStackPanel for smooth scrolling and O(visible) perf
    /// even with hundreds of subfolders — matches Windows 11 Explorer flyout behavior.
    /// </summary>
    private void BreadcrumbDropdown_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
            return;

        // Detach previous ItemsSource so we can repopulate
        menu.ItemsSource = null;

        // Cap dropdown height: ~12 items or 70% of screen, whichever is smaller
        double itemHeight = 32;
        double maxItems = 12;
        double screenMax = System.Windows.SystemParameters.WorkArea.Height * 0.7;
        menu.MaxHeight = Math.Min(itemHeight * maxItems, screenMax);

        // Ensure virtualizing panel for perf with long lists
        menu.ItemsPanel = s_breadcrumbItemsPanel;

        // One-time template + style setup (lazy, cached on first call)
        EnsureBreadcrumbMenuTemplate(menu);

        // Discover target directory
        string? dirPath = null;
        FileEntry? dirEntry = null;
        if (menu.PlacementTarget is FrameworkElement fe)
        {
            if (fe.Tag is ValueTuple<string, string, FileEntry?> segment)
            {
                dirPath = segment.Item1;
                dirEntry = segment.Item3;
            }
            else if (fe.Tag is string tagPath)
            {
                dirPath = tagPath;
            }
        }

        if (string.IsNullOrEmpty(dirPath))
            return;

        List<BreadcrumbItem> items;

        // ── "此电脑" → list drives ──
        if (dirPath == ThisPCSentinel)
        {
            items = DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .Select(d => new BreadcrumbItem(d.Name, d.Name))
                .ToList();
        }
        else
        {
            // ── Try scanned tree first ──
            dirEntry ??= FindEntryByPathInTree(_treemapViewModel?.ScanRoot, dirPath);
            if (dirEntry is not null)
            {
                var children = dirEntry.Children
                    .Where(c => c.IsDirectory)
                    .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (children.Count > 0)
                {
                    items = children.Select(c => new BreadcrumbItem(c.Name, c.Path, c)).ToList();
                    menu.ItemsSource = items;
                    return;
                }
                // Empty scan results → fall through to filesystem
            }

            // ── Filesystem fallback ──
            try
            {
                items = System.IO.Directory.GetDirectories(dirPath)
                    .Select(d => new BreadcrumbItem(System.IO.Path.GetFileName(d), d))
                    .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception)
            {
                items = new List<BreadcrumbItem>();
            }
        }

        // Empty state
        if (items.Count == 0)
        {
            items.Add(new BreadcrumbItem(L.Text("NoSubfoldersText"), ""));
        }

        menu.ItemsSource = items;
    }

    /// <summary>Shared VirtualizingStackPanel template — avoids allocating per-dropdown.</summary>
    private static readonly ItemsPanelTemplate s_breadcrumbItemsPanel =
        new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));

    /// <summary>Lazy-init ItemTemplate + ItemContainerStyle for breadcrumb ContextMenus.</summary>
    private void EnsureBreadcrumbMenuTemplate(ContextMenu menu)
    {
        if (menu.ItemTemplate is not null)
            return; // already set

        // ItemTemplate: simple TextBlock bound to BreadcrumbItem.Name
        var textFactory = new FrameworkElementFactory(typeof(TextBlock));
        textFactory.SetBinding(TextBlock.TextProperty, new Binding("Name"));
        textFactory.SetValue(TextBlock.PaddingProperty, new Thickness(8, 6, 8, 6));
        textFactory.SetValue(TextBlock.FontSizeProperty, 13.0);
        textFactory.SetValue(TextBlock.FontFamilyProperty, (System.Windows.Media.FontFamily)FindResource("VP.FontFamily"));
        menu.ItemTemplate = new DataTemplate(typeof(BreadcrumbItem)) { VisualTree = textFactory };

        // ItemContainerStyle: hover highlight + click handler
        var style = new Style(typeof(MenuItem));

        // Hover background
        var hoverTrigger = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(MenuItem.BackgroundProperty, FindResource("VP.SurfaceHoverBrush")));
        style.Triggers.Add(hoverTrigger);

        // Disabled items (empty state with Path = "")
        var disabledTrigger = new DataTrigger
        {
            Binding = new Binding("Path"),
            Value = ""
        };
        disabledTrigger.Setters.Add(new Setter(MenuItem.IsEnabledProperty, false));
        style.Triggers.Add(disabledTrigger);

        // Click → navigate
        var clickSetter = new EventSetter(MenuItem.ClickEvent, new RoutedEventHandler((s, args) =>
        {
            if (s is MenuItem mi && mi.DataContext is BreadcrumbItem bi && !string.IsNullOrEmpty(bi.Path))
                NavigateToEntryOrPath(bi.Entry, bi.Path);
        }));
        style.Setters.Add(clickSetter);

        menu.ItemContainerStyle = style;
    }

    /// <summary>
    /// Navigate to a path: use treemap navigation if scan data exists,
    /// otherwise update SelectedPath for breadcrumb display.
    /// </summary>
    private void NavigateToPathOrSelect(string path, bool updateSelectedPath = true)
    {
        path = ResolveDisplayPath(path) ?? path;
        _displayPathOverride = null;

        if (_treemapViewModel is not null)
        {
            if (TreemapView.NavigateToPath(path))
            {
                UpdateSelectedPathFromNavigation(path, updateSelectedPath);
                return;
            }

            _treemapViewModel.NavigateToExternalPath(path);
            _displayPathOverride = path;
        }

        UpdateSelectedPathFromNavigation(path, updateSelectedPath);
        RebuildBreadcrumbBar();
    }


    private void NavigateToEntryOrPath(FileEntry? entry, string path, bool updateSelectedPath = true)
    {
        if (entry is not null && _treemapViewModel?.NavigateToEntry(entry) == true)
        {
            UpdateSelectedPathFromNavigation(entry.Path, updateSelectedPath);
            return;
        }

        NavigateToPathOrSelect(path, updateSelectedPath);
    }
    private void UpdateSelectedPathFromNavigation(string path, bool updateSelectedPath)
    {
        if (!updateSelectedPath || DataContext is not MainViewModel mainVm)
            return;

        if (string.Equals(mainVm.SelectedPath, path, StringComparison.OrdinalIgnoreCase))
            return;

        _suppressSelectedPathNavigation = true;
        try
        {
            mainVm.SelectedPath = path;
        }
        finally
        {
            _suppressSelectedPathNavigation = false;
        }
    }

    private static DiskPilotX.Core.Models.FileEntry? FindEntryByPathInTree(
        DiskPilotX.Core.Models.FileEntry? root, string targetPath)
    {
        if (root is null)
            return null;
        if (string.Equals(root.Path, targetPath, StringComparison.OrdinalIgnoreCase))
            return root;
        foreach (var child in root.Children)
        {
            if (child.IsDirectory)
            {
                var found = FindEntryByPathInTree(child, targetPath);
                if (found is not null)
                    return found;
            }
        }
        return null;
    }

}
