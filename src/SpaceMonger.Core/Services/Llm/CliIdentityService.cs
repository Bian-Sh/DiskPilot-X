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

    private static readonly Dictionary<CliIdentityKind, string> LatestVersions = new()
    {
        [CliIdentityKind.ClaudeCode] = "2.1.88",
        [CliIdentityKind.Codex] = "0.151.0",
        [CliIdentityKind.Grok] = "1.0.6",
    };

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

    /// <summary>
    /// 后台刷新各家族最新版本号（npm registry / GitHub Releases）。所有失败都被吞掉并保留当前值；
    /// 每个进程只尝试一次，超时 3 秒，避免阻塞请求路径。
    /// </summary>
    public static async Task RefreshVersionsAsync()
    {
        if (versionsRefreshed) return;
        lock (VersionGate)
        {
            if (versionsRefreshed) return;
            versionsRefreshed = true;
        }

        await Task.WhenAll(RefreshClaudeAsync(), RefreshCodexAsync()).ConfigureAwait(false);
    }

    private static async Task RefreshClaudeAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("SpaceMongerCopilot/1.0 cli-identity-fetch/1.0");
            using var response = await http.GetAsync("https://registry.npmjs.org/@anthropic-ai/claude-code/latest").ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return;
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync().ConfigureAwait(false)).ConfigureAwait(false);
            if (document.RootElement.TryGetProperty("version", out var value) && value.ValueKind == JsonValueKind.String
                && Version.TryParse(value.GetString(), out var parsed) && parsed > Version.Parse(LatestVersions[CliIdentityKind.ClaudeCode]))
            {
                LatestVersions[CliIdentityKind.ClaudeCode] = value.GetString()!;
            }
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException)
        {
        }
    }

    private static async Task RefreshCodexAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("SpaceMongerCopilot/1.0 cli-identity-fetch/1.0");
            using var response = await http.GetAsync("https://api.github.com/repos/openai/codex/releases/latest").ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return;
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync().ConfigureAwait(false)).ConfigureAwait(false);
            if (!document.RootElement.TryGetProperty("tag_name", out var value) || value.ValueKind != JsonValueKind.String) return;
            var match = System.Text.RegularExpressions.Regex.Match(value.GetString()!, @"\d+\.\d+\.\d+");
            if (match.Success && Version.TryParse(match.Value, out var parsed) && parsed > Version.Parse(LatestVersions[CliIdentityKind.Codex]))
            {
                LatestVersions[CliIdentityKind.Codex] = match.Value;
            }
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException)
        {
        }
    }
}
