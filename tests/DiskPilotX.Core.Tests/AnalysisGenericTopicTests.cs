using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using NSubstitute;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Agent;
using DiskPilotX.Core.Services.Analysis;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Settings;

namespace DiskPilotX.Core.Tests;

public sealed class AnalysisGenericTopicTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task 无规则通用分析也能按公开主题累计但必须先启用记录(bool enabled)
    {
        var directory = Directory.CreateTempSubdirectory("diskpilotx-generic-");
        try
        {
            var store = new AnalysisContextStore(directory.FullName);
            if (enabled) store.SetUsageRecording(true);
            var scan = new ScanSession { TargetPath = @"D:\scope", RootEntry = new() { Path = @"D:\scope", IsDirectory = true } };
            var settings = Substitute.For<ISettingsService>(); settings.LoadSettings().Returns(new AppSettings());
            var prompts = 0;
            var workspace = new AssistantWorkspace { ReadContext = _ => Task.FromResult(new AgentContext(scan, scan.RootEntry, null, null)),
                Execute = (op, args, _) => { if (op == "ask_user") { prompts++; return Task.FromResult<JsonNode>(new JsonObject { ["ok"] = true, ["values"] = new JsonObject { ["choice"] = "create" } }); }
                    return Task.FromResult<JsonNode>(new JsonObject { ["ok"] = true, ["recommendations"] = args?["recommendations"]?.DeepClone() ?? new JsonArray() }); } };
            var service = new DiskAssistantService(_ => new AnalysisConversationFlowTests.ScriptClient([
                new("e", "get_analysis_evidence", "{}"),
                new("topic", "report_task_topics", "{\"topics\":[\"npm-cache\"]}"),
                new("p", "publish_recommendations", JsonSerializer.Serialize(new { scan_id = scan.Id, recommendations = Array.Empty<object>() })),
                new("c", "complete_analysis", "{\"coverage_complete\":true,\"unassessed\":[]}")
            ]), [], workspace, settings, analysisContexts: store);
            for (var i = 1; i <= 3; i++)
            {
                var result = await service.RunAsync("分析这类通用缓存", AnalysisConversationFlowTests.Options(scan.TargetPath), null, CancellationToken.None);
                result.Task!.Outcome.Should().Be(AnalysisOutcome.NoRecommendations);
                var draft = await service.SuggestShortcutAsync(CancellationToken.None);
                if (enabled && i == 3) draft.Should().Contain("npm-cache"); else draft.Should().BeNull();
            }
            prompts.Should().Be(enabled ? 1 : 0);
            var saved = File.Exists(Path.Combine(directory.FullName, "analysis-context.json")) ? File.ReadAllText(Path.Combine(directory.FullName, "analysis-context.json")) : "";
            saved.Should().NotContain(@"D:\\scope");
        }
        finally { directory.Delete(true); }
    }
}
