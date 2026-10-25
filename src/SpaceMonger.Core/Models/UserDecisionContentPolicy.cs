using System.Text;
using System.Text.RegularExpressions;

namespace SpaceMonger.Core.Models;

/// <summary>移植 Loom-X 的用户决策隐私边界，仅保留请求与答案所需的认证信息检查。</summary>
internal static class UserDecisionContentPolicy
{
    private static readonly Regex SecretValuePattern = new(
        @"(?ix)(?<![a-z0-9])(?:sk-(?:proj-)?[a-z0-9_-]{12,}|gh[pousr]_[a-z0-9]{20,}|github_pat_[a-z0-9_]{20,}|xox[baprs]-[a-z0-9-]{10,}|AKIA[0-9A-Z]{16}|AIza[0-9A-Za-z_-]{20,}|eyJ[a-z0-9_-]{6,}\.[a-z0-9_-]{6,}\.[a-z0-9_-]{6,})(?![a-z0-9])",
        RegexOptions.CultureInvariant);

    private static readonly HashSet<string> SensitiveNames = new(StringComparer.Ordinal)
    {
        "key",
        "api_key",
        "token",
        "password",
        "secret",
        "authorization",
        "credential",
        "headers",
        "custom_headers",
        "http_headers",
    };

    public static bool IsSensitivePath(IReadOnlyList<string> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        return segments.Any(IsSensitiveSegment);
    }

    public static bool ContainsSensitiveContent(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        if (SecretValuePattern.IsMatch(content)
            || Regex.IsMatch(content, @"密钥|私钥|密码|口令|访问令牌|认证令牌|身份令牌|认证信息|凭据值", RegexOptions.CultureInvariant))
        {
            return true;
        }

        var words = SplitContentWords(content);
        for (var index = 0; index < words.Count; index++)
        {
            var word = words[index];
            var next = index + 1 < words.Count ? words[index + 1] : null;
            if (word is "authorization" or "authorizations"
                or "credential" or "credentials"
                or "secret" or "secrets"
                or "bearer")
            {
                return true;
            }

            if (word is "password" or "passwords" or "passwd")
            {
                if (next is not "policy" and not "policies")
                {
                    return true;
                }
            }

            if (word is "key" or "keys")
            {
                return true;
            }

            if (word is "token" or "tokens")
            {
                return true;
            }

            if ((next is "key" or "keys")
                && (word is "api" or "access" or "private" or "secret"))
            {
                return true;
            }

            if ((next is "token" or "tokens")
                && (word is "access" or "refresh" or "auth" or "id"))
            {
                return true;
            }

            if (word == "client" && next is "secret" or "secrets")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>名称用途允许术语，但仍拒绝可识别凭据、认证赋值及本地凭据引用。</summary>
    public static bool ContainsCredentialValue(string? content) => !string.IsNullOrWhiteSpace(content)
        && (SecretValuePattern.IsMatch(content)
            || content.Contains("LOOMX_CREDENTIAL_", StringComparison.OrdinalIgnoreCase)
            || content.Contains("SPACEMONGER_CREDENTIAL_", StringComparison.OrdinalIgnoreCase)
            || RequestsCredentialValue(content)
            || Regex.IsMatch(content, @"(?i)\b(?:api[_\s-]*key|token|password|secret|authorization|credentials?)\s*[:=]\s*\S+|\bbearer\s+\S+", RegexOptions.CultureInvariant));
    private static bool RequestsCredentialValue(string content)
    {
        // 名称用途可以出现认证术语，但不能借此要求用户输入凭据正文。
        var asksForInput = Regex.IsMatch(content,
            @"(?i)\b(?:enter|paste|provide|send|submit|share|type|upload|reveal)\b|输入|提供|粘贴|填写|发送|提交|上传|告诉|给我|索取",
            RegexOptions.CultureInvariant);
        if (!asksForInput || !ContainsSensitiveContent(content)) return false;
        var namesOnly = Regex.IsMatch(content, @"(?i)名称|名字|别名|标签|\b(?:name|alias|label)s?\b", RegexOptions.CultureInvariant);
        var actualValue = Regex.IsMatch(content, @"(?i)真实|实际|明文|完整|正文|\b(?:actual|raw|value|plaintext)\b", RegexOptions.CultureInvariant);
        return !namesOnly || actualValue;
    }

    private static IReadOnlyList<string> SplitContentWords(string content)
    {
        var normalized = new StringBuilder(content.Length * 2);
        for (var index = 0; index < content.Length; index++)
        {
            var character = content[index];
            if (char.IsUpper(character)
                && index > 0
                && (char.IsLower(content[index - 1]) || char.IsDigit(content[index - 1])))
            {
                normalized.Append(' ');
            }

            normalized.Append(char.IsLetterOrDigit(character)
                ? char.ToLowerInvariant(character)
                : ' ');
        }

        return normalized
            .ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool IsSensitiveSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return false;
        }

        var normalized = segment.Trim().ToLowerInvariant().Replace('-', '_');
        if (SensitiveNames.Contains(normalized))
        {
            return true;
        }

        return SensitiveNames.Any(name => normalized.EndsWith($"_{name}", StringComparison.Ordinal));
    }

}
