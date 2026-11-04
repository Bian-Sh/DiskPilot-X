using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Scanning;
using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using TinyHarness;
namespace DiskPilotX.Core.Tests;
public class SnapshotRepairRegressionTests
{
    private static ScanSession Sample(string file="old.bin")
    {
        var path=@"D:\合成项目\Library";var root=new FileEntry {Path=path,Name="Library",IsDirectory=true};
        root.Children.Add(new() {Path=Path.Combine(path,file),Name=file,Size=123,Parent=root});root.RecalculateSize();
        return new() {TargetPath=path,RootEntry=root,TotalFiles=1,TotalSize=123,StartTime=DateTime.Now};
    }
    [Fact] public void 真实增量删除不改变旧会话和旧快照树且索引指向新树()
    {
        var before=Sample();var cache=new ScanSnapshotCache();var reference=cache.Add(before);
        var type=typeof(IncrementalFileScanner);var stateType=type.GetNestedType("VolumeScanState",BindingFlags.NonPublic)!;var state=Activator.CreateInstance(stateType,true)!;
        stateType.GetProperty("CachedSession")!.SetValue(state,before);
        stateType.GetProperty("FrnIndex")!.SetValue(state,new Dictionary<long,FileEntry>{{100,before.RootEntry!}});
        var changeType=type.Assembly.GetType("DiskPilotX.Core.Services.Scanning.UsnChange")!;var kind=type.Assembly.GetType("DiskPilotX.Core.Services.Scanning.UsnChangeKind")!;
        var changes=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(changeType))!;
        changes.Add(Activator.CreateInstance(changeType,[200L,100L,"old.bin",Enum.Parse(kind,"Delete"),false,0u,0L,null]));
        type.GetMethod("ApplyChanges",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[changes,state,CancellationToken.None]);
        var after=(ScanSession)stateType.GetProperty("CachedSession")!.GetValue(state)!;
        cache.TryGet(reference,out var retained).Should().BeTrue();
        retained!.RootEntry!.Children.Should().ContainSingle();before.RootEntry!.Children.Should().ContainSingle();
        after.RootEntry.Should().NotBeSameAs(before.RootEntry);after.RootEntry!.Children.Should().BeEmpty();
        var index=(Dictionary<long,FileEntry>)stateType.GetProperty("FrnIndex")!.GetValue(state)!;
        index[100].Should().BeSameAs(after.RootEntry);
    }
    [Fact] public void 推荐导航有宿主来源标识且按精确版本取回不受最近访问影响()
    {
        typeof(CleanupRecommendation).GetProperty("SourceScanId").Should().NotBeNull();
        var method=typeof(ScanSnapshotCache).GetMethod("FindById");method.Should().NotBeNull();
        var cache=new ScanSnapshotCache();var old=Sample();var current=Sample("new.bin");var a=cache.Add(old);cache.Add(current);cache.TryGet(a,out _);
        ((ScanSession?)method!.Invoke(cache,[current.Id])).Should().BeSameAs(current);
    }
    [Fact] public async Task 重试等待前就有可展示事件且可保存在会话中()
    {
        var events=new List<AgentEvent>();var visibleAtWait=new List<bool>();var client=new RetryClient();
        var loop=new AgentLoop(client,new ToolRegistry(),NullLogger<AgentLoop>.Instance,retryDelay:(_,_)=>{visibleAtWait.Add(events.Any(e=>e.Detail?.Contains("等待")==true));return Task.CompletedTask;});
        await foreach(var item in loop.RunAsync(new AgentSession(),"重试探针"))events.Add(item);
        visibleAtWait.Should().HaveCount(2).And.OnlyContain(value=>value);
        var message=new DiskPilotX.Core.Models.ChatMessage {Sender=DiskPilotX.Core.Enums.ChatSender.Assistant};foreach(var item in events)message.ApplyActivity(item);
        message.FinalSegments.Should().NotContain(segment=>segment.Text.Contains("等待")||segment.Text.Contains("重试"));
        DiskPilotX.Core.Services.Assistant.SavedChatMessage.Capture(message).Restore().Segments.Should().Contain(segment=>segment.Text.Contains("重试")||segment.Text.Contains("等待"));
    }
    private sealed class RetryClient:IModelClient
    {
        private int calls;
        public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request,[EnumeratorCancellation]CancellationToken ct)
        {await Task.Yield();if(++calls<3)throw new ModelClientException("合成429",ModelErrorKind.RateLimited,429);yield return new TextDeltaEvent("完成");yield return new ModelCompletedEvent("stop");}
    }
}
