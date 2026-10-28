using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TinyHarness;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Llm;
using DiskPilotX.Core.Services.Settings;

namespace DiskPilotX.Core.Tests;

[Collection("空闲超时串行")]
public sealed class ModelIdleTimeoutTests
{
    [Fact]
    public void 默认等待新模型消息的空闲期限为五分钟() =>
        Assert.Equal(TimeSpan.FromMinutes(5), new AgentSessionOptions().ModelTimeout);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 持续推送超过空闲期限仍完成且不计本地消费耗时(bool slowConsumer)
    {
        var client = new FlowClient(false);
        var session = new AgentSession(new() { MaxSteps = 1, ModelTimeout = TimeSpan.FromSeconds(1) });
        var loop = Loop(client);
        using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var events = new List<AgentEvent>();
        await foreach (var activity in loop.RunAsync(session, "测试", watchdog.Token))
        {
            events.Add(activity);
            if (slowConsumer && activity.Kind == AgentEventKind.ReasoningDelta && events.Count(e => e.Kind == AgentEventKind.ReasoningDelta) == 1) await Task.Delay(1200, watchdog.Token);
        }
        Assert.Equal(AgentSessionState.Completed, session.State);
        Assert.Equal(20, events.Count(e => e.Kind == AgentEventKind.ReasoningDelta));
        Assert.Equal(1, client.Calls);
    }

    [Fact]
    public async Task 收到消息后静默超过期限会终止且不重复已输出内容()
    {
        var client = new FlowClient(true);
        var session = new AgentSession(new() { ModelTimeout = TimeSpan.FromMilliseconds(200) });
        var elapsed = Stopwatch.StartNew();
        using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = new List<AgentEvent>();
        await foreach (var activity in Loop(client).RunAsync(session, "测试断流", watchdog.Token)) events.Add(activity);
        Assert.Equal(AgentSessionState.Failed, session.State);
        Assert.Contains(events, e => e.Kind == AgentEventKind.TaskFailed && e.Detail!.Contains("超时"));
        Assert.InRange(elapsed.Elapsed.TotalSeconds, .1, 3);
        Assert.Equal(1, client.Calls);
    }

    [Fact]
    public async Task 尚未聚合成工具调用的分片消息也刷新空闲计时()
    {
        var session = new AgentSession(new() { MaxSteps = 1, ModelTimeout = TimeSpan.FromSeconds(1) });
        using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var _ in Loop(new PartialClient()).RunAsync(session, "参数分片", watchdog.Token)) { }
        Assert.Equal(AgentSessionState.Completed, session.State);
    }

    [Fact]
    public async Task 首次等待静默达到期限即失败不被重试隐式延长()
    {
        var client = new SilentClient();
        var session = new AgentSession(new() { ModelTimeout = TimeSpan.FromMilliseconds(100) });
        using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var _ in Loop(client).RunAsync(session, "无消息", watchdog.Token)) { }
        Assert.Equal(AgentSessionState.Failed, session.State);
        Assert.Equal(1, client.Calls);
    }

    [Fact]
    public async Task 用户停止仍能取消持续模型输出()
    {
        var session = new AgentSession(new() { ModelTimeout = TimeSpan.FromMinutes(5) });
        using var stop = new CancellationTokenSource();
        await foreach (var activity in Loop(new FlowClient(true)).RunAsync(session, "测试停止", stop.Token))
            if (activity.Kind == AgentEventKind.ReasoningDelta) stop.Cancel();
        Assert.Equal(AgentSessionState.Cancelled, session.State);
    }

    [Fact]
    public async Task 新建恢复会话均为五分钟且仅扫描链路取消期限()
    {
        var settings = Substitute.For<ISettingsService>(); settings.LoadSettings().Returns(new AppSettings());
        var client = new CaptureClient();
        var assistant = new DiskAssistantService(_ => client, [], new AssistantWorkspace(), settings);
        await assistant.RunAsync("读取工具", new(new ModelEndpointConfig { ApiKey = "fixture", Model = "fixture" }), null, CancellationToken.None);
        var field = typeof(DiskAssistantService).GetField("session", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.Equal(TimeSpan.FromMinutes(5), ((AgentSession)field.GetValue(assistant)!).Options.ModelTimeout);
        foreach (var name in new[] { "scan_path", "run_skill_source", "analyze_paths" })
            Assert.Equal(Timeout.InfiniteTimeSpan, Assert.Single(client.Request!.Tools, t => t.Name == name).Timeout);
        Assert.Equal(TimeSpan.FromSeconds(180), Assert.Single(client.Request!.Tools, t => t.Name == "get_copilot_context").Timeout);
        Assert.Equal(TimeSpan.FromSeconds(1800), Assert.Single(client.Request!.Tools, t => t.Name == "ask_user").Timeout);
        assistant.RestoreConversation(new ConversationSnapshot("恢复测试", DateTimeOffset.UtcNow, null, AgentSessionState.Completed, [], []));
        Assert.Equal(TimeSpan.FromMinutes(5), ((AgentSession)field.GetValue(assistant)!).Options.ModelTimeout);
    }

    [Fact]
    public async Task 扫描无总期限但仍响应用户停止()
    {
        var registry = new ToolRegistry();
        using var stop = new CancellationTokenSource();
        var entered = false; var cancelled = false;
        registry.Register(new ToolDefinition
        {
            Name = "scan_path", Description = "受控扫描", ParametersSchema = new JsonObject(), Timeout = Timeout.InfiniteTimeSpan,
            Handler = async (_, token) =>
            {
                entered = true;
                stop.CancelAfter(150);
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                catch (OperationCanceledException) { cancelled = true; throw; }
                return ToolResult.Ok("扫描完成");
            }
        });
        var session = new AgentSession(new() { ModelTimeout = TimeSpan.FromMilliseconds(50) });
        var loop = new AgentLoop(new ScanClient(), registry, NullLogger<AgentLoop>.Instance);
        await foreach (var _ in loop.RunAsync(session, "扫描", stop.Token)) { }
        Assert.True(entered); Assert.True(cancelled);
        Assert.Equal(AgentSessionState.Cancelled, session.State);
    }

    private static AgentLoop Loop(IModelClient client) => new(client, new ToolRegistry(), NullLogger<AgentLoop>.Instance,
        retryDelay: (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; });

    private sealed class FlowClient(bool silence) : IModelClient
    {
        public int Calls;
        public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request, [EnumeratorCancellation] CancellationToken token)
        {
            Calls++;
            for (var index = 0; index < 20; index++)
            {
                yield return new ReasoningDeltaEvent("思考增量");
                await Task.Delay(silence ? Timeout.InfiniteTimeSpan : TimeSpan.FromMilliseconds(65), token);
            }
            yield return new ModelCompletedEvent("end_turn");
        }
    }

    private sealed class PartialClient : IModelClient
    {
        public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request, [EnumeratorCancellation] CancellationToken token)
        {
            yield return new ReasoningDeltaEvent("开始等待参数分片");
            var property = typeof(ModelRequest).GetProperty("OnStreamActivity");
            Assert.NotNull(property);
            for (var index = 0; index < 20; index++)
            {
                await Task.Delay(65, token);
                ((Action?)property.GetValue(request))?.Invoke();
            }
            yield return new TextDeltaEvent("分片已全部收到");
            yield return new ModelCompletedEvent("end_turn");
        }
    }

    private sealed class CaptureClient : IModelClient
    {
        public ModelRequest? Request;
        public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request, [EnumeratorCancellation] CancellationToken token)
        { Request = request; await Task.Yield(); yield return new TextDeltaEvent("完成"); yield return new ModelCompletedEvent("end_turn"); }
    }

    private sealed class SilentClient : IModelClient
    {
        public int Calls;
        public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request, [EnumeratorCancellation] CancellationToken token)
        { Calls++; await Task.Delay(Timeout.InfiniteTimeSpan, token); yield break; }
    }

    private sealed class ScanClient : IModelClient
    {
        public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request, [EnumeratorCancellation] CancellationToken token)
        { await Task.Yield(); yield return new ModelToolCallEvent(new ToolCall("扫描调用", "scan_path", "{}")); yield return new ModelCompletedEvent("tool_use"); }
    }
}

[CollectionDefinition("空闲超时串行", DisableParallelization = true)]
public sealed class ModelIdleTimeoutCollection { }
