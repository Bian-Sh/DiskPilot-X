using System.Reflection;
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

public sealed class ConversationTitleTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "DiskPilot-X-标题测试-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task 首轮后台生成标题并持久化且不污染模型上下文()
    {
        var client = new TitleClient();
        var vm = Create(client);
        vm.HeaderText.Should().Be("准备就绪");
        vm.InputText = "请帮我找出可以清理的缓存目录";
        await vm.SubmitOrStopCommand.ExecuteAsync(null);
        vm.IsOperationRunning.Should().BeFalse();
        vm.HeaderText.Should().Be("请帮我找出可以清理的缓存目录");
        client.TitleRequest.Should().NotBeNull();
        client.TitleRequest!.Tools.Should().BeEmpty();
        client.TitleRequest.Messages.Last().Content.Should().Contain("请帮我找出").And.Contain("缓存分析结果");
        client.CompleteTitle("“缓存分析与清理建议”");
        await TitleTask(vm);
        vm.HeaderText.Should().Be("缓存分析与清理建议");
        var id = vm.CurrentConversationId!;
        new ConversationStore(directory).Load(id).Title.Should().Be(vm.HeaderText);
        var summary = vm.ConversationHistory.Single();
        summary.Title.Should().Be(vm.HeaderText);
        vm.ClearConversationCommand.Execute(null);
        vm.HeaderText.Should().Be("准备就绪");
        vm.LoadConversationCommand.Execute(summary);
        vm.HeaderText.Should().Be("缓存分析与清理建议");
        vm.InputText = "继续";
        await vm.SubmitOrStopCommand.ExecuteAsync(null);
        client.TitleRequests.Should().Be(1);
        client.LastChatRequest!.Messages.Should().NotContain(m => m.Content == "缓存分析与清理建议");
    }

    [Fact]
    public async Task 切换会话后忽略旧标题即使服务商未响应取消()
    {
        var client = new TitleClient();
        var vm = Create(client);
        vm.InputText = "旧对话";
        await vm.SubmitOrStopCommand.ExecuteAsync(null);
        var oldTask = TitleTask(vm);
        vm.ClearConversationCommand.Execute(null);
        vm.InputText = "新的对话";
        await vm.SubmitOrStopCommand.ExecuteAsync(null);
        client.CompleteTitle("旧对话的总结");
        await oldTask;
        client.CompleteTitle("新的总结", 1);
        await TitleTask(vm);
        vm.HeaderText.Should().NotBe("旧对话的总结");
        vm.HeaderText.Should().Be("新的总结");
    }

    [Fact]
    public async Task 首轮结束与标题启动之间切换会话也不会使用旧回复()
    {
        var client = new TitleClient();
        var vm = Create(client);
        var service = (DiskAssistantService)typeof(ChatViewModel).GetField("assistant", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
        service.RunFinished += () =>
        {
            vm.ClearConversationCommand.Execute(null);
            vm.Messages.Add(new() { Sender = DiskPilotX.Core.Enums.ChatSender.User, Text = "新会话的内容" });
            service.SaveConversation(vm.Messages);
        };
        vm.InputText = "旧会话的内容";
        await vm.SubmitOrStopCommand.ExecuteAsync(null);
        client.TitleRequests.Should().Be(0);
        vm.HeaderText.Should().Be("新会话的内容");
    }

    [Fact]
    public async Task 标题生成失败保留首条消息且不覆盖聊天错误状态()
    {
        var client = new TitleClient();
        var vm = Create(client);
        vm.InputText = "分析缓存";
        await vm.SubmitOrStopCommand.ExecuteAsync(null);
        client.FailTitle();
        await TitleTask(vm);
        vm.HeaderText.Should().Be("分析缓存");
        vm.ErrorMessage.Should().BeNull();
        vm.Messages.Last().Text.Should().Contain("缓存分析结果");
    }

    [Fact]
    public async Task 恢复旧记录保留完整回退标题供悬停展示()
    {
        var client = new TitleClient();
        var vm = Create(client);
        var text = new string('长', 90);
        vm.InputText = text;
        await vm.SubmitOrStopCommand.ExecuteAsync(null);
        client.FailTitle();
        await TitleTask(vm);
        vm.ClearConversationCommand.Execute(null);
        vm.LoadConversationCommand.Execute(vm.ConversationHistory.Single());
        vm.HeaderText.Should().Be(text);
    }

    [Fact]
    public void 窄面板标题单行省略且提示保留完整标题()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var vm = Create(new TitleClient());
                var fullTitle = string.Concat(Enumerable.Repeat("缓存清理与磁盘空间分析", 12));
                vm.Messages.Add(new() { Text = fullTitle });
                var sourceRoot = new DirectoryInfo(AppContext.BaseDirectory);
                while (sourceRoot is not null && !File.Exists(Path.Combine(sourceRoot.FullName, "src/DiskPilotX.App/Views/ChatPanel.xaml")))
                    sourceRoot = sourceRoot.Parent;
                var document = System.Xml.Linq.XDocument.Load(Path.Combine(sourceRoot!.FullName, "src/DiskPilotX.App/Views/ChatPanel.xaml"));
                System.Xml.Linq.XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
                var element = document.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "ConversationTitleText");
                var label = (System.Windows.Controls.TextBlock)System.Windows.Markup.XamlReader.Parse(element.ToString());
                var panel = new System.Windows.Controls.Grid { Width = 225, Height = 25, DataContext = vm };
                panel.Children.Add(label);
                panel.Measure(new System.Windows.Size(225, 25));
                panel.Arrange(new System.Windows.Rect(0, 0, 225, 25));
                panel.UpdateLayout();
                label.Text.Should().Be(fullTitle);
                label.FontSize.Should().Be(10);
                label.TextTrimming.Should().Be(System.Windows.TextTrimming.CharacterEllipsis);
                label.TextWrapping.Should().Be(System.Windows.TextWrapping.NoWrap);
                label.ActualWidth.Should().BeInRange(1, 250);
                var tooltip = (System.Windows.Controls.ToolTip)label.ToolTip;
                tooltip.PlacementTarget = label;
                tooltip.GetBindingExpression(System.Windows.FrameworkElement.DataContextProperty)!.UpdateTarget();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var content = (System.Windows.Controls.TextBlock)tooltip.Content;
                content.Text.Should().Be(fullTitle);
                content.TextWrapping.Should().Be(System.Windows.TextWrapping.Wrap);
            }
            catch (Exception exception) { failure = exception; }
            finally { System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(10)).Should().BeTrue();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public async Task 删除当前会话后迟到的标题不能恢复记录()
    {
        var client = new TitleClient();
        var vm = Create(client);
        vm.InputText = "等待标题的会话";
        await vm.SubmitOrStopCommand.ExecuteAsync(null);
        var pendingTitle = TitleTask(vm);
        vm.ConfirmConversationDeletion = _ => Task.FromResult(true);
        await vm.DeleteConversationCommand.ExecuteAsync(vm.ConversationHistory.Single());
        client.CompleteTitle("不应复活的标题");
        await pendingTitle;
        vm.CurrentConversationId.Should().BeNull();
        vm.Messages.Should().BeEmpty();
        vm.ConversationHistory.Should().BeEmpty();
        new ConversationStore(directory).List().Should().BeEmpty();
    }

    private static Task TitleTask(ChatViewModel vm) => (Task)typeof(ChatViewModel)
        .GetField("titleGenerationTask", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;

    private ChatViewModel Create(IModelClient client)
    {
        var saved = new AppSettings();
        var settings = Substitute.For<ISettingsService>();
        settings.LoadSettings().Returns(saved);
        settings.GetApiKey(saved).Returns("测试密钥");
        var router = Substitute.For<IAiSkillRouter>();
        router.GetSkillCatalog().Returns(Array.Empty<AiSkillCatalogItem>());
        router.Route(Arg.Any<string>(), Arg.Any<FileEntry?>(), Arg.Any<FileEntry?>(), Arg.Any<bool>(), Arg.Any<string?>()).Returns(new AiSkillRoutingResult([]));
        var service = new DiskAssistantService(_ => client, [], new AssistantWorkspace(), settings, conversationStore: new ConversationStore(directory));
        return new ChatViewModel(service, settings, router);
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

    private sealed class TitleClient : IModelClient
    {
        private readonly List<TaskCompletionSource<string>> titles = [];
        public ModelRequest? TitleRequest { get; private set; }
        public ModelRequest? LastChatRequest { get; private set; }
        public int TitleRequests { get; private set; }
        public void CompleteTitle(string value, int index = 0) => titles[index].TrySetResult(value);
        public void FailTitle() => titles[0].TrySetException(new ModelClientException("标题服务失败"));
        public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request, [EnumeratorCancellation] CancellationToken ct)
        {
            if (request.Messages.First().Content?.StartsWith("请为对话生成标题") == true)
            {
                TitleRequest = request;
                TitleRequests++;
                var title = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                titles.Add(title);
                // 模拟上游忽略取消，验证旧请求不能写入新会话。
                yield return new TextDeltaEvent(await title.Task);
            }
            else
            {
                LastChatRequest = request;
                yield return new TextDeltaEvent("缓存分析结果");
            }
            yield return new ModelCompletedEvent("end_turn");
        }
    }
}
