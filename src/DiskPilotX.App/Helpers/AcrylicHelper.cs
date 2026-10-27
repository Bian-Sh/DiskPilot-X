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
