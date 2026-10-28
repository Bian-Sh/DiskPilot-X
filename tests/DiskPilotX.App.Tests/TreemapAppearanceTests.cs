using System.Collections;
using System.Reflection;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Treemap;

namespace DiskPilotX.App.Tests;

public class TreemapAppearanceTests
{
    [Fact]
    public void FileTypesAreAutomatic_WithoutModesOrDirectoryAccents()
    {
        var root = Root(); var vm = new TreemapViewModel(new SquarifiedTreemapLayout());
        vm.UpdateSize(1000, 700); vm.SetRoot(root);
        Assert.Null(typeof(TreemapViewModel).GetProperty("ColorMode"));
        Assert.Null(typeof(TreemapViewModel).GetProperty("ColorLegend"));
        Assert.All(vm.Nodes!.Where(node => node.Entry.IsDirectory), node =>
        {
            Assert.Equal("#303030", node.ColorHex);
            Assert.Null(typeof(TreemapNode).GetProperty("AccentColorHex"));
        });
        var files = vm.Nodes!.Where(node => !node.Entry.IsDirectory).ToArray();
        Assert.Equal(2, files.Select(node => node.ColorHex).Distinct().Count());
    }

    [Fact]
    public void TypeStatistics_ComeFromEntireScan_AndSurviveDrilldown()
    {
        var root = Root(); var vm = new TreemapViewModel(new SquarifiedTreemapLayout());
        vm.UpdateSize(1000, 700); vm.SetRoot(root);
        var property = typeof(TreemapViewModel).GetProperty("TypeColors", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(property);
        var statistics = property.GetValue(vm);
        Assert.NotEmpty((IEnumerable)statistics!);
        vm.DrillDown(root.Children[0]); vm.SetZoom(2);
        Assert.Same(statistics, property.GetValue(vm));
        vm.SetRoot(Root());
        Assert.NotSame(statistics, property.GetValue(vm));
    }

    [Fact]
    public void 删除后保留扫描类型快照且不改变原配色()
    {
        var root = Root();
        var session = new ScanSession { RootEntry = root, TotalFiles = 2, TotalFolders = 2, TotalSize = 300 };
        var vm = new TreemapViewModel(new SquarifiedTreemapLayout());
        vm.UpdateSize(1000, 700); vm.SetRoot(root, session);
        var property = typeof(TreemapViewModel).GetProperty("TypeColors", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(property);
        object[] Items() => ((IEnumerable)property.GetValue(vm)!).Cast<object>().ToArray();
        string Key(object item) => (string)item.GetType().GetProperty("Key")!.GetValue(item)!;
        string Color(object item) => (string)item.GetType().GetProperty("ColorHex")!.GetValue(item)!;
        string before = Color(Items().Single(item => Key(item) == ".dll"));
        vm.ApplyCleanupResults([new CleanupAction
        {
            Recommendation = new CleanupRecommendation { TargetPath = root.Children[1].Path },
            Result = DiskPilotX.Core.Enums.CleanupResult.Success,
        }], session);
        Assert.Equal(2, Items().Length);
        Assert.Equal(before, Color(Items().Single(item => Key(item) == ".dll")));
        Assert.True(root.Children[1].IsDeleted);
        Assert.Equal(100, session.TotalSize);
    }

    private static FileEntry Root()
    {
        var root = new FileEntry { Name = "根", Path = "D:/根", IsDirectory = true, Size = 300 };
        var code = new FileEntry { Name = "src", Path = "D:/根/src", Parent = root, IsDirectory = true, Size = 100 };
        code.Children.Add(new FileEntry { Name = "程序.dll", Path = "D:/根/src/程序.dll", Parent = code, Size = 100 });
        root.Children.Add(code);
        root.Children.Add(new FileEntry { Name = "照片.png", Path = "D:/根/照片.png", Parent = root, Size = 200 });
        return root;
    }
}
