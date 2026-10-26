using FluentAssertions;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Treemap;

namespace DiskPilotX.App.Tests;

public class TreemapViewModelTests
{
    [Fact]
    public void NavigateToPath_TrailingSeparatorFromBreadcrumb_UsesScannedEntry()
    {
        var root = Directory(@"C:\", "C:");
        var users = AddDirectory(root, "Users");
        var profile = AddDirectory(users, "BianShanghai");
        var appData = AddDirectory(profile, "AppData");
        var local = AddDirectory(appData, "Local");
        var temp = AddDirectory(local, "Temp");
        var thunder = AddDirectory(temp, "Thunder Network");

        var viewModel = new TreemapViewModel(new StubTreemapLayoutEngine());
        viewModel.SetRoot(root);
        viewModel.NavigateToPath(thunder.Path).Should().BeTrue();

        viewModel.NavigateToPath(local.Path + Path.DirectorySeparatorChar).Should().BeTrue();

        viewModel.CurrentRoot.Should().BeSameAs(local);
        viewModel.CurrentRoot!.Children.Should().ContainSingle().Which.Should().BeSameAs(temp);
    }

    [Fact]
    public void NavigateToExternalPath_BuildsParentChainForBreadcrumb()
    {
        var rootPath = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        rootPath.Should().NotBeNullOrWhiteSpace();
        var path = Path.Combine(rootPath!, "Users", "BianShanghai");
        var rootName = rootPath!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var viewModel = new TreemapViewModel(new StubTreemapLayoutEngine());
        viewModel.NavigateToExternalPath(path);

        viewModel.BreadcrumbPath.Should().Equal(rootName, "Users", "BianShanghai");
        viewModel.CurrentRoot!.Parent!.Path.Should().Be(Path.Combine(rootPath!, "Users"));
    }

    [Fact]
    public void RecomputingLayout_ClearsStaleSelectionAndHover()
    {
        var root = Directory(@"C:\", "根");
        var viewModel = new TreemapViewModel(new SquarifiedTreemapLayout());
        viewModel.UpdateSize(800, 600);
        viewModel.SetRoot(root);
        viewModel.SelectedNode = viewModel.Nodes![0];
        viewModel.HoveredNode = viewModel.Nodes[0];

        viewModel.UpdateSize(900, 600);

        viewModel.SelectedNode.Should().BeNull();
        viewModel.HoveredNode.Should().BeNull();
    }

    private static FileEntry Directory(string path, string name) => new()
    {
        Path = path,
        Name = name,
        IsDirectory = true,
        Size = 1,
    };

    private static FileEntry AddDirectory(FileEntry parent, string name)
    {
        var child = Directory(Path.Combine(parent.Path, name), name);
        child.Parent = parent;
        child.Depth = parent.Depth + 1;
        parent.Children.Add(child);
        return child;
    }

    private sealed class StubTreemapLayoutEngine : ITreemapLayoutEngine
    {
        public List<TreemapNode> ComputeLayout(FileEntry root, float width, float height, int maxDepth) => [];
    }
}
