using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using TinyHarness;

namespace DiskPilotX.Core.Tests;

public sealed class ToolPreparationBoundaryTests
{
    [Fact]
    public async Task 只有准备元数据时短暂断线仍安全重试()
    {
        var client = new RetryClient();
        var tools = Registry("fixture", () => ToolResult.Ok("完成"));
        var session = new AgentSession();
        var message = new DiskPilotX.Core.Models.ChatMessage();
        var loop = new AgentLoop(client, tools, NullLogger<AgentLoop>.Instance, retryDelay: (_, _) => Task.CompletedTask);
        await foreach (var item in loop.RunAsync(session, "只读重试测试")) message.ApplyActivity(item);
        Assert.Equal(2, client.Calls);
        Assert.Equal(AgentSessionState.Completed, session.State);
        Assert.Equal(DiskPilotX.Core.Models.ChatSegmentStatus.Cancelled, Assert.Single(message.Segments.Where(s => s.IsTool)).Status);
    }

    [Fact]
    public async Task 复用已取消问答不报告再次执行()
    {
        var calls = 0;
        var tools = Registry("ask_user", () => { calls++; return ToolResult.Ok("{\"cancelled\":true}"); });
        var loop = new AgentLoop(new RepeatedClient(), tools, NullLogger<AgentLoop>.Instance);
        var events = new List<AgentEvent>();
        await foreach (var item in loop.RunAsync(new AgentSession(), "受控取消测试")) events.Add(item);
        Assert.Equal(1, calls);
        Assert.Single(events.Where(e => e.Kind == AgentEventKind.ToolCallStarted));
    }

    [Fact]
    public async Task 重复完整调用标识只保留一张真实结果卡片()
    {
        var calls = 0;
        var message = new DiskPilotX.Core.Models.ChatMessage();
        var loop = new AgentLoop(new DuplicateClient(), Registry("fixture", () => { calls++; return ToolResult.Ok("真实结果"); }), NullLogger<AgentLoop>.Instance);
        await foreach (var item in loop.RunAsync(new AgentSession(), "受控重复扇出")) message.ApplyActivity(item);
        Assert.Equal(1, calls);
        var tool = Assert.Single(message.Segments.Where(s => s.IsTool));
        Assert.Equal("call_1", tool.ToolCallId);
        Assert.Equal("真实结果", tool.Text);
        Assert.Single(DiskPilotX.Core.Services.Assistant.SavedChatMessage.Capture(message).Restore().Segments.Where(s => s.IsTool));
    }
    private sealed class DuplicateClient : IModelClient
    {
        private int step;
        public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request, [EnumeratorCancellation] CancellationToken token)
        {
            await Task.Yield();
            if (step++ == 0)
            {
                yield return new ModelToolCallPreparingEvent("preparing:a", "fixture");
                yield return new ModelToolCallPreparingEvent("preparing:b", "fixture");
                yield return new ModelToolCallEvent(new ToolCall("call_1", "fixture", "{}"), "preparing:a");
                yield return new ModelToolCallEvent(new ToolCall("call_1", "fixture", "{}"), "preparing:b");
                yield return new ModelCompletedEvent("tool_calls");
            }
            else
            {
                yield return new TextDeltaEvent("重复调用已去重");
                yield return new ModelCompletedEvent("stop");
            }
        }
    }

    private static ToolRegistry Registry(string name, Func<ToolResult> action)
    {
        var tools = new ToolRegistry();
        tools.Register(new ToolDefinition { Name = name, Description = "受控只读夹具", ParametersSchema = new JsonObject(), Handler = (_, _) => Task.FromResult(action()) });
        return tools;
    }
    private sealed class RetryClient : IModelClient
    {
        public int Calls;
        public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request, [EnumeratorCancellation] CancellationToken token)
        {
            await Task.Yield();
            if (++Calls == 1)
            {
                yield return new ModelToolCallPreparingEvent("call", "fixture");
                throw new ModelClientException("受控连接中断", ModelErrorKind.ConnectionFailed);
            }
            yield return new TextDeltaEvent("恢复后结束，没有执行工具");
            yield return new ModelCompletedEvent("stop");
        }
    }
    private sealed class RepeatedClient : IModelClient
    {
        private int step;
        public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request, [EnumeratorCancellation] CancellationToken token)
        {
            await Task.Yield();
            if (++step <= 2) yield return new ModelToolCallEvent(new ToolCall("call" + step, "ask_user", "{}"));
            else yield return new TextDeltaEvent("已停止重复请求");
            yield return new ModelCompletedEvent(step <= 2 ? "tool_calls" : "stop");
        }
    }
}
