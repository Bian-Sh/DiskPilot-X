using System.Reflection;
using System.Runtime.ExceptionServices;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Analysis;

namespace DiskPilotX.App.Tests;

public sealed class RecommendationEmptyStateTests
{
    private static bool Set(RecommendationsViewModel vm, string title, string description)
    {
        var method = typeof(RecommendationsViewModel).GetMethod("SetEmptyStatePresentation");
        Assert.NotNull(method);
        try { return (bool)method.Invoke(vm, [title, description])!; }
        catch (TargetInvocationException exception) { ExceptionDispatchInfo.Capture(exception.InnerException!).Throw(); throw; }
    }

    [Fact]
    public void 可自定义中心纯文本并恢复默认()
    {
        var vm = new RecommendationsViewModel();
        var original = vm.EmptyStateTitleText;
        Assert.True(Set(vm, "空间小贴士", "先查看大文件，不执行清理。"));
        Assert.Equal("空间小贴士", vm.EmptyStateTitleText);
        Assert.Equal("先查看大文件，不执行清理。", vm.EmptyStateDescriptionText);
        Assert.Null(vm.AnalysisTask);
        Set(vm, "", "");
        Assert.Equal(original, vm.EmptyStateTitleText);
    }

    [Theory]
    [InlineData(AnalysisTaskStatus.Completed, AnalysisOutcome.Partial)]
    [InlineData(AnalysisTaskStatus.Completed, AnalysisOutcome.Restricted)]
    [InlineData(AnalysisTaskStatus.Failed, AnalysisOutcome.Unknown)]
    [InlineData(AnalysisTaskStatus.Cancelled, AnalysisOutcome.Unknown)]
    public void 自定义不能掩盖宿主受限失败或部分完成(AnalysisTaskStatus status, AnalysisOutcome outcome)
    {
        var vm = new RecommendationsViewModel();
        var task = new AnalysisTaskResult(Guid.NewGuid(), Guid.NewGuid(), "D:/scope", status, outcome, 0, ["必须保留的宿主限制"], new Dictionary<string,string>());
        vm.SetAnalysisTask(task);
        var title = vm.EmptyStateTitleText;
        Set(vm, "误导标题", "可选补充建议");
        Assert.Equal(title, vm.EmptyStateTitleText);
        Assert.Contains("必须保留的宿主限制", vm.EmptyStateDescriptionText);
        Assert.Contains("可选补充建议", vm.EmptyStateDescriptionText);
        Assert.Same(task, vm.AnalysisTask);
    }

    [Fact]
    public void 新分析清除旧文案且有列表时不改内容与勾选()
    {
        var vm = new RecommendationsViewModel();
        Set(vm, "旧说明", "旧建议");
        vm.BeginAnalysis();
        Assert.DoesNotContain("旧说明", vm.EmptyStateTitleText);
        var item = new CleanupRecommendation { TargetPath = "D:/scope/a", IsAccepted = true };
        vm.SetExternalRecommendations([item]);
        Assert.False(Set(vm, "不应显示", "不能改变清单"));
        Assert.Same(item, Assert.Single(vm.Recommendations));
        Assert.True(item.IsAccepted);
    }

    [Fact]
    public void 首个聊天分析清除旧空态但同一任务内的更新保留文案()
    {
        var vm = new RecommendationsViewModel();
        Set(vm, "上轮的标题", "上轮的说明");
        var task = new AnalysisTaskResult(Guid.NewGuid(), Guid.NewGuid(), "D:/scope", AnalysisTaskStatus.Running, AnalysisOutcome.Unknown, 0, [], new Dictionary<string,string>());
        vm.SetAnalysisTask(task);
        vm.SetAnalysisTask(task with { Status = AnalysisTaskStatus.Completed, Outcome = AnalysisOutcome.NoRecommendations });
        Assert.DoesNotContain("上轮", vm.EmptyStateTitleText);
        Assert.DoesNotContain("上轮", vm.EmptyStateDescriptionText);
        Set(vm, "本轮的标题", "本轮的说明");
        vm.SetAnalysisTask(task with { Status = AnalysisTaskStatus.Completed, Outcome = AnalysisOutcome.NoRecommendations });
        Assert.Equal("本轮的标题", vm.EmptyStateTitleText);
        Assert.Equal("本轮的说明", vm.EmptyStateDescriptionText);
    }
    [Fact]
    public void 宿主拒绝超长文案()
    {
        var vm = new RecommendationsViewModel();
        Assert.Throws<ArgumentException>(() => Set(vm, new string('长', 81), "说明"));
        Assert.Throws<ArgumentException>(() => Set(vm, "标题", new string('长', 1201)));
    }
}