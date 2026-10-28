using System.Net.Http;
using System.Xml.Linq;
using DiskPilotX.App.Services;
using DiskPilotX.App.ViewModels;
using NSubstitute;

namespace DiskPilotX.App.Tests;

public sealed class UpdateDisplayAndAutomaticDownloadTests
{
    [Fact]
    public void 右下角始终绑定当前版本且不使用更新状态或更新颜色()
    {
        var root = FindRoot();
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var main = XDocument.Load(Path.Combine(root, "src/DiskPilotX.App/MainWindow.xaml"));
        var version = main.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "VersionDisplay");
        Assert.Contains(version.Descendants(), e => e.Name.LocalName == "Run" && (string?)e.Attribute("Text") == "v");
        Assert.Contains(version.Descendants().Attributes(), a => a.Value == "{Binding UpdateVM.CurrentVersion, Mode=OneWay}");
        Assert.DoesNotContain(version.Descendants().Attributes(), a => a.Value.Contains("UpdateVM.") && !a.Value.Contains("UpdateVM.CurrentVersion"));
    }

    [Fact]
    public void 右下角真实标记不会在版本前缀之间插入空格() => StaHost.Run(() =>
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var main = XDocument.Load(Path.Combine(FindRoot(), "src/DiskPilotX.App/MainWindow.xaml"), LoadOptions.PreserveWhitespace);
        var version = new XElement(main.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "VersionDisplay"));
        // 保留源标记的空白，仅移除不影响版本文字的外观、事件与资源依赖。
        version.RemoveAttributes();
        version.Elements().Where(e => e.Name.LocalName != "Run").Remove();
        var text = (System.Windows.Controls.TextBlock)System.Windows.Markup.XamlReader.Parse(version.ToString(SaveOptions.DisableFormatting));
        text.DataContext = new { UpdateVM = new UpdateViewModel(CreateService()) };
        using var host = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("版本文字验收") { Width = 400, Height = 50 }) { RootVisual = text };
        text.Measure(new System.Windows.Size(400, 50));
        text.Arrange(new System.Windows.Rect(0, 0, 400, 50));
        text.UpdateLayout();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Assert.Equal("v1.0.0", new System.Windows.Documents.TextRange(text.ContentStart, text.ContentEnd).Text);
    });

    [Fact]
    public async Task 自动发现新版后后台下载并在右上角等待安装而不自动安装()
    {
        var service = CreateService();
        service.CheckForUpdateAsync(Arg.Any<CancellationToken>(), false).Returns(NewRelease());
        var download = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.DownloadMsiAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<long>(), Arg.Any<IProgress<(long, long)>>(), Arg.Any<CancellationToken>()).Returns(download.Task);
        var vm = new UpdateViewModel(service);
        var startup = vm.CheckAtStartupAsync();
        try
        {
            Assert.True(vm.IsDownloading);
            Assert.True(vm.IsUpdateEntryVisible);
            Assert.Equal("1.0.0", vm.CurrentVersion);
            Assert.False(vm.IsChecking);
            service.DidNotReceive().LaunchInstaller(Arg.Any<string>());
        }
        finally { download.TrySetResult(Path.Combine(Path.GetTempPath(), "只读安装器夹具.exe")); }
        await startup;
        Assert.True(vm.IsDownloadComplete);
        Assert.True(vm.IsUpdateEntryVisible);
        Assert.False(vm.IsDownloading);
        service.DidNotReceive().LaunchInstaller(Arg.Any<string>());
        await service.Received(1).DownloadMsiAsync("https://example.org/setup.exe", "2.0.0", 3, Arg.Any<IProgress<(long, long)>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task 本地远程都是一零零时不下载且右上角隐藏()
    {
        var service = CreateService();
        service.CheckForUpdateAsync(Arg.Any<CancellationToken>(), false).Returns(new UpdateCheckResult { Success = true, LatestVersion = "1.0.0", UpdateAvailable = false });
        var vm = new UpdateViewModel(service);
        await vm.CheckAtStartupAsync();
        Assert.True(vm.CheckCompleted);
        Assert.Equal("1.0.0", vm.CurrentVersion);
        Assert.False(vm.IsUpdateEntryVisible);
        await AssertNoDownload(service);
    }

    [Fact]
    public async Task 已有完整缓存时只提示安装不重复下载()
    {
        var service = CreateService();
        service.CheckForUpdateAsync(Arg.Any<CancellationToken>(), false).Returns(NewRelease());
        service.GetCachedInstallerPath("2.0.0").Returns(Path.Combine(Path.GetTempPath(), "已校验缓存.exe"));
        var vm = new UpdateViewModel(service);
        await vm.CheckAtStartupAsync();
        Assert.True(vm.IsDownloadComplete);
        Assert.True(vm.IsUpdateEntryVisible);
        await AssertNoDownload(service);
        service.DidNotReceive().LaunchInstaller(Arg.Any<string>());
    }

    [Fact]
    public async Task 禁用自动检查也不会自动下载()
    {
        var service = CreateService();
        var vm = new UpdateViewModel(service) { AutoCheckUpdates = false, UpdateAvailable = true, LatestVersion = "2.0.0", MsiDownloadUrl = "https://example.org/setup.exe" };
        await vm.CheckAtStartupAsync();
        await service.DidNotReceive().CheckForUpdateAsync(Arg.Any<CancellationToken>(), Arg.Any<bool>());
        await AssertNoDownload(service);
    }

    [Fact]
    public async Task 自动检查失败时不下载旧结果并在右上角显示错误()
    {
        var service = CreateService();
        service.CheckForUpdateAsync(Arg.Any<CancellationToken>(), false).Returns(UpdateCheckResult.Failed);
        var vm = new UpdateViewModel(service) { UpdateAvailable = true, LatestVersion = "2.0.0", MsiDownloadUrl = "https://example.org/setup.exe" };
        await vm.CheckAtStartupAsync();
        Assert.True(vm.HasError);
        Assert.True(vm.IsUpdateEntryVisible);
        await AssertNoDownload(service);
    }

    [Fact]
    public async Task 自动下载失败保留右上角重试入口且不启动安装器()
    {
        var service = CreateService();
        service.CheckForUpdateAsync(Arg.Any<CancellationToken>(), false).Returns(NewRelease());
        service.DownloadMsiAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<long>(), Arg.Any<IProgress<(long, long)>>(), Arg.Any<CancellationToken>()).Returns(Task.FromException<string>(new HttpRequestException("验收断网")));
        var vm = new UpdateViewModel(service);
        await vm.CheckAtStartupAsync();
        Assert.True(vm.HasError);
        Assert.False(vm.IsDownloadComplete);
        Assert.False(vm.IsDownloading);
        Assert.True(vm.IsUpdateEntryVisible);
        service.DidNotReceive().LaunchInstaller(Arg.Any<string>());
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void 正在检查或检查失败时状态入口位于右上角(bool checking, bool error)
    {
        var vm = new UpdateViewModel(CreateService()) { IsChecking = checking, HasError = error };
        Assert.True(vm.IsUpdateEntryVisible);
    }

    [Fact]
    public async Task 无已知更新的检查错误可从右上角重新检查()
    {
        var service = CreateService();
        service.CheckForUpdateAsync(Arg.Any<CancellationToken>(), false).Returns(UpdateCheckResult.Failed);
        service.CheckForUpdateAsync(Arg.Any<CancellationToken>(), true).Returns(new UpdateCheckResult { Success = true, LatestVersion = "1.0.0" });
        var vm = new UpdateViewModel(service);
        await vm.CheckAtStartupAsync();
        Assert.True(vm.HasError);
        await vm.ActivateUpdateCommand.ExecuteAsync(null);
        await service.Received(1).CheckForUpdateAsync(Arg.Any<CancellationToken>(), true);
        Assert.False(vm.HasError);
        Assert.False(vm.IsUpdateEntryVisible);
    }

    private static IUpdateService CreateService()
    {
        var service = Substitute.For<IUpdateService>();
        service.GetCurrentVersion().Returns("1.0.0");
        return service;
    }

    private static UpdateCheckResult NewRelease() => new() { Success = true, UpdateAvailable = true, LatestVersion = "2.0.0", MsiDownloadUrl = "https://example.org/setup.exe", MsiFileSize = 3 };
    private static async Task AssertNoDownload(IUpdateService service) => await service.DidNotReceive().DownloadMsiAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<long>(), Arg.Any<IProgress<(long, long)>>(), Arg.Any<CancellationToken>());
    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "src/DiskPilotX.App/MainWindow.xaml"))) return dir.FullName;
        throw new DirectoryNotFoundException("找不到测试源码根目录");
    }
}
