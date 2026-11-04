using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using DiskPilotX.App.Diagnostics;
using DiskPilotX.Core.Enums;

namespace DiskPilotX.App;

public partial class MainWindow
{
    private ToolTip? acceptanceUsageTip;

    /// <summary>仅在验收模式打开真实回复的用量卡，不注入或改写统计。</summary>
    internal object AcceptanceUsageCard(string? action, string? outputPath = null)
    {
        if (!AcceptanceAutomationServer.IsEnabled() || _chatViewModel is null)
            throw new InvalidOperationException("用量卡验收入口未启用。");
        if (action == "close")
        {
            if (acceptanceUsageTip is not null) acceptanceUsageTip.IsOpen = false;
            return new { Closed = true };
        }
        var message = _chatViewModel.Messages.LastOrDefault(item => item.Sender == ChatSender.Assistant);
        var badge = Walk(ChatPanelContainer).OfType<Border>().LastOrDefault(item =>
            item.Name == "UsageBadge" && ReferenceEquals(item.DataContext, message));
        if (message?.HasUsage != true || badge?.ToolTip is not ToolTip tip || !badge.IsVisible)
        {
            if (acceptanceUsageTip is not null) acceptanceUsageTip.IsOpen = false;
            return new { Available = false, BadgeVisible = badge?.IsVisible == true };
        }
        if (action is "open" or "capture")
        {
            if (acceptanceUsageTip is not null && !ReferenceEquals(acceptanceUsageTip, tip)) acceptanceUsageTip.IsOpen = false;
            acceptanceUsageTip = tip;
            tip.PlacementTarget = badge;
            tip.IsOpen = true;
            tip.ApplyTemplate();
            tip.UpdateLayout();
        }
        if (action == "capture") return AcceptanceCapture(outputPath, "usage");
        return new { Available = true, BadgeVisible = badge.IsVisible, tip.IsOpen, tip.ActualWidth, tip.ActualHeight,
            PopupWindowId = (PresentationSource.FromVisual(tip) as HwndSource)?.Handle.ToInt64(),
            Summary = message.UsageSummary, Total = message.UsageTotalText, Rows = message.UsageTooltipDetails };
    }
}