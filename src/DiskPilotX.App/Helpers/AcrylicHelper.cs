using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DiskPilotX.App.Helpers;

/// <summary>通过系统 DWM 应用统一 Acrylic，调用失败时回退实色背景。</summary>
public static class AcrylicHelper
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMSBT_NONE = 1;
    private const int DWMSBT_TRANSIENTWINDOW = 3;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    public static void SetDarkMode(Window window, bool enabled) =>
        TrySetAttribute(window, DWMWA_USE_IMMERSIVE_DARK_MODE, enabled ? 1 : 0);

    public static bool TryEnableAcrylic(Window window) =>
        IsWindows11_22H2OrLater() && TrySetAttribute(window, DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_TRANSIENTWINDOW);

    public static void DisableBackdrop(Window window)
    {
        if (IsWindows11_22H2OrLater())
            TrySetAttribute(window, DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_NONE);
    }

    private static readonly DependencyProperty BackgroundGlassStateProperty = DependencyProperty.RegisterAttached(
        "BackgroundGlassState",typeof(BackgroundGlassState),typeof(AcrylicHelper),new PropertyMetadata(null));

    /// <summary>保持非客户区材质的活跃绘制，不发送 WM_ACTIVATE、不改变输入焦点。</summary>
    public static bool SetKeepGlassWhenInactive(Window window,bool enabled)
    {
        var state=(BackgroundGlassState?)window.GetValue(BackgroundGlassStateProperty);
        if(!enabled)
        {
            window.ClearValue(BackgroundGlassStateProperty);
            state?.Detach(restore:true);
            return false;
        }
        if(state is null)
        {
            var source=HwndSource.FromHwnd(new WindowInteropHelper(window).EnsureHandle());
            if(source is null) return false;
            state=new BackgroundGlassState(window,source);
            window.SetValue(BackgroundGlassStateProperty,state);
        }
        state.Refresh();
        return true;
    }

    public static bool IsKeepingGlassWhenInactive(Window window) => window.GetValue(BackgroundGlassStateProperty) is BackgroundGlassState;

    private sealed class BackgroundGlassState
    {
        private readonly Window window;
        private readonly HwndSource source;
        private bool detached;
        public BackgroundGlassState(Window window,HwndSource source)
        {
            this.window=window;this.source=source;
            source.AddHook(OnMessage);
            window.Deactivated+=OnDeactivated;
            window.Closed+=OnClosed;
        }
        public void Refresh()
        {
            if(!detached && !source.IsDisposed)
                _=DefWindowProc(source.Handle,0x0086,new IntPtr(1),new IntPtr(-1));
        }
        private IntPtr OnMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
        {
            if(detached || message!=0x0086) return IntPtr.Zero;
            handled=true;
            return DefWindowProc(hwnd,message,new IntPtr(1),new IntPtr(-1));
        }
        private void OnDeactivated(object? sender,EventArgs args) => Refresh();
        private void OnClosed(object? sender,EventArgs args)
        {
            window.ClearValue(BackgroundGlassStateProperty);
            Detach(restore:false);
        }
        public void Detach(bool restore)
        {
            if(detached) return;
            detached=true;
            window.Deactivated-=OnDeactivated;
            window.Closed-=OnClosed;
            if(!source.IsDisposed)
            {
                source.RemoveHook(OnMessage);
                if(restore) _=DefWindowProc(source.Handle,0x0086,window.IsActive ? new IntPtr(1) : IntPtr.Zero,new IntPtr(-1));
            }
        }
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr DefWindowProc(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam);
    private static bool TrySetAttribute(Window window, int attribute, int value)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).EnsureHandle();
            return DwmSetWindowAttribute(hwnd, attribute, ref value, Marshal.SizeOf<int>()) >= 0;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }

    public static bool IsWindows11_22H2OrLater() =>
        Environment.OSVersion.Platform == PlatformID.Win32NT
        && Environment.OSVersion.Version.Build >= 22621;
}
