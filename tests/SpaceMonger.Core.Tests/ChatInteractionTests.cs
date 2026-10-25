using FluentAssertions;
using SpaceMonger.Core.Models;
using TinyHarness;
using Message = SpaceMonger.Core.Models.ChatMessage;
using ModelMessage = TinyHarness.ChatMessage;

namespace SpaceMonger.Core.Tests;

public class ChatInteractionTests
{
    [Fact]
    public void 完成时间不覆盖气泡创建时间()
    {
        var started = new DateTime(2026, 10, 26, 9, 0, 0);
        var message = new Message { Timestamp = started };
        message.MarkCompletedAt(started.AddSeconds(12));
        message.Timestamp.Should().Be(started);
        message.CompletedAtText.Should().Be("09:00");
    }

    [Fact]
    public void 最终正文完整输出后折叠前置过程()
    {
        var message = new Message();
        message.ApplyActivity(Event(AgentEventKind.StepStarted));
        message.ApplyActivity(Event(AgentEventKind.ReasoningDelta, "先检查中文目录"));
        message.ApplyActivity(Event(AgentEventKind.TextDelta, "最后答复"));
        foreach (var segment in message.Segments) segment.IsExpanded = true;
        message.ApplyActivity(Event(AgentEventKind.MessageCompleted) with { Message = ModelMessage.Assistant("最后答复") });
        message.Segments.Where(segment => !segment.IsText).Should().OnlyContain(segment => !segment.IsExpanded);
    }

    [Fact]
    public void 工具参数显示中文且保留路径转义()
    {
        var call = new ToolCall("工具1", "scan_path", System.Text.Json.JsonSerializer.Serialize(new { path = @"D:\中文目录", 备注 = new { 说明 = "查看缓存" } })) { ArgumentsAreSafe = true };
        var message = new Message();
        message.ApplyActivity(Event(AgentEventKind.MessageCompleted) with { Message = ModelMessage.AssistantToolCalls([call]) });
        message.Segments.Single().Detail.Should().Contain("中文目录").And.Contain("查看缓存").And.NotContain(@"\u");
        message.Segments.Single().Detail.Should().Contain(@"D:\\中文目录");
    }

    [Fact]
    public void 工具中间消息不提前结束且最终正文与过程分离()
    {
        var start = Event(AgentEventKind.SessionStarted);
        var message = new Message();
        message.ApplyActivity(start);
        message.ApplyActivity(Event(AgentEventKind.StepStarted));
        message.ApplyActivity(Event(AgentEventKind.ReasoningDelta, "分析"));
        message.ApplyActivity(Event(AgentEventKind.TextDelta, "先检查目录"));
        var call = new ToolCall("检查", "scan_path", "{}") { ArgumentsAreSafe = true };
        message.ApplyActivity(Event(AgentEventKind.MessageCompleted) with { Message = ModelMessage.AssistantToolCalls([call], "先检查目录") });
        message.CompletedAt.Should().BeNull();
        message.IsProcessExpanded.Should().BeTrue();
        message.RefreshElapsed(start.Timestamp.LocalDateTime.AddSeconds(4));
        message.DurationText.Should().Be("4s");
        message.ApplyActivity(Event(AgentEventKind.StepStarted));
        message.ApplyActivity(Event(AgentEventKind.TextDelta, "最终答复"));
        message.ApplyActivity(Event(AgentEventKind.MessageCompleted) with
        {
            Timestamp = start.Timestamp.AddSeconds(12), Message = ModelMessage.Assistant("最终答复")
        });
        message.ProcessSegments.Select(segment => segment.Text).Should().Contain("先检查目录");
        message.FinalSegments.Single().Text.Should().Be("最终答复");
        message.CopyText.Should().Be("最终答复");
        message.IsProcessExpanded.Should().BeFalse();
        message.IsProcessExpanded = true;
        message.ApplyActivity(Event(AgentEventKind.TaskCompleted) with { Timestamp = start.Timestamp.AddSeconds(15) });
        message.RefreshElapsed(start.Timestamp.LocalDateTime.AddMinutes(1));
        message.DurationText.Should().Be("12s");
        message.IsProcessExpanded.Should().BeTrue("后续终态事件不能覆盖用户重新展开的选择");
    }

    [Theory]
    [InlineData(AgentEventKind.TaskFailed, ChatSegmentStatus.Failed)]
    [InlineData(AgentEventKind.TaskCancelled, ChatSegmentStatus.Cancelled)]
    public void 失败与取消停止计时并保留真实状态(AgentEventKind kind, ChatSegmentStatus status)
    {
        var start = Event(AgentEventKind.SessionStarted);
        var message = new Message();
        message.ApplyActivity(start);
        message.ApplyActivity(Event(AgentEventKind.ReasoningDelta, "执行中"));
        message.ApplyActivity(Event(kind) with { Timestamp = start.Timestamp.AddSeconds(3), Detail = "任务未完成" });
        message.RefreshElapsed(start.Timestamp.LocalDateTime.AddMinutes(1));
        message.ProcessStatus.Should().Be(status);
        message.DurationText.Should().Be("3s");
        message.IsStreaming.Should().BeFalse();
        message.HasFinalContent.Should().BeFalse();
    }

    [Fact]
    public void 历史往返保留时间耗时与最终边界且兼容旧字段()
    {
        var message = new Message();
        var start = Event(AgentEventKind.SessionStarted);
        message.ApplyActivity(start);
        message.ApplyActivity(Event(AgentEventKind.ReasoningDelta, "核对"));
        message.ApplyActivity(Event(AgentEventKind.TextDelta, "结果"));
        message.ApplyActivity(Event(AgentEventKind.MessageCompleted) with
        {
            Timestamp = start.Timestamp.AddSeconds(8), Message = ModelMessage.Assistant("结果")
        });
        var captured = SpaceMonger.Core.Services.Assistant.SavedChatMessage.Capture(message);
        var json = System.Text.Json.JsonSerializer.Serialize(captured);
        var restored = System.Text.Json.JsonSerializer.Deserialize<SpaceMonger.Core.Services.Assistant.SavedChatMessage>(json)!.Restore();
        restored.Timestamp.Should().Be(message.Timestamp);
        restored.CompletedAt.Should().Be(message.CompletedAt);
        restored.DurationText.Should().Be("8s");
        restored.FinalSegments.Single().Text.Should().Be("结果");
        restored.ProcessSegments.Single().Text.Should().Be("核对");
        var old = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        old.Remove("CompletedAt"); old.Remove("ProcessStatus");
        foreach (var segment in old["Segments"]!.AsArray()) segment!.AsObject().Remove("IsFinal");
        var legacy = System.Text.Json.JsonSerializer.Deserialize<SpaceMonger.Core.Services.Assistant.SavedChatMessage>(old.ToJsonString())!.Restore();
        legacy.CopyText.Should().Be("结果");
        legacy.CompletedAt.Should().BeNull("不能伪造旧记录已经丢失的时间");
        legacy.ProcessSegments.Single().Text.Should().Be("核对");
    }

    [Fact]
    public void 不安全参数仍隐藏且无效文本保留原文()
    {
        var message = new Message();
        var call = new ToolCall("敏感", "scan_path", "{\"secret\":\"不能暴露\"}");
        message.ApplyActivity(Event(AgentEventKind.MessageCompleted) with { Message = ModelMessage.AssistantToolCalls([call]) });
        message.Segments.Single().Detail.Should().Contain("参数已隐藏").And.NotContain("不能暴露");
        ChatSegment.FormatJson("执行日志：不是 JSON").Should().Be("执行日志：不是 JSON");
        using var formatted = System.Text.Json.JsonDocument.Parse(ChatSegment.FormatJson("{\"说明\":\"缓存😀\"}"));
        formatted.RootElement.GetProperty("说明").GetString().Should().Be("缓存😀");
    }

    [Fact]
    public void Emoji直接可读而字面的Unicode路径与控制字符保持原语义()
    {
        var source = new { value = "缓存😀🚀", literal = @"\uD83D\uDE00", path = @"D:\uD83D\uDE00\中文目录", control = "\0\n\t\"" };
        var formatted = ChatSegment.FormatJson(System.Text.Json.JsonSerializer.Serialize(source));
        formatted.Should().Contain("缓存😀🚀");
        using var json = System.Text.Json.JsonDocument.Parse(formatted);
        json.RootElement.GetProperty("literal").GetString().Should().Be(source.literal);
        json.RootElement.GetProperty("path").GetString().Should().Be(source.path);
        json.RootElement.GetProperty("control").GetString().Should().Be(source.control);
    }

    [Theory]
    [InlineData(AgentSessionState.Cancelled, ChatSegmentStatus.Cancelled, false)]
    [InlineData(AgentSessionState.Failed, ChatSegmentStatus.Failed, false)]
    [InlineData(AgentSessionState.Running, ChatSegmentStatus.Cancelled, false)]
    [InlineData(AgentSessionState.Completed, ChatSegmentStatus.Completed, false)]
    [InlineData(AgentSessionState.Running, ChatSegmentStatus.Completed, true)]
    public void 旧会话根终态仅补齐最后助手且不覆盖之前成功轮次(AgentSessionState sessionState, ChatSegmentStatus expected, bool pendingUser)
    {
        SpaceMonger.Core.Services.Assistant.SavedChatMessage Saved(string text) => new(Guid.NewGuid(),
            SpaceMonger.Core.Enums.ChatSender.Assistant, text, "核对", DateTime.Now, "09:00", false,
            [new(ChatSegmentKind.Thinking, "核对", null, null, null, ChatSegmentStatus.Completed, false),
             new(ChatSegmentKind.Text, text, null, null, null, ChatSegmentStatus.Completed, false),
             new(ChatSegmentKind.Tool, "已有工具结果", null, "scan_path", "工具", ChatSegmentStatus.Completed, false)]);
        var snapshot = new SpaceMonger.Core.Services.Assistant.ConversationSnapshot(Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow, null, sessionState, [], [Saved("之前的成功轮次"), Saved("最后一个轮次")]);
        if (pendingUser) snapshot = snapshot with { Messages = [.. snapshot.Messages, new(Guid.NewGuid(), SpaceMonger.Core.Enums.ChatSender.User, "新问题", "", DateTime.Now, "", false, [])] };
        var node = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(snapshot))!;
        foreach (var message in node["Messages"]!.AsArray())
        {
            message!.AsObject().Remove("ProcessStatus"); message.AsObject().Remove("CompletedAt");
            foreach (var segment in message["Segments"]!.AsArray()) segment!.AsObject().Remove("IsFinal");
        }
        var legacy = System.Text.Json.JsonSerializer.Deserialize<SpaceMonger.Core.Services.Assistant.ConversationSnapshot>(node.ToJsonString())!;
        var settings = NSubstitute.Substitute.For<SpaceMonger.Core.Services.Settings.ISettingsService>();
        var service = new SpaceMonger.Core.Services.Assistant.DiskAssistantService(_ => throw new InvalidOperationException("还原历史不应请求模型"),
            [], new SpaceMonger.Core.Services.Assistant.AssistantWorkspace(), settings);
        var restored = service.RestoreConversation(legacy);
        restored[0].ProcessStatus.Should().Be(ChatSegmentStatus.Completed);
        restored[1].ProcessStatus.Should().Be(expected);
        restored[1].Segments.Select(segment => segment.Text).Should().Equal("核对", "最后一个轮次", "已有工具结果");
        restored[1].CompletedAt.Should().BeNull();
    }

    private static AgentEvent Event(AgentEventKind kind, string? text = null) =>
        new("会话", kind, new DateTimeOffset(2026, 10, 26, 9, 0, 12, TimeSpan.FromHours(8))) { Text = text };
}

