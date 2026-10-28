using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DiskPilotX.App.Controls;

/// <summary>在预览阶段接管文档正文的滚轮，避免内层文档查看器吞掉冒泡事件。</summary>
public static class ReleaseNotesScroll
{
    public static void ForwardMouseWheel(ScrollViewer viewer, MouseWheelEventArgs e)
    {
        if (e.Handled || e.Delta == 0) return;
        var lines = SystemParameters.WheelScrollLines;
        var distance = lines < 0 ? viewer.ViewportHeight : lines * 19.0;
        viewer.ScrollToVerticalOffset(viewer.VerticalOffset - e.Delta / 120.0 * distance);
        // 到达边界也不让事件泄漏给弹窗背后的应用界面。
        e.Handled = true;
    }
}
