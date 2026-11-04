using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Analysis;

namespace DiskPilotX.Core.Tests;

public sealed class RecommendationMergeTests
{
    [Fact]
    public void 合并去重和父子重叠保留首批对象与勾选状态()
    {
        var first = new CleanupRecommendation { TargetPath = "D:/scope/a", IsAccepted = true };
        var duplicate = new CleanupRecommendation { TargetPath = "D:/scope/A" };
        var child = new CleanupRecommendation { TargetPath = "D:/scope/a/child" };
        var sibling = new CleanupRecommendation { TargetPath = "D:/scope/b" };
        var merged = RecommendationEngine.MergeRecommendations([first], [duplicate, child, sibling]);
        Assert.Equal(2, merged.Count);
        Assert.Same(first, merged[0]);
        Assert.True(merged[0].IsAccepted);
        Assert.Same(sibling, merged[1]);
    }
}
