using System.ComponentModel;
using FluentAssertions;
using DiskPilotX.Core.Models;

namespace DiskPilotX.Core.Tests;

public class AskUserTranscriptTests
{
    private const string Arguments = """
        {
          "title": "确认清理策略",
          "question": "这批缓存怎么处理？",
          "fields": [
            {
              "id": "mode",
              "label": "「CLI 身份模式」的默认值取哪个？",
              "type": "single_select",
              "options": [
                { "id": "provider", "label": "Provider 级（推荐）", "description": "统一放在 Provider 层" },
                { "id": "model", "label": "Model 级", "description": "每个模型单独配置" }
              ]
            },
            {
              "id": "scope",
              "label": "哪些目录一起处理？",
              "type": "multi_select",
              "options": [
                { "id": "cache", "label": "缓存目录" },
                { "id": "logs", "label": "日志目录" }
              ]
            }
          ]
        }
        """;

    [Fact]
    public void 单选与多选映射为选项标题()
    {
        var segment = new ChatSegment(ChatSegmentKind.Tool)
        {
            ToolName = "ask_user",
            Detail = "参数：\n" + Arguments,
            Text = """{"cancelled":false,"values":{"mode":"provider","scope":["cache","logs"]},"custom_inputs":{}}"""
        };

        segment.IsAskUser.Should().BeTrue();
        segment.HasAskUserEntries.Should().BeTrue();
        segment.AskUserEntries.Should().Equal(
            new AskUserTranscriptEntry("「CLI 身份模式」的默认值取哪个？", "Provider 级（推荐）"),
            new AskUserTranscriptEntry("哪些目录一起处理？", "缓存目录、日志目录"));
    }

    [Fact]
    public void 自定义输入直接展示用户文本()
    {
        var segment = new ChatSegment(ChatSegmentKind.Tool)
        {
            ToolName = "ask_user",
            Detail = "参数：\n" + Arguments,
            Text = """{"cancelled":false,"values":{"mode":null,"scope":["cache"]},"custom_inputs":{"mode":"都不选，保持现状"}}"""
        };

        segment.AskUserEntries![0].Answer.Should().Be("都不选，保持现状");
        segment.AskUserEntries![1].Answer.Should().Be("缓存目录");
    }

    [Fact]
    public void 取消与等待中有明确状态()
    {
        var cancelled = new ChatSegment(ChatSegmentKind.Tool)
        {
            ToolName = "ask_user",
            Detail = "参数：\n" + Arguments,
            Text = """{"cancelled":true,"values":{},"custom_inputs":{}}"""
        };
        cancelled.AskUserEntries.Should().NotBeNull();
        cancelled.AskUserEntries!.Select(entry => entry.Answer).Should().AllBe("已取消");

        var pending = new ChatSegment(ChatSegmentKind.Tool)
        {
            ToolName = "ask_user",
            Detail = "参数：\n" + Arguments
        };
        pending.AskUserEntries.Should().NotBeNull();
        pending.AskUserEntries!.Select(entry => entry.Answer).Should().AllBe("…");
    }

    [Fact]
    public void 旧协议使用整体问题与文本回答()
    {
        var segment = new ChatSegment(ChatSegmentKind.Tool)
        {
            ToolName = "ask_user",
            Detail = "参数：\n{\"title\":\"补充信息\",\"question\":\"清理目标是什么？\"}",
            Text = """{"cancelled":false,"values":{"answer":"临时构建产物"},"custom_inputs":{}}"""
        };

        segment.AskUserEntries.Should().Equal(new AskUserTranscriptEntry("清理目标是什么？", "临时构建产物"));
    }

    [Fact]
    public void 解析失败回退原始展示且其他工具不受影响()
    {
        var broken = new ChatSegment(ChatSegmentKind.Tool) { ToolName = "ask_user", Detail = "参数：\n{损坏" };
        broken.AskUserEntries.Should().BeNull();
        broken.HasAskUserEntries.Should().BeFalse();

        var other = new ChatSegment(ChatSegmentKind.Tool) { ToolName = "scan_path", Detail = "参数：\n{}" };
        other.IsAskUser.Should().BeFalse();
        other.AskUserEntries.Should().BeNull();
    }

    [Fact]
    public void 结果回填时通知界面刷新()
    {
        var segment = new ChatSegment(ChatSegmentKind.Tool) { ToolName = "ask_user", Detail = "参数：\n" + Arguments };
        var raised = new List<string?>();
        segment.PropertyChanged += (_, args) => raised.Add(args.PropertyName);

        segment.Text = """{"cancelled":false,"values":{"mode":"model","scope":[]},"custom_inputs":{}}""";

        raised.Should().Contain(nameof(ChatSegment.AskUserEntries));
        raised.Should().Contain(nameof(ChatSegment.HasAskUserEntries));
        segment.AskUserEntries![0].Answer.Should().Be("Model 级");
    }

    [Fact]
    public void 多选同时保留选项标题与自定义输入()
    {
        var segment = new ChatSegment(ChatSegmentKind.Tool)
        {
            ToolName = "ask_user", Detail = "参数：\n" + Arguments,
            Text = """{"cancelled":false,"values":{"scope":["cache"]},"custom_inputs":{"scope":"另加临时构建目录"}}"""
        };
        segment.AskUserEntries![1].Answer.Should().Be("缓存目录、另加临时构建目录");
    }

    [Fact]
    public void 失败详情追加后仍能保留问题并展示友好状态()
    {
        var segment = new ChatSegment(ChatSegmentKind.Tool)
        {
            ToolName = "ask_user", Detail = "参数：\n" + Arguments + "\n工具执行失败",
            Text = """{"ok":false,"error":"表单未完成"}""", Status = ChatSegmentStatus.Failed
        };
        segment.AskUserEntries.Should().NotBeNull();
        segment.AskUserEntries![0].Question.Should().Be("「CLI 身份模式」的默认值取哪个？");
        segment.AskUserEntries[0].Answer.Should().Be("未能完成提问");
    }

    [Fact]
    public void 尚无工具结果时取消不再显示等待中()
    {
        var segment = new ChatSegment(ChatSegmentKind.Tool) { ToolName = "ask_user", Detail = "参数：\n" + Arguments };
        segment.AskUserEntries![0].Answer.Should().Be("…");
        segment.Status = ChatSegmentStatus.Cancelled;
        segment.AskUserEntries![0].Answer.Should().Be("已取消");
    }

    [Fact]
    public void 历史消息恢复后仍显示问答标题和用户输入()
    {
        var message = new ChatMessage();
        message.Segments.Add(new ChatSegment(ChatSegmentKind.Tool)
        {
            ToolName = "ask_user", Detail = "参数：\n" + Arguments,
            Text = """{"cancelled":false,"values":{"mode":"provider","scope":["logs"]},"custom_inputs":{}}""",
            Status = ChatSegmentStatus.Completed
        });
        var saved = DiskPilotX.Core.Services.Assistant.SavedChatMessage.Capture(message);
        var restored = saved.Restore().Segments.Single();
        restored.AskUserEntries![0].Answer.Should().Be("Provider 级（推荐）");
        restored.AskUserEntries[1].Answer.Should().Be("日志目录");
    }
}
