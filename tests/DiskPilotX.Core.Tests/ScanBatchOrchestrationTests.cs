using System.Text.Json;
using System.Text.Json.Nodes;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Scanning;
namespace DiskPilotX.Core.Tests;
public sealed class ScanBatchOrchestrationTests
{
    [Theory]
    [InlineData("released")][InlineData("evicted")][InlineData("missing")][InlineData("restricted")][InlineData("pending")][InlineData("scanning")]
    public void 非有效实例不能提供地图入口(string state)
    {
        var reference = new ScanSnapshotReference(Guid.NewGuid(),Guid.NewGuid(),@"D:\样本",DateTimeOffset.Now,0,0,0);
        reference.SetState(state);
        Assert.False(reference.CanAct);
        if(state is "released" or "evicted" or "missing") Assert.Contains("失效",reference.StatusText);
    }
    [Fact] public void 长路径在窄cell中优先显示末级名称且保留完整提示路径()
    {
        var path=@"D:\很长的共同路径\aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\目标甲";
        var reference=new ScanSnapshotReference(Guid.NewGuid(),Guid.NewGuid(),path,DateTimeOffset.Now,0,0,0);
        Assert.True(path.Length > 48);
        Assert.StartsWith("目标甲",reference.CompactPath);Assert.Equal(path,reference.DisplayPath);
        reference.SetState("restricted");Assert.DoesNotContain("目标甲",reference.CompactPath);
    }
    [Fact] public void 实时预算区分保护与可回收项()
    {
        var cache=new ScanSnapshotCache(2,100);
        var method=typeof(ScanSnapshotCache).GetMethod("GetBudget");Assert.NotNull(method);
        ScanSession Session(string p)=>new(){StartTime=DateTime.Now,TargetPath=p,RootEntry=new(){Path=p,IsDirectory=true},TotalFiles=9};
        var a=cache.Add(Session(@"D:\甲"));cache.Protect(a.Id,"recommendations");cache.Add(Session(@"D:\乙"));
        var budget=JsonSerializer.SerializeToNode(method.Invoke(cache,null))!;
        Assert.Equal(2,budget["MaximumSnapshots"]!.GetValue<int>());
        Assert.Equal(1,budget["ProtectedSnapshots"]!.GetValue<int>());
        Assert.Equal(20,budget["UsedEntries"]!.GetValue<long>());
        Assert.Equal(1,budget["ReclaimableSnapshots"]!.GetValue<int>());
    }
    [Fact] public void 待扫描和扫描中不能冒充已完成()
    {
        var reference=new ScanSnapshotReference(Guid.NewGuid(),Guid.NewGuid(),@"D:\样本",DateTimeOffset.Now,0,0,0);
        reference.SetState("pending");Assert.Contains("待扫描",reference.StatusText);
        reference.SetState("scanning");Assert.Contains("扫描中",reference.StatusText);
        var restored=JsonSerializer.Deserialize<ScanSnapshotReference>(JsonSerializer.Serialize(reference))!;
        Assert.Equal("released",restored.State);Assert.False(restored.CanAct);
    }
}
