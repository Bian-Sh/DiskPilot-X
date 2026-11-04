using System.Reflection;
using System.Text.Json.Nodes;
using DiskPilotX.App;
using DiskPilotX.Core.Services.Cleanup;
namespace DiskPilotX.App.Tests;
public sealed class CleanupPlanFailureTests
{
    [Fact] public void 两个入口共享的清单错误保留原因且明确没有删除()
    {
        var method=typeof(MainWindow).GetMethod("CleanupPlanFailure",BindingFlags.Static|BindingFlags.NonPublic)!;
        var response=(JsonNode)method.Invoke(null,[new CleanupPlanValidationException("cleanup_selection_duplicate","清理项ID重复")])!;
        Assert.False(response["ok"]!.GetValue<bool>());Assert.False(response["started"]!.GetValue<bool>());
        Assert.Equal(0,response["deleted_count"]!.GetValue<int>());
        Assert.Equal("cleanup_selection_duplicate",response["code"]!.GetValue<string>());
        Assert.Equal("清理项ID重复",response["error"]!.GetValue<string>());
    }
}
