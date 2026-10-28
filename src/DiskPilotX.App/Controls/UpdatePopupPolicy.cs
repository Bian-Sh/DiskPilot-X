using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace DiskPilotX.App.Controls;

/// <summary>浮层不靠离开定时器关闭；只观察外部真正可交互的命中祖先。</summary>
public static class UpdatePopupPolicy
{
    public static bool ShouldClose(DependencyObject? hit, DependencyObject popup, DependencyObject trigger)
    {
        var interactive = false;
        for (var node = hit; node is not null; node = Parent(node))
        {
            if (ReferenceEquals(node, popup) || ReferenceEquals(node, trigger)) return false;
            if (node is UIElement { IsEnabled: false }) return false;
            interactive |= node is ButtonBase or TextBoxBase or Selector or RangeBase or MenuItem or PasswordBox or Hyperlink
                || node is FrameworkElement { Cursor: var cursor } && cursor == Cursors.Hand
                || node is UIElement { Focusable: true } && node is not (ScrollViewer or Window);
        }
        return interactive;
    }

    public static bool ShouldCloseOnPointerDown(DependencyObject? hit, DependencyObject popup, DependencyObject trigger)
    {
        for (var node = hit; node is not null; node = Parent(node))
            if (ReferenceEquals(node, popup) || ReferenceEquals(node, trigger)) return false;
        return true;
    }

    private static DependencyObject? Parent(DependencyObject node) => node switch
    {
        Visual or Visual3D => VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node),
        FrameworkContentElement content => content.Parent,
        _ => LogicalTreeHelper.GetParent(node)
    };
}
