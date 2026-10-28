using System.Security.Cryptography;
using System.Text;
using DiskPilotX.Core.Models;

namespace DiskPilotX.Core.Services.Llm;

/// <summary>协议候选来自官方 SDK 参数定义；实际支持集合由用户配置，不根据模型名推断。</summary>
public static class ThinkingCapabilities
{
    // 官方依据：openai/openai-python 的 shared/reasoning_effort.py；anthropics/anthropic-sdk-python 的 output_config_param.py。
    public static IReadOnlyList<ThinkingLevel> GetCandidates(LlmApiFormat format, ThinkingControlMode mode) => format switch
    {
        LlmApiFormat.OpenAiResponses or LlmApiFormat.OpenAiChatCompletions =>
            [ThinkingLevel.Default, ThinkingLevel.Off, ThinkingLevel.Minimal, ThinkingLevel.Low, ThinkingLevel.Medium, ThinkingLevel.High, ThinkingLevel.XHigh, ThinkingLevel.Max],
        _ when mode == ThinkingControlMode.Effort =>
            [ThinkingLevel.Default, ThinkingLevel.Off, ThinkingLevel.Low, ThinkingLevel.Medium, ThinkingLevel.High, ThinkingLevel.XHigh, ThinkingLevel.Max],
        _ => [ThinkingLevel.Default, ThinkingLevel.Off, ThinkingLevel.Low, ThinkingLevel.Medium, ThinkingLevel.High],
    };

    public static string GetProfileKey(AppSettings settings, string? model = null)
    {
        var format = LlmContracts.ParseApiFormat(settings.ApiFormat);
        var endpoint = settings.AnthropicBaseUrl?.Trim().TrimEnd('/') ?? string.Empty;
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)) endpoint = uri.AbsoluteUri.TrimEnd('/');
        var scope = string.Join("\n", LlmContracts.FormatApiFormat(format), endpoint, (model ?? settings.ChatModelName)?.Trim() ?? string.Empty);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scope)));
    }

    private static ModelThinkingSettings? Find(AppSettings settings, string? model = null) =>
        settings.ModelThinkingConfigurations.GetValueOrDefault(GetProfileKey(settings, model));

    private static ModelThinkingSettings GetOrCreate(AppSettings settings)
    {
        var key = GetProfileKey(settings);
        if (!settings.ModelThinkingConfigurations.TryGetValue(key, out var profile))
        {
            profile = new ModelThinkingSettings
            {
                ControlMode = GetControlMode(settings) == ThinkingControlMode.Effort ? "effort" : "budget",
                SelectedLevel = LlmContracts.FormatThinkingLevel(ResolveSelectedLevel(settings)),
            };
            settings.ModelThinkingConfigurations[key] = profile;
        }
        return profile;
    }

    public static ThinkingControlMode GetControlMode(AppSettings settings, string? model = null) =>
        LlmContracts.ParseApiFormat(settings.ApiFormat) != LlmApiFormat.AnthropicMessages || Find(settings, model)?.ControlMode == "effort"
            ? ThinkingControlMode.Effort : ThinkingControlMode.Budget;

    public static IReadOnlyList<ThinkingLevel> GetSupportedLevels(AppSettings settings, string? model = null)
    {
        var candidates = GetCandidates(LlmContracts.ParseApiFormat(settings.ApiFormat), GetControlMode(settings, model));
        var declared = Find(settings, model)?.SupportedLevels;
        if (declared is null) return candidates;
        var levels = candidates.Where(level => declared.Contains(LlmContracts.FormatThinkingLevel(level), StringComparer.Ordinal)).ToArray();
        return levels.Length == 0 ? [ThinkingLevel.Default] : levels;
    }

    public static ThinkingLevel ResolveSelectedLevel(AppSettings settings, string? model = null)
    {
        var profile = Find(settings, model);
        var value = profile?.SelectedLevel;
        var level = value is null
            ? string.IsNullOrWhiteSpace(settings.ThinkingLevel)
                ? (settings.EnableThinking ? ThinkingLevel.Low : ThinkingLevel.Off)
                : LlmContracts.ParseThinkingLevel(settings.ThinkingLevel)
            : LlmContracts.ParseThinkingLevel(value);
        // 旧版 OpenAI 的 off 实际是省略 effort，迁移时保持原来的请求语义。
        if (value is null && level == ThinkingLevel.Off && LlmContracts.ParseApiFormat(settings.ApiFormat) != LlmApiFormat.AnthropicMessages)
            level = ThinkingLevel.Default;
        if (value is null && level == ThinkingLevel.Minimal && LlmContracts.ParseApiFormat(settings.ApiFormat) == LlmApiFormat.AnthropicMessages)
            level = ThinkingLevel.Low;
        var supported = GetSupportedLevels(settings, model);
        return supported.Contains(level) ? level : supported.Contains(ThinkingLevel.Default) ? ThinkingLevel.Default : supported[0];
    }

    public static void SetSupportedLevels(AppSettings settings, IEnumerable<ThinkingLevel> levels)
    {
        var profile = GetOrCreate(settings);
        var candidates = GetCandidates(LlmContracts.ParseApiFormat(settings.ApiFormat), GetControlMode(settings));
        var selected = candidates.Where(levels.Contains).ToArray();
        profile.SupportedLevels = (selected.Length == 0 ? [ThinkingLevel.Default] : selected).Select(LlmContracts.FormatThinkingLevel).ToList();
        profile.SelectedLevel = LlmContracts.FormatThinkingLevel(ResolveSelectedLevel(settings));
    }

    public static void SetSelectedLevel(AppSettings settings, ThinkingLevel level)
    {
        var profile = GetOrCreate(settings);
        var supported = GetSupportedLevels(settings);
        profile.SelectedLevel = LlmContracts.FormatThinkingLevel(supported.Contains(level) ? level : ResolveSelectedLevel(settings));
    }

    public static void SetControlMode(AppSettings settings, ThinkingControlMode mode)
    {
        var profile = GetOrCreate(settings);
        profile.ControlMode = mode == ThinkingControlMode.Effort ? "effort" : "budget";
        profile.SupportedLevels = null;
        profile.SelectedLevel = LlmContracts.FormatThinkingLevel(ResolveSelectedLevel(settings));
    }
}
