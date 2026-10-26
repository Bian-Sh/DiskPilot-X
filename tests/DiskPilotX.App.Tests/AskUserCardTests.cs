using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using DiskPilotX.App.ViewModels;
using DiskPilotX.App.Views;
using DiskPilotX.Core.Models;

namespace DiskPilotX.App.Tests;

public sealed class AskUserCardTests
{
    [Fact]
    public void 窄卡片约束长题目高度且保留底部提交按钮() => RunSta(() =>
    {
        var options = new JsonArray(Enumerable.Range(1, 35).Select(i => (JsonNode)new JsonObject { ["id"] = $"o{i}", ["label"] = $"第{i}个清理目录及说明" }).ToArray());
        var request = UserDecisionRequest.Parse(new JsonObject
        {
            ["title"] = "选择清理目录", ["question"] = "请确认希望清理的目录",
            ["fields"] = new JsonArray(new JsonObject { ["id"] = "targets", ["label"] = "目录", ["type"] = "multi_select", ["options"] = options })
        });
        var card = Arrange(new AskUserDialogViewModel(request), 232);
        Assert.InRange(card.DesiredSize.Width, 1, 232);
        Assert.InRange(card.DesiredSize.Height, 1, 420);
        var scroll = Visuals<ScrollViewer>(card).First();
        Assert.True(scroll.ScrollableHeight > 0);
        var submit = Visuals<Button>(card).Single(b => b.Content as string == "提交");
        var location = submit.TransformToAncestor(card).Transform(new Point());
        Assert.True(location.Y >= 0 && location.Y + submit.ActualHeight <= card.ActualHeight);
    });

    [Fact]
    public async Task 控件实际文本绑定和提交事件返回用户输入()
    {
        Task<JsonNode>? completion = null;
        RunSta(() =>
        {
            var request = UserDecisionRequest.Parse(JsonNode.Parse("""{"title":"确认","question":"希望清理哪里？"}"""));
            var vm = new AskUserDialogViewModel(request);
            var card = Arrange(vm, 232);
            var input = Assert.Single(Visuals<TextBox>(card));
            input.Text = "临时目录";
            input.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            var submit = Visuals<Button>(card).Single(b => b.Content as string == "提交");
            submit.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.True(vm.Completion.IsCompletedSuccessfully);
            completion = vm.Completion;
            Assert.False(card.FindName("FieldContent") is ContentControl { IsEnabled: true });
        });
        Assert.Equal("临时目录", (await completion!)["values"]!["answer"]!.GetValue<string>());
    }

    private static AskUserCard Arrange(AskUserDialogViewModel vm, double width)
    {
        var card = new AskUserCard { DataContext = vm };
        card.Measure(new Size(width, double.PositiveInfinity));
        card.Arrange(new Rect(0, 0, width, card.DesiredSize.Height));
        card.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        return card;
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

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "WPF 控件测试未在时限内完成。");
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
