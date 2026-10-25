using FluentAssertions;
using SpaceMonger.Core.Enums;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Assistant;
using TinyHarness;
using Message = SpaceMonger.Core.Models.ChatMessage;
using ModelMessage = TinyHarness.ChatMessage;

namespace SpaceMonger.Core.Tests;

public sealed class ConversationStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "SpaceMonger-快照测试-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void 往返保留消息与工具分段但排除运行态交互及磁盘引用()
    {
        var message = new Message { Sender = ChatSender.Assistant, Text = "结果", Thinking = "思考",
            Timestamp = DateTime.Now, IsStreaming = true, LinkedEntry = new FileEntry { Path = "磁盘引用不得保存" } };
        message.Segments.Add(new ChatSegment(ChatSegmentKind.Tool) { ToolName = "scan_path", ToolCallId = "工具1",
            Text = "工具结果", Detail = "参数说明", Status = ChatSegmentStatus.WaitingForUser });
        var user = new Message { Sender = ChatSender.User, Text = "  第一行\n第二行\t标题  " };
        var snapshot = Snapshot([user, message]);
        var store = new ConversationStore(directory);
        store.Save(snapshot);
        var restored = store.Load(snapshot.Id).Messages.Last().Restore();
        restored.Text.Should().Be("结果");
        restored.Thinking.Should().Be("思考");
        restored.IsStreaming.Should().BeFalse();
        restored.LinkedEntry.Should().BeNull();
        restored.InteractionCard.Should().BeNull();
        restored.Segments.Single().Status.Should().Be(ChatSegmentStatus.Cancelled);
        restored.Segments.Single().ToolCallId.Should().Be("工具1");
        restored.Segments.Single().Detail.Should().Be("参数说明");
        File.ReadAllText(Path.Combine(directory, snapshot.Id + ".json")).Should().NotContain("磁盘引用不得保存");
        store.List().Single().Title.Should().Be("第一行 第二行 标题");
        Directory.GetFiles(directory, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void 历史按更新时间排序且跳过损坏文件与不完整结构()
    {
        var store = new ConversationStore(directory);
        var old = Snapshot([new Message { Sender = ChatSender.User, Text = "旧对话" }]) with { UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1) };
        var recent = Snapshot([new Message { Sender = ChatSender.User, Text = new string('长', 80) }]);
        store.Save(recent);
        store.Save(old);
        File.WriteAllText(Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json"), "{");
        File.WriteAllText(Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json"), "{}");
        store.List().Select(item => item.Id).Should().Equal(recent.Id, old.Id);
        store.List().First().Title.Should().HaveLength(41);
    }

    [Fact]
    public void 空对话不创建文件且无效标识不能跨目录访问()
    {
        var store = new ConversationStore(directory);
        store.Save(Snapshot([]));
        Directory.Exists(directory).Should().BeFalse();
        var load = () => store.Load("../其他目录");
        load.Should().Throw<InvalidDataException>();
    }

    private static ConversationSnapshot Snapshot(IEnumerable<Message> messages) => new(Guid.NewGuid().ToString("N"),
        DateTimeOffset.UtcNow, "[]", AgentSessionState.Running, [ModelMessage.User("模型问题")],
        messages.Select(SavedChatMessage.Capture).ToArray());

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
