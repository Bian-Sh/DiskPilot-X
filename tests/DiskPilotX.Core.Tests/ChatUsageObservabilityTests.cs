using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using TinyHarness;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Agent;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Llm;
using DiskPilotX.Core.Services.Scanning;
using DiskPilotX.Core.Services.Settings;
using Message = DiskPilotX.Core.Models.ChatMessage;

namespace DiskPilotX.Core.Tests;

public class ChatUsageObservabilityTests
{
    [Fact]
    public void 模型ID和缓存分母随历史保存而不是绑定当前模型()
    {
        var message = new Message { ModelId = "实际模型-a", Usage = new(10, 15, 125, 80, 20, TotalInputTokens: 110) };
        var restored = JsonSerializer.Deserialize<SavedChatMessage>(JsonSerializer.Serialize(SavedChatMessage.Capture(message)))!.Restore();
        restored.ModelId.Should().Be("实际模型-a");
        restored.Usage!.TotalInputTokens.Should().Be(110);
        restored.CacheHitRateText.Should().Be((80d / 110).ToString("P1"));
        new SavedChatMessage(Guid.NewGuid(), DiskPilotX.Core.Enums.ChatSender.Assistant, "旧记录", "", DateTime.Now, "", false, []).Restore().HasModelId.Should().BeFalse();
    }

    [Fact]
    public void 多步骤缓存命中率按总输入加权且重复事件不重复计数()
    {
        var message = new Message();
        var first = new AgentEvent("会话", AgentEventKind.MessageCompleted, DateTimeOffset.Now)
        { Message = TinyHarness.ChatMessage.AssistantToolCalls([new("工具", "get_copilot_context", "{}")]) with { ModelId = "实际模型", Usage = new(100, 20, 120, 80, TotalInputTokens: 100) } };
        message.ApplyActivity(first); message.ApplyActivity(first);
        message.ApplyActivity(new("会话", AgentEventKind.MessageCompleted, DateTimeOffset.Now)
        { Message = TinyHarness.ChatMessage.Assistant("完成") with { ModelId = "实际模型", Usage = new(900, 20, 920, 90, TotalInputTokens: 900) } });
        message.ModelId.Should().Be("实际模型");
        message.CacheHitRateText.Should().Be(.17.ToString("P1"));
        message.UsageDetails.Should().Contain(row => row.Key == "缓存命中率" && row.Value == .17.ToString("P1"));
    }

    [Theory]
    [InlineData(null, 100, "未提供")]
    [InlineData(0L, 0L, "未提供")]
    [InlineData(120L, 100L, "未提供")]
    [InlineData(-1L, 100L, "未提供")]
    public void 缺失零分母和异常值不伪造百分比(long? cached, long input, string expected)
        => new Message { Usage = new(input, 20, input + 20, cached, TotalInputTokens: input) }.CacheHitRateText.Should().Be(expected);

    [Fact]
    public void 明确零命中与部分请求缺失分别展示()
    {
        new Message { Usage = new(100, 20, 120, 0, TotalInputTokens: 100) }.CacheHitRateText.Should().Be(0d.ToString("P1"));
        var message = new Message();
        message.ApplyActivity(new("会话", AgentEventKind.MessageCompleted, DateTimeOffset.Now) { Message = TinyHarness.ChatMessage.Assistant("第一步") });
        message.ApplyActivity(new("会话", AgentEventKind.MessageCompleted, DateTimeOffset.Now)
        { Message = TinyHarness.ChatMessage.Assistant("第二步") with { Usage = new(100, 20, 120, 80, TotalInputTokens: 100) } });
        message.CacheHitRateText.Should().Be("未提供（部分请求缺少统计）");
        SavedChatMessage.Capture(message).Restore().CacheHitRateText.Should().Be(message.CacheHitRateText);
    }

    [Fact]
    public async Task 实时上下文同时保留已有MFT结果和后来确认前取消的请求()
    {
        var path = Path.GetTempPath();
        var scan = new ScanSession { TargetPath = path, RootEntry = new() { Path = path, IsDirectory = true },
            Execution = new(path, ScanMethod.Mft, ScanExecutionStatus.Completed, DateTime.Now, DateTime.Now) };
        var cancelled = new ScanExecutionInfo(path, ScanMethod.Unknown, ScanExecutionStatus.CancelledBeforeStart, CompletedAt: DateTime.Now);
        var headless = new ScanExecutionInfo(path, ScanMethod.DirectoryTraversal, ScanExecutionStatus.Completed, DateTime.Now, DateTime.Now);
        var workspace = new AssistantWorkspace { ReadContext = _ => Task.FromResult(new AgentContext(scan, scan.RootEntry, null, null, IsElevated: false, LastScanExecution: cancelled, LastCompletedScanExecution: headless)) };
        var settings = Substitute.For<ISettingsService>(); settings.LoadSettings().Returns(new AppSettings());
        var client = new ContextClient();
        var service = new DiskAssistantService(_ => client, [], workspace, settings);
        await service.RunAsync("刚才走的是 MFT 还是普通遍历？", new(new ModelEndpointConfig { ApiKey = "key", Model = "请求模型" }), null, CancellationToken.None);
        using var json = JsonDocument.Parse(client.Context!);
        json.RootElement.GetProperty("is_elevated").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("current_scan").GetProperty("method").GetString().Should().Be("mft");
        json.RootElement.GetProperty("last_scan_attempt").GetProperty("status").GetString().Should().Be("cancelled_before_start");
        json.RootElement.GetProperty("latest_completed_scan").GetProperty("method").GetString().Should().Be("directory_traversal");
        client.SystemPrompt.Should().Contain("不能").And.Contain("扫描路线");
    }

    [Fact]
    public void 单步异常缓存统计不会被后续大分母掩盖()
    {
        var message = new Message();
        foreach (var usage in new ModelTokenUsage[] { new(100, 20, 120, 120, TotalInputTokens: 100), new(900, 20, 920, 0, TotalInputTokens: 900) })
            message.ApplyActivity(new("会话", AgentEventKind.MessageCompleted, DateTimeOffset.Now) { Message = TinyHarness.ChatMessage.Assistant("一步") with { Usage = usage } });
        message.CacheHitRateText.Should().StartWith("未提供");
    }

    [Fact]
    public void 旧式可推导输入与新式总输入混合累计不会漏掉分母()
    {
        var message = new Message();
        foreach (var usage in new ModelTokenUsage[] { new(null, 20, 120, 80), new(900, 20, 920, 90, TotalInputTokens: 900) })
            message.ApplyActivity(new("会话", AgentEventKind.MessageCompleted, DateTimeOffset.Now) { Message = TinyHarness.ChatMessage.Assistant("一步") with { Usage = usage } });
        message.Usage!.TotalInputTokens.Should().Be(1000);
        message.CacheHitRateText.Should().Be(.17.ToString("P1"));
    }

    [Fact]
    public async Task 新增扫描事实字段同样遵守路径隐私且保留管理员状态()
    {
        var directory = Directory.CreateTempSubdirectory("DiskPilotX-private-secret-");
        try
        {
            var hidden = directory.FullName;
            var execution = new ScanExecutionInfo(hidden, ScanMethod.Mft, ScanExecutionStatus.Completed, DateTime.Now, DateTime.Now);
            var scan = new ScanSession { TargetPath = hidden, RootEntry = new() { Path = hidden, IsDirectory = true }, Execution = execution };
            var workspace = new AssistantWorkspace { ReadContext = _ => Task.FromResult(new AgentContext(scan, scan.RootEntry, null, null,
                IsElevated: false, LastScanExecution: execution, LastCompletedScanExecution: execution)) };
            var settings = Substitute.For<ISettingsService>(); settings.LoadSettings().Returns(new AppSettings { AiConversationWhitelist = [new() { Path = hidden }] });
            var client = new ContextClient();
            await new DiskAssistantService(_ => client, [], workspace, settings).RunAsync("查询扫描状态", new(new ModelEndpointConfig { ApiKey = "key", Model = "模型" }), null, CancellationToken.None);
            client.Context.Should().NotContain("private-secret");
            using var json = JsonDocument.Parse(client.Context!);
            json.RootElement.GetProperty("current_scan").ValueKind.Should().Be(JsonValueKind.Null);
            json.RootElement.GetProperty("latest_completed_scan").ValueKind.Should().Be(JsonValueKind.Null);
            json.RootElement.GetProperty("last_scan_attempt").ValueKind.Should().Be(JsonValueKind.Null);
            json.RootElement.GetProperty("is_elevated").GetBoolean().Should().BeFalse();
        }
        finally { directory.Delete(recursive: true); }
    }

    private sealed class ContextClient : IModelClient
    {
        private int turn;
        public string? Context { get; private set; }
        public string? SystemPrompt { get; private set; }
        public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request, [EnumeratorCancellation] CancellationToken ct)
        {
            SystemPrompt = request.Messages.First(message => message.Role == ChatRole.System).Content;
            if (++turn == 1) yield return new ModelToolCallEvent(new("上下文", "get_copilot_context", "{}"));
            else { Context = request.Messages.Last(message => message.Role == ChatRole.Tool).Content; yield return new TextDeltaEvent("已有扫描走 MFT，后来请求确认前已取消。"); }
            yield return new ModelCompletedEvent(turn == 1 ? "tool_use" : "end_turn", ModelId: "实际模型");
            await Task.CompletedTask;
        }
    }
}
