using FluentAssertions;
using TinyHarness;
namespace SpaceMonger.Core.Tests;
public class ChatToolDetailsTests
{
    [Fact] public void SafeArgumentsRemainVisibleAfterToolCompletes()
    {
        var message=new SpaceMonger.Core.Models.ChatMessage();
        var call=new ToolCall("1","scan_path","{\"path\":\"D:/fixture\"}"){ArgumentsAreSafe=true};
        message.ApplyActivity(AgentEvent.Create("s",AgentEventKind.MessageCompleted) with{Message=ChatMessage.AssistantToolCalls([call])});
        message.ApplyActivity(AgentEvent.Create("s",AgentEventKind.ToolCallStarted) with{ToolName=call.Name,ToolCallId=call.Id});
        message.ApplyActivity(AgentEvent.Create("s",AgentEventKind.ToolCallCompleted) with{ToolName=call.Name,ToolCallId=call.Id,Success=true});
        message.Segments.Single().Detail.Should().Contain("D:/fixture");
    }
}
