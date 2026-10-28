using System.Net;
using System.Net.Http;
using System.Text;
using System.Xml.Linq;
using DiskPilotX.App.Services;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Settings;
using NSubstitute;

namespace DiskPilotX.App.Tests;

public class UpdateExperienceTests
{
    [Fact]
    public async Task 手动检查失败可见且保留已知更新()
    {
        var service = Substitute.For<IUpdateService>();
        service.GetCurrentVersion().Returns("1.0.0");
        service.CheckForUpdateAsync(Arg.Any<CancellationToken>(), true).Returns(new UpdateCheckResult { Success = true, UpdateAvailable = true, LatestVersion = "2.0.0", MsiDownloadUrl = "https://example.org/update.msi" }, UpdateCheckResult.Failed);
        var vm = new UpdateViewModel(service);
        await vm.CheckForUpdateCommand.ExecuteAsync(null);
        Assert.True(vm.IsUpdateEntryVisible);
        await vm.CheckForUpdateCommand.ExecuteAsync(null);
        Assert.True(vm.HasError);
        Assert.True(vm.UpdateAvailable);
        Assert.False(vm.IsChecking);
    }

    [Fact]
    public async Task 下载失败显示错误而不会误报安装就绪()
    {
        var service = Substitute.For<IUpdateService>();
        service.GetCurrentVersion().Returns("1.0.0");
        service.DownloadMsiAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<long>(), Arg.Any<IProgress<(long downloaded, long total)>>(), Arg.Any<CancellationToken>()).Returns(Task.FromException<string>(new IOException("网络中断")));
        var vm = new UpdateViewModel(service) { UpdateAvailable = true, LatestVersion = "2.0.0", MsiDownloadUrl = "https://example.org/update.msi" };
        await vm.DownloadCommand.ExecuteAsync(null);
        Assert.True(vm.HasError);
        Assert.False(vm.IsDownloadComplete);
        Assert.False(vm.IsDownloading);
    }

    [Fact]
    public async Task 历史翻页失败保留内容并重试原页且过滤重复版本()
    {
        var service = Substitute.For<IUpdateService>();
        service.GetCurrentVersion().Returns("1.0.0");
        service.GetReleasesAsync(1, Arg.Any<CancellationToken>()).Returns(Enumerable.Range(1, 20).Select(i => new UpdateRelease { Version = $"2.0.{i}" }).ToArray());
        var attempts = 0;
        service.GetReleasesAsync(2, Arg.Any<CancellationToken>()).Returns(_ => ++attempts == 1 ? Task.FromException<IReadOnlyList<UpdateRelease>>(new HttpRequestException("断网")) : Task.FromResult<IReadOnlyList<UpdateRelease>>([new() { Version = "2.0.20" }, new() { Version = "1.0.0" }]));
        var vm = new UpdateViewModel(service);
        await vm.LoadHistoryCommand.ExecuteAsync(null);
        await vm.LoadMoreHistoryCommand.ExecuteAsync(null);
        Assert.Equal(20, vm.Releases.Count);
        Assert.True(vm.HistoryHasError);
        await vm.LoadMoreHistoryCommand.ExecuteAsync(null);
        Assert.Equal(21, vm.Releases.Count);
        Assert.False(vm.HistoryHasError);
        Assert.False(vm.CanLoadMoreHistory);
    }

    [Fact]
    public void 更新偏好持久化且不覆盖其他设置()
    {
        var settings = Substitute.For<ISettingsService>();
        var saved = new AppSettings { Language = "zh-TW" };
        settings.LoadSettings().Returns(saved);
        var vm = new UpdateViewModel(Substitute.For<IUpdateService>(), settings);
        settings.ClearReceivedCalls();
        vm.AutoCheckUpdates = false;
        vm.SelectedProxyKind = DiskPilotX.Core.Services.Llm.ProxyKind.Direct;
        Assert.False(saved.AutoCheckUpdates);
        Assert.Equal("direct", saved.UpdateProxyMode);
        Assert.Equal("zh-TW", saved.Language);
        settings.Received(2).SaveSettings(saved);
    }

    [Fact]
    public async Task 不完整安装包不会作为可安装文件缓存()
    {
        var dir = Path.Combine(Path.GetTempPath(), "DiskPilotX-update-tests", Guid.NewGuid().ToString("N"));
        using var client = new HttpClient(new ResponseHandler("abc"));
        var service = new UpdateService(client, dir);
        await Assert.ThrowsAsync<IOException>(() => service.DownloadMsiAsync("https://example.org/update.msi", "2.0.0", 9, new Progress<(long,long)>(), CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(dir, "DiskPilot-X-2.0.0-win64.msi")));
    }

    [Fact]
    public void 标题栏浮层受限高且不在离开时关闭_关于页没有更新命令()
    {
        var root = FindRoot();
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var title = XDocument.Load(Path.Combine(root, "src/DiskPilotX.App/Controls/WindowTitleBar.xaml"));
        var popup = title.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "UpdateReleasePopup");
        Assert.Equal("True", (string?)popup.Attribute("StaysOpen"));
        Assert.Contains(popup.Descendants(), e => (string?)e.Attribute("MaxHeight") == "420");
        Assert.Contains(popup.Descendants(), e => e.Name.LocalName == "ScrollViewer" && (string?)e.Attribute("VerticalScrollBarVisibility") == "Auto");
        Assert.DoesNotContain(popup.Descendants(), e => e.Attribute("MouseLeave") != null);
        Assert.DoesNotContain(popup.Descendants(), e => e.Name.LocalName == "Button");
        Assert.Contains(popup.Descendants(), e => e.Name.LocalName == "ScrollViewer" && e.Attribute("PreviewMouseWheel") != null);
        var about = XDocument.Load(Path.Combine(root,"src/DiskPilotX.App/Views/AboutPage.xaml"));
        Assert.DoesNotContain(about.Descendants().Attributes(), a => a.Name.LocalName == "Command" && new[] { "Download", "Install", "CheckForUpdate", "ReDownload", "CancelDownload" }.Any(c => a.Value.Contains(c)));
    }

    [Fact]
    public void 浮层只在外部可交互控件上关闭()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var popup = new System.Windows.Controls.Border();
                var trigger = new System.Windows.Controls.Button();
                var inside = new System.Windows.Controls.Button();
                popup.Child = inside;
                Assert.False(DiskPilotX.App.Controls.UpdatePopupPolicy.ShouldClose(inside, popup, trigger));
                Assert.False(DiskPilotX.App.Controls.UpdatePopupPolicy.ShouldClose(trigger, popup, trigger));
                Assert.False(DiskPilotX.App.Controls.UpdatePopupPolicy.ShouldClose(new System.Windows.Controls.Border(), popup, trigger));
                var blank = new System.Windows.Controls.Border();
                var window = new System.Windows.Window { Content = blank };
                Assert.False(DiskPilotX.App.Controls.UpdatePopupPolicy.ShouldClose(blank, popup, trigger));
                window.Close();
                Assert.True(DiskPilotX.App.Controls.UpdatePopupPolicy.ShouldClose(new System.Windows.Controls.Button(), popup, trigger));
                Assert.True(DiskPilotX.App.Controls.UpdatePopupPolicy.ShouldClose(new System.Windows.Controls.TextBox(), popup, trigger));
                Assert.False(DiskPilotX.App.Controls.UpdatePopupPolicy.ShouldClose(new System.Windows.Controls.Button { IsEnabled = false }, popup, trigger));
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }

    [Fact]
    public async Task 禁用自动检查后启动不联网但手动检查绕过缓存()
    {
        var service = Substitute.For<IUpdateService>();
        service.GetCurrentVersion().Returns("1.0.0");
        service.CheckForUpdateAsync(Arg.Any<CancellationToken>(), true).Returns(new UpdateCheckResult { Success = true });
        var vm = new UpdateViewModel(service) { AutoCheckUpdates = false };
        await vm.CheckAtStartupAsync();
        await service.DidNotReceive().CheckForUpdateAsync(Arg.Any<CancellationToken>(), Arg.Any<bool>());
        await vm.CheckForUpdateCommand.ExecuteAsync(null);
        await service.Received(1).CheckForUpdateAsync(Arg.Any<CancellationToken>(), true);
    }

    [Fact]
    public async Task 下载中主操作仍可取消且取消不显示失败()
    {
        var service = Substitute.For<IUpdateService>();
        service.GetCurrentVersion().Returns("1.0.0");
        service.DownloadMsiAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<long>(), Arg.Any<IProgress<(long,long)>>(), Arg.Any<CancellationToken>())
            .Returns(async call => { await Task.Delay(Timeout.Infinite, call.ArgAt<CancellationToken>(4)); return ""; });
        var vm = new UpdateViewModel(service) { UpdateAvailable = true, MsiDownloadUrl = "https://example.org/app.msi" };
        vm.PrimaryActionCommand.Execute(null);
        Assert.True(vm.IsDownloading);
        Assert.True(vm.PrimaryActionCommand.CanExecute(null));
        vm.PrimaryActionCommand.Execute(null);
        await vm.DownloadCommand.ExecutionTask!;
        Assert.False(vm.IsDownloading);
        Assert.False(vm.IsDownloadComplete);
        Assert.False(vm.HasError);
    }

    [Fact]
    public async Task 手动检查强制刷新且历史分页请求保留Markdown()
    {
        const string release = "{\"tag_name\":\"v9.0.0\",\"body\":\"# 更新\",\"published_at\":\"2026-10-28T00:00:00Z\"}";
        var handler = new ResponseHandler(release);
        using var client = new HttpClient(handler);
        var dir = Path.Combine(Path.GetTempPath(), "DiskPilotX-update-tests", Guid.NewGuid().ToString("N"));
        var service = new UpdateService(client, dir);
        Assert.True((await service.CheckForUpdateAsync()).Success);
        await service.CheckForUpdateAsync();
        Assert.Equal(1, handler.RequestCount);
        await service.CheckForUpdateAsync(forceRefresh: true);
        Assert.Equal(2, handler.RequestCount);
        handler.Body = "[" + release + "]";
        var history = await service.GetReleasesAsync(2);
        Assert.Contains("per_page=20&page=2", handler.LastUri);
        Assert.Equal("# 更新", history[0].ReleaseNotes);
        Assert.Equal("9.0.0", history[0].Version);
    }

    [Fact]
    public async Task 完整下载落盘后没有临时文件()
    {
        var dir = Path.Combine(Path.GetTempPath(), "DiskPilotX-update-tests", Guid.NewGuid().ToString("N"));
        using var client = new HttpClient(new ResponseHandler("abc"));
        var service = new UpdateService(client, dir);
        var path = await service.DownloadMsiAsync("https://example.org/app.msi", "2.0.0", 3, new Progress<(long,long)>(), default);
        Assert.Equal("abc", await File.ReadAllTextAsync(path));
        Assert.Empty(Directory.GetFiles(dir, "*.partial"));
    }

    [Fact]
    public void 主操作按钮不会泄漏格式占位符()
    {
        var service = Substitute.For<IUpdateService>();
        service.GetCurrentVersion().Returns("1.0.0");
        var vm = new UpdateViewModel(service);
        Assert.DoesNotContain("{0}", vm.PrimaryActionText);
        vm.LatestVersion = "9.9.9";
        vm.UpdateAvailable = true;
        Assert.Equal("", vm.UpdateEntryHint);
    }

    [Fact]
    public void 更新日志紧凑排版且鼠标滚轮在嵌套文档上生效()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var document = (System.Windows.Documents.FlowDocument)new DiskPilotX.App.Converters.ReleaseNotesMarkdownConverter().Convert(
                    "# 标题\n\n正文\n\n- 项目一\n- 项目二", typeof(System.Windows.Documents.FlowDocument), null!, System.Globalization.CultureInfo.InvariantCulture);
                Assert.Equal(13, document.FontSize);
                Assert.Equal(new System.Windows.Thickness(0), document.PagePadding);
                Assert.Equal(18, document.Blocks.FirstBlock!.FontSize);
                Assert.Equal(19, document.LineHeight);
                var child = new System.Windows.Controls.Border { Height = 1500, Width = 200 };
                var viewer = new System.Windows.Controls.ScrollViewer { Height = 150, Width = 240, Content = child };
                viewer.PreviewMouseWheel += (_, e) => DiskPilotX.App.Controls.ReleaseNotesScroll.ForwardMouseWheel(viewer, e);
                viewer.Measure(new System.Windows.Size(240, 150));
                viewer.Arrange(new System.Windows.Rect(0, 0, 240, 150)); viewer.UpdateLayout();
                var wheel = new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, -120)
                    { RoutedEvent=System.Windows.Input.Mouse.PreviewMouseWheelEvent };
                child.RaiseEvent(wheel); viewer.UpdateLayout();
                Assert.True(wheel.Handled);
                Assert.True(viewer.VerticalOffset > 0);
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }

    [Fact]
    public async Task 检查更新期间不能启动旧版本下载()
    {
        var service = Substitute.For<IUpdateService>();
        service.GetCurrentVersion().Returns("1.0.0");
        var pending = new TaskCompletionSource<UpdateCheckResult>();
        service.CheckForUpdateAsync(Arg.Any<CancellationToken>(), true).Returns(pending.Task);
        var vm = new UpdateViewModel(service) { UpdateAvailable=true, LatestVersion="2.0.0", MsiDownloadUrl="https://example.org/old.msi" };
        var checking = vm.CheckForUpdateCommand.ExecuteAsync(null);
        Assert.True(vm.IsChecking);
        Assert.False(vm.CanUsePrimaryAction);
        await vm.DownloadCommand.ExecuteAsync(null);
        await service.DidNotReceive().DownloadMsiAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<long>(), Arg.Any<IProgress<(long,long)>>(), Arg.Any<CancellationToken>());
        pending.SetResult(new UpdateCheckResult { Success=true, UpdateAvailable=true, LatestVersion="3.0.0", MsiDownloadUrl="https://example.org/new.msi" });
        await checking;
        Assert.Equal("3.0.0", vm.LatestVersion);
        Assert.False(vm.IsDownloadComplete);
    }

    [Fact]
    public void 弹窗外空白点击关闭而内部和入口点击不关闭()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var popup = new System.Windows.Controls.Border();
                var inside = new System.Windows.Controls.TextBlock(); popup.Child = inside;
                var trigger = new System.Windows.Controls.Button();
                Assert.True(DiskPilotX.App.Controls.UpdatePopupPolicy.ShouldCloseOnPointerDown(new System.Windows.Controls.Border(), popup, trigger));
                Assert.False(DiskPilotX.App.Controls.UpdatePopupPolicy.ShouldCloseOnPointerDown(inside, popup, trigger));
                Assert.False(DiskPilotX.App.Controls.UpdatePopupPolicy.ShouldCloseOnPointerDown(trigger, popup, trigger));
            }
            catch (Exception ex) { error=ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }

    [Fact]
    public async Task 更新识别x64安装程序并按exe缓存完整下载()
    {
        var handler = new ResponseHandler("""
            {"tag_name":"v9.0.0","assets":[
              {"name":"DiskPilot-X-9.0.0-arm64-setup.exe","browser_download_url":"https://example.org/arm64.exe","size":3},
              {"name":"DiskPilot-X-9.0.0-win64.msi","browser_download_url":"https://example.org/legacy.msi","size":3},
              {"name":"DiskPilot-X-9.0.0-setup.exe","browser_download_url":"https://example.org/DiskPilot-X-9.0.0-setup.exe","size":3}]}
            """);
        using var client = new HttpClient(handler);
        var dir = Path.Combine(Path.GetTempPath(), "DiskPilotX-update-tests", Guid.NewGuid().ToString("N"));
        var service = new UpdateService(client, dir);
        var result = await service.CheckForUpdateAsync(forceRefresh:true);
        Assert.Equal("DiskPilot-X-9.0.0-setup.exe", result.MsiFileName);
        handler.Body = "abc";
        var downloaded = await service.DownloadMsiAsync(result.MsiDownloadUrl!, "9.0.0", 3, new Progress<(long,long)>(), default);
        Assert.EndsWith(".exe", downloaded);
        Assert.Equal(downloaded, service.GetCachedInstallerPath("9.0.0"));
        var exe = UpdateService.CreateInstallerStartInfo(downloaded);
        Assert.Equal(downloaded, exe.FileName);
        Assert.True(exe.UseShellExecute);
        Assert.Equal("runas", exe.Verb);
        var msi = UpdateService.CreateInstallerStartInfo(Path.ChangeExtension(downloaded,".msi"));
        Assert.DoesNotContain("/qb", msi.Arguments);
        Assert.Contains("/i", msi.Arguments);
    }

    [Fact]
    public async Task 点击标题栏入口会在下载完成后启动安装器且启动失败可见()
    {
        var path = Path.Combine(Path.GetTempPath(), "DiskPilotX-update-tests", Guid.NewGuid()+".exe");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllTextAsync(path,"安装器夹具");
        var service = Substitute.For<IUpdateService>(); service.GetCurrentVersion().Returns("0.9.0");
        service.DownloadMsiAsync(Arg.Any<string>(),Arg.Any<string>(),Arg.Any<long>(),Arg.Any<IProgress<(long,long)>>(),Arg.Any<CancellationToken>()).Returns(path);
        var vm = new UpdateViewModel(service) { UpdateAvailable=true,LatestVersion="1.0.0",MsiDownloadUrl="https://example.org/setup.exe" };
        await vm.ActivateUpdateCommand.ExecuteAsync(null);
        service.Received(1).LaunchInstaller(path);
        service.When(x=>x.LaunchInstaller(path)).Do(_=>throw new System.ComponentModel.Win32Exception(1223));
        await vm.ActivateUpdateCommand.ExecuteAsync(null);
        Assert.True(vm.HasError); Assert.True(vm.IsDownloadComplete);
    }

    [Fact]
    public async Task GitHub摘要不匹配的安装程序不能进入缓存()
    {
        var handler = new ResponseHandler("""
          {"tag_name":"v9.0.0","assets":[{"name":"DiskPilot-X-9.0.0-setup.exe","browser_download_url":"https://example.org/setup.exe","size":3,"digest":"sha256:0000000000000000000000000000000000000000000000000000000000000000"}]}
          """);
        using var client = new HttpClient(handler);
        var dir = Path.Combine(Path.GetTempPath(), "DiskPilotX-update-tests", Guid.NewGuid().ToString("N"));
        var service = new UpdateService(client, dir);
        await service.CheckForUpdateAsync(forceRefresh:true);
        handler.Body="abc";
        await Assert.ThrowsAsync<IOException>(()=>service.DownloadMsiAsync("https://example.org/setup.exe","9.0.0",3,new Progress<(long,long)>(),default));
        Assert.Null(service.GetCachedInstallerPath("9.0.0"));
    }

    [Fact]
    public async Task 缓存元数据写入失败也不能跳过已知摘要校验()
    {
        var handler = new ResponseHandler("""
          {"tag_name":"v9.0.0","assets":[{"name":"DiskPilot-X-9.0.0-setup.exe","browser_download_url":"https://example.org/setup.exe","size":3,"digest":"sha256:0000000000000000000000000000000000000000000000000000000000000000"}]}
          """);
        using var client = new HttpClient(handler);
        var dir = Path.Combine(Path.GetTempPath(), "DiskPilotX-update-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir,"update-cache.json"));
        var service = new UpdateService(client, dir);
        Assert.True((await service.CheckForUpdateAsync(forceRefresh:true)).Success);
        handler.Body="abc";
        await Assert.ThrowsAsync<IOException>(()=>service.DownloadMsiAsync("https://example.org/setup.exe","9.0.0",3,new Progress<(long,long)>(),default));
    }

    [Fact]
    public void 关于页不显示更新状态和更新引导()
    {
        var about = XDocument.Load(Path.Combine(FindRoot(), "src/DiskPilotX.App/Views/AboutPage.xaml"));
        Assert.DoesNotContain(about.Descendants().Attributes(), a =>
            a.Value.Contains("StatusText") || a.Value.Contains("UpdateAboutHint"));
        Assert.Contains(about.Descendants().Attributes(), a => a.Value == "{Binding CurrentVersion}");
    }

    [Fact]
    public void 右下角版本入口打开设置并定位更新分区()
    {
        var source = File.ReadAllText(Path.Combine(FindRoot(), "src/DiskPilotX.App/MainWindow.Shell.cs"));
        var handler = source.Split("private void VersionDisplay_Click", 2)[1].Split("private void", 2)[0];
        Assert.Contains("ShowSettingsPage();", handler);
        Assert.Contains("SettingsPage.NavigateToUpdates();", handler);
        Assert.DoesNotContain("SwitchToAboutTab", handler);
    }

    [Theory]
    [InlineData("Strings.resx", "Check Release Note")]
    [InlineData("Strings.zh-CN.resx", "查看发布日志")]
    [InlineData("Strings.zh-TW.resx", "查看發佈日誌")]
    public void 版本入口悬停提示使用发布日志文案(string resource, string expected)
    {
        var strings = XDocument.Load(Path.Combine(FindRoot(), "src/DiskPilotX.App/Localization", resource));
        Assert.Equal(expected, strings.Root!.Elements("data")
            .Single(e => (string?)e.Attribute("name") == "VersionDisplayToolTip").Element("value")!.Value);
    }

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "src/DiskPilotX.App/Controls/WindowTitleBar.xaml"))) return dir.FullName;
        throw new DirectoryNotFoundException("找不到测试源码根目录");
    }
    private sealed class ResponseHandler(string body) : HttpMessageHandler
    {
        public string Body { get; set; } = body;
        public int RequestCount { get; private set; }
        public string LastUri { get; private set; } = "";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            RequestCount++;
            LastUri = request.RequestUri!.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Body, Encoding.UTF8) });
        }
    }
}
