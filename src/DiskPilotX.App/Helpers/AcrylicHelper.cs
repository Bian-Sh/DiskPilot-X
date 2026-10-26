using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DiskPilotX.App.Helpers;

/// <summary>
/// P/Invoke helpers for enabling Windows 11 Mica / Acrylic backdrop
/// and immersive dark mode via the Desktop Window Manager API.
/// </summary>
public static class AcrylicHelper
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    // DWMWA_SYSTEMBACKDROP_TYPE values
    private const int DWMSBT_MAINWINDOW = 2;   // Mica
    private const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic
    private const int DWMSBT_TABBEDWINDOW = 4; // Tabbed

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    /// <summary>
    /// Enables the immersive dark mode for the window's non-client area (title bar / borders).
    /// Works on Windows 10 1809+ and Windows 11.
    /// </summary>
    public static void EnableDarkMode(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        EnableDarkMode(hwnd);
    }

    public static void EnableDarkMode(IntPtr hwnd)
    {
        try
        {
            int value = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE,
                ref value, Marshal.SizeOf<int>());
        }
        catch
        {
            // Silently fail on older Windows versions
        }
    }

    /// <summary>
    /// Enables Mica backdrop material (Windows 11 22H2+, build 22621+).
    /// Falls back silently on unsupported versions.
    /// </summary>
    public static void EnableMica(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        EnableMica(hwnd);
    }

    public static void EnableMica(IntPtr hwnd)
    {
        if (!IsWindows11_22H2OrLater())
            return;

        try
        {
            int value = DWMSBT_MAINWINDOW;
            DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE,
                ref value, Marshal.SizeOf<int>());
        }
        catch
        {
            // Silently fail
        }
    }

    /// <summary>
    /// Enables Acrylic backdrop material (Windows 11 22H2+, build 22621+).
    /// Falls back silently on unsupported versions.
    /// </summary>
    public static void EnableAcrylic(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        EnableAcrylic(hwnd);
    }

    public static void EnableAcrylic(IntPtr hwnd)
    {
        if (!IsWindows11_22H2OrLater())
            return;

        try
        {
            int value = DWMSBT_TRANSIENTWINDOW;
            DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE,
                ref value, Marshal.SizeOf<int>());
        }
        catch
        {
            // Silently fail
        }
    }

    /// <summary>
    /// Disables the system backdrop (resets to default / no material).
    /// </summary>
    public static void DisableBackdrop(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        DisableBackdrop(hwnd);
    }

    public static void DisableBackdrop(IntPtr hwnd)
    {
        if (!IsWindows11_22H2OrLater())
            return;

        try
        {
            int value = 0; // DWMSBT_AUTO / none
            DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE,
                ref value, Marshal.SizeOf<int>());
        }
        catch
        {
            // Silently fail
        }
    }

    /// <summary>
    /// Returns true if running on Windows 11 22H2 or later (build 22621+),
    /// which is required for DWMWA_SYSTEMBACKDROP_TYPE support.
    /// </summary>
    public static bool IsWindows11_22H2OrLater()
    {
        return Environment.OSVersion.Platform == PlatformID.Win32NT
               && Environment.OSVersion.Version.Build >= 22621;
    }
}
