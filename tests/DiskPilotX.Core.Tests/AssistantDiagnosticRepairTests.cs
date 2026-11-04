using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Agent;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Llm;
using DiskPilotX.Core.Services.Settings;
using NSubstitute;
using FluentAssertions;
using TinyHarness;
namespace DiskPilotX.Core.Tests;
public class AssistantDiagnosticRepairTests
{
    [Theory][InlineData(false)][InlineData(true)]
    public async Task 过期证据标识不能回落主扫描且无扫描也有类型诊断(bool noScan)
    {
        var scan=Sample();var client=new ScriptClient(scan.Id,true);var (service,_)=Create(client,noScan?null:scan);
        await service.RunAsync("证据标识回归",new(new ModelEndpointConfig {ApiKey="fixture",Model="fixture"}),null,CancellationToken.None);
        var result=JsonNode.Parse(client.Last!.Messages.Last(m=>m.Role==ChatRole.Tool).Content!)!;
        result["ok"]!.GetValue<bool>().Should().BeFalse();result["code"]!.GetValue<string>().Should().Be("scan_expired");
    }
    [Fact] public async Task 普通发布全部被过滤仍保留最初提交和拒绝计数()
    {
        var scan=Sample();var client=new ScriptClient(scan.Id,false);var(service,published)=Create(client,scan);
        await service.RunAsync("十个未扫描候选",new(new ModelEndpointConfig {ApiKey="fixture",Model="fixture"}),null,CancellationToken.None);
        var result=JsonNode.Parse(client.Last!.Messages.Last(m=>m.Role==ChatRole.Tool).Content!)!;
        result["ok"]!.GetValue<bool>().Should().BeFalse();
        var validation=result["validation"]!;
        (validation["input_count"]??validation["input"])!.GetValue<int>().Should().Be(10);
        validation["not_scanned"]!.GetValue<int>().Should().Be(10);
    }
    [Theory]
    [InlineData("none","scan_required")]
    [InlineData("outside","scope_not_scanned")]
    [InlineData("publish","evidence_required")]
    public async Task 缺证据和范围错误有不同类别及下一步(string mode,string expected)
    {
        var scan=Sample();var arguments=mode=="outside"?JsonSerializer.Serialize(new{path=@"D:\其他范围"}):mode=="publish"?JsonSerializer.Serialize(new{scan_id=scan.Id,recommendations=Array.Empty<object>()}):"{}";
        var client=new SingleClient(mode=="publish"?"publish_recommendations":"get_analysis_evidence",arguments);
        var(service,_)=Create(client,mode=="none"?null:scan);
        await service.RunAsync("诊断分类",new(new ModelEndpointConfig {ApiKey="fixture",Model="fixture"}),null,CancellationToken.None);
        var result=JsonNode.Parse(client.Last!.Messages.Last(message=>message.Role==ChatRole.Tool).Content!)!;
        result["code"]!.GetValue<string>().Should().Be(expected);result["next_step"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
    }
    private sealed class SingleClient(string tool,string arguments):IModelClient
    {
        private int calls;public ModelRequest? Last;
        public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request,[EnumeratorCancellation]CancellationToken ct)
        {
            await Task.Yield();Last=request with{Messages=request.Messages.ToArray()};
            if(calls++==0)yield return new ModelToolCallEvent(new("one",tool,arguments));else yield return new TextDeltaEvent("完成");
            yield return new ModelCompletedEvent(calls==1?"tool_use":"stop");
        }
    }
    private static ScanSession Sample()=>new(){TargetPath=@"D:\合成项目",RootEntry=new(){Path=@"D:\合成项目",Name="样本",IsDirectory=true},StartTime=DateTime.Now};
    private static (DiskAssistantService,object) Create(IModelClient client,ScanSession? scan)
    {
        var settings=Substitute.For<ISettingsService>();settings.LoadSettings().Returns(new AppSettings());
        var workspace=new AssistantWorkspace {ReadContext=_=>Task.FromResult(new AgentContext(scan,scan?.RootEntry,null,null)),Execute=(name,_,_)=>Task.FromResult<JsonNode>(name=="describe_analysis_batch"
            ?new JsonObject {["ok"]=false,["code"]="scan_expired",["error"]="失效",["scope_count"]=0}
            :new JsonObject {["ok"]=false,["code"]="no_accepted_candidates",["validation"]=new JsonObject {["input_count"]=0,["accepted_count"]=0,["not_scanned"]=0}})};
        return(new DiskAssistantService(_=>client,[],workspace,settings),new());
    }
    private sealed class ScriptClient(Guid scan,bool invalid):IModelClient
    {
        private int step;public ModelRequest? Last;
        public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request,[EnumeratorCancellation]CancellationToken ct)
        {
            await Task.Yield();Last=request with {Messages=request.Messages.ToArray()};
            if(step++==0)yield return new ModelToolCallEvent(new("evidence","get_analysis_evidence",JsonSerializer.Serialize(new{scan_id=invalid?Guid.NewGuid():scan})));
            else if(!invalid&&step==2)yield return new ModelToolCallEvent(new("publish","publish_recommendations",JsonSerializer.Serialize(new{scan_id=scan,recommendations=Enumerable.Range(0,10).Select(n=>new{path=@"D:\合成项目\missing"+n,category="BuildCache",safety_rating="ReviewFirst",explanation="合成未扫描项"})})));
            else yield return new TextDeltaEvent("完成");
            yield return new ModelCompletedEvent(step==1||!invalid&&step==2?"tool_use":"stop");
        }
    }
}
