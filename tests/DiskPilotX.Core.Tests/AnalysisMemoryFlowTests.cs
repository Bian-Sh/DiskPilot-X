using System.Text.Json.Nodes;
using FluentAssertions;
using NSubstitute;
using TinyHarness;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Agent;
using DiskPilotX.Core.Services.Analysis;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Llm;
using DiskPilotX.Core.Services.Settings;

namespace DiskPilotX.Core.Tests;

public sealed class AnalysisMemoryFlowTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PreferenceIsWrittenOnlyAfterHostConfirmation(bool confirmed)
    {
        var directory = Directory.CreateTempSubdirectory("diskpilotx-memory-");
        try
        {
            var store = new AnalysisContextStore(directory.FullName);
            var confirmations = 0;
            var client = new AnalysisConversationFlowTests.ScriptClient([new("m", "manage_analysis_memory",
                """{"operation":"save","scope_path":"D:\\scope","text":"保留用户下载的文件"}""")]);
            var settings = Substitute.For<ISettingsService>(); settings.LoadSettings().Returns(new AppSettings());
            var workspace = new AssistantWorkspace { Execute = (op, _, _) => { confirmations++; return Task.FromResult<JsonNode>(new JsonObject { ["ok"] = confirmed, ["cancelled"] = !confirmed }); } };
            var service = new DiskAssistantService(_ => client, [], workspace, settings, analysisContexts: store);
            await service.RunAsync("记住这个范围的保留偏好", new(new ModelEndpointConfig { ApiKey = "test", Model = "test" }), null, CancellationToken.None);
            confirmations.Should().Be(1);
            store.ListPreferences().Count.Should().Be(confirmed ? 1 : 0);
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public void PreferenceAtDriveRootAppliesToItsChildren()
    {
        var d = Directory.CreateTempSubdirectory("diskpilotx-pref-");
        try { var s = new AnalysisContextStore(d.FullName); s.SavePreference(@"D:\", "保留项目源文件"); s.GetPreferences(@"D:\scope").Should().ContainSingle(); }
        finally { d.Delete(true); }
    }

    [Fact]
    public void UsageIsOptInAndActionSpecificAndPromptIsThrottled()
    {
        var d = Directory.CreateTempSubdirectory("diskpilotx-usage-");
        try
        {
            var s = new AnalysisContextStore(d.FullName);
            for (var i = 0; i < 4; i++) s.RecordUsage("npm-cache", "analysis", true);
            s.ShouldSuggestShortcut("npm-cache", "analysis").Should().BeFalse();
            s.SetUsageRecording(true);
            for (var i = 0; i < 2; i++) s.RecordUsage("npm-cache", "analysis", true);
            s.RecordUsage("npm-cache", "analysis", false);
            s.RecordUsage("npm-cache", "query", true);
            s.ShouldSuggestShortcut("npm-cache", "analysis").Should().BeFalse();
            s.RecordUsage("npm-cache", "analysis", true);
            s.ShouldSuggestShortcut("npm-cache", "analysis").Should().BeTrue();
            s.MarkShortcutOffered("npm-cache", "analysis");
            s.ShouldSuggestShortcut("npm-cache", "analysis").Should().BeFalse();
            new AnalysisContextStore(d.FullName).UsageRecordingEnabled.Should().BeTrue();
        }
        finally { d.Delete(true); }
    }

    [Fact]
    public void ClearingUsageDoesNotEraseExplicitNeverRemindPreference()
    {
        var d = Directory.CreateTempSubdirectory("diskpilotx-never-");
        try
        {
            var s = new AnalysisContextStore(d.FullName); s.SetUsageRecording(true);
            s.DismissShortcut("npm-cache", "analysis", true); s.ClearUsage();
            for (var i = 0; i < 3; i++) s.RecordUsage("npm-cache", "analysis", true);
            s.ShouldSuggestShortcut("npm-cache", "analysis").Should().BeFalse();
            s.ResetShortcutDismissal("npm-cache", "analysis");
            s.ShouldSuggestShortcut("npm-cache", "analysis").Should().BeTrue();
        }
        finally { d.Delete(true); }
    }

    [Fact]
    public void ConversationSummaryRequiresCurrentPrivacyAndVisibleSource()
    {
        var d = Directory.CreateTempSubdirectory("diskpilotx-source-");
        try
        {
            var s = new AnalysisContextStore(d.FullName); var id = Guid.NewGuid();
            s.SaveScan(new(id, @"D:\scope", "chat", "source-chat", "message", "盘点空间", [], "privacy"));
            s.ReadScan(id, "privacy").Should().BeNull();
            s.ReadScan(id, "different", _ => true).Should().BeNull();
            s.ReadScan(id, "privacy", _ => false).Should().BeNull();
            s.ReadScan(id, "privacy", _ => true)!.Goal.Should().Be("盘点空间");
        }
        finally { d.Delete(true); }
    }

    [Fact]
    public void CorruptStorageFailsClosedWithoutOverwritingOriginal()
    {
        var d = Directory.CreateTempSubdirectory("diskpilotx-corrupt-");
        try
        {
            var file = Path.Combine(d.FullName, "analysis-context.json"); File.WriteAllText(file, "broken");
            var s = new AnalysisContextStore(d.FullName); s.GetPreferences(@"D:\scope").Should().BeEmpty();
            s.Warning.Should().NotBeNull();
            Action write = () => s.SavePreference(@"D:\scope", "保留资料"); write.Should().Throw<InvalidDataException>();
            File.ReadAllText(file).Should().Be("broken");
        }
        finally { d.Delete(true); }
    }
}
