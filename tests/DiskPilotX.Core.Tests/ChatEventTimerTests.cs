using DiskPilotX.Core.Models;
using TinyHarness;
using Message = DiskPilotX.Core.Models.ChatMessage;

namespace DiskPilotX.Core.Tests;

public sealed class ChatEventTimerTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 2, 8, 0, 0, TimeSpan.FromHours(8));
    private static void Apply(Message message, AgentEventKind kind, int seconds, string? id = null, string? previous = null) =>
        message.ApplyActivity(AgentEvent.Create("timer", kind) with
        { Timestamp = Start.AddSeconds(seconds), ToolCallId = id, PreviousToolCallId = previous, ToolName = "scan_path", Text = "流式片段" });
    private static string CurrentTime(Message message) => message.ActivityDurationText;

    [Fact]
    public void 新工具从零计时且不改变整轮总耗时()
    {
        var message = new Message();
        Apply(message, AgentEventKind.SessionStarted, 0);
        Apply(message, AgentEventKind.ToolCallStarted, 49, "a");
        message.RefreshElapsed(Start.AddSeconds(49).LocalDateTime);
        Assert.Equal("0s", CurrentTime(message));
        Assert.Equal("49s", message.DurationText);
        message.RefreshElapsed(Start.AddSeconds(52).LocalDateTime);
        Assert.Equal("3s", CurrentTime(message));
        Apply(message, AgentEventKind.ToolCallStarted, 60, "b");
        message.RefreshElapsed(Start.AddSeconds(62).LocalDateTime);
        Assert.Equal("2s", CurrentTime(message));
        Assert.Equal("1m02s", message.DurationText);
    }

    [Fact]
    public void 同阶段分片和准备标识绑定不会反复清零()
    {
        var message = new Message();
        Apply(message, AgentEventKind.SessionStarted, 0);
        Apply(message, AgentEventKind.ReasoningDelta, 10);
        Apply(message, AgentEventKind.ReasoningDelta, 15);
        message.RefreshElapsed(Start.AddSeconds(18).LocalDateTime);
        Assert.Equal("8s", CurrentTime(message));
        Apply(message, AgentEventKind.ToolCallPreparing, 20, "preparing:a");
        Apply(message, AgentEventKind.ToolCallPreparing, 25, "preparing:a");
        Apply(message, AgentEventKind.ToolCallPreparing, 30, "call_a", "preparing:a");
        message.RefreshElapsed(Start.AddSeconds(33).LocalDateTime);
        Assert.Equal("13s", CurrentTime(message));
        Apply(message, AgentEventKind.ToolCallStarted, 35, "call_a");
        message.RefreshElapsed(Start.AddSeconds(37).LocalDateTime);
        Assert.Equal("2s", CurrentTime(message));
    }

    [Fact]
    public void 新等待与重试独立计时且结束后冻结()
    {
        var message = new Message();
        Apply(message, AgentEventKind.SessionStarted, 0);
        Apply(message, AgentEventKind.ToolCallStarted, 20, "a");
        Apply(message, AgentEventKind.ToolCallCompleted, 30, "a");
        message.RefreshElapsed(Start.AddSeconds(33).LocalDateTime);
        Assert.Equal("3s", CurrentTime(message));
        Apply(message, AgentEventKind.ModelRetrying, 40);
        message.RefreshElapsed(Start.AddSeconds(41).LocalDateTime);
        Assert.Equal("1s", CurrentTime(message));
        Apply(message, AgentEventKind.TaskCancelled, 45);
        message.RefreshElapsed(Start.AddSeconds(100).LocalDateTime);
        Assert.Equal("5s", CurrentTime(message));
        Assert.Equal("45s", message.DurationText);
    }

    [Fact]
    public void 进入和退出真实确认都重新计时而重复回调不清零()
    {
        var message = new Message();
        Apply(message, AgentEventKind.SessionStarted, 0);
        Apply(message, AgentEventKind.ToolCallStarted, 20, "a");
        message.SetWaitingForUser(true, Start.AddSeconds(40).LocalDateTime);
        Assert.Equal("0s", CurrentTime(message));
        message.RefreshElapsed(Start.AddSeconds(45).LocalDateTime);
        Assert.Equal("5s", CurrentTime(message));
        message.SetWaitingForUser(true, Start.AddSeconds(48).LocalDateTime);
        message.RefreshElapsed(Start.AddSeconds(49).LocalDateTime);
        Assert.Equal("9s", CurrentTime(message));
        message.SetWaitingForUser(false, Start.AddSeconds(50).LocalDateTime);
        Assert.Equal("0s", CurrentTime(message));
        message.RefreshElapsed(Start.AddSeconds(53).LocalDateTime);
        Assert.Equal("3s", CurrentTime(message));
        Assert.Equal("53s", message.DurationText);
    }
}
