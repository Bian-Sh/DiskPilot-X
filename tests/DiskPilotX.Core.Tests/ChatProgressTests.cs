using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Models;
using TinyHarness;
using Message = DiskPilotX.Core.Models.ChatMessage;

namespace DiskPilotX.Core.Tests;

public sealed class ChatProgressTests
{
    [Fact]
    public void 单步文字结束后仍等待整轮任务终态()
    {
        var message = new Message { Sender = ChatSender.Assistant };
        message.ApplyActivity(AgentEvent.Create("fixture", AgentEventKind.SessionStarted));
        message.ApplyActivity(AgentEvent.Create("fixture", AgentEventKind.TextDelta) with { Text = "中间说明" });
        message.ApplyActivity(AgentEvent.Create("fixture", AgentEventKind.MessageCompleted) with
        {
            Message = TinyHarness.ChatMessage.Assistant("中间说明")
        });
        Assert.True(message.IsStreaming);
        Assert.Null(message.CompletedAt);
        message.ApplyActivity(AgentEvent.Create("fixture", AgentEventKind.TaskCompleted));
        Assert.False(message.IsStreaming);
        Assert.NotNull(message.CompletedAt);
    }

    [Fact]
    public void 同一工具卡片从准备到确认执行与完成不重复()
    {
        var message = new Message { Sender = ChatSender.Assistant };
        message.ApplyActivity(AgentEvent.Create("fixture", AgentEventKind.SessionStarted));
        var callId = "call";
        void Apply(AgentEventKind kind) => message.ApplyActivity(AgentEvent.Create("fixture", kind) with
        { ToolCallId = callId, ToolName = "publish_recommendations" });
        Apply(AgentEventKind.ToolCallPreparing);
        var tool = Assert.Single(message.Segments);
        Assert.Equal(ChatSegmentStatus.Preparing, tool.Status);
        Assert.Equal(ChatActivityPhase.PreparingTool, message.Activity);
        Assert.Null(tool.Detail);
        message.ApplyActivity(AgentEvent.Create("fixture", AgentEventKind.ToolCallPreparing) with
        { PreviousToolCallId = "call", ToolCallId = "call_complete", ToolName = "publish_recommendations" });
        Assert.Same(tool, Assert.Single(message.Segments));
        Assert.Equal("call_complete", tool.ToolCallId);
        callId = "call_complete";
        Apply(AgentEventKind.ToolApprovalRequested);
        Assert.Equal(ChatActivityPhase.WaitingForUser, message.Activity);
        Assert.False(message.ShowActivityAnimation);
        Apply(AgentEventKind.ToolCallStarted);
        Assert.Same(tool, Assert.Single(message.Segments));
        Assert.Equal(ChatSegmentStatus.Running, tool.Status);
        Assert.Equal(ChatActivityPhase.ExecutingTool, message.Activity);
        message.SetWaitingForUser(true);
        Assert.Equal(ChatSegmentStatus.WaitingForUser, tool.Status);
        message.SetWaitingForUser(false);
        Assert.Equal(ChatSegmentStatus.Running, tool.Status);
        Apply(AgentEventKind.ToolCallCompleted);
        Assert.Equal(ChatSegmentStatus.Completed, tool.Status);
        Assert.True(message.IsStreaming);
        Assert.Equal(ChatActivityPhase.WaitingForModel, message.Activity);
        Apply(AgentEventKind.TaskCompleted);
        Assert.False(message.IsStreaming);
    }

    [Fact]
    public void 后续准备标识与旧调用前缀相同也不改写已完成结果()
    {
        var message = new Message();
        message.ApplyActivity(AgentEvent.Create("fixture", AgentEventKind.StepStarted));
        message.ApplyActivity(AgentEvent.Create("fixture", AgentEventKind.ToolCallStarted) with { ToolCallId = "call_1", ToolName = "first_tool" });
        message.ApplyActivity(AgentEvent.Create("fixture", AgentEventKind.MessageCompleted) with
        { Message = new TinyHarness.ChatMessage(ChatRole.Tool, "第一个调用的真实结果") { ToolCallId = "call_1" } });
        message.ApplyActivity(AgentEvent.Create("fixture", AgentEventKind.ToolCallCompleted) with { ToolCallId = "call_1", ToolName = "first_tool" });
        message.ApplyActivity(AgentEvent.Create("fixture", AgentEventKind.StepStarted));
        message.ApplyActivity(AgentEvent.Create("fixture", AgentEventKind.ToolCallPreparing) with { ToolCallId = "call_1", ToolName = "second_tool" });
        message.ApplyActivity(AgentEvent.Create("fixture", AgentEventKind.ToolCallPreparing) with { PreviousToolCallId = "call_1", ToolCallId = "call_12", ToolName = "second_tool" });
        var tools = message.Segments.Where(item => item.IsTool).ToArray();
        Assert.Equal(2, tools.Length);
        Assert.Equal("first_tool", tools[0].ToolName);
        Assert.Equal("call_1", tools[0].ToolCallId);
        Assert.Equal("第一个调用的真实结果", tools[0].Text);
        Assert.Equal("call_12", tools[1].ToolCallId);
        var restored = DiskPilotX.Core.Services.Assistant.SavedChatMessage.Capture(message).Restore();
        Assert.Equal("第一个调用的真实结果", restored.Segments.First().Text);
        Assert.Equal(2, restored.Segments.Count);
    }

    [Theory]
    [InlineData(AgentEventKind.TaskFailed, ChatSegmentStatus.Failed)]
    [InlineData(AgentEventKind.TaskCancelled, ChatSegmentStatus.Cancelled)]
    public void 失败或取消收口未开始的工具(AgentEventKind terminal, ChatSegmentStatus expected)
    {
        var message = new Message { Sender = ChatSender.Assistant };
        message.ApplyActivity(AgentEvent.Create("fixture", AgentEventKind.ToolCallPreparing) with
        { ToolCallId = "call", ToolName = "publish_recommendations" });
        message.ApplyActivity(AgentEvent.Create("fixture", terminal));
        Assert.Equal(expected, Assert.Single(message.Segments.Where(segment => segment.IsTool)).Status);
        Assert.False(message.IsStreaming);
    }

    [Fact]
    public void 恢复历史时准备中的工具不伪装成仍在运行()
    {
        var message = new Message { Sender = ChatSender.Assistant };
        message.ApplyActivity(AgentEvent.Create("fixture", AgentEventKind.ToolCallPreparing) with
        { ToolCallId = "call", ToolName = "publish_recommendations" });
        var restored = DiskPilotX.Core.Services.Assistant.SavedChatMessage.Capture(message).Restore();
        Assert.Equal(ChatSegmentStatus.Cancelled, Assert.Single(restored.Segments).Status);
        Assert.False(restored.IsStreaming);
    }

    [Fact]
    public void 重试或整轮结束不能把从未执行的准备卡片说成完成()
    {
        var message = new Message { Sender = ChatSender.Assistant };
        message.ApplyActivity(AgentEvent.Create("fixture", AgentEventKind.ToolCallPreparing) with
        { ToolCallId = "call", ToolName = "publish_recommendations" });
        message.ApplyActivity(AgentEvent.Create("fixture", AgentEventKind.StepStarted));
        Assert.Equal(ChatSegmentStatus.Cancelled, Assert.Single(message.Segments).Status);
        message.ApplyActivity(AgentEvent.Create("fixture", AgentEventKind.TaskCompleted));
        Assert.Equal(ChatSegmentStatus.Cancelled, Assert.Single(message.Segments).Status);
    }
}
