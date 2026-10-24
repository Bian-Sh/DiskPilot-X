using System.Runtime.CompilerServices;
using FluentAssertions;
using TinyHarness;
using NSubstitute;
using SpaceMonger.App.ViewModels;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Assistant;
using SpaceMonger.Core.Services.Copilot;
using SpaceMonger.Core.Services.Settings;

namespace SpaceMonger.App.Tests;

public class ChatViewModelProposalTests
{
    [Fact]
    public async Task NativeConversationWorksBeforeFirstScanAndUsesConfiguredModel()
    {
        var client=new TextClient();
        var model="";
        var vm=Create(options=>{model=options.Model;return client;});
        vm.InputText="你好";
        await vm.SubmitOrStopCommand.ExecuteAsync(null);
        model.Should().Be("configured-model");
        vm.Messages.Should().HaveCount(2);
        vm.Messages.Last().Text.Should().Be("已完成");
        vm.IsOperationRunning.Should().BeFalse();
        vm.Messages.Last().Segments.Should().ContainSingle();
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UserConfirmationReturnsDecisionAndClearsCard(bool approve)
    {
        var vm=Create(_=>new TextClient());
        var waiting=vm.ConfirmAsync("清理","目标清单","移入回收站",CancellationToken.None);
        vm.PendingInteractionCard.Should().NotBeNull();
        if(approve)vm.ConfirmInteractionCommand.Execute(null);else vm.CancelInteractionCommand.Execute(null);
        (await waiting).Should().Be(approve);
        vm.PendingInteractionCard.Should().BeNull();
    }
    [Fact]
    public async Task CancellationReleasesPendingConfirmation()
    {
        var vm=Create(_=>new TextClient());using var cts=new CancellationTokenSource();
        var waiting=vm.ConfirmAsync("清理","清单",null,cts.Token);cts.Cancel();
        Func<Task> action=async()=>await waiting;
        await action.Should().ThrowAsync<OperationCanceledException>();
        vm.PendingInteractionCard.Should().BeNull();
    }
    [Fact]
    public void SkillMentionAndSlashCompletionRemainAvailable()
    {
        var vm=Create(_=>new TextClient());
        vm.InputText="@";vm.IsSkillMentionMenuOpen.Should().BeTrue();
        vm.ConfirmActiveCompletion().Should().BeTrue();vm.InputText.Should().Be("@disk-management ");
        vm.InputText="/";vm.IsSlashCommandMenuOpen.Should().BeTrue();
        vm.ConfirmActiveCompletion().Should().BeTrue();vm.InputText.Should().Be("/new");
    }
    [Fact]
    public async Task StopCancelsTheSameModelRunAndAllowsNextMessage()
    {
        var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts=0;
        var vm=Create(_=>++attempts==1?new WaitingClient(started):new TextClient());
        vm.InputText="慢任务";var run=vm.SubmitOrStopCommand.ExecuteAsync(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await vm.SubmitOrStopCommand.ExecuteAsync(null);
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        vm.InputText="继续";await vm.SubmitOrStopCommand.ExecuteAsync(null);
        vm.Messages.Last().Text.Should().Be("已完成");vm.IsOperationRunning.Should().BeFalse();
    }
    [Fact]
    public async Task ModelFailureShowsSafeActionableError()
    {
        var vm=Create(_=>new ErrorClient()); vm.InputText="连接测试";
        await vm.SubmitOrStopCommand.ExecuteAsync(null);
        vm.ErrorMessage.Should().Contain("API Key").And.NotContain("unsafe-provider-body");
        vm.Messages.Last().IsError.Should().BeTrue();
    }
    private sealed class ErrorClient:IModelClient
    {
        public IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request,CancellationToken ct)=>throw new ModelClientException("unsafe-provider-body",ModelErrorKind.Authentication);
    }
    [Fact] public async Task ComposerReturnsToChatModelAfterExternalAnalysis()
    {
        DiskAssistantService? service=null;var vm=Create(_=>new TextClient(),value=>service=value);
        await service!.RunAsync("手动分析",new("key",null,"analysis-model",IsAnalysis:true),null,CancellationToken.None);
        vm.ModelDisplayName.Should().Be("configured-model");
    }
    private static ChatViewModel Create(Func<AssistantRunOptions,IModelClient> factory,Action<DiskAssistantService>? observe=null)
    {
        var settings=Substitute.For<ISettingsService>();var saved=new AppSettings{ChatModelName="configured-model"};
        settings.LoadSettings().Returns(saved);settings.GetApiKey(saved).Returns("test-key");
        var router=Substitute.For<IAiSkillRouter>();router.GetSkillCatalog().Returns(new[]{new AiSkillCatalogItem("disk-management","磁盘管理","管理磁盘")});
        router.Route(Arg.Any<string>(),Arg.Any<FileEntry?>(),Arg.Any<FileEntry?>(),Arg.Any<bool>(),Arg.Any<string?>()).Returns(new AiSkillRoutingResult([]));
        var service=new DiskAssistantService(factory,[],new AssistantWorkspace(),settings);observe?.Invoke(service);return new(service,settings,router);
    }
    private sealed class TextClient:IModelClient
    {
        public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request,[EnumeratorCancellation]CancellationToken ct)
        {await Task.Yield();yield return new TextDeltaEvent("已完成");yield return new ModelCompletedEvent("end_turn");}
    }
    private sealed class WaitingClient(TaskCompletionSource started):IModelClient
    {
        public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request,[EnumeratorCancellation]CancellationToken ct)
        {yield return new TextDeltaEvent("处理中");started.TrySetResult();await Task.Delay(Timeout.Infinite,ct);}
    }
}
