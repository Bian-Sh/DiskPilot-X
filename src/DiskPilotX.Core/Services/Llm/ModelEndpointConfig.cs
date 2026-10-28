using DiskPilotX.Core.Models;

namespace DiskPilotX.Core.Services.Llm;

/// <summary>
/// 一次模型调用的完整端点配置：密钥、地址、模型、接口格式、客户端指纹、思考等级与代理。
/// 代理口令仅在内存中短暂存在，禁止写入日志或异常信息。
/// </summary>
public sealed record ModelEndpointConfig
{
    public required string ApiKey { get; init; }
    public string? BaseUrl { get; init; }
    public string? ModelsUrl { get; init; }
    public required string Model { get; init; }
    public LlmApiFormat ApiFormat { get; init; } = LlmApiFormat.AnthropicMessages;
    public CliIdentityKind CliIdentity { get; init; } = CliIdentityKind.None;
    public ThinkingLevel ThinkingLevel { get; init; } = ThinkingLevel.Default;
    public ThinkingControlMode ThinkingControlMode { get; init; } = ThinkingControlMode.Budget;
    public ProxyKind ProxyMode { get; init; } = ProxyKind.System;
    public string? ProxyHost { get; init; }
    public int? ProxyPort { get; init; }
    public string? ProxyUsername { get; init; }
    public string? ProxyPassword { get; init; }

    /// <summary>从持久化设置构建端点配置；未配置思考等级时按旧版 EnableThinking 布尔值迁移。</summary>
    public static ModelEndpointConfig FromSettings(AppSettings settings, string apiKey, string model, string? proxyPassword = null) => new()
    {
        ApiKey = apiKey,
        BaseUrl = string.IsNullOrWhiteSpace(settings.AnthropicBaseUrl) ? null : settings.AnthropicBaseUrl.Trim(),
        ModelsUrl = string.IsNullOrWhiteSpace(settings.ModelsUrl) ? null : settings.ModelsUrl.Trim(),
        Model = model,
        ApiFormat = LlmContracts.ParseApiFormat(settings.ApiFormat),
        CliIdentity = LlmContracts.ParseCliIdentity(settings.CliIdentity),
        ThinkingLevel = ThinkingCapabilities.ResolveSelectedLevel(settings, model),
        ThinkingControlMode = ThinkingCapabilities.GetControlMode(settings, model),
        ProxyMode = LlmContracts.ParseProxyKind(settings.ProxyMode),
        ProxyHost = settings.ProxyHost,
        ProxyPort = settings.ProxyPort,
        ProxyUsername = settings.ProxyUsername,
        ProxyPassword = string.IsNullOrEmpty(proxyPassword) ? null : proxyPassword,
    };

    public static ThinkingLevel ResolveThinkingLevel(AppSettings settings) => ThinkingCapabilities.ResolveSelectedLevel(settings);
}

/// <summary>模型名回退规则的唯一实现：显式配置优先，其次按域名推断，最后回落默认 Claude 模型。</summary>
public static class ModelEndpointResolver
{
    public const string DefaultChatModel = "claude-sonnet-4-20250514";

    public static string ResolveModel(string? configured, string? baseUrl)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim();
        return Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) && uri.Host.EndsWith("deepseek.com", StringComparison.OrdinalIgnoreCase)
            ? "deepseek-v4-flash"
            : DefaultChatModel;
    }
}
