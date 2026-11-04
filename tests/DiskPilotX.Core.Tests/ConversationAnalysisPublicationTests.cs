using System.Text.Json;
using System.Text.Json.Nodes;
using NSubstitute;
using TinyHarness;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Agent;
using DiskPilotX.Core.Services.Analysis;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Llm;
using DiskPilotX.Core.Services.Settings;
using DiskPilotX.Core.Services.Copilot;
using DiskPilotX.Core.Services.Skills;

namespace DiskPilotX.Core.Tests;

public sealed class ConversationAnalysisPublicationTests
{
    [Fact]
    public async Task 读取整体再查局部不会缩窄发布范围()
    {
        var scan = Scan();
        var client = new AnalysisConversationFlowTests.ScriptClient([
            Evidence("all", scan.TargetPath), Evidence("detail", scan.RootEntry!.Children[0].Path),
            Publish(scan, "first", "replace", 0, 1), Complete(true)
        ]);
        var host = new Host(scan);
        var run = await host.Run(client);
        Assert.Equal(2, host.Items.Count);
        Assert.Equal(scan.TargetPath, host.Payloads.Single()["focus_path"]!.GetValue<string>());
        Assert.All(Results(client, "publish_recommendations"), item => Assert.Equal(0, item["validation"]!["not_scanned"]!.GetValue<int>()));
        Assert.True(Result(client, "complete_analysis")["ok"]!.GetValue<bool>());
        Assert.Equal(AnalysisOutcome.Recommendations, run.Task!.Outcome);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task 普通技能聊天的空清单可以真实完成或标记部分完成(bool coverage)
    {
        var scan = Scan();
        var client = new AnalysisConversationFlowTests.ScriptClient([Evidence("e", scan.TargetPath), Publish(scan, "p", "replace"), Complete(coverage)]);
        var run = await new Host(scan).Run(client);
        Assert.True(Result(client, "complete_analysis")["ok"]!.GetValue<bool>());
        Assert.Equal(coverage ? AnalysisOutcome.NoRecommendations : AnalysisOutcome.Partial, run.Task!.Outcome);
    }

    [Fact]
    public async Task 分批追加透传模式并保留前批与整轮实际数量()
    {
        var scan = Scan();
        var client = new AnalysisConversationFlowTests.ScriptClient([Evidence("e", scan.TargetPath),
            Publish(scan, "a", "replace", 0), Publish(scan, "b", "merge", 1), Complete(true)]);
        var host = new Host(scan);
        var run = await host.Run(client);
        Assert.Equal(new[] { "replace", "merge" }, host.Payloads.Select(p => p["mode"]?.GetValue<string>()));
        Assert.Equal(2, host.Items.Count);
        Assert.Equal(2, run.Task!.AcceptedCount);
        Assert.Equal(2, run.Analysis!.Recommendations.Count);
        Assert.Equal(2, Result(client, "complete_analysis")["validation"]!["input"]!.GetValue<int>());
        Assert.All(Results(client, "publish_recommendations"), item => Assert.Equal(1, item["validation"]!["input"]!.GetValue<int>()));
    }

    [Fact]
    public async Task 越界候选不会因局部证据修复而获准且限制不会被空发布抹掉()
    {
        var scan = Scan();
        var scope = scan.RootEntry!.Children[0].Path;
        var client = new AnalysisConversationFlowTests.ScriptClient([Evidence("e", scope),
            Publish(scan, "bad", "replace", 1), Publish(scan, "empty", "replace"), Complete(true)]);
        var host = new Host(scan);
        var run = await host.Run(client);
        Assert.Empty(host.Items);
        Assert.Equal(AnalysisOutcome.Partial, run.Task!.Outcome);
        Assert.Contains(run.Task.Unassessed, item => item.Contains("候选"));
    }

    [Theory]
    [InlineData("隐私")]
    [InlineData("扫描")]
    public async Task 普通聊天发布确认期间条件变化时拒绝提交(string changed)
    {
        var scan = Scan();
        var host = new Host(scan);
        host.BeforeCommit = () =>
        {
            if (changed == "隐私") host.Settings.AiConversationWhitelist.Add(new() { Path = scan.RootEntry!.Children[0].Path });
            else host.Current = Scan();
        };
        var client = new AnalysisConversationFlowTests.ScriptClient([Evidence("e", scan.TargetPath), Publish(scan, "p", "replace", 0), Complete(false)]);
        await host.Run(client);
        Assert.Empty(host.Items);
        Assert.False(Result(client, "publish_recommendations")["ok"]!.GetValue<bool>());
    }

    [Fact]
    public async Task 没有证据和发布不能伪造完成()
    {
        var client = new AnalysisConversationFlowTests.ScriptClient([Complete(true)]);
        await new Host(Scan()).Run(client);
        Assert.False(Result(client, "complete_analysis")["ok"]!.GetValue<bool>());
    }

    [Fact]
    public async Task 重复追加候选不产生重复清单或提交后异常()
    {
        var scan = Scan();
        var client = new AnalysisConversationFlowTests.ScriptClient([Evidence("e", scan.TargetPath),
            Publish(scan, "a", "replace", 0), Publish(scan, "b", "merge", 0), Complete(true)]);
        var host = new Host(scan);
        var run = await host.Run(client);
        Assert.Single(host.Items);
        Assert.All(Results(client, "publish_recommendations"), item => Assert.True(item["ok"]!.GetValue<bool>()));
        Assert.Equal(1, run.Task!.AcceptedCount);
        Assert.Single(run.Analysis!.Recommendations);
    }

    [Fact]
    public async Task 先预览源码规则再读证据仍保留未执行限制()
    {
        var temp = Directory.CreateTempSubdirectory("analysis-preview-before-evidence-");
        try
        {
            var scope = Directory.CreateDirectory(Path.Combine(temp.FullName, "scope"));
            var packages = new CleanupSkillStore(Path.Combine(temp.FullName, "packages"), []);
            packages.Save(new CleanupSkillPackage { Id = "requires-source", Title = "受控源码规则", Description = "需要源码实际核验", SkillMarkdown = "# 受控源码规则", Source = "public class PreviewOnly {}" });
            var provider = new FileSkillPromptProvider([], Path.Combine(temp.FullName, "markdown"));
            var scan = new ScanSession { TargetPath = scope.FullName, RootEntry = new() { Path = scope.FullName, Name = "scope", IsDirectory = true } };
            var host = new Host(scan) { Rules = new AnalysisRuleCatalog(provider, packages), Packages = packages };
            var client = new AnalysisConversationFlowTests.ScriptClient([
                new("preview", "preview_analysis_rule", JsonSerializer.Serialize(new { id = "requires-source", scope_path = scope.FullName })),
                Evidence("e", scope.FullName), Publish(scan, "p", "replace"), Complete(true)]);
            var run = await host.Run(client);
            Assert.True(Result(client, "preview_analysis_rule")["ok"]!.GetValue<bool>());
            Assert.Equal(AnalysisOutcome.Partial, run.Task!.Outcome);
            Assert.Contains(run.Task.Unassessed, item => item.Contains("源码尚未执行"));
        }
        finally { temp.Delete(true); }
    }

    [Fact]
    public async Task 每批过滤计数独立但整轮保留之前的限制()
    {
        var scan = Scan();
        var client = new AnalysisConversationFlowTests.ScriptClient([Evidence("e", scan.RootEntry!.Children[0].Path),
            Publish(scan, "invalid", "replace", 1), Publish(scan, "valid", "merge", 0), Complete(true)]);
        var run = await new Host(scan).Run(client);
        var batches = Results(client, "publish_recommendations").ToArray();
        Assert.Equal(1, batches[0]["validation"]!["not_scanned"]!.GetValue<int>());
        Assert.Equal(1, batches[1]["validation"]!["accepted"]!.GetValue<int>());
        Assert.Equal(0, batches[1]["validation"]!["not_scanned"]!.GetValue<int>());
        Assert.Equal(1, Result(client, "complete_analysis")["validation"]!["not_scanned"]!.GetValue<int>());
        Assert.Equal(AnalysisOutcome.Partial, run.Task!.Outcome);
    }
    private static ScanSession Scan()
    {
        var root = new FileEntry { Path = @"D:/scope", Name = "scope", IsDirectory = true, Size = 30 };
        root.Children.Add(new() { Path = root.Path + @"/a", Name = "a", IsDirectory = true, Parent = root, Size = 10 });
        root.Children.Add(new() { Path = root.Path + @"/b", Name = "b", IsDirectory = true, Parent = root, Size = 20 });
        return new() { RootEntry = root, TargetPath = root.Path };
    }
    private static ToolCall Evidence(string id, string path) => new(id, "get_analysis_evidence", JsonSerializer.Serialize(new { path }));
    private static ToolCall Complete(bool coverage) => new("complete", "complete_analysis", JsonSerializer.Serialize(new { coverage_complete = coverage, unassessed = Array.Empty<string>() }));
    private static ToolCall Publish(ScanSession scan, string id, string mode, params int[] entries) => new(id, "publish_recommendations", JsonSerializer.Serialize(new
    { scan_id = scan.Id, mode, recommendations = entries.Select(i => new { path = scan.RootEntry!.Children[i].Path, category = "BuildCache", safety_rating = "ReviewFirst", explanation = "已核实只读候选，未授权删除" }) }));
    private static IEnumerable<JsonNode> Results(AnalysisConversationFlowTests.ScriptClient client, string tool)
    {
        var messages = client.Requests.Last().Messages;
        var ids = messages.SelectMany(m => m.ToolCalls).Where(c => c.Name == tool).Select(c => c.Id).ToHashSet();
        return messages.Where(m => m.Role == ChatRole.Tool && ids.Contains(m.ToolCallId!)).Select(m => JsonNode.Parse(m.Content!)!);
    }
    private static JsonNode Result(AnalysisConversationFlowTests.ScriptClient client, string tool) => Results(client, tool).Last();
    private sealed class Host
    {
        public ScanSession Current;
        public AppSettings Settings = new();
        public List<JsonObject> Payloads = [];
        public JsonArray Items = [];
        public Action? BeforeCommit;
        public AnalysisRuleCatalog? Rules;
        public CleanupSkillStore? Packages;
        private readonly AssistantWorkspace workspace;
        private readonly ISettingsService settings = Substitute.For<ISettingsService>();
        public Host(ScanSession scan)
        {
            Current = scan;
            settings.LoadSettings().Returns(_ => Settings);
            workspace = new() { ReadContext = _ => Task.FromResult(new AgentContext(Current, Current.RootEntry, null, null)) };
            workspace.Execute = (operation, args, _) =>
            {
                if (operation != "publish_recommendations") return Task.FromResult<JsonNode>(new JsonObject { ["ok"] = false });
                Payloads.Add((JsonObject)args!.DeepClone());
                BeforeCommit?.Invoke();
                if (Current.Id.ToString() != args?["scan_id"]?.GetValue<string>() || !workspace.ValidateAnalysisCommit(args)) return Task.FromResult<JsonNode>(new JsonObject { ["ok"] = false, ["error"] = "确认后条件改变" });
                if (args?["mode"]?.GetValue<string>() != "merge") Items = [];
                foreach (var item in args!["recommendations"]!.AsArray())
                    if (!Items.Any(existing => string.Equals(existing!["path"]!.GetValue<string>(), item!["path"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase))) Items.Add(item!.DeepClone());
                return Task.FromResult<JsonNode>(new JsonObject { ["ok"] = true, ["recommendations"] = Items.DeepClone() });
            };
        }
        public Task<AssistantRunResult> Run(AnalysisConversationFlowTests.ScriptClient client) =>
            new DiskAssistantService(_ => client, [], workspace, settings, analysisRules: Rules, analysisPackages: Packages).RunAsync("按已选规则只读分析并预览", new(new ModelEndpointConfig { ApiKey = "fixture", Model = "fixture" }), null, default);
    }
}
