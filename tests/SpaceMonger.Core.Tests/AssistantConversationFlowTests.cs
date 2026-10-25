using System.Runtime.CompilerServices;
using FluentAssertions;
using TinyHarness;
using NSubstitute;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Agent;
using SpaceMonger.Core.Services.Assistant;
using SpaceMonger.Core.Services.Llm;
using SpaceMonger.Core.Services.Settings;

namespace SpaceMonger.Core.Tests;

/// <summary>扫描取消反馈、对话内分析与白名单隐私签名的行为契约。</summary>
public class AssistantConversationFlowTests
{
    [Fact]
    public async Task UserCancelledScan_FeedsCancelledResultToModelAndBlocksSameRunRetry()
    {
        var executions = 0;
        var workspace = new AssistantWorkspace
        {
            ReadContext = _ => Task.FromResult(new AgentContext(null, null, null, null)),
            Execute = (_, _, _) => { executions++; throw new OperationCancelledByUserException("用户中断了扫描。"); }
        };
        var client = new ScriptedScanClient();
        var settings = Substitute.For<ISettingsService>(); settings.LoadSettings().Returns(new AppSettings());
        var service = new DiskAssistantService(_ => client, [], workspace, settings);

        var run = await service.RunAsync("扫描 D 盘", new(new ModelEndpointConfig { ApiKey = "key", Model = "test" }), null, CancellationToken.None);

        run.State.Should().Be(AgentSessionState.Completed);
        // 用户取消后同回合重试被短路，工作区只执行过一次。
        executions.Should().Be(1);
        var firstResult = client.Requests[1].Messages.Single(m => m.Role == ChatRole.Tool && m.ToolCallId == "scan1");
        firstResult.Content.Should().Contain("\"cancelled\":true").And.Contain("用户中断了该操作");
        var retryResult = client.Requests[2].Messages.Single(m => m.Role == ChatRole.Tool && m.ToolCallId == "scan2");
        retryResult.Content.Should().Contain("不再请求批准");
    }

    [Fact]
    public async Task InConversationAnalysis_SharesHistoryWhileIsolatedAnalysisStaysOut()
    {
        var requests = new List<ModelRequest>();
        var settings = Substitute.For<ISettingsService>(); settings.LoadSettings().Returns(new AppSettings());
        var service = new DiskAssistantService(_ => new CounterClient(requests), [], new AssistantWorkspace(), settings);

        await service.RunAsync("第一轮对话", new(new ModelEndpointConfig { ApiKey = "key", Model = "chat" }), null, CancellationToken.None);
        await service.RunAsync("对话内分析", new(new ModelEndpointConfig { ApiKey = "key", Model = "chat" }, IsAnalysis: true, InConversationSession: true), null, CancellationToken.None);
        await service.RunAsync("继续对话", new(new ModelEndpointConfig { ApiKey = "key", Model = "chat" }), null, CancellationToken.None);

        // 对话内分析轮次留在会话历史里，后续对话可见。
        requests[2].Messages.Should().Contain(m => m.Content!.Contains("标记-2"));

        // 默认（隔离）分析不进入对话历史。
        await service.RunAsync("独立分析", new(new ModelEndpointConfig { ApiKey = "key", Model = "chat" }, IsAnalysis: true), null, CancellationToken.None);
        await service.RunAsync("收尾对话", new(new ModelEndpointConfig { ApiKey = "key", Model = "chat" }), null, CancellationToken.None);
        requests[4].Messages.Should().NotContain(m => m.Content!.Contains("标记-4"));
        requests[4].Messages.Should().Contain(m => m.Content!.Contains("标记-2"));
    }

    [Fact]
    public async Task ChangedAnalysisWhitelistAlsoResetsConversation()
    {
        var saved = new AppSettings();
        var settings = Substitute.For<ISettingsService>(); settings.LoadSettings().Returns(saved);
        var requests = new List<ModelRequest>();
        var service = new DiskAssistantService(_ => new CounterClient(requests), [], new AssistantWorkspace(), settings);

        await service.RunAsync("第一轮", new(new ModelEndpointConfig { ApiKey = "key", Model = "chat" }), null, CancellationToken.None);
        saved.CleanupRecommendationWhitelist.Add(new() { Path = @"D:\analysis-hidden" });
        await service.RunAsync("规则变更后的对话", new(new ModelEndpointConfig { ApiKey = "key", Model = "chat" }), null, CancellationToken.None);

        // 任一白名单变更都必须丢弃旧会话，分析证据不能跨白名单重放。
        requests[1].Messages.Should().NotContain(m => m.Content!.Contains("标记-1"));
    }

    private sealed class ScriptedScanClient : IModelClient
    {
        public List<ModelRequest> Requests { get; } = [];
        public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request, [EnumeratorCancellation] CancellationToken ct)
        {
            Requests.Add(request with { Messages = request.Messages.ToArray() });
            await Task.Yield();
            if (Requests.Count == 1) yield return new ModelToolCallEvent(new ToolCall("scan1", "scan_path", System.Text.Json.JsonSerializer.Serialize(new { path = @"D:\data" })));
            else if (Requests.Count == 2) yield return new ModelToolCallEvent(new ToolCall("scan2", "scan_path", System.Text.Json.JsonSerializer.Serialize(new { path = @"D:\data" })));
            else yield return new TextDeltaEvent("扫描已被中断，等待你的指示。");
            yield return new ModelCompletedEvent(Requests.Count < 3 ? "tool_use" : "end_turn");
        }
    }

    private sealed class CounterClient(List<ModelRequest> requests) : IModelClient
    {
        public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request, [EnumeratorCancellation] CancellationToken ct)
        {
            requests.Add(request with { Messages = request.Messages.ToArray() });
            await Task.Yield();
            yield return new TextDeltaEvent("标记-" + requests.Count);
            yield return new ModelCompletedEvent("end_turn");
        }
    }
}
