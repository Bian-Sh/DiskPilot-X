using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DiskPilotX.App.Controls;

namespace DiskPilotX.App.Tests;

[Collection("聊天滚动控件")]
public sealed class AppModalConfirmationTests
{
    [Fact]
    public void 删除确认复用模态按钮样式且默认取消() => StaHost.Run(() =>
    {
        var host = CreateHost();
        _ = host.ShowConfirmationAsync("删除会话", "删除后无法恢复", "确认删除");
        var buttons = Buttons(host);
        var cancel = buttons.Single(button => button.IsCancel);
        var confirm = buttons.Single(button => Equals(button.Content, "确认删除"));
        Assert.True(cancel.IsDefault);
        Assert.False(confirm.IsDefault);
        Assert.Same(host.Resources["VP.AccentButton"], confirm.Style);
        Assert.Same(host.Resources["VP.Button"], cancel.Style);
        Assert.Equal(Visibility.Visible, host.Visibility);
        host.CloseCurrent(ModalResult.Negative);
    });

    [Fact]
    public void 原有关闭确认的默认按钮保持不变() => StaHost.Run(() =>
    {
        var host = CreateHost();
        _ = host.ShowAsync("关闭应用", "确定关闭？", ModalMessageType.Warning,
            ModalButtonFlags.Positive | ModalButtonFlags.Negative);
        var buttons = Buttons(host);
        Assert.True(buttons.Single(button => !button.IsCancel).IsDefault);
        Assert.False(buttons.Single(button => button.IsCancel).IsDefault);
        host.CloseCurrent(ModalResult.Negative);
    });

    private static AppModalHost CreateHost()
    {
        var host = new AppModalHost();
        foreach (var key in new[] { "VP.SurfaceBrush", "VP.TextPrimaryBrush", "VP.TextSecondaryBrush", "VP.WarningTextBrush" })
            host.Resources[key] = Brushes.Black;
        host.Resources["VP.AccentButton"] = new Style(typeof(Button));
        host.Resources["VP.Button"] = new Style(typeof(Button));
        return host;
    }

    private static Button[] Buttons(AppModalHost host)
    {
        var presenter = (ContentPresenter)host.FindName("ModalContentPresenter");
        var root = (Grid)presenter.Content;
        return root.Children.OfType<StackPanel>().SelectMany(panel => panel.Children.OfType<Button>()).ToArray();
    }
}
