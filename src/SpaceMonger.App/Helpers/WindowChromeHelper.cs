using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using SpaceMonger.App.Controls;
using Border = System.Windows.Controls.Border;

namespace SpaceMonger.App.Helpers;

/// <summary>任务窗复用应用标题栏，保留系统缩放和各显示器工作区边界。</summary>
internal static class WindowChromeHelper
{
    public static void Apply(Window window, Border frame, WindowTitleBar titleBar)
    {
        window.WindowStyle = WindowStyle.None;
        window.ResizeMode = ResizeMode.CanResize;
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = 36,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0),
            UseAeroCaptionButtons = false
        });
        WindowChrome.SetIsHitTestVisibleInChrome(titleBar, true);
        void UpdateClip() => frame.Clip = new RectangleGeometry(
            new Rect(0, 0, frame.ActualWidth, frame.ActualHeight), frame.CornerRadius.TopLeft, frame.CornerRadius.TopLeft);
        var cornerProperty = DependencyPropertyDescriptor.FromProperty(Border.CornerRadiusProperty, typeof(Border));
        EventHandler updateClip = (_, _) => UpdateClip();
        cornerProperty.AddValueChanged(frame, updateClip);
        window.Closed += (_, _) => cornerProperty.RemoveValueChanged(frame, updateClip);
        void UpdateFrame()
        {
            var maximized = window.WindowState == WindowState.Maximized;
            titleBar.UpdateMaximizeIcon(maximized);
            if (maximized) frame.CornerRadius = new CornerRadius(0);
            else frame.SetResourceReference(Border.CornerRadiusProperty, "VP.WindowCornerRadius");
            UpdateClip();
        }
        frame.SizeChanged += (_, _) => UpdateFrame();
        window.StateChanged += (_, _) => UpdateFrame();
        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            var source = HwndSource.FromHwnd(handle);
            source?.AddHook(ConstrainMaximize);
            // Windows 11 使用原生圆角；旧系统保留内容裁切和可缩放边框。
            var rounded = 2;
            _ = DwmSetWindowAttribute(handle, 33, ref rounded, sizeof(int));
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
