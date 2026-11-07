using System.Reflection;
using DiskPilotX.App.Views;
using DiskPilotX.Core.Models;
namespace DiskPilotX.App.Tests;
public class RecommendationPathGestureTests
{
    [Fact] public void 单击延迟后只Ping不激活()
    {
        var pings=new List<CleanupRecommendation>(); var opened=new List<CleanupRecommendation>();
        var gesture=Create(pings.Add,opened.Add); var item=new CleanupRecommendation();
        Call(gesture,"Queue",item); Assert.Empty(pings); Assert.Empty(opened);
        Call(gesture,"Complete"); Assert.Equal([item],pings); Assert.Empty(opened);
    }
    [Fact] public void 双击取消单击并且仅激活一次()
    {
        var pings=new List<CleanupRecommendation>(); var opened=new List<CleanupRecommendation>();
        var gesture=Create(pings.Add,opened.Add); var item=new CleanupRecommendation();
        Call(gesture,"Queue",item); Call(gesture,"Activate",item); Call(gesture,"Complete");
        Assert.Empty(pings); Assert.Equal([item],opened);
    }
    [Fact] public void 卸载取消尚未发出的Ping()
    {
        var pings=new List<CleanupRecommendation>(); var gesture=Create(pings.Add,_=>{});
        Call(gesture,"Queue",new CleanupRecommendation()); Call(gesture,"Cancel"); Call(gesture,"Complete");
        Assert.Empty(pings);
    }
    private static object Create(Action<CleanupRecommendation> ping,Action<CleanupRecommendation> activate)
    {
        var type=typeof(RecommendationsPanel).Assembly.GetType("DiskPilotX.App.Views.RecommendationPathGesture"); Assert.NotNull(type);
        return Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,[ping,activate],null)!;
    }
    private static void Call(object gesture,string name,params object[] args)=>gesture.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(gesture,args);
}
