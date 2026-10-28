using System.Windows.Threading;
using DiskPilotX.App.ViewModels;
using TinyHarness;

namespace DiskPilotX.App.Tests;

[Collection("聊天滚动控件")]
public sealed class ChatActivityBufferTests
{
    [Fact]
    public void UI忙时生产线程不阻塞且冲刷保留增量和结束顺序() => StaHost.Run(() =>
    {
        var applied = new List<AgentEvent>();
        var buffer = new ChatActivityBuffer(applied.Add, Dispatcher.CurrentDispatcher);
        var producer = new Thread(() =>
        {
            buffer.Enqueue(AgentEvent.Create("测试", AgentEventKind.SessionStarted));
            for (var index = 0; index < 10000; index++)
                buffer.Enqueue(AgentEvent.Create("测试", AgentEventKind.ReasoningDelta) with { Text = "思" });
            buffer.Enqueue(AgentEvent.Create("测试", AgentEventKind.ReasoningDelta) with { Text = "摘要", IsSummary = true });
            buffer.Enqueue(AgentEvent.Create("测试", AgentEventKind.TextDelta) with { Text = "结论" });
            buffer.Enqueue(AgentEvent.Create("测试", AgentEventKind.TaskFailed) with { Detail = "受控失败" });
        });
        producer.Start();
        Assert.True(producer.Join(TimeSpan.FromSeconds(2)), "UI 不处理消息期间也不能阻塞流式生产线程");
        Assert.Empty(applied);
        buffer.Flush();
        Assert.Equal(5, applied.Count);
        Assert.Equal(AgentEventKind.SessionStarted, applied[0].Kind);
        Assert.Equal(new string('思', 10000), applied[1].Text);
        Assert.True(applied[2].IsSummary);
        Assert.Equal("摘要", applied[2].Text);
        Assert.Equal("结论", applied[3].Text);
        Assert.Equal(AgentEventKind.TaskFailed, applied[4].Kind);
        buffer.Flush();
        Assert.Equal(5, applied.Count);
    });
    [Fact]
    public void 纯增量按时间窗口自动刷新且会话和步骤边界不合并() => StaHost.Run(() =>
    {
        var applied = new List<AgentEvent>();
        var buffer = new ChatActivityBuffer(applied.Add, Dispatcher.CurrentDispatcher);
        for (var index = 0; index < 100; index++)
            buffer.Enqueue(AgentEvent.Create("甲", AgentEventKind.ReasoningDelta) with { Text = "字", Step = 1 });
        buffer.Enqueue(AgentEvent.Create("甲", AgentEventKind.ReasoningDelta) with { Text = "下一步", Step = 2 });
        buffer.Enqueue(AgentEvent.Create("乙", AgentEventKind.ReasoningDelta) with { Text = "另一会话", Step = 2 });
        Assert.Empty(applied);
        var until = DateTime.UtcNow.AddSeconds(2);
        while (applied.Count == 0 && DateTime.UtcNow < until)
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Thread.Sleep(5);
        }
        Assert.Equal(3, applied.Count);
        Assert.Equal(new string('字', 100), applied[0].Text);
        Assert.Equal("下一步", applied[1].Text);
        Assert.Equal("乙", applied[2].SessionId);
    });

}
