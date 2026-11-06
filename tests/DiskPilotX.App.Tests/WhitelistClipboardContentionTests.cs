using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using DiskPilotX.App.Services;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Llm;
using DiskPilotX.Core.Services.Settings;
using NSubstitute;

namespace DiskPilotX.App.Tests;

[CollectionDefinition("系统剪贴板", DisableParallelization = true)]
public sealed class SystemClipboardCollection { }

[Collection("系统剪贴板")]
public sealed class WhitelistClipboardContentionTests
{
    [Fact]
    public void 真实剪贴板被其他线程占用时复制不抛出异常()
    {
        using var clipboardLock = new ClipboardLock();
        RunSta(() =>
        {
            var saved = new AppSettings { ScanWhitelist = [new() { Path = @"D:\ClipboardTest", Description = "验收条目" }] };
            var settings = Substitute.For<ISettingsService>();
            settings.LoadSettings().Returns(saved);
            using var clients = new ModelHttpClientFactory();
            var vm = new SettingsViewModel(settings, new ModelConnectionTester(new ModelClientFactory(clients)), new ThemeManager(settings));
            var error = Record.Exception(() => vm.CopyWhitelistCommand.Execute("scan"));
            Assert.Null(error);
            Assert.False(string.IsNullOrWhiteSpace(vm.WhitelistErrorMessage));
            Assert.Single(vm.ScanWhitelist);
            settings.DidNotReceive().SaveSettings(Arg.Any<AppSettings>());
        });
    }

    [Fact]
    public void 真实剪贴板被其他线程占用时粘贴不抛异常也不改动配置()
    {
        using var clipboardLock = new ClipboardLock();
        RunSta(() =>
        {
            var saved = new AppSettings { ScanWhitelist = [new() { Path = @"D:\ClipboardTest", Description = "验收条目" }] };
            var settings = Substitute.For<ISettingsService>();
            settings.LoadSettings().Returns(saved);
            using var clients = new ModelHttpClientFactory();
            var vm = new SettingsViewModel(settings, new ModelConnectionTester(new ModelClientFactory(clients)), new ThemeManager(settings));
            var error = Record.Exception(() => vm.PasteWhitelistCommand.Execute("scan"));
            Assert.Null(error);
            Assert.False(string.IsNullOrWhiteSpace(vm.WhitelistErrorMessage));
            Assert.Equal("验收条目", Assert.Single(vm.ScanWhitelist).Description);
            settings.DidNotReceive().SaveSettings(Arg.Any<AppSettings>());
        });
    }
    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "STA 剪贴板检查超时。");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    // 仅持有系统剪贴板锁，不读取、清空或修改任何内容。
    private sealed class ClipboardLock : IDisposable
    {
        private readonly ManualResetEventSlim _ready = new();
        private readonly ManualResetEventSlim _release = new();
        private readonly Thread _thread;
        private int _error;
        public ClipboardLock()
        {
            _thread = new Thread(() =>
            {
                // NULL 窗口在本机不能形成跨线程竞争，使用仅消息窗口明确锁的归属。
                var owner = CreateWindowEx(0, "STATIC", "剪贴板竞争测试", 0, 0, 0, 0, 0,
                    new IntPtr(-3), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                if (owner == IntPtr.Zero)
                {
                    _error = Marshal.GetLastWin32Error() is var error && error != 0 ? error : 5;
                    _ready.Set();
                    return;
                }
                try
                {
                    var opened = false;
                    for (var attempt = 0; attempt < 20 && !opened; attempt++)
                    {
                        opened = OpenClipboard(owner);
                        if (!opened) Thread.Sleep(20);
                    }
                    if (!opened) _error = Marshal.GetLastWin32Error() is var error && error != 0 ? error : 5;
                    _ready.Set();
                    if (opened) { try { _release.Wait(); } finally { CloseClipboard(); } }
                }
                finally { DestroyWindow(owner); }
            });
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            if (!_ready.Wait(TimeSpan.FromSeconds(3))) throw new TimeoutException("未能建立剪贴板占用条件。");
            if (_error != 0) { Dispose(); throw new Win32Exception(_error, "未能锁定剪贴板以复现异常。"); }
            // 先证明其他线程确实打不开，避免假占用使复制测试覆盖用户剪贴板。
            if (OpenClipboard(IntPtr.Zero))
            {
                CloseClipboard();
                Dispose();
                throw new InvalidOperationException("剪贴板占用未生效，拒绝继续执行真实复制或粘贴。");
            }
        }
        public void Dispose() { _release.Set(); _thread.Join(); _ready.Dispose(); _release.Dispose(); }
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(int extendedStyle, string className, string windowName,
            int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyWindow(IntPtr window);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenClipboard(IntPtr owner);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseClipboard();
    }
}
