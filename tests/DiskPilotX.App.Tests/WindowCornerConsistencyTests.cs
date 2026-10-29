using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using DiskPilotX.App.Views;
using DiskPilotX.Core.Models;
namespace DiskPilotX.App.Tests;

[Collection("聊天滚动控件")]
public sealed class WindowCornerConsistencyTests
{
    [Fact]
    public void 系统圆角不叠加人工区域与Wpf轮廓() => StaHost.Run(() =>
    {
        var window=new SkillWindow(new SkillUiSpec { Title="单一圆角验收" });
        try
        {
            var hwnd=new WindowInteropHelper(window).EnsureHandle();
            var frame=(Border)window.Content;
            Assert.Null(frame.Clip);
            Assert.Equal(new CornerRadius(0),frame.CornerRadius);
            Assert.Equal(new Thickness(0),frame.BorderThickness);
            var chrome=System.Windows.Shell.WindowChrome.GetWindowChrome(window);
            Assert.Equal(new CornerRadius(0),chrome.CornerRadius);
            Assert.Equal(new Thickness(-1),chrome.GlassFrameThickness);
            var region=CreateRectRgn(0,0,0,0);
            try { Assert.Equal(0,GetWindowRgn(hwnd,region)); }
            finally { DeleteObject(region); }
            if(OperatingSystem.IsWindowsVersionAtLeast(10,0,22000))
            {
                Assert.Equal(0,DwmGetWindowAttribute(hwnd,33,out var preference,sizeof(int)));
                Assert.Equal(2,preference);
            }
        }
        finally { window.Close(); }
    });    [DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr window,IntPtr region);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left,int top,int right,int bottom);
    [DllImport("gdi32.dll")] private static extern bool PtInRegion(IntPtr region,int x,int y);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out int value,int size);
}
