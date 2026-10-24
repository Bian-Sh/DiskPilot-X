using FluentAssertions;
using TinyHarness;
using SpaceMonger.Core.Models;
using ChatMessage = SpaceMonger.Core.Models.ChatMessage;
using HarnessMessage = TinyHarness.ChatMessage;

namespace SpaceMonger.Core.Tests;

public class ChatSegmentTests
{
    [Fact]
    public void Activity_PreservesReasoningTextAndToolOrderWithoutMergingAcrossTools()
    {
        var message = new ChatMessage();
        message.ApplyActivity(Activity(AgentEventKind.ReasoningDelta, "先检查"));
        message.ApplyActivity(Activity(AgentEventKind.TextDelta, "正在查看"));
        message.ApplyActivity(Activity(AgentEventKind.TextDelta, "目录。"));
        message.ApplyActivity(Tool(AgentEventKind.ToolCallStarted, "scan-1"));
        message.ApplyActivity(Activity(AgentEventKind.TextDelta, "发现缓存。"));
        message.ApplyActivity(Activity(AgentEventKind.ReasoningDelta, "再核对"));

        message.Segments.Select(segment => segment.Kind).Should().Equal(
            ChatSegmentKind.Thinking, ChatSegmentKind.Text, ChatSegmentKind.Tool,
            ChatSegmentKind.Text, ChatSegmentKind.Thinking);
        message.Segments[1].Text.Should().Be("正在查看目录。");
        message.Text.Should().Be("正在查看目录。发现缓存。");
        message.Thinking.Should().Be("先检查再核对");
    }

    [Fact]
    public void ToolResults_UpdateMatchingCallAndNeverBecomeAssistantText()
    {
        var message = new ChatMessage();
        message.ApplyActivity(Tool(AgentEventKind.ToolCallStarted, "first"));
        message.ApplyActivity(Tool(AgentEventKind.ToolCallStarted, "second"));
        message.ApplyActivity(Activity(AgentEventKind.MessageCompleted) with
        {
            Message = new HarnessMessage(ChatRole.Tool, "{\"bytes\":42}")
                { ToolCallId = "second", ToolName = "scan" }
        });
        message.ApplyActivity(Tool(AgentEventKind.ToolCallCompleted, "second") with { Success = true });
        message.ApplyActivity(Tool(AgentEventKind.ToolCallCompleted, "first") with
            { Success = false, Detail = "访问被拒绝" });

        message.Segments.Should().HaveCount(2);
        message.Segments[0].Status.Should().Be(ChatSegmentStatus.Failed);
        message.Segments[0].Detail.Should().Be("访问被拒绝");
        message.Segments[1].Text.Should().Be("{\"bytes\":42}");
        message.Segments[1].Status.Should().Be(ChatSegmentStatus.Completed);
        message.Text.Should().BeEmpty();
    }

    [Fact]
    public void IncrementalUpdates_NotifyBindingsAndPreserveUserExpansionChoice()
    {
        var message = new ChatMessage();
        message.ApplyActivity(Activity(AgentEventKind.ReasoningDelta, "思考"));
        var thinking = message.Segments.Single();
        thinking.IsExpanded = true;
        thinking.IsExpanded = false;
        var changed = new List<string?>();
        thinking.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        message.ApplyActivity(Activity(AgentEventKind.ReasoningDelta, "继续"));

        thinking.Text.Should().Be("思考继续");
        thinking.IsExpanded.Should().BeFalse();
        changed.Should().Contain(nameof(ChatSegment.Text));
        changed.Should().Contain(nameof(ChatSegment.Preview));

        message.ApplyActivity(Tool(AgentEventKind.ToolCallStarted, "scan-1"));
        var tool = message.Segments.Last();
        tool.IsExpanded = true;
        message.ApplyActivity(Activity(AgentEventKind.MessageCompleted) with
        {
            Message = new HarnessMessage(ChatRole.Tool, "结果") { ToolCallId = "scan-1" }
        });
        message.ApplyActivity(Tool(AgentEventKind.ToolCallCompleted, "scan-1") with { Success = true });
        message.ApplyActivity(Activity(AgentEventKind.TaskCompleted));
        tool.IsExpanded.Should().BeTrue();
        thinking.IsExpanded.Should().BeFalse();
    }

    [Theory]
    [InlineData(AgentEventKind.TaskCompleted, ChatSegmentStatus.Completed)]
    [InlineData(AgentEventKind.TaskFailed, ChatSegmentStatus.Failed)]
    [InlineData(AgentEventKind.TaskCancelled, ChatSegmentStatus.Cancelled)]
    public void TaskTerminalEvent_ClosesRunningSegments(AgentEventKind kind, ChatSegmentStatus expected)
    {
        var message = new ChatMessage();
        message.ApplyActivity(Activity(AgentEventKind.ReasoningDelta, "检查"));
        message.ApplyActivity(Tool(AgentEventKind.ToolCallStarted, "scan-1"));
        message.ApplyActivity(Activity(kind) with { Detail = "任务已结束" });

        message.IsStreaming.Should().BeFalse();
        message.Segments.Last().Status.Should().Be(expected);
        message.Segments.Should().OnlyContain(segment => !segment.IsRunning);
        message.HasCompletedAtText.Should().BeTrue();
        message.IsError.Should().Be(kind == AgentEventKind.TaskFailed);
    }

    [Fact]
    public void AssistantCompletedSnapshot_DoesNotDuplicateStreamedText()
    {
        var message = new ChatMessage();
        message.ApplyActivity(Activity(AgentEventKind.TextDelta, "已检查"));
        message.ApplyActivity(Activity(AgentEventKind.MessageCompleted) with
            { Message = HarnessMessage.Assistant("已检查") });
        message.ApplyActivity(Tool(AgentEventKind.ToolCallStarted, "scan-1"));
        message.ApplyActivity(Activity(AgentEventKind.StepStarted));
        message.ApplyActivity(Activity(AgentEventKind.MessageCompleted) with
            { Message = HarnessMessage.Assistant("结论") });

        message.Text.Should().Be("已检查结论");
        message.Segments.Select(segment => segment.Kind).Should().Equal(
            ChatSegmentKind.Text, ChatSegmentKind.Tool, ChatSegmentKind.Text);
    }

    [Fact]
    public void ToolCompletedBeforeResult_KeepsStatusAndExpansionWhenResultArrives()
    {
        var message = new ChatMessage();
        message.ApplyActivity(Tool(AgentEventKind.ToolCallCompleted, "scan-1") with { Success = false });
        var tool = message.Segments.Single();
        tool.IsExpanded = true;
        message.ApplyActivity(Activity(AgentEventKind.MessageCompleted) with
        {
            Message = new HarnessMessage(ChatRole.Tool, "{\"error\":\"denied\"}") { ToolCallId = "scan-1" }
        });

        message.Segments.Should().ContainSingle().Which.Should().BeSameAs(tool);
        tool.Status.Should().Be(ChatSegmentStatus.Failed);
        tool.IsExpanded.Should().BeTrue();
        tool.Text.Should().Be("{\"error\":\"denied\"}");
        message.Text.Should().BeEmpty();
    }

    private static AgentEvent Activity(AgentEventKind kind, string? text = null) =>
        AgentEvent.Create("session", kind) with { Text = text };

    private static AgentEvent Tool(AgentEventKind kind, string callId) =>
        Activity(kind) with { ToolCallId = callId, ToolName = "scan" };
}
