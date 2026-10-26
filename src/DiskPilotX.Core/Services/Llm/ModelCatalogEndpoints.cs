using System.Text.RegularExpressions;

namespace DiskPilotX.Core.Services.Llm;

/// <summary>协议决定默认目录，已知域名补充预设；未知中转仅在同源部署前缀内探测。</summary>
public static class ModelCatalogEndpoints
{
    public static IReadOnlyList<Uri> GetCandidates(ModelEndpointConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.ModelsUrl))
            return [Validate(config.ModelsUrl.Trim())];

        var baseUri = config.ApiFormat == LlmApiFormat.AnthropicMessages
            ? AnthropicOptions.GetBaseUri(config.BaseUrl) : OpenAiOptions.GetBaseUri(config.BaseUrl);
        Validate(baseUri.AbsoluteUri);
        var result = new List<Uri>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(string path, bool anthropic = false)
        {
            var uri = new UriBuilder(baseUri) { Path = path, Query = anthropic ? "limit=1000" : "", Fragment = "" }.Uri;
            // 不重复请求仅查询参数不同的相同目录。
            if (seen.Add(uri.GetLeftPart(UriPartial.Path))) result.Add(uri);
        }

        var host = baseUri.IdnHost.ToLowerInvariant();
        var preset = host switch
        {
            "api.deepseek.com" => "/models",
            "dashscope.aliyuncs.com" or "dashscope-intl.aliyuncs.com" => "/compatible-mode/v1/models",
            "open.bigmodel.cn" or "api.z.ai" => "/api/paas/v4/models",
            "openrouter.ai" => "/api/v1/models",
            "api.groq.com" => "/openai/v1/models",
            "generativelanguage.googleapis.com" => "/v1beta/models",
            _ when host.StartsWith("ark.", StringComparison.Ordinal) && host.EndsWith(".volces.com", StringComparison.Ordinal) => "/api/v3/models",
            _ => null,
        };
        if (preset is not null) Add(preset);

        var prefix = Regex.Replace(baseUri.AbsolutePath.TrimEnd('/'), @"/(?:chat/completions|responses|messages|models)$", "", RegexOptions.IgnoreCase);
        var isAnthropic = config.ApiFormat == LlmApiFormat.AnthropicMessages;
        Add(prefix.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? prefix + "/models" : prefix + "/v1/models", isAnthropic);
        // 去掉已识别的协议/版本后缀，不丢弃 tenant、gateway 等部署路径。
        prefix = Regex.Replace(prefix, @"/(?:v1|v1beta)$", "", RegexOptions.IgnoreCase);
        Add(prefix + "/models");
        prefix = Regex.Replace(prefix, @"/(?:anthropic|openai)$", "", RegexOptions.IgnoreCase);
        foreach (var suffix in new[] { "/v1/models", "/models", "/api/v1/models", "/openai/v1/models", "/api/models", "/v1beta/openai/models", "/v1beta/models" })
            Add(prefix + suffix);
        return result.Take(12).ToArray();
    }

    public static string Describe(ModelEndpointConfig config)
    {
        var host = GetCandidates(config)[0].IdnHost;
        return host switch
        {
            "api.deepseek.com" => "国产服务商 / DeepSeek",
            "dashscope.aliyuncs.com" or "dashscope-intl.aliyuncs.com" => "国产服务商 / 通义千问",
            "open.bigmodel.cn" or "api.z.ai" => "国产服务商 / 智谱",
            "api.siliconflow.cn" or "api.siliconflow.com" => "国产服务商 / 硅基流动",
            "api.moonshot.cn" or "api.moonshot.ai" => "国产服务商 / Moonshot",
            "api.openai.com" => "海外服务商 / OpenAI",
            "api.anthropic.com" => "海外服务商 / Anthropic",
            "generativelanguage.googleapis.com" => "海外服务商 / Gemini",
            "openrouter.ai" => "海外聚合 / OpenRouter",
            "api.groq.com" => "海外服务商 / Groq",
            "api.x.ai" => "海外服务商 / xAI",
            "api.mistral.ai" => "海外服务商 / Mistral",
            _ when host.StartsWith("ark.", StringComparison.Ordinal) && host.EndsWith(".volces.com", StringComparison.Ordinal) => "国产服务商 / 豆包",
            _ => "通用中转（兼容 New API / Sub2API）",
        };
    }

    private static Uri Validate(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException("模型列表地址必须是完整的 HTTP(S) URL，且不能包含内嵌账户密码。");
        return uri;
    }
}
