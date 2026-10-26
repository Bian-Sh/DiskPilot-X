using System.Net.Http.Headers;
using System.Text.Json;
using TinyHarness;

namespace DiskPilotX.Core.Services.Llm;

public sealed record DiscoveredModel(string Id, string? DisplayName, string? OwnedBy);

/// <summary>从上游拉取可用模型列表（Anthropic /v1/models 或 OpenAI 兼容 /v1/models）。</summary>
public sealed class ModelCatalogService(ModelHttpClientFactory httpClients)
{
    public async Task<IReadOnlyList<DiscoveredModel>> FetchAsync(ModelEndpointConfig config, CancellationToken cancellationToken)
    {
        var isAnthropic = config.ApiFormat == LlmApiFormat.AnthropicMessages;
        var uri = GetModelsUri(config);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        if (isAnthropic)
        {
            request.Headers.Add("x-api-key", config.ApiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
        }
        foreach (var (name, value) in CliIdentityService.BuildHeaders(config.CliIdentity))
            request.Headers.TryAddWithoutValidation(name, value);

        // 工厂缓存并复用 HttpClient，这里不能 using 释放。
        var httpClient = httpClients.Create(config);
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new ModelClientException("模型列表获取失败。", ModelErrorClassifier.Classify((int)response.StatusCode, null), (int)response.StatusCode);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return Extract(document.RootElement);
    }

    /// <summary>自定义地址只影响目录请求，必须是完整 HTTP(S) URL。</summary>
    public static Uri GetModelsUri(ModelEndpointConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.ModelsUrl))
            return config.ApiFormat == LlmApiFormat.AnthropicMessages
                ? AnthropicOptions.GetModelsUri(config.BaseUrl)
                : OpenAiOptions.GetEndpointUri("/models", config.BaseUrl);
        if (!Uri.TryCreate(config.ModelsUrl.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("模型列表地址需要完整的 HTTP(S) URL。");
        return uri;
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
            if (item.ValueKind != JsonValueKind.Object) continue;
            var id = String(item, "id") ?? String(item, "name") ?? String(item, "model");
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id.Trim())) continue;
            var display = String(item, "display_name") ?? id;
            result.Add(new DiscoveredModel(id.Trim(), display.Trim(), String(item, "owned_by") ?? String(item, "ownedBy")));
        }
        result.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        return result;
    }

    private static string? String(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
