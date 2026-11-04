using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using TinyHarness;

namespace DiskPilotX.Core.Tests;

public sealed class ToolPreparingEventTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task 准备事件先于执行且拒绝审批不报告执行(bool approve)
    {
        var ran = false;
        var tools = new ToolRegistry();
        tools.Register(new ToolDefinition
        {
            Name = "fixture", Description = "只读受控夹具", ParametersSchema = JsonNode.Parse("{\"type\":\"object\"}")!,
            RiskLevel = ToolRiskLevel.Write,
            Handler = (_, _) => { ran = true; return Task.FromResult(ToolResult.Ok("已完成")); }
        });
        var loop = new AgentLoop(new Client(), tools, NullLogger<AgentLoop>.Instance,
            approvalGate: (_, _, _) => Task.FromResult(approve));
        var kinds = new List<AgentEventKind>();
        await foreach (var item in loop.RunAsync(new AgentSession(), "受控测试"))
        {
            kinds.Add(item.Kind);
            if (item.Kind is AgentEventKind.ToolCallPreparing or AgentEventKind.ToolApprovalRequested or AgentEventKind.ToolCallStarted)
                Assert.False(ran);
        }
        Assert.Equal(approve, ran);
        Assert.Contains(AgentEventKind.ToolCallPreparing, kinds);
        Assert.Contains(AgentEventKind.ToolApprovalRequested, kinds);
        if (approve)
            Assert.True(kinds.IndexOf(AgentEventKind.ToolApprovalRequested) < kinds.IndexOf(AgentEventKind.ToolCallStarted));
        else Assert.DoesNotContain(AgentEventKind.ToolCallStarted, kinds);
    }

    private sealed class Client : IModelClient
    {
        private int step;
        public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (step++ == 0)
            {
                yield return new ModelToolCallPreparingEvent("call", "fixture");
                await Task.Yield();
                yield return new ModelToolCallEvent(new ToolCall("call", "fixture", "{}"));
                yield return new ModelCompletedEvent("tool_calls");
            }
            else
            {
                yield return new TextDeltaEvent("受控测试结束");
                yield return new ModelCompletedEvent("stop");
            }
        }
    }
}
