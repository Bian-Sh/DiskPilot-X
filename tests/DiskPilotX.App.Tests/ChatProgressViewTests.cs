using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using NSubstitute;
using TinyHarness;
using DiskPilotX.App.ViewModels;
using DiskPilotX.App.Views;
using DiskPilotX.App.Localization;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Copilot;
using DiskPilotX.Core.Services.Settings;
using Message = DiskPilotX.Core.Models.ChatMessage;

namespace DiskPilotX.App.Tests;

[Collection("聊天滚动控件")]
public sealed class ChatProgressViewTests
{
    [Fact]
    public void 气泡尾部状态不依赖过程展开且只在整轮结束后消失() => StaHost.Run(() =>
    {
        var settings = Substitute.For<ISettingsService>();
        settings.LoadSettings().Returns(new AppSettings());
        var router = Substitute.For<IAiSkillRouter>();
        router.GetSkillCatalog().Returns(Array.Empty<AiSkillCatalogItem>());
        var vm = new ChatViewModel(new DiskAssistantService(_ => throw new InvalidOperationException(), [], new AssistantWorkspace(), settings), settings, router);
        var panel = new ChatPanel();
        panel.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/DiskPilot-X;component/Themes/VisionProTheme.xaml", UriKind.Relative) });
        using var source = new HwndSource(new HwndSourceParameters("聊天活动状态回归")
        { Width = 360, Height = 500, WindowStyle = unchecked((int)0x80000000) }) { RootVisual = panel };
        panel.SetViewModel(vm);
        var message = new Message { Sender = DiskPilotX.Core.Enums.ChatSender.Assistant };
        message.ApplyActivity(AgentEvent.Create("fixture", AgentEventKind.SessionStarted));
        message.ApplyActivity(AgentEvent.Create("fixture", AgentEventKind.TextDelta) with { Text = "扫描已完成，将继续发布清单。" });
        message.ApplyActivity(AgentEvent.Create("fixture", AgentEventKind.ToolCallPreparing) with { ToolCallId = "call", ToolName = "publish_recommendations", Timestamp = new DateTimeOffset(message.Timestamp.AddSeconds(30)) });
        vm.Messages.Add(message);
        message.IsProcessExpanded = false;
        Layout(panel);
        var footer = Assert.Single(Visuals<StackPanel>(panel).Where(item => item.Name == "MessageActivity"));
        Assert.Equal(Visibility.Visible, footer.Visibility);
        Assert.Contains(Visuals<TextBlock>(footer), text => text.Text == L.Text("CopilotActivity_PreparingTool"));
        message.RefreshElapsed(message.Timestamp.AddSeconds(35));
        Layout(panel);
        Assert.Contains(Visuals<TextBlock>(footer), text => text.Text == "5s");
        message.ApplyActivity(AgentEvent.Create("fixture", AgentEventKind.ToolCallStarted) with
        { ToolCallId = "call", ToolName = "publish_recommendations", Timestamp = new DateTimeOffset(message.Timestamp.AddSeconds(36)) });
        message.RefreshElapsed(message.Timestamp.AddSeconds(38));
        Layout(panel);
        Assert.Contains(Visuals<TextBlock>(footer), text => text.Text == "2s");
        Assert.Equal("38s", message.DurationText);
        message.SetWaitingForUser(true);
        Layout(panel);
        Assert.Contains(Visuals<TextBlock>(footer), text => text.Text == L.Text("CopilotActivity_WaitingForUser"));
        Assert.Equal(Visibility.Collapsed, Assert.Single(Visuals<ProgressBar>(footer)).Visibility);
        message.ApplyActivity(AgentEvent.Create("fixture", AgentEventKind.TaskCancelled));
        Layout(panel);
        Assert.Equal(Visibility.Collapsed, footer.Visibility);
    });

    private static void Layout(ChatPanel panel)
    {
        panel.Measure(new Size(360, 500)); panel.Arrange(new Rect(0, 0, 360, 500)); panel.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static IEnumerable<T> Visuals<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var nested in Visuals<T>(child)) yield return nested;
        }
    }
}
