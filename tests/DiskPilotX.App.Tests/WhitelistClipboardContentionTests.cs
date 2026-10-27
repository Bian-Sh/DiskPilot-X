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
                var opened = false;
                for (var attempt = 0; attempt < 20 && !opened; attempt++)
                {
                    opened = OpenClipboard(IntPtr.Zero);
                    if (!opened) Thread.Sleep(20);
                }
                if (!opened) _error = Marshal.GetLastWin32Error() is var error && error != 0 ? error : 5;
                _ready.Set();
                if (opened) { try { _release.Wait(); } finally { CloseClipboard(); } }
            });
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            if (!_ready.Wait(TimeSpan.FromSeconds(3))) throw new TimeoutException("未能建立剪贴板占用条件。");
            if (_error != 0) { Dispose(); throw new Win32Exception(_error, "未能锁定剪贴板以复现异常。"); }
        }
        public void Dispose() { _release.Set(); _thread.Join(); _ready.Dispose(); _release.Dispose(); }
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenClipboard(IntPtr owner);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseClipboard();
    }
}
