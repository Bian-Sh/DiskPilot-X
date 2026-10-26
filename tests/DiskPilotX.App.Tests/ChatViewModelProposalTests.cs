using System.Runtime.CompilerServices;
using FluentAssertions;
using TinyHarness;
using NSubstitute;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Llm;
using DiskPilotX.Core.Services.Copilot;
using DiskPilotX.Core.Services.Settings;

namespace DiskPilotX.App.Tests;

public class ChatViewModelProposalTests
{
    [Fact]
    public async Task NativeConversationWorksBeforeFirstScanAndUsesConfiguredModel()
    {
        var client=new TextClient();
        var model="";
        var vm=Create(options=>{model=options.Endpoint.Model;return client;});
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
    [Fact] public async Task IsolatedAnalysisRunLeavesComposerModelUnchanged()
    {
        DiskAssistantService? service=null;var vm=Create(_=>new TextClient(),value=>service=value);
        await service!.RunAsync("独立分析",new(new ModelEndpointConfig { ApiKey = "key", Model = "analysis-model" }, IsAnalysis: true),null,CancellationToken.None);
        vm.ModelDisplayName.Should().Be("configured-model");
    }
    [Fact] public async Task SendAppAnalysisUsesChatModelWithScopedAnalysisOptionsAndSystemMessage()
    {
        AssistantRunOptions? observed=null;
        var vm=Create(options=>{observed=options;return new TextClient();});
        var result=await vm.SendAppAnalysisAsync("【应用操作】请求分析当前范围并发布建议。",@"D:\scope");
        observed.Should().NotBeNull();
        observed!.Endpoint.Model.Should().Be("configured-model");
        observed.FocusPath.Should().Be(@"D:\scope");
        observed.IsAnalysis.Should().BeTrue();
        observed.InConversationSession.Should().BeTrue();
        (observed.Skills is { Count: > 0 }).Should().BeFalse();
        vm.Messages.Should().HaveCount(2);
        vm.Messages[0].Sender.Should().Be(ChatSender.System);
        vm.Messages[0].Text.Should().Contain("应用操作");
        result.Should().NotBeNull();
        vm.IsOperationRunning.Should().BeFalse();
    }
    [Fact]
    public async Task 提问期间发送按钮提交全部答案并恢复普通输入状态()
    {
        var vm = Create(_ => new TextClient());
        vm.InputText = "暂存的草稿";
        var waiting = vm.AskUserAsync(System.Text.Json.Nodes.JsonNode.Parse("""{"title":"确认","question":"保留哪些内容？","fields":[{"id":"scope","label":"保留范围","type":"text","is_required":true,"default_text":"用户资料"}]}"""), CancellationToken.None);
        vm.SendButtonText.Should().Be("提交回答");
        await vm.SubmitOrStopCommand.ExecuteAsync(null);
        var result = await waiting.WaitAsync(TimeSpan.FromSeconds(2));
        result["values"]!["scope"]!.GetValue<string>().Should().Be("用户资料");
        result["cancelled"]!.GetValue<bool>().Should().BeFalse();
        vm.PendingAskUser.Should().BeNull();
        vm.InputText.Should().Be("暂存的草稿");
        vm.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task 提交缺失答案时保持卡片并定位到需要补充的问题()
    {
        var vm = Create(_ => new TextClient());
        var waiting = vm.AskUserAsync(System.Text.Json.Nodes.JsonNode.Parse("""{"title":"确认","question":"请补充信息","fields":[{"id":"first","label":"第一题","type":"text","is_required":true},{"id":"second","label":"第二题","type":"text","is_required":true,"default_text":"已有答案"}]}"""), CancellationToken.None);
        vm.PendingAskUser!.MoveNextWithoutValidation();
        await vm.SubmitOrStopCommand.ExecuteAsync(null);
        waiting.IsCompleted.Should().BeFalse();
        vm.PendingAskUser.CurrentFieldIndex.Should().Be(0);
        vm.PendingAskUser.CurrentField.HasError.Should().BeTrue();
        vm.PendingAskUser.TryCancel();
        (await waiting)["cancelled"]!.GetValue<bool>().Should().BeTrue();
    }

    private static ChatViewModel Create(Func<AssistantRunOptions,IModelClient> factory,Action<DiskAssistantService>? observe=null)
    {
        var settings=Substitute.For<ISettingsService>();var saved=new AppSettings{ChatModelName="configured-model",DiscoveredModels=["configured-model"]};
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
