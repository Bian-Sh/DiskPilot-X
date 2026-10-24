using System.Text.Json;
using System.Text.RegularExpressions;

namespace TinyHarness;

/// <summary>
/// 模型请求错误类别。覆盖 OpenAI / Anthropic / Google 及常见中转站的通用错误语义，
/// 每种类别对应一条多语言文案（assistant.error.kind.*）。
/// </summary>
public enum ModelErrorKind
{
    /// <summary>无法归类的其他错误。</summary>
    Unknown,

    /// <summary>网络层连接失败（DNS、拒绝连接、TLS 握手失败等）。</summary>
    ConnectionFailed,

    /// <summary>请求超时。</summary>
    Timeout,

    /// <summary>API Key 无效、缺失或已过期（401 / invalid_api_key / authentication_error）。</summary>
    Authentication,

    /// <summary>密钥有效但没有访问该模型或资源的权限（403 / permission_error）。</summary>
    PermissionDenied,

    /// <summary>模型不存在或已下线（404 / model_not_found / not_found_error）。</summary>
    ModelNotFound,

    /// <summary>请求参数无效（400 / 422 / invalid_request_error）。</summary>
    InvalidRequest,

    /// <summary>上下文超长（context_length_exceeded / prompt_too_long / 413）。</summary>
    ContextLengthExceeded,

    /// <summary>触发速率限制（429 / rate_limit_exceeded / rate_limit_error）。</summary>
    RateLimited,

    /// <summary>账户额度不足或欠费（insufficient_quota / billing / 402）。</summary>
    InsufficientQuota,

    /// <summary>内容被 Provider 安全策略过滤（content_filter / content_policy）。</summary>
    ContentFiltered,

    /// <summary>服务过载（503 / 529 / overloaded_error）。</summary>
    ServerOverloaded,

    /// <summary>服务端内部错误（5xx / internal_error / server_error / api_error）。</summary>
    ServerError,
}

/// <summary>
/// 模型请求错误的结构化分类：先按 Provider 返回的错误码/类型匹配（OpenAI、Anthropic 等头部厂商的
/// 错误码语义已趋于通用），匹配不到再按 HTTP 状态码兜底。
/// </summary>
public static class ModelErrorClassifier
{
    /// <summary>读取错误响应体的上限，避免异常大的响应拖慢失败路径。</summary>
    public const int MaxErrorBodyBytes = 8 * 1024;

    /// <summary>脱敏后上游描述的最大长度。</summary>
    public const int MaxUpstreamMessageLength = 300;

    // 按优先级排列：先命中更具体的语义。匹配方式为"包含"（小写），兼容中转站拼接的前缀/后缀。
    private static readonly (string Needle, ModelErrorKind Kind)[] CodeRules =
    [
        ("invalid_api_key", ModelErrorKind.Authentication),
        ("incorrect_api_key", ModelErrorKind.Authentication),
        ("authentication", ModelErrorKind.Authentication),
        ("unauthorized", ModelErrorKind.Authentication),
        ("invalid_token", ModelErrorKind.Authentication),
        ("permission", ModelErrorKind.PermissionDenied),
        ("forbidden", ModelErrorKind.PermissionDenied),
        ("insufficient_quota", ModelErrorKind.InsufficientQuota),
        ("quota_exceeded", ModelErrorKind.InsufficientQuota),
        ("billing", ModelErrorKind.InsufficientQuota),
        ("balance", ModelErrorKind.InsufficientQuota),
        ("payment", ModelErrorKind.InsufficientQuota),
        ("context_length", ModelErrorKind.ContextLengthExceeded),
        ("context_window", ModelErrorKind.ContextLengthExceeded),
        ("prompt_too_long", ModelErrorKind.ContextLengthExceeded),
        ("max_tokens", ModelErrorKind.ContextLengthExceeded),
        ("request_too_large", ModelErrorKind.ContextLengthExceeded),
        ("rate_limit", ModelErrorKind.RateLimited),
        ("ratelimit", ModelErrorKind.RateLimited),
        ("too_many_requests", ModelErrorKind.RateLimited),
        ("content_filter", ModelErrorKind.ContentFiltered),
        ("content_policy", ModelErrorKind.ContentFiltered),
        ("moderation", ModelErrorKind.ContentFiltered),
        ("safety", ModelErrorKind.ContentFiltered),
        ("model_not_found", ModelErrorKind.ModelNotFound),
        ("no_such_model", ModelErrorKind.ModelNotFound),
        ("not_found", ModelErrorKind.ModelNotFound),
        ("overloaded", ModelErrorKind.ServerOverloaded),
        ("capacity", ModelErrorKind.ServerOverloaded),
        ("server_error", ModelErrorKind.ServerError),
        ("internal_error", ModelErrorKind.ServerError),
        ("api_error", ModelErrorKind.ServerError),
        ("timeout", ModelErrorKind.Timeout),
        ("invalid_request", ModelErrorKind.InvalidRequest),
        ("bad_request", ModelErrorKind.InvalidRequest),
    ];

    /// <summary>按错误码/类型字符串分类（OpenAI error.code、error.type，Anthropic error.type 等）。</summary>
    public static ModelErrorKind ClassifyCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return ModelErrorKind.Unknown;
        // 统一分隔符：部分中转站返回 "group rate limit exceeded" 这类自然语言码
        var normalized = code.Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_');
        foreach (var (needle, kind) in CodeRules)
        {
            if (normalized.Contains(needle, StringComparison.Ordinal)) return kind;
        }
        return ModelErrorKind.Unknown;
    }

    /// <summary>按 HTTP 状态码兜底分类。</summary>
    public static ModelErrorKind ClassifyStatusCode(int statusCode) => statusCode switch
    {
        400 or 405 or 422 => ModelErrorKind.InvalidRequest,
        401 => ModelErrorKind.Authentication,
        402 => ModelErrorKind.InsufficientQuota,
        403 => ModelErrorKind.PermissionDenied,
        404 => ModelErrorKind.ModelNotFound,
        408 => ModelErrorKind.Timeout,
        413 => ModelErrorKind.ContextLengthExceeded,
        429 => ModelErrorKind.RateLimited,
        503 or 529 => ModelErrorKind.ServerOverloaded,
        >= 500 and < 600 => ModelErrorKind.ServerError,
        _ => ModelErrorKind.Unknown,
    };

    /// <summary>综合分类：错误码优先，HTTP 状态码兜底。</summary>
    public static ModelErrorKind Classify(int? statusCode, string? providerCode)
    {
        var byCode = ClassifyCode(providerCode);
        if (byCode != ModelErrorKind.Unknown) return byCode;
        return statusCode is int code ? ClassifyStatusCode(code) : ModelErrorKind.Unknown;
    }

    /// <summary>
    /// 从错误响应体提取 Provider 错误码与描述。兼容：
    /// OpenAI { "error": { "code", "type", "message" } }、Anthropic { "error": { "type", "message" } }、
    /// 部分中转站 { "error": "string" } 或顶层 { "message" } / { "code" }。
    /// code 优先取 error.code，其次 error.type，再次顶层 code/type。
    /// </summary>
    public static (string? Code, string? Message) ParseErrorBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return (null, null);
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, null);

            string? code = null;
            string? message = null;

            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.Object)
                {
                    code = ReadString(error, "code") ?? ReadString(error, "type");
                    message = ReadString(error, "message");
                }
                else if (error.ValueKind == JsonValueKind.String)
                {
                    message = error.GetString();
                }
            }

            code ??= ReadString(root, "code") ?? ReadString(root, "type");
            message ??= ReadString(root, "message") ?? ReadString(root, "msg") ?? ReadString(root, "error_description");
            return (code, message);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    /// <summary>
    /// 上游描述脱敏：折叠空白、遮蔽 API Key/Bearer 片段、截断长度。
    /// 允许进入 UI 与事件 Detail，但仍禁止写入日志正文以外的敏感上下文。
    /// </summary>
    public static string? SanitizeUpstreamMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;
        var collapsed = Regex.Replace(message.Trim(), @"\s+", " ");
        collapsed = Regex.Replace(collapsed, @"(?i)bearer\s+[A-Za-z0-9._\-]+", "Bearer ***");
        collapsed = Regex.Replace(collapsed, @"\b(sk|pk|key|ak|token)-[A-Za-z0-9_\-]{3,}", "$1-***");
        collapsed = Regex.Replace(collapsed, @"\b[A-Za-z0-9_\-]{32,}\b", "***");
        return collapsed.Length <= MaxUpstreamMessageLength
            ? collapsed
            : collapsed[..MaxUpstreamMessageLength] + "…";
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
