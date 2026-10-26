namespace DiskPilotX.Core.Services.Llm;

public static class AnthropicOptions
{
    public const string DefaultBaseUrl = "https://api.anthropic.com";
    public const string DiskPilotXBaseUrlEnvironmentVariable = "DISKPILOT_X_ANTHROPIC_BASE_URL";
    public const string AnthropicBaseUrlEnvironmentVariable = "ANTHROPIC_BASE_URL";

    public static Uri GetBaseUri(string? configuredBaseUrl = null)
    {
        configuredBaseUrl = string.IsNullOrWhiteSpace(configuredBaseUrl)
            ? Environment.GetEnvironmentVariable(DiskPilotXBaseUrlEnvironmentVariable)
            : configuredBaseUrl;

        if (string.IsNullOrWhiteSpace(configuredBaseUrl))
        {
            configuredBaseUrl = Environment.GetEnvironmentVariable(AnthropicBaseUrlEnvironmentVariable);
        }

        if (string.IsNullOrWhiteSpace(configuredBaseUrl))
        {
            configuredBaseUrl = DefaultBaseUrl;
        }

        if (!Uri.TryCreate(configuredBaseUrl, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                $"Invalid Anthropic base URL '{configuredBaseUrl}'. Set {DiskPilotXBaseUrlEnvironmentVariable} or {AnthropicBaseUrlEnvironmentVariable} to an absolute http(s) URL.");
        }

        return baseUri;
    }

    public static Uri GetMessagesUri(string? configuredBaseUrl = null)
    {
        var baseUri = GetBaseUri(configuredBaseUrl);
        var builder = new UriBuilder(baseUri);
        var path = builder.Path.TrimEnd('/');

        if (string.IsNullOrEmpty(path))
        {
            path = "/v1/messages";
        }
        else if (path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            path += "/messages";
        }
        else if (!path.EndsWith("/v1/messages", StringComparison.OrdinalIgnoreCase))
        {
            path += "/v1/messages";
        }

        builder.Path = path;
        return builder.Uri;
    }

    public static Uri GetModelsUri(string? configuredBaseUrl = null)
    {
        var baseUri = GetBaseUri(configuredBaseUrl);
        var builder = new UriBuilder(baseUri);
        var path = builder.Path.TrimEnd('/');

        if (string.IsNullOrEmpty(path))
        {
            path = "/v1/models";
        }
        else if (path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            path += "/models";
        }
        else if (!path.EndsWith("/v1/models", StringComparison.OrdinalIgnoreCase) && !path.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
        {
            path += "/v1/models";
        }

        builder.Path = path;
        // Anthropic 官方接口需要 limit 参数才返回完整列表。
        builder.Query = "limit=1000";
        return builder.Uri;
    }

}
