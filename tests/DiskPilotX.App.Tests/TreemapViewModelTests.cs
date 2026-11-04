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

    [Theory]
    [InlineData(@"D:\AppData\Visual Studio\Projects\DiskPilot-X\")]
    [InlineData("D:/AppData/Visual Studio/Projects/DiskPilot-X/")]
    public void 外部目录带末尾分隔符_面包屑不重复当前目录(string path)
    {
        var viewModel = new TreemapViewModel(new StubTreemapLayoutEngine());

        viewModel.NavigateToExternalPath(path);

        viewModel.BreadcrumbPath.Should().Equal("D:", "AppData", "Visual Studio", "Projects", "DiskPilot-X");
        viewModel.CurrentRoot!.Parent!.Path.Should().Be(@"D:\AppData\Visual Studio\Projects");
    }

    [Theory]
    [InlineData(@"D:\AppData\Visual Studio\Projects\DiskPilot-X\")]
    [InlineData("D:/AppData/Visual Studio/Projects/DiskPilot-X/")]
    public void 扫描根带末尾分隔符_向上导航进入真正父目录(string path)
    {
        var root = Directory(path, "DiskPilot-X");
        var viewModel = new TreemapViewModel(new StubTreemapLayoutEngine());
        viewModel.SetRoot(root);

        viewModel.NavigateToParent();

        viewModel.CurrentRoot!.Path.Should().Be(@"D:\AppData\Visual Studio\Projects");
        viewModel.BreadcrumbPath.Should().Equal("D:", "AppData", "Visual Studio", "Projects");
    }

    [Fact]
    public void 外部目录末尾分隔符_向上和历史导航不产生同名伪父级()
    {
        var viewModel = new TreemapViewModel(new StubTreemapLayoutEngine());
        viewModel.NavigateToExternalPath(@"D:\AppData\Visual Studio\Projects\DiskPilot-X\outputs\");
        viewModel.NavigateToParent();
        viewModel.BreadcrumbPath.Should().Equal("D:", "AppData", "Visual Studio", "Projects", "DiskPilot-X");

        viewModel.NavigateBack();
        viewModel.BreadcrumbPath.Should().Equal("D:", "AppData", "Visual Studio", "Projects", "DiskPilot-X", "outputs");

        viewModel.NavigateForward();
        viewModel.BreadcrumbPath.Should().Equal("D:", "AppData", "Visual Studio", "Projects", "DiskPilot-X");
    }

    [Theory]
    [InlineData(@"D:\")]
    [InlineData("D:/")]
    [InlineData(@"\\server\share\")]
    public void 根目录保留有效路径且没有伪父级(string path)
    {
        var viewModel = new TreemapViewModel(new StubTreemapLayoutEngine());
        viewModel.NavigateToExternalPath(path);
        var root = viewModel.CurrentRoot;

        root!.Path.Should().Be(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)));
        root.Parent.Should().BeNull();
        viewModel.NavigateToParent();
        viewModel.CurrentRoot.Should().BeSameAs(root);
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
        public List<TreemapNode> ComputeLayout(FileEntry root, float width, float height, int maxDepth, float zoom = 1f) => [];
    }
}
