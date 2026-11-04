using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Assistant;
using FluentAssertions;
using TinyHarness;
using Message = DiskPilotX.Core.Models.ChatMessage;
namespace DiskPilotX.Core.Tests;
public class ChatFailurePersistenceTests
{
    [Fact] public void 无最终文字时失败原因仍保存为可见段落()
    {
        var message=new Message { Sender=ChatSender.Assistant };
        message.ApplyActivity(AgentEvent.Create("sample",AgentEventKind.TaskFailed) with { Detail="连接中断（本步骤已尝试 5 次），已完成结果保留。" });
        var restored=SavedChatMessage.Capture(message).Restore();
        restored.FinalSegments.Should().Contain(segment=>segment.Text.Contains("连接中断"));
    }
}
