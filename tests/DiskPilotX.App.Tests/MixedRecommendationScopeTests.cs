using DiskPilotX.App.Services;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.FileTree;

namespace DiskPilotX.App.Tests;

public sealed class MixedRecommendationScopeTests
{
    [Fact]
    public void 工作区扫描和后续无头范围合并时原建议仍有真实条目来源()
    {
        var root=new FileEntry { Path=@"D:\工程", Name="工程", IsDirectory=true, Size=1000 };
        var target=new FileEntry { Path=@"D:\工程\target", Name="target", Parent=root, IsDirectory=true, Size=600 };
        root.Children.Add(target);
        var modules=new FileEntry { Path=@"D:\工程\node_modules", Name="node_modules", IsDirectory=true, Size=400 };
        var workspace=new ScanSession { RootEntry=root, TargetPath=root.Path, TotalSize=1000 };
        var headless=new ScanSession { RootEntry=modules, TargetPath=modules.Path, TotalSize=400 };
        var scopes=new AssistantAnalysisBatch();
        scopes.AddOrReplace(new() { Path=workspace.TargetPath, Session=workspace });
        scopes.AddOrReplace(new() { Path=headless.TargetPath, Session=headless });
        var combined=scopes.ToSession(); var query=new FileTreeQueryService();
        Assert.Same(target,query.FindByPath(combined,target.Path));
        Assert.Same(modules,query.FindByPath(combined,modules.Path));
        Assert.Same(root,target.Parent);
        Assert.Null(modules.Parent);
    }
}