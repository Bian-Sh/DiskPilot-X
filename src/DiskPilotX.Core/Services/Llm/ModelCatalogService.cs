using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TinyHarness;

namespace DiskPilotX.Core.Services.Llm;

public sealed record DiscoveredModel(string Id, string? DisplayName, string? OwnedBy);

/// <summary>候选目录均已尝试但没有有效模型，宿主应提示用户提供完整目录地址。</summary>
public sealed class ModelCatalogExhaustedException(int attempts) : Exception($"已尝试 {attempts} 个模型列表地址，仍未获取有效模型。请提供完整的模型获取 URL 后重试。")
{
    public int Attempts { get; } = attempts;
}

/// <summary>模型目录有界探测；请求沿用代理与客户端身份，失败明细交给宿主日志双写。</summary>
public sealed class ModelCatalogService(ModelHttpClientFactory httpClients, ILogger<ModelCatalogService>? logger = null,
    TimeSpan? requestTimeout = null, TimeSpan? overallTimeout = null)
{
    private readonly ILogger<ModelCatalogService> log = logger ?? NullLogger<ModelCatalogService>.Instance;
    private static readonly JsonSerializerOptions DiagnosticJson = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task<IReadOnlyList<DiscoveredModel>> FetchAsync(ModelEndpointConfig config, CancellationToken cancellationToken)
    {
        var candidates = ModelCatalogEndpoints.GetCandidates(config);
        // 目录请求禁止跟随重定向，防止 x-api-key 被带到另一个站点；不改变对话请求行为。
        var httpClient = httpClients.Create(config, allowAutoRedirect: false);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(overallTimeout ?? TimeSpan.FromSeconds(45));
        log.LogInformation("开始模型发现：{Provider}，协议 {ApiFormat}，代理 {ProxyMode}，候选 {Count}",
            ModelCatalogEndpoints.Describe(config), config.ApiFormat, config.ProxyMode, candidates.Count);

        for (var index = 0; index < candidates.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (budget.IsCancellationRequested)
                throw new ModelClientException("模型列表探测已达到总体超时，请检查代理和网络后重试。", ModelErrorKind.Timeout);
            var uri = candidates[index];
            var safeUrl = Sanitize(config, uri.AbsoluteUri);
            var watch = Stopwatch.StartNew();
            int? status = null;
            string? preview = null;
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(budget.Token);
            attempt.CancelAfter(requestTimeout ?? TimeSpan.FromSeconds(8));
            log.LogInformation("模型目录尝试 {Attempt}/{Total}：GET {Url}", index + 1, candidates.Count, safeUrl);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                var google = uri.Host == "generativelanguage.googleapis.com" || uri.AbsolutePath.EndsWith("/v1beta/models", StringComparison.OrdinalIgnoreCase);
                if (!google) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
                if (google)
                    request.Headers.Add("x-goog-api-key", config.ApiKey);
                else if (config.ApiFormat == LlmApiFormat.AnthropicMessages && uri.Host != "api.deepseek.com")
                {
                    request.Headers.Add("x-api-key", config.ApiKey);
                    request.Headers.Add("anthropic-version", "2023-06-01");
                }
                foreach (var (name, value) in CliIdentityService.BuildHeaders(config.CliIdentity))
                    if (!request.Headers.Contains(name)) request.Headers.TryAddWithoutValidation(name, value);

                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, attempt.Token).ConfigureAwait(false);
                status = (int)response.StatusCode;
                var body = await ReadBodyAsync(response.Content, response.IsSuccessStatusCode ? 1024 * 1024 : 8192,
                    response.IsSuccessStatusCode, attempt.Token).ConfigureAwait(false);
                preview = ResponseSummary(config, body);
                if (!response.IsSuccessStatusCode)
                {
                    log.LogWarning("模型目录响应 HTTP {StatusCode}：{Url}；摘要 {Response}", status, safeUrl,
                        preview);
                    throw new ModelClientException($"模型列表请求失败（HTTP {status}）。", ModelErrorClassifier.Classify(status.Value, null), status);
                }
                using var document = JsonDocument.Parse(body);
                var models = Extract(document.RootElement);
                if (models.Count == 0)
                    throw new ModelClientException("该地址未返回有效模型 ID。", ModelErrorKind.Unknown);
                log.LogInformation("模型目录成功：{Url}，模型 {Count} 个，耗时 {ElapsedMs} ms", safeUrl, models.Count, watch.ElapsedMilliseconds);
                return models;
            }
            catch (Exception ex) when (ex is ModelClientException or HttpRequestException or IOException or InvalidDataException or JsonException or InvalidOperationException or OperationCanceledException or FormatException)
            {
                log.LogWarning("模型目录尝试失败：{Url}，HTTP {StatusCode}，耗时 {ElapsedMs} ms，异常 {ExceptionType}：{Detail}；响应摘要 {Response}",
                    safeUrl, status, watch.ElapsedMilliseconds, ex.GetType().Name, Sanitize(config, ex.ToString()), preview);
                if (cancellationToken.IsCancellationRequested) throw;
                if (budget.IsCancellationRequested)
                    throw new ModelClientException("模型列表探测已达到总体超时，请检查代理和网络后重试。", ModelErrorKind.Timeout);
                // 鉴权、权限、额度、限流或服务故障不是路径缺失，不通过盲目换路径掩盖错误。
                if (ex is ModelClientException { StatusCode: { } code }
                    && code is not (404 or 405 or 410 or 501) && (code < 300 || code >= 400))
                    throw;
                if (ex is FormatException)
                    throw new ModelClientException("模型请求配置格式无效，请检查凭据与地址。", ModelErrorKind.InvalidRequest);
            }
        }
        var exhausted = new ModelCatalogExhaustedException(candidates.Count);
        log.LogError("模型发现失败：{Reason}", exhausted.Message);
        throw exhausted;
    }

    public static Uri GetModelsUri(ModelEndpointConfig config) => ModelCatalogEndpoints.GetCandidates(config)[0];

    /// <summary>对 UI、异常和日志统一脱敏，保留诊断类型与堆栈；URL 不输出查询参数或内嵌凭据。</summary>
    public static string Sanitize(ModelEndpointConfig config, string text)
    {
        // 先收集完整凭据及编码形式，长值先遮蔽，避免短密钥拆散较长的代理口令。
        var secrets = new[] { config.ApiKey, config.ProxyPassword }
            .Where(value => !string.IsNullOrEmpty(value))
            .SelectMany(value => new[] { value!, Uri.EscapeDataString(value!), JsonEncodedText.Encode(value!).ToString(), JsonEncodedText.Encode(value!, DiagnosticJson.Encoder).ToString() })
            .Distinct(StringComparer.Ordinal).OrderByDescending(value => value.Length).ToArray();
        return SanitizeText(text, secrets, 0);
    }

    private static string SanitizeText(string text, string[] secrets, int depth)
    {
        if (depth >= 8) return "[嵌套内容已隐藏]";
        var trimmed = text.TrimStart();
        if (trimmed.Length > 0 && trimmed[0] is '{' or '[' or '"')
        {
            try
            {
                using var document = JsonDocument.Parse(text);
                return JsonSerializer.Serialize(SanitizeJson(document.RootElement, secrets, depth), DiagnosticJson);
            }
            catch (JsonException)
            {
                return "[结构化内容无效或不完整，原文已隐藏]";
            }
        }
        return SanitizePlain(text, secrets);
    }

    private static object? SanitizeJson(JsonElement value, string[] secrets, int depth)
    {
        if (depth >= 8) return "[嵌套内容已隐藏]";
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var result = new Dictionary<string, object?>();
                foreach (var property in value.EnumerateObject())
                {
                    // 字段名已由 JSON 解析器解码，不再用正则猜测转义引号的边界。
                    var name = property.Name.Replace("-", "").Replace("_", "").ToLowerInvariant();
                    var sensitive = name is "apikey" or "xapikey" or "authorization" or "proxyauthorization"
                        or "password" or "proxypassword" or "accesstoken" or "token" or "secret";
                    result[SanitizePlain(property.Name, secrets)] = sensitive ? "***" : SanitizeJson(property.Value, secrets, depth + 1);
                }
                return result;
            case JsonValueKind.Array:
                return value.EnumerateArray().Select(item => SanitizeJson(item, secrets, depth + 1)).ToArray();
            case JsonValueKind.String:
                // 中转错误常把另一段 JSON 放进 message/error 字符串，逐层解码后再遮蔽。
                return SanitizeText(value.GetString() ?? "", secrets, depth + 1);
            case JsonValueKind.Null:
                return null;
            default:
                return SanitizePlain(value.GetRawText(), secrets);
        }
    }

    private static string SanitizePlain(string text, string[] secrets)
    {
        foreach (var secret in secrets) text = text.Replace(secret, "***", StringComparison.Ordinal);
        // 无法按完整 JSON 解释的嵌入片段宁可隐藏，不能留下残缺的编码凭据。
        text = Regex.Replace(text, @"[^\r\n]*(?:\{|\\[""\\/]|\\u[0-9a-fA-F]{4})[^\r\n]*", "[结构化片段已隐藏]");
        text = Regex.Replace(text, @"(?i)((?:api[_-]?key|x-api-key|authorization|proxy-authorization|password|proxy[_-]?password|access[_-]?token|token|secret)[""']?\s*[:=]\s*)[^\r\n]*", "$1***");
        text = Regex.Replace(text, @"(?i)\b(?:bearer|basic)\s+[^\r\n]*", "认证 ***");
        return Regex.Replace(text, @"https?://[^\s<>""']+", match =>
        {
            if (!Uri.TryCreate(match.Value, UriKind.Absolute, out var uri)) return "[地址已隐藏]";
            return new UriBuilder(uri) { UserName = "", Password = "", Query = "", Fragment = "" }.Uri.AbsoluteUri;
        }, RegexOptions.IgnoreCase);
    }

    private static string? ResponseSummary(ModelEndpointConfig config, string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return ModelErrorClassifier.SanitizeUpstreamMessage(Sanitize(config, document.RootElement.GetRawText()));
        }
        catch (JsonException)
        {
            // HTML、被截断的 JSON 等无法可靠解释，不能把可能残缺的转义凭据原样写入日志。
            return "响应不是完整 JSON，原始正文已隐藏。";
        }
    }

    private static async Task<string> ReadBodyAsync(HttpContent content, int limit, bool rejectOversize, CancellationToken token)
    {
        await using var stream = await content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var body = new MemoryStream();
        var buffer = new byte[8192];
        while (body.Length <= limit)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, limit + 1L - body.Length)), token).ConfigureAwait(false);
            if (read == 0) break;
            body.Write(buffer, 0, read);
        }
        if (body.Length > limit && rejectOversize) throw new InvalidDataException("模型目录响应超过 1 MiB 的安全上限。");
        return Encoding.UTF8.GetString(body.GetBuffer(), 0, (int)Math.Min(body.Length, limit));
    }

    /// <summary>兼容根数组、data 数组（OpenAI）与 models 数组（部分中转）三种响应形状。</summary>
    public static IReadOnlyList<DiscoveredModel> Extract(JsonElement root)
    {
        var source = root.ValueKind switch
        {
            JsonValueKind.Array => root,
            JsonValueKind.Object when root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array => data,
            JsonValueKind.Object when root.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array => models,
            _ => throw new ModelClientException("模型列表响应格式无效。", ModelErrorKind.Unknown),
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<DiscoveredModel>();
        foreach (var item in source.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var value = item.GetString()?.Trim();
                if (!string.IsNullOrEmpty(value) && seen.Add(value)) result.Add(new DiscoveredModel(value, value, null));
                continue;
            }
            if (item.ValueKind != JsonValueKind.Object) continue;
            var id = String(item, "id") ?? String(item, "name") ?? String(item, "model") ?? String(item, "slug");
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id.Trim())) continue;
            var display = String(item, "display_name") ?? id;
            result.Add(new DiscoveredModel(id.Trim(), display.Trim(), String(item, "owned_by") ?? String(item, "ownedBy")));
        }
        result.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        return result;
    }

    private static string? String(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
