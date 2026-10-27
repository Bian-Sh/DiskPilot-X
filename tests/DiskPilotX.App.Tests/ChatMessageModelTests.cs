using System.Runtime.CompilerServices;
using FluentAssertions;
using NSubstitute;
using TinyHarness;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Copilot;
using DiskPilotX.Core.Services.Settings;

namespace DiskPilotX.App.Tests;

public class ChatMessageModelTests
{
    [Fact]
    public async Task 同一会话切换模型后旧回复保持实际模型ID()
    {
        var saved = new AppSettings { ChatModelName = "model-a", DiscoveredModels = ["model-a", "model-b"] };
        var settings = Substitute.For<ISettingsService>(); settings.LoadSettings().Returns(saved); settings.GetApiKey(saved).Returns("fixture-key");
        var router = Substitute.For<IAiSkillRouter>(); router.GetSkillCatalog().Returns(Array.Empty<AiSkillCatalogItem>());
        router.Route(Arg.Any<string>(), Arg.Any<FileEntry?>(), Arg.Any<FileEntry?>(), Arg.Any<bool>(), Arg.Any<string?>()).Returns(new AiSkillRoutingResult([]));
        var service = new DiskAssistantService(options => new Client("reported-" + options.Endpoint.Model), [], new AssistantWorkspace(), settings);
        var vm = new ChatViewModel(service, settings, router);
        await vm.ExplorePromptCommand.ExecuteAsync("检查空间");
        var first = vm.Messages.Single(message => message.Sender == ChatSender.Assistant);
        first.ModelId.Should().Be("reported-model-a");
        vm.SelectedChatModel = "model-b";
        await vm.ExplorePromptCommand.ExecuteAsync("继续检查");
        vm.Messages.Where(message => message.Sender == ChatSender.Assistant).Select(message => message.ModelId).Should().Equal("reported-model-a", "reported-model-b");
        SavedChatMessage.Capture(first).Restore().ModelId.Should().Be("reported-model-a");
    }
    private sealed class Client(string model) : IModelClient
    {
        public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request, [EnumeratorCancellation] CancellationToken ct)
        {
            yield return new TextDeltaEvent("已检查");
            yield return new ModelCompletedEvent("end_turn", new(100, 10, 110, 80, TotalInputTokens: 100), model);
            await Task.CompletedTask;
        }
    }
}
