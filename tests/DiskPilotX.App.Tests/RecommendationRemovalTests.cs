using System.ComponentModel;
using System.Windows.Input;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Models;

namespace DiskPilotX.App.Tests;

public sealed class RecommendationRemovalTests
{
    [Fact]
    public void 移除失败建议只移除该项并保留其他选择()
    {
        var vm = new RecommendationsViewModel();
        var first = new CleanupRecommendation { Id = "1", TargetPath = @"D:\缓存\已移除", IsAccepted = true };
        var second = new CleanupRecommendation { Id = "2", TargetPath = @"D:\缓存\保留", IsAccepted = true };
        vm.SetExternalRecommendations([first, second]);
        vm.DismissCommand.Execute(first);
        Assert.Single(vm.Recommendations);
        Assert.Same(second, vm.Recommendations[0]);
        Assert.True(second.IsAccepted);
        Assert.Equal(1, vm.TotalSelectedCount);
    }

    [Fact]
    public void 清空清单存在且不受筛选影响并通知宿主释放证据()
    {
        var vm = new RecommendationsViewModel();
        vm.SetExternalRecommendations([new CleanupRecommendation { Id = "1", TargetPath = @"D:\缓存\样本" }]);
        var notifications = 0;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Recommendations)) notifications++; };
        var property = typeof(RecommendationsViewModel).GetProperty("ClearRecommendationsCommand");
        Assert.NotNull(property);
        ((ICommand)property.GetValue(vm)!).Execute(null);
        Assert.Empty(vm.Recommendations);
        Assert.Empty(vm.FilteredRecommendations);
        Assert.False(vm.HasRecommendations);
        Assert.True(notifications > 0);
    }

    [Fact]
    public void 删除最后一项后通知宿主而非长期保留扫描树()
    {
        var vm = new RecommendationsViewModel();
        vm.SetExternalRecommendations([new CleanupRecommendation { Id = "1", TargetPath = @"D:\缓存\样本" }]);
        var notified = false;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Recommendations) && vm.Recommendations.Count == 0) notified = true; };
        vm.SetExternalRecommendations([]);
        Assert.True(notified);
    }
}
