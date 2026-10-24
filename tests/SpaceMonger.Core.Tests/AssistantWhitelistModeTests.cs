using System.Runtime.CompilerServices;
using FluentAssertions;
using TinyHarness;
using NSubstitute;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Agent;
using SpaceMonger.Core.Services.Assistant;
using SpaceMonger.Core.Services.Settings;
namespace SpaceMonger.Core.Tests;
public class AssistantWhitelistModeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConversationAndRecommendationWhitelistsKeepIndependentMeaning(bool analysisMode)
    {
        var temp=Directory.CreateTempSubdirectory("spacemonger-mode-");
        try
        {
            var ai=Directory.CreateDirectory(Path.Combine(temp.FullName,"ai-hidden")); var cleanup=Directory.CreateDirectory(Path.Combine(temp.FullName,"cleanup-hidden"));
            var root=new FileEntry{Path=temp.FullName,Name=temp.Name,IsDirectory=true,Size=30,Children=[new(){Path=ai.FullName,Name=ai.Name,IsDirectory=true,Size=10,Depth=1},new(){Path=cleanup.FullName,Name=cleanup.Name,IsDirectory=true,Size=20,Depth=1}]};
            var scan=new ScanSession{TargetPath=root.Path,RootEntry=root};
            var settings=Substitute.For<ISettingsService>();settings.LoadSettings().Returns(new AppSettings{AiConversationWhitelist=[new(){Path=ai.FullName}],CleanupRecommendationWhitelist=[new(){Path=cleanup.FullName}]});
            var workspace=new AssistantWorkspace{ReadContext=_=>Task.FromResult(new AgentContext(scan,root,null,null))};var client=new EvidenceClient();
            var service=new DiskAssistantService(_=>client,[],workspace,settings);
            await service.RunAsync("分析",new("key",null,"model",IsAnalysis:analysisMode),null,CancellationToken.None);
            client.Evidence.Should().NotContain("cleanup-hidden");
            if(analysisMode)client.Evidence.Should().Contain("ai-hidden");else client.Evidence.Should().NotContain("ai-hidden");
        }
        finally { temp.Delete(true); }
    }
    private sealed class EvidenceClient:IModelClient
    {
        private int calls; public string Evidence="";
        public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request,[EnumeratorCancellation]CancellationToken ct)
        {
            await Task.Yield();
            if(calls++==0)yield return new ModelToolCallEvent(new ToolCall("evidence","get_analysis_evidence","{}"));
            else {Evidence=request.Messages.Last(m=>m.Role==ChatRole.Tool).Content!;yield return new TextDeltaEvent("完成");}
            yield return new ModelCompletedEvent(calls==1?"tool_use":"end_turn");
        }
    }
}
