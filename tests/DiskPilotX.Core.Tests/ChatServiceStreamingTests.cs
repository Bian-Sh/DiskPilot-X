using System.Runtime.CompilerServices;
using FluentAssertions;
using TinyHarness;
using NSubstitute;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Llm;
using DiskPilotX.Core.Services.Settings;

namespace DiskPilotX.Core.Tests;

public class ChatServiceStreamingTests
{
    [Fact]
    public async Task FirstTokenArrivesBeforeWholeModelTurnCompletes()
    {
        var client=new ControlledClient();
        var settings=Substitute.For<ISettingsService>();settings.LoadSettings().Returns(new AppSettings());
        var assistant=new DiskAssistantService(_=>client,[],new AssistantWorkspace(),settings);
        var activities = new List<AgentEvent>();
        var first=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var running=assistant.RunAsync("测试流式",new(new ModelEndpointConfig { ApiKey = "key", Model = "test" }),e=>{activities.Add(e);if(e.Kind==AgentEventKind.TextDelta)first.TrySetResult(e.Text!);},CancellationToken.None);
        (await first.Task.WaitAsync(TimeSpan.FromSeconds(5))).Should().Be("第一段");
        running.IsCompleted.Should().BeFalse();
        client.Release.TrySetResult();
        var result=await running.WaitAsync(TimeSpan.FromSeconds(5));
        result.Text.Should().Be("第一段第二段");
        activities.Single(e => e.Kind == AgentEventKind.MessageCompleted && e.Message?.Role == ChatRole.Assistant).Message!.Usage.Should().Be(new ModelTokenUsage(101, 7, 108, 80));
    }
    private sealed class ControlledClient:IModelClient
    {
        public TaskCompletionSource Release {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request,[EnumeratorCancellation]CancellationToken ct)
        {
            yield return new TextDeltaEvent("第一段");await Release.Task.WaitAsync(ct);
            yield return new TextDeltaEvent("第二段");yield return new ModelCompletedEvent("end_turn", new(101, 7, 108, 80));
        }
    }
}
