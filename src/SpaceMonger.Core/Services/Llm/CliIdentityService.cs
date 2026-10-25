using System.Collections.Concurrent;
using System.Text.Json;

namespace SpaceMonger.Core.Services.Llm;

/// <summary>
/// 出站请求的 CLI 客户端指纹模拟。部分中转站只放行已知 CLI 客户端的 User-Agent 与特征头，
/// 这里按家族生成与 Loom-X 一致的完整头集合；版本号内置安全默认值，可后台刷新到最新。
/// </summary>
public static class CliIdentityService
{
    public sealed record CliIdentityProfile(CliIdentityKind Kind, string DisplayName, string UaTemplate, string[] StaticHeaders);

    public static readonly IReadOnlyDictionary<CliIdentityKind, CliIdentityProfile> Profiles =
        new Dictionary<CliIdentityKind, CliIdentityProfile>
        {
            [CliIdentityKind.ClaudeCode] = new(CliIdentityKind.ClaudeCode, "Claude Code", "claude-cli/{version} (external, cli)",
            [
                "anthropic-version=2023-06-01",
                "x-stainless-lang=js",
                "x-stainless-os=Linux",
                "x-stainless-arch=x64",
                "x-stainless-runtime=node",
                "x-stainless-runtime-version=22.14.0",
                "x-stainless-package-version={version}",
                "x-stainless-timeout=600000",
                "x-stainless-retries=2",
            ]),
            [CliIdentityKind.Codex] = new(CliIdentityKind.Codex, "Codex CLI", "codex_cli_rs/{version}",
            [
                "originator=codex_cli_rs",
            ]),
            [CliIdentityKind.Grok] = new(CliIdentityKind.Grok, "Grok CLI", "grok-cli/{version} (external, cli)",
            [
                "x-grok-client-identifier=grok-cli",
                "x-grok-client-mode=cli",
                "X-XAI-Token-Auth=true",
                "x-authenticateresponse=true",
            ]),
        };

    private static readonly ConcurrentDictionary<CliIdentityKind, string> LatestVersions = new()
    {
        [CliIdentityKind.ClaudeCode] = "2.1.88",
        [CliIdentityKind.Codex] = "0.151.0",
        [CliIdentityKind.Grok] = "1.0.6",
    };

    /// <summary>版本缓存发生变化时通知界面；后台刷新与手动刷新共用此入口。</summary>
    public static event Action<CliIdentityKind>? VersionChanged;

    private static readonly object VersionGate = new();
    private static volatile bool versionsRefreshed;

    public static string GetVersion(CliIdentityKind kind) => LatestVersions.GetValueOrDefault(kind) ?? "1.0.0";

    /// <summary>生成目标家族的完整指纹头（含 User-Agent）。返回空字典表示不模拟。</summary>
    public static IReadOnlyDictionary<string, string> BuildHeaders(CliIdentityKind kind)
    {
        if (kind == CliIdentityKind.None || !Profiles.TryGetValue(kind, out var profile))
            return new Dictionary<string, string>();

        var version = GetVersion(kind);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["User-Agent"] = profile.UaTemplate.Replace("{version}", version),
        };
        foreach (var header in profile.StaticHeaders)
        {
            var separator = header.IndexOf('=');
            if (separator > 0)
            {
                var name = header[..separator];
                var value = header[(separator + 1)..].Replace("{version}", version);
                headers[name] = value;
            }
        }
        if (kind == CliIdentityKind.Codex)
            headers["version"] = version;
        if (kind == CliIdentityKind.Grok)
            headers["x-grok-client-version"] = version;
        return headers;
    }

    /// <summary>后台只尝试一次；设置页逐项刷新使用不受此限制的入口。</summary>
    public static async Task RefreshVersionsAsync()
    {
        lock (VersionGate)
        {
            if (versionsRefreshed) return;
            versionsRefreshed = true;
        }
        await Task.WhenAll(RefreshVersionAsync(CliIdentityKind.ClaudeCode), RefreshVersionAsync(CliIdentityKind.Codex)).ConfigureAwait(false);
    }

    public static async Task<bool> RefreshVersionAsync(CliIdentityKind kind, CancellationToken cancellationToken = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SpaceMongerCopilot/1.0 cli-identity-fetch/1.0");
        return await RefreshVersionAsync(kind, http, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>获取一个身份的最新版本；失败保留现有指纹，返回 false，允许用户重试。</summary>
    public static async Task<bool> RefreshVersionAsync(CliIdentityKind kind, HttpClient http, CancellationToken cancellationToken = default)
    {
        var url = kind switch
        {
            CliIdentityKind.ClaudeCode => "https://registry.npmjs.org/@anthropic-ai/claude-code/latest",
            CliIdentityKind.Codex => "https://api.github.com/repos/openai/codex/releases/latest",
            _ => null,
        };
        if (url is null) return false;
        try
        {
            using var response = await http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return false;
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var field = kind == CliIdentityKind.ClaudeCode ? "version" : "tag_name";
            if (!document.RootElement.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.String) return false;
            var raw = value.GetString() ?? "";
            var version = kind == CliIdentityKind.Codex
                ? System.Text.RegularExpressions.Regex.Match(raw, @"\d+\.\d+\.\d+").Value
                : raw;
            return RememberVersion(kind, version);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException)
        {
            return false;
        }
    }

    public static void RestoreVersions(IReadOnlyDictionary<string, string>? versions)
    {
        if (versions is null) return;
        foreach (var (identity, version) in versions)
            RememberVersion(LlmContracts.ParseCliIdentity(identity), version);
    }

    private static bool RememberVersion(CliIdentityKind kind, string version)
    {
        if (!Profiles.ContainsKey(kind) || !Version.TryParse(version, out var parsed)) return false;
        var previous = GetVersion(kind);
        var actual = LatestVersions.AddOrUpdate(kind, version, (_, current) => parsed > Version.Parse(current) ? version : current);
        if (!string.Equals(previous, actual, StringComparison.Ordinal)) VersionChanged?.Invoke(kind);
        return true;
    }
}
