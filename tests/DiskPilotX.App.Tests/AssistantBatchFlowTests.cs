using DiskPilotX.App.Services;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Models;
using FluentAssertions;
namespace DiskPilotX.App.Tests;
public class AssistantBatchFlowTests
{
    private static AssistantAnalysisScope Scope(string path) => new() { Path = path, Session = new() { TargetPath = path, RootEntry = new() { Path = path, IsDirectory = true } } };
    [Fact] public void 相同范围更新但其他范围保持且采用最具体归属()
    {
        var batch = new AssistantAnalysisBatch(); var outer = Scope(@"D:\甲"); var inner = Scope(@"D:\甲\子");
        batch.AddOrReplace(outer); batch.AddOrReplace(inner); batch.AddOrReplace(Scope(@"D:\乙"));
        batch.AddOrReplace(Scope(@"D:\乙"));
        batch.Scopes.Count.Should().Be(3);
        batch.FindScope(@"D:\甲\子\Library").Should().BeSameAs(inner);
        batch.FindScope(@"D:\甲乙\Library").Should().BeNull();
    }
    [Fact] public void 建立清理聚合会话不改变原快照根的父节点()
    {
        var batch = new AssistantAnalysisBatch(); var a=Scope(@"D:\甲"); var b=Scope(@"D:\乙");
        batch.Scopes.Add(a); batch.Scopes.Add(b); batch.ToSession();
        a.Root.Parent.Should().BeNull(); b.Root.Parent.Should().BeNull();
    }
    [Fact] public void 精确版本里的文件推荐定位到其父目录而非把文件当树根()
    {
        var root=new FileEntry {Path=@"D:\样本",Name="样本",IsDirectory=true};var file=new FileEntry {Path=@"D:\样本\cache.bin",Name="cache.bin",Parent=root,Size=8};root.Children.Add(file);root.RecalculateSize();
        var vm=new TreemapViewModel(new DiskPilotX.Core.Services.Treemap.SquarifiedTreemapLayout());
        vm.SetRoot(root,new ScanSession {RootEntry=root,TargetPath=root.Path});
        vm.NavigateToEntry(file).Should().BeTrue();vm.CurrentRoot.Should().BeSameAs(root);
    }
}
