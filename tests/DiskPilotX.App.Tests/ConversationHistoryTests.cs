using System.Runtime.CompilerServices;
using FluentAssertions;
using NSubstitute;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Copilot;
using DiskPilotX.Core.Services.Settings;
using TinyHarness;

namespace DiskPilotX.App.Tests;

public sealed class ConversationHistoryTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "DiskPilot-X-历史测试-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task 重启后从历史恢复展示与模型上下文并继续保存同一会话()
    {
        var first = Create(new ReplyClient());
        var commandStates = new List<bool>();
        first.ClearConversationCommand.CanExecuteChanged += (_, _) => commandStates.Add(first.ClearConversationCommand.CanExecute(null));
        first.InputText = "第一轮问题";
        await first.SubmitOrStopCommand.ExecuteAsync(null);
        commandStates.Last().Should().BeTrue("运行锁释放后必须通知 WPF 重新启用按钮");
        var id = first.CurrentConversationId;
        first.ClearConversationCommand.Execute(null);
        first.Messages.Should().BeEmpty();
        first.ConversationHistory.Should().ContainSingle().Which.Id.Should().Be(id);
        var client = new ReplyClient();
        var restarted = Create(client);
        restarted.LoadConversationCommand.Execute(restarted.ConversationHistory.Single());
        restarted.Messages.Select(message => message.Text).Should().Equal("第一轮问题", "已完成");
        restarted.Messages.Last().Segments.Should().ContainSingle();
        restarted.InputText = "继续刚才的问题";
        await restarted.SubmitOrStopCommand.ExecuteAsync(null);
        client.Request!.Messages.Should().Contain(message => message.Content != null && message.Content.Contains("第一轮问题"));
        restarted.CurrentConversationId.Should().Be(id);
        restarted.ConversationHistory.Should().ContainSingle();
        File.ReadAllText(Directory.GetFiles(directory, "*.json").Single()).Should().NotContain("配置密钥不得写入");
    }

    [Fact]
    public async Task 运行期间禁止新建与加载且取消后保留历史()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = Create(new WaitingClient(started));
        vm.InputText = "等待取消";
        var running = vm.SubmitOrStopCommand.ExecuteAsync(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        vm.ClearConversationCommand.CanExecute(null).Should().BeFalse();
        vm.LoadConversationCommand.CanExecute(vm.ConversationHistory.FirstOrDefault()).Should().BeFalse();
        vm.ClearConversationCommand.Execute(null);
        vm.Messages.Should().NotBeEmpty();
        await vm.SubmitOrStopCommand.ExecuteAsync(null);
        await running.WaitAsync(TimeSpan.FromSeconds(5));
        vm.ClearConversationCommand.CanExecute(null).Should().BeTrue();
        Create(new ReplyClient()).ConversationHistory.Should().ContainSingle();
    }

    [Fact]
    public async Task 损坏文件不影响列表与当前消息且新建保留旧对话()
    {
        var vm = Create(new ReplyClient());
        vm.InputText = "原始消息";
        await vm.SubmitOrStopCommand.ExecuteAsync(null);
        var summary = vm.ConversationHistory.Single();
        File.WriteAllText(Path.Combine(directory, summary.Id + ".json"), "损坏内容");
        vm.LoadConversationCommand.Execute(summary);
        vm.Messages.First().Text.Should().Be("原始消息");
        vm.ErrorMessage.Should().NotBeNullOrEmpty();
        Create(new ReplyClient()).ConversationHistory.Should().BeEmpty();
    }

    [Fact]
    public async Task 白名单改变后历史可显示但旧模型上下文不进入请求()
    {
        var vm = Create(new ReplyClient());
        vm.InputText = "原白名单里的问题";
        await vm.SubmitOrStopCommand.ExecuteAsync(null);
        var client = new ReplyClient();
        var restarted = Create(client, new AppSettings { AiConversationWhitelist = [new() { Path = @"D:\新的白名单" }] });
        restarted.LoadConversationCommand.Execute(restarted.ConversationHistory.Single());
        restarted.InputText = "新问题";
        await restarted.SubmitOrStopCommand.ExecuteAsync(null);
        client.Request!.Messages.Should().NotContain(message => message.Content != null && message.Content.Contains("原白名单里的问题"));
    }

    [Fact]
    public async Task 保存失败时新建不会丢失当前消息()
    {
        var vm = Create(new ReplyClient());
        Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
        File.WriteAllText(directory, "阻止创建存储目录");
        try
        {
            vm.InputText = "保存失败也要保留";
            await vm.SubmitOrStopCommand.ExecuteAsync(null);
            vm.ErrorMessage.Should().Contain("保存失败");
            vm.ClearConversationCommand.Execute(null);
            vm.Messages.First().Text.Should().Be("保存失败也要保留");
        }
        finally { File.Delete(directory); }
    }

    [Fact]
    public async Task 恢复运行中遗留工具调用后续聊补齐取消结果且不会执行旧工具()
    {
        var id = Guid.NewGuid().ToString("N");
        var user = new DiskPilotX.Core.Models.ChatMessage { Sender = DiskPilotX.Core.Enums.ChatSender.User, Text = "旧问题" };
        new ConversationStore(directory).Save(new(id, DateTimeOffset.UtcNow, "[]", AgentSessionState.Running,
            [TinyHarness.ChatMessage.User("旧问题"), TinyHarness.ChatMessage.AssistantToolCalls([new ToolCall("call-1", "不存在的旧工具", "{}")])],
            [SavedChatMessage.Capture(user)]));
        var client = new ReplyClient();
        var vm = Create(client);
        vm.LoadConversationCommand.Execute(vm.ConversationHistory.Single());
        vm.InputText = "继续";
        await vm.SubmitOrStopCommand.ExecuteAsync(null);
        client.Request!.Messages.Should().Contain(message => message.Role == ChatRole.Tool
            && message.ToolCallId == "call-1" && message.Content!.Contains("cancelled"));
        vm.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task 模型失败仍保存历史且重新加载后可以继续()
    {
        var failed = Create(new ErrorClient());
        failed.InputText = "失败前的问题";
        await failed.SubmitOrStopCommand.ExecuteAsync(null);
        failed.Messages.Last().IsError.Should().BeTrue();
        var client = new ReplyClient();
        var restarted = Create(client);
        restarted.LoadConversationCommand.Execute(restarted.ConversationHistory.Single());
        restarted.Messages.Last().IsError.Should().BeTrue();
        restarted.InputText = "重试继续";
        await restarted.SubmitOrStopCommand.ExecuteAsync(null);
        client.Request!.Messages.Should().Contain(message => message.Content != null && message.Content.Contains("失败前的问题"));
        restarted.ErrorMessage.Should().BeNull();
    }

    private sealed class ErrorClient : IModelClient
    {
        public IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request, CancellationToken ct) =>
            throw new ModelClientException("不得暴露的提供商错误", ModelErrorKind.Authentication);
    }

    private ChatViewModel Create(IModelClient client, AppSettings? saved = null)
    {
        saved ??= new AppSettings();
        var settings = Substitute.For<ISettingsService>();
        settings.LoadSettings().Returns(saved);
        settings.GetApiKey(saved).Returns("配置密钥不得写入");
        var router = Substitute.For<IAiSkillRouter>();
        router.GetSkillCatalog().Returns(Array.Empty<AiSkillCatalogItem>());
        router.Route(Arg.Any<string>(), Arg.Any<FileEntry?>(), Arg.Any<FileEntry?>(), Arg.Any<bool>(), Arg.Any<string?>()).Returns(new AiSkillRoutingResult([]));
        var service = new DiskAssistantService(_ => client, [], new AssistantWorkspace(), settings, conversationStore: new ConversationStore(directory));
        return new ChatViewModel(service, settings, router);
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

    private sealed class ReplyClient : IModelClient
    {
        public ModelRequest? Request { get; private set; }
        public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request, [EnumeratorCancellation] CancellationToken ct)
        {
            // 历史测试只驱动主对话；标题生成由独立测试控制，避免后台写入干扰快照断言。
            if (request.Messages.First().Content?.StartsWith("请为对话生成标题") == true)
            {
                yield return new ModelCompletedEvent("end_turn");
                yield break;
            }
            Request = request;
            await Task.Yield();
            yield return new TextDeltaEvent("已完成");
            yield return new ModelCompletedEvent("end_turn");
        }
    }

    private sealed class WaitingClient(TaskCompletionSource started) : IModelClient
    {
        public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request, [EnumeratorCancellation] CancellationToken ct)
        {
            yield return new TextDeltaEvent("处理中");
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
        }
    }
}
