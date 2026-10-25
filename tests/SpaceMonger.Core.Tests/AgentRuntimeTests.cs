using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using FluentAssertions;
using TinyHarness;
using NSubstitute;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Assistant;
using SpaceMonger.Core.Services.Llm;
using SpaceMonger.Core.Services.Settings;

namespace SpaceMonger.Core.Tests;

public class AgentRuntimeTests
{
    [Theory]
    [InlineData("scan_path", "{不合法")]
    [InlineData("unregistered", "{}")]
    public async Task InvalidNativeCalls_DoNotExecuteWorkspace(string tool,string args)
    {
        var executions=0;
        var workspace=new AssistantWorkspace{Execute=(_,_,_)=>{executions++;return Task.FromResult<JsonNode>(new JsonObject{["ok"]=true});}};
        var settings=Substitute.For<ISettingsService>(); settings.LoadSettings().Returns(new AppSettings());
        var client=new InvalidClient(tool,args);
        var assistant=new DiskAssistantService(_=>client,[],workspace,settings);
        var result=await assistant.RunAsync("测试非法工具",new(new ModelEndpointConfig { ApiKey = "key", Model = "model" }),null,CancellationToken.None);
        executions.Should().Be(0);
        result.State.Should().Be(AgentSessionState.Completed);
        client.LastRequest!.Messages.Should().Contain(m=>m.Role==ChatRole.Tool && !string.IsNullOrEmpty(m.Content));
    }
    private sealed class InvalidClient(string tool,string args):IModelClient
    {
        private int count;
        public ModelRequest? LastRequest{get;private set;}
        public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request,[EnumeratorCancellation]CancellationToken ct)
        {
            LastRequest=request with{Messages=request.Messages.ToArray()};await Task.Yield();
            if(count++==0)yield return new ModelToolCallEvent(new ToolCall("call",tool,args));
            else yield return new TextDeltaEvent("已收到错误，未执行磁盘操作。");
            yield return new ModelCompletedEvent(count==1?"tool_use":"end_turn");
        }
    }
}
