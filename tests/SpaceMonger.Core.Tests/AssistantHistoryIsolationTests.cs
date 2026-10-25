using System.Runtime.CompilerServices;
using FluentAssertions;
using TinyHarness;
using NSubstitute;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Assistant;
using SpaceMonger.Core.Services.Llm;
using SpaceMonger.Core.Services.Settings;
namespace SpaceMonger.Core.Tests;
public class AssistantHistoryIsolationTests
{
    [Fact] public async Task ManualAnalysisHistoryDoesNotEnterConversationContext()
    {
        var settings=Substitute.For<ISettingsService>();settings.LoadSettings().Returns(new AppSettings());var clients=new List<Client>();
        var service=new DiskAssistantService(options=>{var client=new Client(options.IsAnalysis?"analysis-private-detail":"chat-detail");clients.Add(client);return client;},[],new AssistantWorkspace(),settings);
        await service.RunAsync("手动分析",new(new ModelEndpointConfig { ApiKey = "key", Model = "analysis" }, IsAnalysis: true),null,CancellationToken.None);
        await service.RunAsync("继续对话",new(new ModelEndpointConfig { ApiKey = "key", Model = "chat" }),null,CancellationToken.None);
        clients[1].Messages.Should().NotContain(message=>message.Content!=null&&message.Content.Contains("analysis-private-detail"));
    }
    [Fact] public async Task ChangedConversationWhitelistDoesNotReplayOldContext()
    {
        var saved=new AppSettings();var settings=Substitute.For<ISettingsService>();settings.LoadSettings().Returns(saved);var clients=new List<Client>();
        var service=new DiskAssistantService(_=>{var client=new Client("previous-private-detail");clients.Add(client);return client;},[],new AssistantWorkspace(),settings);
        await service.RunAsync("第一轮",new(new ModelEndpointConfig { ApiKey = "key", Model = "chat" }),null,CancellationToken.None);
        saved.AiConversationWhitelist.Add(new(){Path=@"D:\hidden"});
        await service.RunAsync("规则变更后的对话",new(new ModelEndpointConfig { ApiKey = "key", Model = "chat" }),null,CancellationToken.None);
        clients[1].Messages.Should().NotContain(message=>message.Content!=null&&message.Content.Contains("previous-private-detail"));
    }
    private sealed class Client(string output):IModelClient
    {
        public IReadOnlyList<TinyHarness.ChatMessage> Messages=[];
        public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request,[EnumeratorCancellation]CancellationToken ct)
        {Messages=request.Messages.ToArray();await Task.Yield();yield return new TextDeltaEvent(output);yield return new ModelCompletedEvent("end_turn");}
    }
}
