using NSubstitute;
using DiskPilotX.Core.Services.Settings;
using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Assistant;
using FluentAssertions;
using TinyHarness;
using Message = DiskPilotX.Core.Models.ChatMessage;
namespace DiskPilotX.Core.Tests;
public sealed class ConversationSummaryReadTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"DiskPilot-X-摘要-"+Guid.NewGuid().ToString("N"));
    [Fact] public void 正文被独占时仍可从有效摘要列出历史且入口元数据可恢复()
    {
        var store=new ConversationStore(root); var message=new Message { Sender=ChatSender.User,Text="三个目录" };
        message.ScanSnapshots.Add(new(Guid.NewGuid(),Guid.NewGuid(),@"D:\样本",DateTimeOffset.UtcNow,10,2,1));
        var snapshot=new ConversationSnapshot(Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow,"[]",AgentSessionState.Completed,[],[SavedChatMessage.Capture(message)]);
        store.Save(snapshot);
        using(var held=File.Open(Path.Combine(root,snapshot.Id+".json"),FileMode.Open,FileAccess.ReadWrite,FileShare.None))
            store.List().Should().ContainSingle().Which.Title.Should().Be("三个目录");
        store.Load(snapshot.Id).Messages[0].Restore().ScanSnapshots.Should().ContainSingle().Which.Path.Should().Be(@"D:\样本");
    }
    [Fact] public async Task 浏览恢复与标题更新不推进内容时间而新内容会推进()
    {
        var store=new ConversationStore(root); var settings=Substitute.For<ISettingsService>(); settings.LoadSettings().Returns(new AppSettings());
        var service=new DiskAssistantService(_=>Substitute.For<IModelClient>(),[],new AssistantWorkspace(),settings,conversationStore:store);
        var message=new Message { Sender=ChatSender.User,Text="原消息" };
        service.SaveConversation([message]); var id=service.CurrentConversationId!; var before=store.Load(id);
        await Task.Delay(20); service.SetConversationTitle(id,"新标题"); service.SaveConversation([message]);
        store.Load(id).UpdatedAt.Should().Be(before.UpdatedAt);
        var restored=service.RestoreConversation(store.Load(id)); await Task.Delay(20); service.SaveConversation(restored);
        store.Load(id).UpdatedAt.Should().Be(before.UpdatedAt);
        restored[0].Text="内容有更新"; service.SaveConversation(restored);
        store.Load(id).UpdatedAt.Should().BeAfter(before.UpdatedAt);
    }
    [Fact] public void 四十六份大正文的常规列表只读摘要且损坏摘要可局部修复()
    {
        var store=new ConversationStore(root); var snapshots=new List<ConversationSnapshot>();
        for(var n=0;n<46;n++)
        {
            var snapshot=new ConversationSnapshot(Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow,"[]",AgentSessionState.Completed,[],
                [SavedChatMessage.Capture(new Message {Sender=ChatSender.User,Text=new string('x',247000)+n})]);
            snapshots.Add(snapshot);store.Save(snapshot);
        }
        var handles=new List<FileStream>();
        try
        {
            foreach(var snapshot in snapshots)handles.Add(File.Open(Path.Combine(root,snapshot.Id+".json"),FileMode.Open,FileAccess.ReadWrite,FileShare.None));
            store.List().Count.Should().Be(46);
        }
        finally {foreach(var stream in handles)stream.Dispose();}
        File.WriteAllText(Path.Combine(root,snapshots[0].Id+".summary"),"损坏摘要");
        store.List().Count.Should().Be(46);
        store.ReadMetadata(snapshots[0].Id).Should().NotBeNull("摘要修复不更改授权元数据");
    }
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}
