using DiskPilotX.App.Services;

namespace DiskPilotX.App.Tests;

/// <summary>
/// 卸载命令解析：注册表里的 UninstallString 必须能拆成「可执行文件 + 参数」，
/// 否则会把整串命令当成文件名交给 Process.Start 直接失败。
/// </summary>
public sealed class SteamUninstallCommandTests
{
    [Fact]
    public void 拆分带引号的路径与协议参数()
    {
        var (exe, args) = SteamGameUninstaller.SplitCommand(@"""D:\Program Files\Steam\steam.exe"" steam://uninstall/970800");

        Assert.Equal(@"D:\Program Files\Steam\steam.exe", exe);
        Assert.Equal("steam://uninstall/970800", args);
    }

    [Fact]
    public void 拆分不带引号的路径()
    {
        var (exe, args) = SteamGameUninstaller.SplitCommand(@"C:\Steam\steam.exe steam://uninstall/220");

        Assert.Equal(@"C:\Steam\steam.exe", exe);
        Assert.Equal("steam://uninstall/220", args);
    }

    [Fact]
    public void 只有可执行文件时不丢字也不多出参数()
    {
        var (exe, args) = SteamGameUninstaller.SplitCommand(@"C:\Steam\steam.exe");

        Assert.Equal(@"C:\Steam\steam.exe", exe);
        Assert.Equal(string.Empty, args);
    }

    [Fact]
    public void 空命令返回空而不是抛异常()
    {
        var (exe, args) = SteamGameUninstaller.SplitCommand("   ");

        Assert.Equal(string.Empty, exe);
        Assert.Equal(string.Empty, args);
    }
}
