using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using FluentAssertions;
using TinyHarness;
using NSubstitute;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Agent;
using SpaceMonger.Core.Services.Assistant;
using SpaceMonger.Core.Services.Llm;
using SpaceMonger.Core.Services.FileTree;
using SpaceMonger.Core.Services.Settings;

namespace SpaceMonger.Core.Tests;

public class DiskAssistantServiceTests
{
    [Fact]
    public async Task ScanThenQuery_UsesNewContextAndNativeToolHistory()
    {
        AgentContext context = new(null, null, null, null);
        var workspace = new AssistantWorkspace
        {
            ReadContext = _ => Task.FromResult(context),
            Execute = (name, args, _) =>
            {
                name.Should().Be("scan_path");
                var root = new FileEntry { Path = @"D:\sandbox", Name = "sandbox", IsDirectory = true,
                    Children = [new() {Path = @"D:\sandbox\cache", Name = "cache", Size = 12, IsDirectory = true}] };
                context = new(new() { RootEntry = root, TargetPath = root.Path }, root, null, null);
                return Task.FromResult<JsonNode>(new JsonObject { ["ok"] = true });
            }
        };
        var client = new ScriptedClient();
        var settings = Substitute.For<ISettingsService>(); settings.LoadSettings().Returns(new AppSettings());
        var service = new DiskAssistantService(_ => client,
            [new ListChildrenTool(new FileTreeQueryService())], workspace, settings);
        var events = new List<AgentEvent>();
        var result = await service.RunAsync("扫描并查看目录", new(new ModelEndpointConfig { ApiKey = "key", Model = "test" }), events.Add, CancellationToken.None);
        result.State.Should().Be(AgentSessionState.Completed);
        result.Text.Should().Contain("完成");
        client.Requests.Should().HaveCount(3);
        client.Requests[1].Messages.Should().Contain(m => m.Role == ChatRole.Tool && m.ToolCallId == "scan");
        client.Requests[2].Messages.Should().Contain(m => m.Role == ChatRole.Tool && m.Content!.Contains("cache"));
        events.Should().Contain(e => e.Kind == AgentEventKind.TextDelta);
    }

    [Fact]
    public async Task CancelledRun_CanContinueWithoutDanglingToolCalls()
    {
        var client = new CancelThenCompleteClient();
        var settings = Substitute.For<ISettingsService>(); settings.LoadSettings().Returns(new AppSettings());
        var service = new DiskAssistantService(_ => client, [], new AssistantWorkspace(), settings);
        using var cts = new CancellationTokenSource();
        var first = service.RunAsync("第一轮", new(new ModelEndpointConfig { ApiKey = "key", Model = "test" }), e =>
        { if (e.Kind == AgentEventKind.TextDelta) cts.Cancel(); }, cts.Token);
        var cancelled = await first;
        cancelled.State.Should().Be(AgentSessionState.Cancelled);
        var next = await service.RunAsync("继续", new(new ModelEndpointConfig { ApiKey = "key", Model = "test" }), null, CancellationToken.None);
        next.State.Should().Be(AgentSessionState.Completed);
    }

    [Fact]
    public async Task PlainJsonText_DoesNotExecuteWorkspaceTool()
    {
        var executions = 0;
        var settings = Substitute.For<ISettingsService>(); settings.LoadSettings().Returns(new AppSettings());
        var workspace = new AssistantWorkspace { Execute = (_, _, _) =>
        { executions++; return Task.FromResult<JsonNode>(new JsonObject { ["ok"] = true }); }};
        var service = new DiskAssistantService(_ => new TextClient(), [], workspace, settings);
        await service.RunAsync("展示例子", new(new ModelEndpointConfig { ApiKey = "key", Model = "test" }), null, CancellationToken.None);
        executions.Should().Be(0);
    }

    [Fact]
    public async Task ModelFailure_SurfacesDetailThroughAnalysisEngine()
    {
        var settings = Substitute.For<ISettingsService>(); settings.LoadSettings().Returns(new AppSettings());
        var service = new DiskAssistantService(_ => new FailingClient(), [], new AssistantWorkspace(), settings);
        var session = new ScanSession { TargetPath = @"D:\sandbox", RootEntry = new FileEntry { Path = @"D:\sandbox", IsDirectory = true } };
        var engine = new HarnessRecommendationEngine(service);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.AnalyzeWithDiagnosticsAsync(session, new ModelEndpointConfig { ApiKey = "key", Model = "test" }, "zh-CN", CancellationToken.None));
        // 失败原因（FormatFailure 的友好摘要）必须出现在分析错误里，而不是只有笼统的"未发布有效结果"。
        error.Message.Should().Contain("分析未发布有效结果：");
        error.Message.Should().Contain("模型响应未能完成");
    }

    private sealed class FailingClient : IModelClient
    {
        public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request, [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            if (request.Messages.Count < 0) yield return new TextDeltaEvent("");
            throw new ModelClientException("模型响应无效或流未完整结束。 [stream-end]", ModelErrorKind.Unknown);
        }
    }

    private sealed class ScriptedClient : IModelClient
    {
        public List<ModelRequest> Requests { get; } = [];
        public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request, [EnumeratorCancellation] CancellationToken ct)
        {
            Requests.Add(request with { Messages = request.Messages.ToArray() });
            await Task.Yield();
            if (Requests.Count == 1) yield return new ModelToolCallEvent(new ToolCall("scan", "scan_path", System.Text.Json.JsonSerializer.Serialize(new {path = @"D:\sandbox"})));
            else if (Requests.Count == 2) yield return new ModelToolCallEvent(new ToolCall("children", "list_children", System.Text.Json.JsonSerializer.Serialize(new {path = @"D:\sandbox"})));
            else yield return new TextDeltaEvent("已完成查询。");
            yield return new ModelCompletedEvent(Requests.Count < 3 ? "tool_use" : "end_turn");
        }
    }
    private sealed class CancelThenCompleteClient : IModelClient
    {
        private int calls;
        public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request, [EnumeratorCancellation] CancellationToken ct)
        {
            if (++calls == 1) { yield return new TextDeltaEvent("处理中"); await Task.Delay(Timeout.Infinite, ct); }
            else { yield return new TextDeltaEvent("继续完成"); yield return new ModelCompletedEvent("end_turn"); }
        }
    }
    private sealed class TextClient : IModelClient
    {
        public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request, [EnumeratorCancellation] CancellationToken ct)
        { await Task.Yield(); yield return new TextDeltaEvent("{\"tool_calls\":[{\"name\":\"scan_path\"}]}"); yield return new ModelCompletedEvent("end_turn"); }
    }
}
