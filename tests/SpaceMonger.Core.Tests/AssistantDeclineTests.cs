using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using FluentAssertions;
using TinyHarness;
using NSubstitute;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Assistant;
using SpaceMonger.Core.Services.Settings;
namespace SpaceMonger.Core.Tests;
public class AssistantDeclineTests
{
    [Fact] public async Task DeclinedOperationDoesNotPromptAgainInSameRun()
    {
        var calls=0;var settings=Substitute.For<ISettingsService>();settings.LoadSettings().Returns(new AppSettings());
        var workspace=new AssistantWorkspace{Execute=(_,_,_)=>{calls++;return Task.FromResult<JsonNode>(new JsonObject{["ok"]=false,["cancelled"]=true});}};
        var service=new DiskAssistantService(_=>new RepeatClient(),[],workspace,settings);
        await service.RunAsync("询问",new("key",null,"model"),null,CancellationToken.None);
        calls.Should().Be(1);
    }
    [Fact] public void UserDeclinedToolHasCancelledStatus()
    {
        var message=new SpaceMonger.Core.Models.ChatMessage();
        var call=new ToolCall("ask","ask_user","{}"){ArgumentsAreSafe=true};
        message.ApplyActivity(AgentEvent.Create("s",AgentEventKind.ToolCallStarted) with{ToolCallId=call.Id,ToolName=call.Name});
        message.ApplyActivity(AgentEvent.Create("s",AgentEventKind.MessageCompleted) with{Message=TinyHarness.ChatMessage.ToolResult(call,"{\"ok\":false,\"cancelled\":true}")});
        message.ApplyActivity(AgentEvent.Create("s",AgentEventKind.ToolCallCompleted) with{ToolCallId=call.Id,ToolName=call.Name,Success=false});
        message.Segments.Single().Status.Should().Be(ChatSegmentStatus.Cancelled);
    }
    private sealed class RepeatClient:IModelClient
    {
        private int count;
        public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request,[EnumeratorCancellation]CancellationToken ct)
        {
            await Task.Yield();count++;if(count<=2)yield return new ModelToolCallEvent(new ToolCall("ask-"+count,"ask_user","{\"question\":\"测试\"}"));else yield return new TextDeltaEvent("已取消");
            yield return new ModelCompletedEvent(count<=2?"tool_use":"end_turn");
        }
    }
}
