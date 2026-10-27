using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using TinyHarness;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Services.Agent;
using DiskPilotX.Core.Services.Analysis;
using DiskPilotX.Core.Services.Assistant;

namespace DiskPilotX.Core.Tests;

public sealed class AnalysisPrivacyTests
{
    [Fact]
    public async Task 关联任务摘要不把宿主隐私签名中的排除路径提供给模型()
    {
        var directory = Directory.CreateTempSubdirectory("diskpilotx-summary-");
        try
        {
            var settings = Substitute.For<DiskPilotX.Core.Services.Settings.ISettingsService>();
            settings.LoadSettings().Returns(new AppSettings { AiConversationWhitelist = [new() { Path = @"D:\private-secret" }] });
            var signature = JsonSerializer.Serialize(new { conversation = new[] { @"D:\private-secret" }, analysis = Array.Empty<string>() });
            var id = Guid.NewGuid().ToString("N");
            var history = new ConversationStore(Path.Combine(directory.FullName, "history"));
            var message = new DiskPilotX.Core.Models.ChatMessage { Sender = ChatSender.User, Text = "盘点空间" };
            history.Save(new(id, DateTimeOffset.UtcNow, signature, AgentSessionState.Completed, [TinyHarness.ChatMessage.User("盘点空间")], [SavedChatMessage.Capture(message)]));
            var scan = new ScanSession { TargetPath = @"D:\scope", RootEntry = new() { Path = @"D:\scope", IsDirectory = true } };
            var memory = new AnalysisContextStore(Path.Combine(directory.FullName, "context"));
            memory.SaveScan(new(scan.Id, scan.TargetPath, "chat", id, message.Id.ToString(), "盘点空间", [], signature));
            var client = new AnalysisConversationFlowTests.ScriptClient([]);
            var workspace = new AssistantWorkspace { ReadContext = _ => Task.FromResult(new AgentContext(scan, scan.RootEntry, null, null)) };
            var service = new DiskAssistantService(_ => client, [], workspace, settings, conversationStore: history, analysisContexts: memory);
            await service.RunAsync("分析", AnalysisConversationFlowTests.Options(scan.TargetPath), null, CancellationToken.None);
            var input = client.Requests[0].Messages.Last(x => x.Role == ChatRole.User).Content!;
            input.Should().Contain("盘点空间");
            input.Should().NotContain("private-secret");
            input.Should().NotContain("privacy_signature");
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public void 可选统计容量异常不会覆盖真实清理结果()
    {
        var directory = Directory.CreateTempSubdirectory("diskpilotx-stat-");
        try
        {
            var preferences = Enumerable.Range(0, 600).Select(_ => new AnalysisPreference(Guid.NewGuid(), @"D:\scope", new string('a', 2000), true, DateTimeOffset.UtcNow)).ToArray();
            File.WriteAllText(Path.Combine(directory.FullName, "analysis-context.json"), JsonSerializer.Serialize(new { UsageRecordingEnabled = true, Preferences = preferences, Scans = Array.Empty<object>(), Uses = Array.Empty<object>(), Prompts = Array.Empty<object>() }));
            var memory = new AnalysisContextStore(directory.FullName);
            var settings = Substitute.For<DiskPilotX.Core.Services.Settings.ISettingsService>(); settings.LoadSettings().Returns(new AppSettings());
            var client = new AnalysisConversationFlowTests.ScriptClient([]);
            var service = new DiskAssistantService(_ => client, [], new AssistantWorkspace(), settings, analysisContexts: memory);
            Action record = () => service.RecordCleanupUsage([new CleanupAction { Result = CleanupResult.Success, Recommendation = new() { TargetPath = @"D:\scope\cache", Category = RecommendationCategory.BuildCache } }]);
            record.Should().NotThrow();
        }
        finally { directory.Delete(true); }
    }
}
