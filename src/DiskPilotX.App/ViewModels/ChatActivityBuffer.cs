using System.Text;
using System.Windows.Threading;
using TinyHarness;

namespace DiskPilotX.App.ViewModels;

/// <summary>流读取线程只入队；按短时间窗口合并连续增量，语义边界保持顺序并及时冲刷。</summary>
public sealed class ChatActivityBuffer
{
    private readonly Action<AgentEvent> apply;
    private readonly Dispatcher? dispatcher;
    private readonly DispatcherTimer? timer;
    private readonly object gate = new();
    private readonly Queue<AgentEvent> pending = new();
    private bool scheduled;

    public ChatActivityBuffer(Action<AgentEvent> apply, Dispatcher? dispatcher)
    {
        this.apply = apply;
        this.dispatcher = dispatcher;
        if (dispatcher is null) return;
        timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(80) };
        timer.Tick += (_, _) => Flush();
    }

    public void Enqueue(AgentEvent activity)
    {
        // 无应用 Dispatcher 的无头宿主沿用同步投影，不创建无主定时器。
        if (dispatcher is null) { apply(activity); return; }
        bool start;
        lock (gate)
        {
            pending.Enqueue(activity);
            start = !scheduled;
            scheduled = true;
        }
        if (!IsDelta(activity)) _ = dispatcher.InvokeAsync(Flush, DispatcherPriority.Normal);
        else if (start) _ = dispatcher.InvokeAsync(() => timer!.Start(), DispatcherPriority.Background);
    }

    /// <summary>结束、保存或切换会话前在 UI 线程冲刷，确保最后一批增量不丢失。</summary>
    public void Flush()
    {
        dispatcher?.VerifyAccess();
        timer?.Stop();
        AgentEvent[] batch;
        lock (gate)
        {
            batch = pending.ToArray();
            pending.Clear();
            scheduled = false;
        }
        for (var index = 0; index < batch.Length; index++)
        {
            var activity = batch[index];
            if (IsDelta(activity))
            {
                var text = new StringBuilder(activity.Text);
                while (index + 1 < batch.Length && CanMerge(activity, batch[index + 1]))
                    text.Append(batch[++index].Text);
                activity = activity with { Text = text.ToString() };
            }
            apply(activity);
        }
    }

    private static bool IsDelta(AgentEvent activity) => activity.Kind is AgentEventKind.TextDelta or AgentEventKind.ReasoningDelta;
    private static bool CanMerge(AgentEvent left, AgentEvent right) => left.Kind == right.Kind
        && left.SessionId == right.SessionId && left.Step == right.Step && left.IsSummary == right.IsSummary
        && left.ToolCallId == right.ToolCallId && left.ToolName == right.ToolName;
}
