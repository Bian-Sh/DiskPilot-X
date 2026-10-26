using FluentAssertions;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Models;

namespace DiskPilotX.App.Tests;

public class TreeViewModelTests
{
    [Fact]
    public void TreeViewItemViewModel_UsesSubtreeStatsForCounts()
    {
        var root = Directory(@"C:\", "C:");
        var projects = AddDirectory(root, "Projects");
        AddFile(projects, "app.cs", 100);
        AddFile(projects, "readme.md", 20);
        AddDirectory(projects, "bin");
        AddFile(root, "pagefile.sys", 300);

        RecalculateTree(root);

        var rootItem = new TreeViewItemViewModel(root, 0, "Size", true, []);
        rootItem.EnsureChildrenLoaded();

        var projectsItem = rootItem.Children.Single(child => child.Entry == projects);

        rootItem.ItemCount.Should().Be(6);
        rootItem.FileCount.Should().Be(3);
        rootItem.FolderCount.Should().Be(3);
        projectsItem.ItemCount.Should().Be(4);
        projectsItem.FileCount.Should().Be(2);
        projectsItem.FolderCount.Should().Be(2);
        projectsItem.AllocatedText.Should().Be("128 B");
    }

    [Fact]
    public void TreeViewItemViewModel_KeepsKnownZeroAllocatedSize()
    {
        var file = new FileEntry
        {
            Path = @"C:\sparse.bin",
            Name = "sparse.bin",
            IsDirectory = false,
            Size = 4096,
            AllocatedSize = 0,
            HasAllocatedSize = true,
            SubtreeItemCount = 1,
            SubtreeFileCount = 1,
        };

        var item = new TreeViewItemViewModel(file, 0, "Size", true, []);

        item.AllocatedText.Should().Be("0");
    }

    [Fact]
    public void SelectEntry_LoadsPathExpandsTargetAndSelectsIt()
    {
        var root = Directory(@"C:\", "C:");
        var parent = AddDirectory(root, "Parent");
        var child = AddDirectory(parent, "Child");
        AddFile(child, "file.bin", 10);
        RecalculateTree(root);

        var viewModel = new TreeViewModel();
        viewModel.SetRoot(root);

        viewModel.SelectEntry(child);

        var rootItem = viewModel.RootItems.Single();
        var parentItem = rootItem.Children.Single(item => item.Entry == parent);
        var childItem = parentItem.Children.Single(item => item.Entry == child);

        rootItem.IsExpanded.Should().BeTrue();
        parentItem.IsExpanded.Should().BeTrue();
        childItem.IsExpanded.Should().BeTrue();
        childItem.HasLoadedChildren.Should().BeTrue();
        childItem.IsSelected.Should().BeTrue();
        viewModel.SelectedItem.Should().BeSameAs(childItem);
    }

    [Fact]
    public void SelectEntry_SelectsRootEvenWhenEntryHasParent()
    {
        var scanRoot = Directory(@"C:\", "C:");
        var currentRoot = AddDirectory(scanRoot, "Current");
        AddFile(currentRoot, "file.bin", 10);
        RecalculateTree(scanRoot);

        var viewModel = new TreeViewModel();
        viewModel.SetRoot(currentRoot);

        viewModel.SelectEntry(currentRoot);

        var rootItem = viewModel.RootItems.Single();
        rootItem.IsSelected.Should().BeTrue();
        viewModel.SelectedItem.Should().BeSameAs(rootItem);
    }

    private static FileEntry Directory(string path, string name) => new()
    {
        Path = path,
        Name = name,
        IsDirectory = true,
    };

    private static FileEntry AddDirectory(FileEntry parent, string name)
    {
        var child = Directory(Path.Combine(parent.Path, name), name);
        child.Parent = parent;
        child.Depth = parent.Depth + 1;
        parent.Children.Add(child);
        return child;
    }

    private static FileEntry AddFile(FileEntry parent, string name, long size)
    {
        var child = new FileEntry
        {
            Path = Path.Combine(parent.Path, name),
            Name = name,
            IsDirectory = false,
            Size = size,
            AllocatedSize = size + 4,
            HasAllocatedSize = true,
            Parent = parent,
            Depth = parent.Depth + 1,
            Extension = Path.GetExtension(name),
        };

        parent.Children.Add(child);
        return child;
    }

    private static void RecalculateTree(FileEntry entry)
    {
        foreach (var child in entry.Children)
        {
            RecalculateTree(child);
        }

        if (entry.IsDirectory)
        {
            entry.Size = entry.Children.Sum(child => child.Size);
            entry.AllocatedSize = entry.Children.Sum(child => child.AllocatedSize);
            entry.HasAllocatedSize = true;
            entry.SubtreeFileCount = entry.Children.Sum(child => child.SubtreeFileCount);
            entry.SubtreeFolderCount = 1 + entry.Children.Sum(child => child.SubtreeFolderCount);
            entry.SubtreeItemCount = entry.SubtreeFileCount + entry.SubtreeFolderCount;
        }
        else
        {
            entry.SubtreeFileCount = 1;
            entry.SubtreeFolderCount = 0;
            entry.SubtreeItemCount = 1;
        }
    }
}
