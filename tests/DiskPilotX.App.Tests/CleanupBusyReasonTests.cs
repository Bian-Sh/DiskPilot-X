using System.Reflection;
using DiskPilotX.App;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Models;

namespace DiskPilotX.App.Tests;

public class CleanupBusyReasonTests
{
    [Theory]
    [InlineData(false, false, false, true, false, true, null)]
    [InlineData(true, false, false, true, false, true, "正在执行另一项清理，请等待完成。")]
    [InlineData(false, true, false, true, false, true, "磁盘扫描尚未完成，请等待完成。")]
    [InlineData(false, false, true, true, false, true, "推荐清单正在发布，请等待完成。")]
    [InlineData(false, false, false, true, true, true, "AI 分析尚未发布可执行结果，请等待完成。")]
    [InlineData(false, false, false, true, false, false, "AI 分析仍在进行，请等待完成。")]
    [InlineData(false, false, false, false, false, false, null)]
    public void 分析生命周期与真实磁盘忙碌分别判断(bool cleanup, bool scan, bool publish, bool analysis, bool waiting, bool assistant, string? expected)
    {
        var method = typeof(MainWindow).GetMethod("GetCleanupBusyReason", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        Assert.Equal(expected, (string?)method.Invoke(null, [cleanup, scan, publish, analysis, waiting, assistant]));
    }

    [Fact]
    public void 发布结果后分析仍运行但不再等待结果()
    {
        var vm = new RecommendationsViewModel();
        vm.BeginAnalysis();
        vm.SetExternalRecommendations([new CleanupRecommendation { TargetPath = @"D:\本任务夹具\待清理.txt", Size = 1 }]);
        Assert.True(vm.IsAnalyzing);
        Assert.False(vm.IsWaitingForExternalRecommendations);
        Assert.True(vm.HasAnyRecommendations);
    }
}
