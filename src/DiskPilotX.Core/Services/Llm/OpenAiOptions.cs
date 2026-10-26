namespace DiskPilotX.Core.Services.Llm;

/// <summary>OpenAI 兼容端点（/v1/responses、/v1/chat/completions、/v1/models）的地址规范化。</summary>
public static class OpenAiOptions
{
    public const string DefaultBaseUrl = "https://api.openai.com";

    /// <summary>与 AnthropicOptions 相同的环境变量兜底链，便于一套中转地址服务多种格式。</summary>
    public static Uri GetBaseUri(string? configuredBaseUrl = null)
    {
        configuredBaseUrl = string.IsNullOrWhiteSpace(configuredBaseUrl)
            ? Environment.GetEnvironmentVariable(AnthropicOptions.DiskPilotXBaseUrlEnvironmentVariable)
            : configuredBaseUrl;

        if (string.IsNullOrWhiteSpace(configuredBaseUrl))
        {
            configuredBaseUrl = Environment.GetEnvironmentVariable(AnthropicOptions.AnthropicBaseUrlEnvironmentVariable);
        }

        if (string.IsNullOrWhiteSpace(configuredBaseUrl))
        {
            configuredBaseUrl = DefaultBaseUrl;
        }

        if (!Uri.TryCreate(configuredBaseUrl, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException($"Invalid OpenAI-compatible base URL '{configuredBaseUrl}'.");
        }

        return baseUri;
    }

    /// <summary>把任意 base URL 规范化到以 <paramref name="suffix"/>（如 /responses）结尾的绝对地址。</summary>
    public static Uri GetEndpointUri(string suffix, string? configuredBaseUrl = null)
    {
        var baseUri = GetBaseUri(configuredBaseUrl);
        var builder = new UriBuilder(baseUri);
        var path = builder.Path.TrimEnd('/');
        var versionedSuffix = "/v1" + suffix;

        if (string.IsNullOrEmpty(path))
        {
            path = versionedSuffix;
        }
        else if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            // 已经指向目标端点（如 https://relay/openai/v1/chat/completions）。
        }
        else if (path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            path += suffix;
        }
        else if (path.EndsWith(versionedSuffix, StringComparison.OrdinalIgnoreCase))
        {
            // 已经是 /v1 + suffix，无需处理。
        }
        else
        {
            path += versionedSuffix;
        }

        builder.Path = path;
        return builder.Uri;
    }
}
