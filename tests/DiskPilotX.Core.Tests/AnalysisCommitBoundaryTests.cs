using System.Text.Json;
using System.Text.Json.Nodes;
using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Agent;
using DiskPilotX.Core.Services.Analysis;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Copilot;
using DiskPilotX.Core.Services.Settings;
using DiskPilotX.Core.Services.Skills;
using NSubstitute;
using TinyHarness;

namespace DiskPilotX.Core.Tests;

public sealed class AnalysisCommitBoundaryTests : IDisposable
{
    private const string Topic = "boundary-cache";
    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("diskpilotx-commit-boundary-");

    [Theory]
    [InlineData("规则")]
    [InlineData("偏好")]
    [InlineData("未变更")]
    public async Task 确认等待后条件变化必须拒绝提交且保留原推荐列表(string change)
    {
        var contexts = new AnalysisContextStore(Path.Combine(directory.FullName, "contexts"));
        var packages = new CleanupSkillStore(Path.Combine(directory.FullName, "packages"), []);
        var preference = contexts.SavePreference(@"D:\boundary-scope", "保留用户资料");
        var body = "只检查缓存，保留用户资料。";
        var provider = Substitute.For<ISkillPromptProvider>();
        provider.GetSkillCatalog().Returns([new AiSkillCatalogItem(Topic, "缓存检查", "核实缓存")]);
        provider.GetRawContent(Topic).Returns(_ => body);
        var scan = Scan();
        var client = new AnalysisConversationFlowTests.ScriptClient([
            new("read", "read_analysis_rule", JsonSerializer.Serialize(new { id = Topic })),
            new("evidence", "get_analysis_evidence", "{}"),
            Publish(scan)
        ]);
        var enteredConfirmation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var confirmed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = new JsonArray(new JsonObject { ["path"] = @"D:\boundary-scope\original.bin", ["id"] = "original" });
        var originalJson = original.ToJsonString();
        JsonArray recommendations = original;
        JsonObject? payload = null;
        bool? allowed = null;
        var executions = 0;
        var workspace = Workspace(scan);
        workspace.Execute = async (operation, args, ct) =>
        {
            Assert.Equal("publish_recommendations", operation);
            executions++;
            payload = Assert.IsType<JsonObject>(args);
            enteredConfirmation.SetResult();
            await confirmed.Task.WaitAsync(ct);
            // 模拟宿主：只有确认返回后的 guard 通过，才真正替换原列表。
            allowed = workspace.ValidateAnalysisCommit(args);
            if (!allowed.Value) return new JsonObject { ["ok"] = false, ["error"] = "确认期间条件已变化，未写入推荐。" };
            recommendations = Assert.IsType<JsonArray>(payload["recommendations"]!.DeepClone());
            return new JsonObject { ["ok"] = true, ["recommendations"] = recommendations.DeepClone() };
        };
        var service = new DiskAssistantService(_ => client, [], workspace, Settings(),
            analysisRules: new AnalysisRuleCatalog(provider, packages), analysisContexts: contexts, analysisPackages: packages);
        var pending = service.RunAsync("分析当前缓存", AnalysisConversationFlowTests.Options(scan.TargetPath), null, CancellationToken.None);
        try
        {
            await enteredConfirmation.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.NotNull(payload);
            Assert.True(payload["analysis_mode"]!.GetValue<bool>());
            Assert.Equal(scan.Id.ToString(), payload["scan_id"]!.GetValue<string>());
            Assert.True(Guid.TryParse(payload["task_id"]!.GetValue<string>(), out _));
            Assert.True(workspace.ValidateAnalysisCommit(payload));
            if (change == "规则") body = "规则已更新，全部缓存必须保留。";
            if (change == "偏好") contexts.UpdatePreference(preference.Id, "全部缓存必须保留");
        }
        finally { confirmed.TrySetResult(); }
        var run = await pending.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(AgentSessionState.Completed, run.State);
        Assert.Equal(1, executions);
        Assert.NotNull(allowed);
        var task = Assert.IsType<AnalysisTaskResult>(run.Task);
        Assert.Equal(task.Id.ToString(), payload!["task_id"]!.GetValue<string>());
        if (change == "未变更")
        {
            Assert.True(allowed.Value);
            Assert.NotSame(original, recommendations);
            Assert.Equal(3, recommendations.Count);
            Assert.Equal(scan.RootEntry!.Children[0].Path, recommendations[0]!["path"]!.GetValue<string>());
            Assert.Equal(3, Assert.IsType<AnalysisResult>(run.Analysis).Recommendations.Count);
        }
        else
        {
            Assert.False(allowed.Value);
            Assert.Same(original, recommendations);
            Assert.Equal(originalJson, recommendations.ToJsonString());
            Assert.Null(run.Analysis);
            Assert.Equal(0, task.AcceptedCount);
            Assert.NotEmpty(task.Unassessed);
        }
    }

    [Fact]
    public async Task 清理只累计实际成功且同主题每批一次不影响查询计数()
    {
        var contexts = new AnalysisContextStore(Path.Combine(directory.FullName, "contexts"));
        contexts.SetUsageRecording(true);
        var packages = new CleanupSkillStore(Path.Combine(directory.FullName, "packages"), []);
        var provider = Substitute.For<ISkillPromptProvider>();
        provider.GetSkillCatalog().Returns([new AiSkillCatalogItem(Topic, "缓存检查", "核实缓存")]);
        provider.GetRawContent(Topic).Returns("缓存须核实后另行确认清理。");
        var scan = Scan();
        var client = new AnalysisConversationFlowTests.ScriptClient([
            new("read", "read_analysis_rule", JsonSerializer.Serialize(new { id = Topic })),
            new("evidence", "get_analysis_evidence", "{}"),
            Publish(scan)
        ]);
        var workspace = Workspace(scan);
        var commits = 0;
        workspace.Execute = (operation, args, _) =>
        {
            Assert.Equal("publish_recommendations", operation);
            Assert.True(workspace.ValidateAnalysisCommit(args));
            commits++;
            var payload = Assert.IsType<JsonObject>(args);
            return Task.FromResult<JsonNode>(new JsonObject { ["ok"] = true, ["recommendations"] = payload["recommendations"]!.DeepClone() });
        };
        var service = new DiskAssistantService(_ => client, [], workspace, Settings(),
            analysisRules: new AnalysisRuleCatalog(provider, packages), analysisContexts: contexts, analysisPackages: packages);
        var run = await service.RunAsync("分析当前缓存", AnalysisConversationFlowTests.Options(scan.TargetPath), null, CancellationToken.None);
        Assert.Equal(AgentSessionState.Completed, run.State);
        Assert.Equal(1, commits);
        var recommendations = Assert.IsType<AnalysisResult>(run.Analysis).Recommendations;
        Assert.Equal(3, recommendations.Count);
        Assert.Contains(Topic, Assert.IsType<AnalysisTaskResult>(run.Task).RuleVersions.Keys);
        // 查询已有两次：清理若误写 query，会立即触发阈值。
        contexts.RecordUsage(Topic, "query", true);
        contexts.RecordUsage(Topic, "query", true);
        foreach (var result in new[] { CleanupResult.Failed, CleanupResult.Skipped, CleanupResult.AlreadyRemoved })
        {
            for (var i = 0; i < 3; i++)
                service.RecordCleanupUsage(recommendations.Select(r => new CleanupAction { Recommendation = r, Result = result }));
            Assert.False(contexts.ShouldSuggestShortcut(Topic, "cleanup"));
            Assert.False(contexts.ShouldSuggestShortcut(Topic, "query"));
        }
        service.RecordCleanupUsage([]);
        Assert.False(contexts.ShouldSuggestShortcut(Topic, "cleanup"));
        // 同一主题的三个成功项，每批只算一次；恰在第三批达到阈值。
        for (var batch = 1; batch <= 3; batch++)
        {
            service.RecordCleanupUsage(recommendations.Select(r => new CleanupAction { Recommendation = r, Result = CleanupResult.Success }));
            Assert.Equal(batch == 3, contexts.ShouldSuggestShortcut(Topic, "cleanup"));
            Assert.False(contexts.ShouldSuggestShortcut(Topic, "query"));
        }
        contexts.RecordUsage(Topic, "query", true);
        Assert.True(contexts.ShouldSuggestShortcut(Topic, "query"));
        Assert.False(contexts.ShouldSuggestShortcut("category-packagemanagercache", "cleanup"));
    }

    private static ScanSession Scan()
    {
        var root = new FileEntry { Path = @"D:\boundary-scope", Name = "boundary-scope", IsDirectory = true };
        for (var i = 0; i < 3; i++)
            root.Children.Add(new FileEntry { Path = root.Path + @"\cache-" + i + ".bin", Name = "cache-" + i + ".bin", Size = 64, Parent = root });
        return new ScanSession { TargetPath = root.Path, RootEntry = root };
    }

    private static ToolCall Publish(ScanSession scan) => new("publish", "publish_recommendations", JsonSerializer.Serialize(new
    {
        scan_id = scan.Id,
        recommendations = scan.RootEntry!.Children.Select(entry => new
        {
            path = entry.Path, category = "PackageManagerCache", safety_rating = "ReviewFirst", explanation = "已核实为可再生缓存，需用户另行确认清理。"
        }).ToArray()
    }));

    private static AssistantWorkspace Workspace(ScanSession scan) => new()
    {
        ReadContext = _ => Task.FromResult(new AgentContext(scan, scan.RootEntry, null, null))
    };

    private static ISettingsService Settings()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.LoadSettings().Returns(new AppSettings());
        return settings;
    }

    public void Dispose()
    {
        // 仅删除本测试实例创建的隔离目录，不读取真实用户存储。
        directory.Delete(true);
    }
}