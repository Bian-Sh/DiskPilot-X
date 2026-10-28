using DiskPilotX.Core.Models;
using TinyHarness;

namespace DiskPilotX.Core.Tests;

public sealed class ThinkingDisplayStateTests
{
    [Fact]
    public void 新思考默认折叠且完整保留所有增量()
    {
        var message = new DiskPilotX.Core.Models.ChatMessage();
        message.ApplyActivity(AgentEvent.Create("测试", AgentEventKind.ReasoningDelta) with { Text = "先检查" });
        message.ApplyActivity(AgentEvent.Create("测试", AgentEventKind.ReasoningDelta) with { Text = "再核对" });
        var thinking = Assert.Single(message.Segments);
        Assert.False(thinking.IsExpanded);
        Assert.Equal("先检查再核对", thinking.Text);
        Assert.Equal(thinking.Text, message.Thinking);
    }
}
