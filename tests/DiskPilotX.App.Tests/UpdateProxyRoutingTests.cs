using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using DiskPilotX.App.Services;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Llm;
using DiskPilotX.Core.Services.Settings;

namespace DiskPilotX.App.Tests;

public sealed class UpdateProxyRoutingTests
{
    [Fact]
    public async Task 更新下载使用独立HTTP代理且每次请求读取新配置()
    {
        using var first = new TcpListener(IPAddress.Loopback, 0); first.Start();
        using var second = new TcpListener(IPAddress.Loopback, 0); second.Start();
        var folder = Folder();
        var settings = new SettingsService(Path.Combine(folder, "settings.json"));
        settings.SaveSettings(new AppSettings { ProxyMode = "custom", ProxyHost = "ai-only.invalid", ProxyPort = 1,
            UpdateProxyMode = "custom", UpdateProxyHost = "127.0.0.1", UpdateProxyPort = ((IPEndPoint)first.LocalEndpoint).Port });
        using var clients = new ModelHttpClientFactory();
        var update = new UpdateService(settings, clients, Path.Combine(folder, "cache"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var request1 = Serve(first, timeout.Token);
        var path1 = await update.DownloadMsiAsync("http://installer.invalid/first.exe", "9.1.0", 3, new Progress<(long, long)>(), timeout.Token);
        Assert.StartsWith("GET http://installer.invalid/first.exe", await request1);
        Assert.Equal("abc", await File.ReadAllTextAsync(path1));
        var saved = settings.LoadSettings(); saved.UpdateProxyPort = ((IPEndPoint)second.LocalEndpoint).Port; settings.SaveSettings(saved);
        var request2 = Serve(second, timeout.Token);
        var path2 = await update.DownloadMsiAsync("http://installer.invalid/second.exe", "9.2.0", 3, new Progress<(long, long)>(), timeout.Token);
        Assert.StartsWith("GET http://installer.invalid/second.exe", await request2);
        Assert.Equal("abc", await File.ReadAllTextAsync(path2));
        Assert.Equal("ai-only.invalid", settings.LoadSettings().ProxyHost);
    }

    [Fact]
    public async Task 更新直连模式忽略AI自定义代理()
    {
        using var target = new TcpListener(IPAddress.Loopback, 0); target.Start();
        var folder = Folder(); var settings = new SettingsService(Path.Combine(folder, "settings.json"));
        settings.SaveSettings(new AppSettings { ProxyMode = "custom", ProxyHost = "ai-only.invalid", ProxyPort = 1, UpdateProxyMode = "direct" });
        using var clients = new ModelHttpClientFactory(); var update = new UpdateService(settings, clients, Path.Combine(folder, "cache"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); var request = Serve(target, timeout.Token);
        var port = ((IPEndPoint)target.LocalEndpoint).Port;
        var path = await update.DownloadMsiAsync($"http://127.0.0.1:{port}/direct.exe", "9.3.0", 3, new Progress<(long, long)>(), timeout.Token);
        Assert.StartsWith("GET /direct.exe", await request);
        Assert.Equal("abc", await File.ReadAllTextAsync(path));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public async Task 自定义更新代理端口无效时明确失败不擅自改用默认端口(int? port)
    {
        var folder = Folder(); var settings = new SettingsService(Path.Combine(folder, "settings.json"));
        settings.SaveSettings(new AppSettings { UpdateProxyMode = "custom", UpdateProxyHost = "127.0.0.1", UpdateProxyPort = port });
        using var clients = new ModelHttpClientFactory(() => new BytesHandler());
        var update = new UpdateService(settings, clients, Path.Combine(folder, "cache"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => update.DownloadMsiAsync("http://installer.invalid/test.exe", "9.4.0", 3, new Progress<(long, long)>(), default));
    }

    [Fact]
    public async Task 更新请求空端口使用7897并保存默认值()
    {
        var folder = Folder(); var settings = new SettingsService(Path.Combine(folder, "settings.json"));
        settings.SaveSettings(new AppSettings { UpdateProxyMode = "custom", UpdateProxyHost = "127.0.0.1" });
        using var clients = new ModelHttpClientFactory(() => new BytesHandler());
        var update = new UpdateService(settings, clients, Path.Combine(folder, "cache"));
        var path = await update.DownloadMsiAsync("http://installer.invalid/test.exe", "9.5.0", 3, new Progress<(long, long)>(), default);
        Assert.Equal("abc", await File.ReadAllTextAsync(path));
        Assert.Equal(7897, settings.LoadSettings().UpdateProxyPort);
    }

    private static async Task<string> Serve(TcpListener listener, CancellationToken ct)
    {
        using var connection = await listener.AcceptTcpClientAsync(ct);
        await using var stream = connection.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        var request = await reader.ReadLineAsync(ct) ?? "";
        while (await reader.ReadLineAsync(ct) is { Length: > 0 }) { }
        await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 3\r\nConnection: close\r\n\r\nabc"), ct);
        return request;
    }
    private static string Folder()
    {
        var path = Path.Combine(Path.GetTempPath(), "DiskPilotX-更新代理请求", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path); return path;
    }
    private sealed class BytesHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("abc") });
    }
}
