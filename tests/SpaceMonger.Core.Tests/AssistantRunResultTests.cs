using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using TinyHarness;
using NSubstitute;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Agent;
using SpaceMonger.Core.Services.Assistant;
using SpaceMonger.Core.Services.Settings;
namespace SpaceMonger.Core.Tests;
public class AssistantRunResultTests
{
    [Fact] public async Task PublishedArtifactBelongsToItsRunAfterNextConversationStarts()
    {
        var (service,scan)=Create(false);
        var first=await service.RunAsync("分析",new("key",null,"analysis",IsAnalysis:true),null,CancellationToken.None);
        var next=await service.RunAsync("继续",new("key",null,"chat"),null,CancellationToken.None);
        first.Analysis.Should().NotBeNull();first.Analysis!.Recommendations.Should().ContainSingle();next.Analysis.Should().BeNull();
    }
    [Fact] public async Task CancellingFinalNarrationKeepsAlreadyPublishedAnalysis()
    {
        var (service,scan)=Create(true);using var cts=new CancellationTokenSource();
        service.Activity+=e=>{if(e.Kind==AgentEventKind.TextDelta)cts.Cancel();};
        var result=await new HarnessRecommendationEngine(service).AnalyzeWithDiagnosticsAsync(scan,"key",null,"analysis",false,"zh-CN",cts.Token);
        result.Recommendations.Should().ContainSingle();
    }
    private static (DiskAssistantService,ScanSession) Create(bool hold)
    {
        var root=new FileEntry{Path=@"D:\scope",Name="scope",IsDirectory=true,Size=10,Children=[new(){Path=@"D:\scope\cache",Name="cache",IsDirectory=true,Size=10,Depth=1}]};
        var scan=new ScanSession{TargetPath=root.Path,RootEntry=root};var settings=Substitute.For<ISettingsService>();settings.LoadSettings().Returns(new AppSettings());
        var workspace=new AssistantWorkspace{ReadContext=_=>Task.FromResult(new AgentContext(scan,root,null,null)),Execute=(_,a,_)=>Task.FromResult<JsonNode>(new JsonObject{["ok"]=true,["recommendations"]=a!["recommendations"]!.DeepClone()})};
        return(new DiskAssistantService(options=>new Client(options.IsAnalysis,scan.Id,hold),[],workspace,settings),scan);
    }
    private sealed class Client(bool analysis,Guid scanId,bool hold):IModelClient
    {
        private int step;
        public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request,[EnumeratorCancellation]CancellationToken ct)
        {
            await Task.Yield();step++;
            if(analysis&&step==1)yield return new ModelToolCallEvent(new ToolCall("e","get_analysis_evidence","{}"));
            else if(analysis&&step==2)yield return new ModelToolCallEvent(new ToolCall("p","publish_recommendations",JsonSerializer.Serialize(new{scan_id=scanId.ToString(),recommendations=new[]{new{path=@"D:\scope\cache",category="TemporaryFiles",safety_rating="ReviewFirst",explanation="临时缓存"}}})));
            else {yield return new TextDeltaEvent("完成");if(hold)await Task.Delay(Timeout.Infinite,ct);}
            yield return new ModelCompletedEvent(analysis&&step<=2?"tool_use":"end_turn");
        }
    }
}
