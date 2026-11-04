using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Models;
namespace DiskPilotX.App.Tests;
public sealed class RecommendationBatchUpdateTests
{
    [Fact] public void 多批追加不替换集合也不重置已展示对象()
    {
        var vm=new RecommendationsViewModel();var a=new CleanupRecommendation{Id="1",TargetPath=@"D:\甲",IsAccepted=true};
        vm.SetExternalRecommendations([a]);var source=vm.Recommendations;var filtered=vm.FilteredRecommendations;
        var resets=0;filtered.CollectionChanged+=(_,e)=>{if(e.Action==System.Collections.Specialized.NotifyCollectionChangedAction.Reset)resets++;};
        vm.SetExternalRecommendations([a,new(){Id="2",TargetPath=@"D:\乙"}]);
        Assert.Same(source,vm.Recommendations);Assert.Same(filtered,vm.FilteredRecommendations);
        Assert.Same(a,vm.FilteredRecommendations[0]);Assert.True(a.IsAccepted);Assert.Equal(0,resets);
    }
}
