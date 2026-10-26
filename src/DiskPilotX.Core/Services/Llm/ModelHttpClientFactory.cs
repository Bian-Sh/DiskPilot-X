using System.Collections.Concurrent;
using System.Net;

namespace DiskPilotX.Core.Services.Llm;

/// <summary>
/// 按代理配置构建并复用模型请求 HttpClient。签名相同的配置共享同一个实例，
/// 避免每次请求新建连接；口令只参与签名计算，不进入任何日志。
/// handlerFactory 仅供测试注入伪处理器，生产路径恒为 null。
/// </summary>
public sealed class ModelHttpClientFactory(Func<HttpMessageHandler>? handlerFactory = null) : IDisposable
{
    private readonly ConcurrentDictionary<string, HttpClient> clients = new(StringComparer.Ordinal);

    public HttpClient Create(ProxyKind mode, string? host, int? port, string? username, string? password, bool allowAutoRedirect = true)
    {
        var signature = string.Join("|", allowAutoRedirect, (int)mode, host ?? "", port?.ToString() ?? "", username ?? "", password is null ? 0 : password.Length, password is null ? "" : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(password))));
        return clients.GetOrAdd(signature, _ => Build(mode, host, port, username, password, allowAutoRedirect));
    }

    public HttpClient Create(ModelEndpointConfig config, bool allowAutoRedirect = true) =>
        Create(config.ProxyMode, config.ProxyHost, config.ProxyPort, config.ProxyUsername, config.ProxyPassword, allowAutoRedirect);

    private HttpClient Build(ProxyKind mode, string? host, int? port, string? username, string? password, bool allowAutoRedirect)
    {
        var proxy = mode == ProxyKind.Custom ? BuildWebProxy(host, port, username, password) : null;
        if (handlerFactory is not null)
            return new HttpClient(handlerFactory()) { Timeout = TimeSpan.FromSeconds(300) };
        var handler = new HttpClientHandler { AllowAutoRedirect = allowAutoRedirect };
        switch (mode)
        {
            case ProxyKind.Direct:
                handler.UseProxy = false;
                break;
            case ProxyKind.Custom:
                handler.Proxy = proxy;
                handler.UseProxy = true;
                break;
            // System：保持默认（跟随系统代理），与历史版本行为一致。
        }
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(300) };
        client.DefaultRequestHeaders.ExpectContinue = false;
        return client;
    }

    private static IWebProxy BuildWebProxy(string? host, int? port, string? username, string? password)
    {
        // 自定义代理无效时明确失败，不能悄悄降级为系统代理或直连。
        if (string.IsNullOrWhiteSpace(host) || port is <= 0 or >= 65536)
            throw new InvalidOperationException("自定义代理需要有效的主机和端口（1–65535）。");
        var authority = host.Trim().TrimEnd('/');
        if (!authority.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !authority.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            authority = "http://" + authority;
        if (!Uri.TryCreate(authority, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(baseUri.UserInfo) || baseUri.AbsolutePath != "/"
            || !string.IsNullOrEmpty(baseUri.Query) || !string.IsNullOrEmpty(baseUri.Fragment))
            throw new InvalidOperationException("自定义代理地址必须是 HTTP(S) 主机地址。");
        var effectivePort = port is > 0 and < 65536 ? port.Value : baseUri.Port;
        var builder = new UriBuilder(baseUri) { Port = effectivePort };
        var proxy = new WebProxy(builder.Uri) { BypassProxyOnLocal = false };
        if (!string.IsNullOrEmpty(username))
            proxy.Credentials = new NetworkCredential(username, password ?? string.Empty);
        return proxy;
    }

    public void Dispose()
    {
        foreach (var client in clients.Values) client.Dispose();
        clients.Clear();
    }
}
