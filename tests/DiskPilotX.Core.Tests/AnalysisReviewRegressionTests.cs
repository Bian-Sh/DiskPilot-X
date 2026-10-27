using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using NSubstitute;
using TinyHarness;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Agent;
using DiskPilotX.Core.Services.Analysis;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Copilot;
using DiskPilotX.Core.Services.Llm;
using DiskPilotX.Core.Services.Settings;

namespace DiskPilotX.Core.Tests;

public sealed class AnalysisReviewRegressionTests
{
    [Fact]
    public async Task EmptyRepublishCannotEraseProtectedFinding()
    {
        var root = new FileEntry { Path = @"C:\Program Files (x86)\Sample", Name = "Sample", IsDirectory = true };
        root.Children.Add(new() { Path = root.Path + @"\cache", Name = "cache", IsDirectory = true });
        var scan = new ScanSession { TargetPath = root.Path, RootEntry = root };
        var client = new AnalysisConversationFlowTests.ScriptClient([
            new("e", "get_analysis_evidence", "{}"),
            new("p", "publish_recommendations", JsonSerializer.Serialize(new { scan_id = scan.Id, recommendations = new[] { new { path = root.Children[0].Path, category = "SystemCache", safety_rating = "ReviewFirst", explanation = "核实内容" } } })),
            new("p2", "publish_recommendations", JsonSerializer.Serialize(new { scan_id = scan.Id, recommendations = Array.Empty<object>() })),
            new("done", "complete_analysis", """{"coverage_complete":true,"unassessed":[]}""")
        ]);
        var (service, _) = AnalysisConversationFlowTests.Create(client, scan);
        var run = await service.RunAsync("分析", AnalysisConversationFlowTests.Options(root.Path), null, CancellationToken.None);
        run.Task!.Outcome.Should().Be(AnalysisOutcome.Restricted);
    }

    [Fact]
    public async Task CancellationAfterReportingPartialDoesNotBecomeCompleted()
    {
        var scan = new ScanSession { TargetPath = @"D:\scope", RootEntry = new() { Path = @"D:\scope", IsDirectory = true } };
        var settings = Substitute.For<ISettingsService>(); settings.LoadSettings().Returns(new AppSettings());
        var workspace = new AssistantWorkspace { ReadContext = _ => Task.FromResult(new AgentContext(scan, scan.RootEntry, null, null)),
            Execute = (_, _, _) => Task.FromResult<JsonNode>(new JsonObject { ["ok"] = true, ["recommendations"] = new JsonArray() }) };
        var service = new DiskAssistantService(_ => new CancelClient(scan.Id), [], workspace, settings);
        using var cts = new CancellationTokenSource();
        var run = await service.RunAsync("分析", AnalysisConversationFlowTests.Options(scan.TargetPath), e => { if (e.Kind == AgentEventKind.TextDelta) cts.Cancel(); }, cts.Token);
        run.State.Should().Be(AgentSessionState.Cancelled);
        run.Task!.Status.Should().Be(AnalysisTaskStatus.Cancelled);
    }

    [Fact]
    public async Task UnrelatedLaterChatsDoNotCountPreviousAnalysisRuleAsQueries()
    {
        var dir = Directory.CreateTempSubdirectory("diskpilotx-round-");
        try
        {
            var store = new AnalysisContextStore(dir.FullName); store.SetUsageRecording(true);
            var provider = Substitute.For<ISkillPromptProvider>();
            provider.GetSkillCatalog().Returns([new AiSkillCatalogItem("npm-cache", "npm", "检查 npm 缓存")]);
            provider.GetRawContent("npm-cache").Returns("只检查已有缓存，保留资料");
            var settings = Substitute.For<ISettingsService>(); settings.LoadSettings().Returns(new AppSettings());
            var scan = new ScanSession { TargetPath = @"D:\scope", RootEntry = new() { Path = @"D:\scope", IsDirectory = true } };
            var workspace = new AssistantWorkspace { ReadContext = _ => Task.FromResult(new AgentContext(scan, scan.RootEntry, null, null)) };
            var service = new DiskAssistantService(o => new AnalysisConversationFlowTests.ScriptClient(o.IsAnalysis ? [new("r", "read_analysis_rule", """{"id":"npm-cache"}""")] : []), [], workspace, settings,
                analysisRules: new AnalysisRuleCatalog(provider), analysisContexts: store);
            await service.RunAsync("分析", AnalysisConversationFlowTests.Options(scan.TargetPath), null, CancellationToken.None);
            for (var i = 0; i < 3; i++) await service.RunAsync("无关查询", new(new ModelEndpointConfig { ApiKey = "test", Model = "test" }), null, CancellationToken.None);
            store.ShouldSuggestShortcut("npm-cache", "query").Should().BeFalse();
        }
        finally { dir.Delete(true); }
    }

    private sealed class CancelClient(Guid id) : IModelClient
    {
        private int step;
        public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest r, [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            if (++step == 1) yield return new ModelToolCallEvent(new("e", "get_analysis_evidence", "{}"));
            else if (step == 2) yield return new ModelToolCallEvent(new("p", "publish_recommendations", JsonSerializer.Serialize(new { scan_id = id, recommendations = Array.Empty<object>() })));
            else if (step == 3) yield return new ModelToolCallEvent(new("c", "complete_analysis", """{"coverage_complete":false,"unassessed":["待核实"]}"""));
            else { yield return new TextDeltaEvent("整理结论"); await Task.Delay(Timeout.Infinite, ct); }
            yield return new ModelCompletedEvent(step <= 3 ? "tool_use" : "end_turn");
        }
    }
}
