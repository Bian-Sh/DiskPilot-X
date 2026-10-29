using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using DiskPilotX.App.Controls;
using Border = System.Windows.Controls.Border;

namespace DiskPilotX.App.Helpers;

/// <summary>任务窗复用应用标题栏，保留系统缩放和各显示器工作区边界。</summary>
internal static class WindowChromeHelper
{
    public static void Apply(Window window, Border frame, WindowTitleBar titleBar)
    {
        window.WindowStyle = WindowStyle.None;
        // 非透明 HWND 的透明底会在圆角抗锯齿处漏出黑色；底层与内容必须同色。
        window.SetResourceReference(Window.BackgroundProperty, "VP.BackgroundAltBrush");
        window.UseLayoutRounding = true;
        window.SnapsToDevicePixels = true;
        window.ResizeMode = ResizeMode.CanResize;
        var chrome = new WindowChrome
        {
            CaptionHeight = 36,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(-1),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        };
        WindowChrome.SetWindowChrome(window, chrome);
        WindowChrome.SetIsHitTestVisibleInChrome(titleBar, true);
        // 只让 DWM 绘制和裁切外轮廓：人工 HRGN、WPF 圆角和 Clip 叠加会留下错位白边。
        frame.ClearValue(Border.CornerRadiusProperty);
        frame.CornerRadius = new CornerRadius(0);
        frame.ClearValue(Border.ClipProperty);
        frame.BorderThickness = new Thickness(0);
        void UpdateFrame()
        {
            titleBar.UpdateMaximizeIcon(window.WindowState == WindowState.Maximized);
        }
        frame.SizeChanged += (_, _) => UpdateFrame();
        window.StateChanged += (_, _) => UpdateFrame();
        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            var source = HwndSource.FromHwnd(handle);
            source?.AddHook(ConstrainMaximize);
            // DWM 是唯一外轮廓来源；Windows 11 原生抗锯齿自动处理最大化和还原。
            var rounded = 2;
            _ = DwmSetWindowAttribute(handle, 33, ref rounded, sizeof(int));
            // 原生边框也关闭，不再与主题内容叠出第二道轮廓。
            var noBorder = unchecked((int)0xFFFFFFFE);
            _ = DwmSetWindowAttribute(handle, 34, ref noBorder, sizeof(int));
            UpdateFrame();
            window.Closed += (_, _) => source?.RemoveHook(ConstrainMaximize);
        };
    }

    private static IntPtr ConstrainMaximize(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != 0x0024) return IntPtr.Zero;
        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref monitor)) return IntPtr.Zero;
        var bounds = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        bounds.MaxPosition = new NativePoint(monitor.Work.Left - monitor.Monitor.Left, monitor.Work.Top - monitor.Monitor.Top);
        bounds.MaxSize = new NativePoint(monitor.Work.Right - monitor.Work.Left, monitor.Work.Bottom - monitor.Work.Top);
        Marshal.StructureToPtr(bounds, lParam, false);
        // 继续交给 WPF 写入按当前 DPI 换算的最小、最大跟踪尺寸。
        handled = false;
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y) { public int X = x, Y = y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo { public NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
