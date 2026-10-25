using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace SpaceMonger.Core.Services.Skills;

internal static class CleanupSkillReferenceImporter
{
    internal const int MaxBytes = 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static async Task<CleanupSkillReference> ImportAsync(string source, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        byte[] bytes;
        if (Path.IsPathFullyQualified(source) && !source.Contains("://", StringComparison.Ordinal))
        {
            if (source.StartsWith(@"\\") || source.IndexOf(':', 2) >= 0 || !new[] { ".txt", ".md", ".json" }.Contains(Path.GetExtension(source), StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException("本地资料仅支持 txt、md、json，不接受网络路径。");
            source = Path.GetFullPath(source); CleanupSkillSafety.EnsureNoReparsePoints(source);
            await using var file = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            bytes = await ReadLimitedAsync(file, timeout.Token).ConfigureAwait(false);
        }
        else
        {
            if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
                !string.IsNullOrEmpty(uri.UserInfo) || uri.HostNameType == UriHostNameType.Unknown)
                throw new ArgumentException("仅支持无凭据的公开 HTTP(S) 网址。");
            if (IPAddress.TryParse(uri.DnsSafeHost, out var literal) && !IsPublic(literal)) throw new ArgumentException("禁止访问本机、内网或保留地址。");
            if (uri.DnsSafeHost.Equals("localhost", StringComparison.OrdinalIgnoreCase) || uri.DnsSafeHost.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) || !IPAddress.TryParse(uri.DnsSafeHost, out _) && !uri.DnsSafeHost.Contains('.'))
                throw new ArgumentException("禁止访问本机或内网主机名。");
            using var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false, UseProxy = false, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                ConnectTimeout = TimeSpan.FromSeconds(5), ConnectCallback = ConnectPublicAsync
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400) throw new InvalidDataException("资料导入不自动跟随跳转，请直接提供最终公开网址。");
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxBytes) throw new InvalidDataException("资料超过 1 MiB 限制。");
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is not null && !mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) && mediaType is not ("application/json" or "application/xml" or "application/xhtml+xml"))
                throw new InvalidDataException("网址必须返回可读取文本、网页或 JSON。");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            bytes = await ReadLimitedAsync(stream, timeout.Token).ConfigureAwait(false);
            source = uri.AbsoluteUri;
        }
        var content = Utf8.GetString(bytes).TrimStart('\uFEFF');
        if (content.Contains('\0')) throw new InvalidDataException("资料不是可读取文本。");
        var digest = CleanupSkillStore.Hash(content);
        return new("ref-" + CleanupSkillStore.Hash(source + "\n" + digest)[..24], source, content, digest, DateTimeOffset.UtcNow);
    }

    private static async Task<byte[]> ReadLimitedAsync(Stream stream, CancellationToken token)
    {
        using var buffer = new MemoryStream();
        var block = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(block, token).ConfigureAwait(false)) != 0)
        {
            if (buffer.Length + count > MaxBytes) throw new InvalidDataException("资料超过 1 MiB 限制。");
            buffer.Write(block, 0, count);
        }
        return buffer.ToArray();
    }

    private static async ValueTask<Stream> ConnectPublicAsync(SocketsHttpConnectionContext context, CancellationToken token)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, token).ConfigureAwait(false);
        // 解析与连接使用同一批已验证地址，禁止 DNS 重绑定以及代理转发绕过。
        if (addresses.Length == 0 || addresses.Any(address => !IsPublic(address))) throw new HttpRequestException("目标解析到非公开地址，已拒绝访问。");
        Exception? last = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), token).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            { socket.Dispose(); last = ex; token.ThrowIfCancellationRequested(); }
        }
        throw new HttpRequestException("无法连接公开资料目标。", last);
    }

    private static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return false;
        var b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
            return !(b[0] is 0 or 10 or 127 || b[0] >= 224 || b[0] == 169 && b[1] == 254 || b[0] == 172 && b[1] is >= 16 and <= 31 ||
                b[0] == 192 && (b[1] == 168 || b[1] == 0 || b[1] == 2 || b[1] == 88 && b[2] == 99) ||
                b[0] == 100 && b[1] is >= 64 and <= 127 || b[0] == 198 && b[1] is 18 or 19 ||
                b[0] == 198 && b[1] == 51 && b[2] == 100 || b[0] == 203 && b[1] == 0 && b[2] == 113);
        // 只接受全球单播；排除文档地址、隧道及特殊分配前缀。
        return address.AddressFamily == AddressFamily.InterNetworkV6 && (b[0] & 0xe0) == 0x20 &&
            !(b[0] == 0x20 && b[1] == 0x01 && (b[2] < 2 || b[2] == 0x0d && b[3] == 0xb8)) && !(b[0] == 0x20 && b[1] == 0x02);
    }
}
