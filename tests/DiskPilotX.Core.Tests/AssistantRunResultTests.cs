using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using TinyHarness;
using NSubstitute;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Agent;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Llm;
using DiskPilotX.Core.Services.Settings;
namespace DiskPilotX.Core.Tests;
public class AssistantRunResultTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task 仅宿主已登记的批次可在无可见扫描时交回逐范围校验(bool registered)
    {
        var batchId = Guid.NewGuid();
        var settings = Substitute.For<ISettingsService>();
        settings.LoadSettings().Returns(new AppSettings());
        JsonNode? published = null;
        var workspace = new AssistantWorkspace
        {
            IsAnalysisBatch = id => registered && id == batchId,
            ReadContext = _ => Task.FromResult(new AgentContext(null, null, null, null)),
            Execute = (operation, args, _) =>
            {
                if (operation == "publish_recommendations") published = args?.DeepClone();
                return Task.FromResult<JsonNode>(new JsonObject { ["ok"] = true });
            }
        };
        var service = new DiskAssistantService(_ => new BatchClient(batchId), [], workspace, settings);
        await service.RunAsync("从技能视图回传两盘选择", new(new ModelEndpointConfig { ApiKey = "key", Model = "chat" }), null, CancellationToken.None);
        if (!registered) { published.Should().BeNull("未知或过期批次不能绕过扫描校验"); return; }
        published.Should().NotBeNull("宿主已知批次不应被可见扫描为空拦截");
        published!["scan_id"]!.GetValue<string>().Should().Be(batchId.ToString());
        published["mode"]!.GetValue<string>().Should().Be("merge");
        published["recommendations"]!.AsArray().Should().HaveCount(2);
    }

    private sealed class BatchClient(Guid batchId) : IModelClient
    {
        private bool called;
        public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request, [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            if (!called)
            {
                called = true;
                yield return new ModelToolCallEvent(new ToolCall("batch", "publish_recommendations", JsonSerializer.Serialize(new
                {
                    scan_id = batchId.ToString(), mode = "merge", recommendations = new[]
                    {
                        new { path = @"C:\验收\缓存", category = "BuildCache", safety_rating = "ReviewFirst", explanation = "只读样本" },
                        new { path = @"D:\验收\缓存", category = "BuildCache", safety_rating = "ReviewFirst", explanation = "只读样本" }
                    }
                })));
                yield return new ModelCompletedEvent("tool_use");
            }
            else { yield return new TextDeltaEvent("批次已交回宿主校验"); yield return new ModelCompletedEvent("end_turn"); }
        }
    }
    [Fact] public async Task PublishedArtifactBelongsToItsRunAfterNextConversationStarts()
    {
        var (service,scan)=Create(false);
        var first=await service.RunAsync("分析",new(new ModelEndpointConfig { ApiKey = "key", Model = "analysis" }, IsAnalysis: true),null,CancellationToken.None);
        var next=await service.RunAsync("继续",new(new ModelEndpointConfig { ApiKey = "key", Model = "chat" }),null,CancellationToken.None);
        first.Analysis.Should().NotBeNull();first.Analysis!.Recommendations.Should().ContainSingle();next.Analysis.Should().BeNull();
    }
    [Fact] public async Task CancellingFinalNarrationKeepsAlreadyPublishedAnalysis()
    {
        var (service,scan)=Create(true);using var cts=new CancellationTokenSource();
        service.Activity+=e=>{if(e.Kind==AgentEventKind.TextDelta)cts.Cancel();};
        var result=await service.RunAsync("分析",new(new ModelEndpointConfig{ApiKey="key",Model="analysis"},IsAnalysis:true),null,cts.Token);
        result.Analysis.Should().NotBeNull();result.Analysis!.Recommendations.Should().ContainSingle();
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
