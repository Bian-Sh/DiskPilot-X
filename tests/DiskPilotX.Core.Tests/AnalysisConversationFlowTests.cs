using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using NSubstitute;
using TinyHarness;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Agent;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Llm;
using DiskPilotX.Core.Services.Settings;

namespace DiskPilotX.Core.Tests;

public class AnalysisConversationFlowTests
{
    [Fact]
    public async Task AnalysisExposesDiscoveryAndQuestionsButNotRuleMutationOrDeletion()
    {
        var client = new ScriptClient([]);
        var (service, scan) = Create(client);
        await service.RunAsync("分析当前范围", Options(scan.TargetPath), null, CancellationToken.None);
        var names = client.Requests[0].Tools.Select(t => t.Name).ToArray();
        names.Should().Contain(["list_analysis_rules", "read_analysis_rule", "preview_analysis_rule", "ask_user", "complete_analysis"]);
        names.Should().NotContain(["manage_disk_skills", "manage_cleanup_skill", "run_skill_source", "cleanup_recommendations"]);
    }

    [Fact]
    public async Task AnalysisCannotQuerySiblingOfTheClickedScope()
    {
        var root = new FileEntry { Path = @"D:\scope", Name = "scope", IsDirectory = true };
        var a = new FileEntry { Path = @"D:\scope\a", Name = "a", IsDirectory = true };
        var b = new FileEntry { Path = @"D:\scope\b", Name = "private-sibling", IsDirectory = true };
        root.Children.Add(a); root.Children.Add(b);
        var scan = new ScanSession { TargetPath = root.Path, RootEntry = root };
        var client = new ScriptClient([new("e", "get_analysis_evidence", JsonSerializer.Serialize(new { path = b.Path }))]);
        var (service, _) = Create(client, scan);
        await service.RunAsync("分析", Options(a.Path), null, CancellationToken.None);
        var result = JsonNode.Parse(client.Requests[1].Messages.Last(m => m.Role == ChatRole.Tool).Content!)!;
        result["ok"]!.GetValue<bool>().Should().BeFalse();
        result.ToJsonString().Should().NotContain("private-sibling");
    }

    [Fact]
    public async Task ProtectedCandidatesHaveActualRejectionCountsInsteadOfSilentEmptySuccess()
    {
        var root = new FileEntry { Path = @"C:\Program Files (x86)\Sample", Name = "Sample", IsDirectory = true };
        root.Children.Add(new() { Path = root.Path + @"\cache", Name = "cache", IsDirectory = true, Size = 42 });
        var scan = new ScanSession { TargetPath = root.Path, RootEntry = root };
        var client = new ScriptClient([
            new("e", "get_analysis_evidence", "{}"),
            new("p", "publish_recommendations", JsonSerializer.Serialize(new { scan_id = scan.Id, recommendations = new[] { new { path = root.Children[0].Path, category = "SystemCache", safety_rating = "ReviewFirst", explanation = "已核实候选" } } }))
        ]);
        var (service, _) = Create(client, scan);
        await service.RunAsync("分析", Options(root.Path), null, CancellationToken.None);
        var published = JsonNode.Parse(client.Requests[2].Messages.Last(m => m.Role == ChatRole.Tool).Content!)!;
        published["validation"].Should().NotBeNull();
        published["validation"]!["protected"]!.GetValue<int>().Should().Be(1);
        published["validation"]!["accepted"]!.GetValue<int>().Should().Be(0);
    }

    internal static AssistantRunOptions Options(string path) => new(new ModelEndpointConfig { ApiKey = "test", Model = "test" }, FocusPath: path, IsAnalysis: true);
    internal static (DiskAssistantService, ScanSession) Create(ScriptClient client, ScanSession? scan = null)
    {
        scan ??= new() { TargetPath = @"D:\scope", RootEntry = new() { Path = @"D:\scope", Name = "scope", IsDirectory = true } };
        var current = scan;
        var settings = Substitute.For<ISettingsService>(); settings.LoadSettings().Returns(new AppSettings());
        var workspace = new AssistantWorkspace {
            ReadContext = _ => Task.FromResult(new AgentContext(current, current.RootEntry, null, null)),
            Execute = (_, args, _) => Task.FromResult<JsonNode>(new JsonObject { ["ok"] = true, ["recommendations"] = args?["recommendations"]?.DeepClone() ?? new JsonArray() })
        };
        return (new DiskAssistantService(_ => client, [], workspace, settings), scan);
    }

    internal sealed class ScriptClient(IReadOnlyList<ToolCall> calls) : IModelClient
    {
        public List<ModelRequest> Requests { get; } = [];
        public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request, [EnumeratorCancellation] CancellationToken ct)
        {
            Requests.Add(request with { Messages = request.Messages.ToArray() });
            await Task.Yield();
            if (Requests.Count <= calls.Count) yield return new ModelToolCallEvent(calls[Requests.Count - 1]);
            else yield return new TextDeltaEvent("已根据本轮检查说明结论和限制。");
            yield return new ModelCompletedEvent(Requests.Count <= calls.Count ? "tool_use" : "end_turn");
        }
    }
}
