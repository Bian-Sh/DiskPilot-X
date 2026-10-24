using FluentAssertions;
using SpaceMonger.App.ViewModels;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Enums;
using SpaceMonger.Core.Services.Treemap;
namespace SpaceMonger.App.Tests;
public class CleanupProjectionTests
{
    [Fact]
    public void CopiedRecommendationUpdatesCanonicalTreeAndNestedCounts()
    {
        var root=new FileEntry{Path=@"D:\scan",Name="scan",IsDirectory=true};
        var cache=new FileEntry{Path=@"D:\scan\cache",Name="cache",IsDirectory=true,Parent=root};root.Children.Add(cache);
        var nested=new FileEntry{Path=@"D:\scan\cache\nested",Name="nested",IsDirectory=true,Parent=cache};cache.Children.Add(nested);
        var file=new FileEntry{Path=@"D:\scan\cache\nested\file.tmp",Name="file.tmp",Size=42,Parent=nested};nested.Children.Add(file);file.RecalculateSize();
        var session=new ScanSession{RootEntry=root,TotalFiles=1,TotalFolders=3,TotalSize=42};
        var vm=new TreemapViewModel(new Layout());vm.SetRoot(root,session);
        vm.ApplyCleanupResults([new CleanupAction{Result=CleanupResult.Success,Recommendation=new(){TargetPath=cache.Path,Entry=new(){Path=cache.Path,IsDirectory=true},Size=42}}],session);
        root.Children.Should().BeEmpty();session.TotalFiles.Should().Be(0);session.TotalFolders.Should().Be(1);session.TotalSize.Should().Be(0);
    }
    [Fact]
    public void FileCleanupDoesNotRequireDirectoryOnlyNavigationLookup()
    {
        var root=new FileEntry{Path=@"D:\scan",IsDirectory=true};var file=new FileEntry{Path=@"D:\scan\a.tmp",Size=7,Parent=root};root.Children.Add(file);file.RecalculateSize();
        var session=new ScanSession{RootEntry=root,TotalFiles=1,TotalFolders=1,TotalSize=7};var vm=new TreemapViewModel(new Layout());vm.SetRoot(root,session);
        vm.ApplyCleanupResults([new(){Result=CleanupResult.AlreadyRemoved,Recommendation=new(){TargetPath=file.Path}}],session);
        root.Children.Should().BeEmpty();session.TotalSize.Should().Be(0);
    }

    [Theory]
    [InlineData(CleanupResult.Success)]
    [InlineData(CleanupResult.AlreadyRemoved)]
    public void RepeatedResultsOnlySubtractCanonicalSubtreeOnce(CleanupResult result)
    {
        var (vm, session, cache, nested, file) = CreateTree();
        var action = new CleanupAction
        {
            Result = result,
            Recommendation = new() { TargetPath = cache.Path, Entry = cache, Size = 999 }
        };

        vm.ApplyCleanupResults([action, action], session);
        vm.ApplyCleanupResults([action], session);

        session.RootEntry!.Children.Should().ContainSingle().Which.Name.Should().Be("keep.dat");
        session.TotalFiles.Should().Be(1);
        session.TotalFolders.Should().Be(1);
        session.TotalSize.Should().Be(5);
        session.RootEntry.Size.Should().Be(5);
    }

    [Theory]
    [InlineData(CleanupResult.Failed)]
    [InlineData(CleanupResult.Skipped)]
    public void UnsuccessfulResultsPreserveTreeStatisticsAndNavigation(CleanupResult result)
    {
        var (vm, session, cache, nested, file) = CreateTree();
        vm.DrillDown(cache);
        var selected = new TreemapNode { Entry = file };
        vm.SelectedNode = selected;
        vm.HoveredNode = selected;

        vm.ApplyCleanupResults([new()
        {
            Result = result,
            Recommendation = new() { TargetPath = cache.Path, Entry = cache }
        }], session);

        session.RootEntry!.Children.Should().Contain(cache);
        session.TotalFiles.Should().Be(2);
        session.TotalFolders.Should().Be(3);
        session.TotalSize.Should().Be(47);
        vm.CurrentRoot.Should().BeSameAs(cache);
        vm.SelectedNode.Should().BeSameAs(selected);
        vm.HoveredNode.Should().BeSameAs(selected);
        vm.CanGoBack.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeletingCurrentDirectoryOrAncestorReturnsToSurvivingParent(bool deleteAncestor)
    {
        var (vm, session, cache, nested, file) = CreateTree();
        vm.DrillDown(cache);
        vm.DrillDown(nested);
        vm.SelectedNode = new() { Entry = file };
        vm.HoveredNode = new() { Entry = nested };
        var target = deleteAncestor ? cache : nested;
        var parent = target.Parent;

        vm.ApplyCleanupResults([new()
        {
            Result = CleanupResult.Success,
            Recommendation = new() { TargetPath = target.Path }
        }], session);

        vm.CurrentRoot.Should().BeSameAs(parent);
        vm.SelectedNode.Should().BeNull();
        vm.HoveredNode.Should().BeNull();
        vm.BreadcrumbPath.Should().Equal(deleteAncestor ? new[] { "scan" } : new[] { "scan", "cache" });
        vm.CanGoForward.Should().BeFalse();
        vm.CanGoBack.Should().Be(!deleteAncestor);
        vm.NavigateBack();
        vm.CurrentRoot.Should().BeSameAs(session.RootEntry);
        vm.NavigateForward();
        vm.CurrentRoot.Should().BeSameAs(parent);
    }

    [Fact]
    public void DeletedSubtreeIsRemovedFromForwardHistory()
    {
        var (vm, session, cache, nested, file) = CreateTree();
        vm.DrillDown(cache);
        vm.DrillDown(nested);
        vm.NavigateBack();

        vm.ApplyCleanupResults([new()
        {
            Result = CleanupResult.Success,
            Recommendation = new() { TargetPath = cache.Path }
        }], session);

        vm.CurrentRoot.Should().BeSameAs(session.RootEntry);
        vm.CanGoForward.Should().BeFalse();
        vm.CanGoBack.Should().BeFalse();
        vm.NavigateForward();
        vm.NavigateBack();
        vm.CurrentRoot.Should().BeSameAs(session.RootEntry);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OverlappingSubtreesDoNotDoubleSubtract(bool childFirst)
    {
        var (vm, session, cache, nested, file) = CreateTree();
        CleanupAction Action(FileEntry entry) => new()
        {
            Result = CleanupResult.Success,
            Recommendation = new() { TargetPath = entry.Path, Entry = entry }
        };

        vm.ApplyCleanupResults(childFirst ? [Action(file), Action(cache)] : [Action(cache), Action(file)], session);

        session.TotalFiles.Should().Be(1);
        session.TotalFolders.Should().Be(1);
        session.TotalSize.Should().Be(5);
        session.RootEntry!.Size.Should().Be(5);
    }

    private static (TreemapViewModel Vm, ScanSession Session, FileEntry Cache, FileEntry Nested, FileEntry File) CreateTree()
    {
        var root = new FileEntry { Path = @"D:\scan", Name = "scan", IsDirectory = true };
        var cache = new FileEntry { Path = @"D:\scan\cache", Name = "cache", IsDirectory = true, Parent = root };
        var nested = new FileEntry { Path = @"D:\scan\cache\nested", Name = "nested", IsDirectory = true, Parent = cache };
        var file = new FileEntry { Path = @"D:\scan\cache\nested\file.tmp", Name = "file.tmp", Size = 42, Parent = nested };
        root.Children.Add(cache);
        cache.Children.Add(nested);
        nested.Children.Add(file);
        root.Children.Add(new FileEntry { Path = @"D:\scan\keep.dat", Name = "keep.dat", Size = 5, Parent = root });
        file.RecalculateSize();
        var session = new ScanSession { RootEntry = root, TotalFiles = 2, TotalFolders = 3, TotalSize = 47 };
        var vm = new TreemapViewModel(new Layout());
        vm.SetRoot(root, session);
        return (vm, session, cache, nested, file);
    }
    private sealed class Layout:ITreemapLayoutEngine{public List<TreemapNode> ComputeLayout(FileEntry root,float width,float height,int maxDepth)=>[];}
}
