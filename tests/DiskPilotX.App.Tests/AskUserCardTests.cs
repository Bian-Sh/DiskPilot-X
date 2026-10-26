using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using DiskPilotX.App.ViewModels;
using DiskPilotX.App.Views;
using DiskPilotX.Core.Models;

namespace DiskPilotX.App.Tests;

public sealed class AskUserCardTests
{
    [Fact]
    public void 窄卡片约束长题目高度且保留底部分页和右上角取消() => RunSta(() =>
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
        var next = (Button)card.FindName("NextButton");
        var previous = (Button)card.FindName("PreviousButton");
        var location = next.TransformToAncestor(card).Transform(new Point());
        Assert.Equal(28, next.ActualHeight);
        Assert.True(location.Y >= 0 && location.Y + next.ActualHeight <= card.ActualHeight);
        var start = previous.TransformToAncestor(card).Transform(new Point());
        Assert.InRange((start.X + location.X + next.ActualWidth) / 2, card.ActualWidth / 2 - 1, card.ActualWidth / 2 + 1);
        var close = (Button)card.FindName("CancelButton");
        var closeLocation = close.TransformToAncestor(card).Transform(new Point());
        Assert.True(closeLocation.X > card.ActualWidth - 45 && closeLocation.Y < 30);
        Assert.DoesNotContain(Visuals<Button>(card), button => button.Content as string is "提交" or "继续" or "取消");
    });

    [Fact]
    public async Task 控件实际文本绑定和回车提交返回用户输入()
    {
        Task<JsonNode>? completion = null;
        RunSta(() =>
        {
            var request = UserDecisionRequest.Parse(JsonNode.Parse("""{"title":"确认","question":"希望清理哪里？","fields":[{"id":"answer","label":"目标目录","type":"text","is_required":true}]}"""));
            var vm = new AskUserDialogViewModel(request);
            var card = Arrange(vm, 232);
            var input = Assert.Single(Visuals<TextBox>(card));
            input.Text = "临时目录";
            input.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            using var source = new HwndSource(new HwndSourceParameters("问答输入回归")
            { Width = 232, Height = 420, WindowStyle = unchecked((int)0x80000000) }) { RootVisual = card };
            input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Enter)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Assert.True(vm.Completion.IsCompletedSuccessfully);
            completion = vm.Completion;
            Assert.False(card.FindName("FieldContent") is ContentControl { IsEnabled: true });
        });
        Assert.Equal("临时目录", (await completion!)["values"]!["answer"]!.GetValue<string>());
    }

    [Fact]
    public void 下一题校验当前答案且不会提前提交() => RunSta(() =>
    {
        var request = UserDecisionRequest.Parse(JsonNode.Parse("""{"title":"确认","question":"请补充信息","fields":[{"id":"first","label":"第一题","type":"text","is_required":true},{"id":"second","label":"第二题","type":"text","is_required":true,"default_text":"已有答案"}]}"""));
        var vm = new AskUserDialogViewModel(request);
        var card = Arrange(vm, 232);
        var next = (Button)card.FindName("NextButton");
        next.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.Equal(0, vm.CurrentFieldIndex);
        Assert.True(vm.CurrentField.HasError);
        var input = Assert.Single(Visuals<TextBox>(card));
        input.Text = "补充的答案";
        input.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        next.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.Equal(1, vm.CurrentFieldIndex);
        Assert.False(vm.Completion.IsCompleted);
        ((Button)card.FindName("CancelButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.True(vm.Completion.IsCompletedSuccessfully);
    });

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
