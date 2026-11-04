using System.Text.Json;
using System.Text.Json.Nodes;
using NSubstitute;
using TinyHarness;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Agent;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Llm;
using DiskPilotX.Core.Services.Settings;

namespace DiskPilotX.Core.Tests;

public sealed class RecommendationEmptyStateToolTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 聊天与分析均可调用仅展示空态工具(bool analysisMode)
    {
        JsonObject? received = null;
        var settings = Substitute.For<ISettingsService>(); settings.LoadSettings().Returns(new AppSettings());
        var scan = new ScanSession { TargetPath = "D:/scope", RootEntry = new() { Path = "D:/scope", Name = "scope", IsDirectory = true } };
        var workspace = new AssistantWorkspace
        {
            ReadContext = _ => Task.FromResult(new AgentContext(scan, scan.RootEntry, null, null)),
            Execute = (name, args, _) => { Assert.Equal("set_recommendations_empty_state", name); received = (JsonObject)args!.DeepClone(); return Task.FromResult<JsonNode>(new JsonObject { ["ok"] = true }); }
        };
        var client = new AnalysisConversationFlowTests.ScriptClient([new("empty", "set_recommendations_empty_state", JsonSerializer.Serialize(new
        { title = "空间小贴士", description = "可以先看看大文件，不执行删除。", recommendations = new[] { "不得改变建议" }, scan_id = "不得覆盖" }))]);
        await new DiskAssistantService(_ => client, [], workspace, settings).RunAsync("展示空态说明", new(new ModelEndpointConfig { ApiKey = "fixture", Model = "fixture" }, FocusPath: scan.TargetPath, IsAnalysis: analysisMode), null, default);
        Assert.NotNull(received);
        Assert.Equal("空间小贴士", received["title"]!.GetValue<string>());
        Assert.Equal(2, received.Count);
        Assert.Equal(TinyHarness.ToolRiskLevel.Read, client.Requests[0].Tools.Single(t => t.Name == "set_recommendations_empty_state").RiskLevel);
    }

    [Fact]
    public async Task 超长文案不能到达宿主()
    {
        var calls = 0;
        var settings = Substitute.For<ISettingsService>(); settings.LoadSettings().Returns(new AppSettings());
        var workspace = new AssistantWorkspace { Execute = (_, _, _) => { calls++; return Task.FromResult<JsonNode>(new JsonObject { ["ok"] = true }); } };
        var client = new AnalysisConversationFlowTests.ScriptClient([new("long", "set_recommendations_empty_state", JsonSerializer.Serialize(new { title = new string('长', 81), description = "说明" }))]);
        await new DiskAssistantService(_ => client, [], workspace, settings).RunAsync("展示", new(new ModelEndpointConfig { ApiKey = "fixture", Model = "fixture" }), null, default);
        Assert.Equal(0, calls);
    }
}