using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DiskPilotX.Core.Services.Copilot;
using DiskPilotX.Core.Services.Skills;

namespace DiskPilotX.Core.Services.Analysis;

public sealed record AnalysisRuleDescriptor(
    string Id, string Title, string Description, string Version, string Digest, string Kind,
    IReadOnlyList<string> Hints, bool RequiresSource, bool Readable, string? Error);

public sealed record AnalysisRuleRead(
    string Id, string Version, string Digest, string Stage, string Content, bool Truncated,
    int NextOffset, IReadOnlyList<string> Preserve, IReadOnlyList<string> Excludes);

/// <summary>分析侧的只读规则视图；每次调用重新发现，不保存规则、执行源码或触发清理。</summary>
public sealed class AnalysisRuleCatalog(ISkillPromptProvider provider, CleanupSkillStore? packages = null)
{
    private readonly ISkillPromptProvider provider = provider ?? throw new ArgumentNullException(nameof(provider));
    private readonly CleanupSkillStore packages = packages ?? new CleanupSkillStore();
    private const int MaxReadChars = 12000;
    private static readonly Regex IdPattern = new("^[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions RuleJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>既有 List 会跳过损坏包，此警告表示目录覆盖率未知，而不是虚构坏包信息。</summary>
    public string? Warning { get; private set; }

    public IReadOnlyList<AnalysisRuleDescriptor> GetCatalog()
    {
        var result = new List<AnalysisRuleDescriptor>();
        foreach (var entry in Discover())
        {
            try { result.Add(Resolve(entry).Descriptor); }
            catch (Exception ex) when (IsReadFailure(ex))
            {
                // 不可读条目没有可信版本或摘要；不能把失败包装成已读取。
                result.Add(new(entry.Id, entry.Item?.DisplayName ?? entry.Package?.Title ?? entry.Id,
                    entry.Item?.Description ?? entry.Package?.Description ?? "", "", "",
                    entry.Package is null ? "markdown" : "package", [], false, false,
                    $"规则读取失败：{ex.Message}"));
            }
        }
        return result.AsReadOnly();
    }

    /// <summary>正文按 UTF-16 字符偏移分页，每页最多 12000 字符；保护与排除列表不随正文截断。</summary>
    public AnalysisRuleRead Read(string id, string stage = "analysis", int offset = 0, int maxChars = MaxReadChars)
    {
        if (string.IsNullOrWhiteSpace(id) || !IdPattern.IsMatch(id))
            throw new ArgumentException("规则 ID 必须为小写字母、数字和连字符，长度不超过 64。", nameof(id));
        if (stage is not ("discovery" or "analysis" or "handling" or "constraints"))
            throw new ArgumentException("阶段必须是 discovery、analysis、handling 或 constraints。", nameof(stage));
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset), "正文偏移不能为负数。");
        if (maxChars is < 1 or > MaxReadChars)
            throw new ArgumentOutOfRangeException(nameof(maxChars), $"单页正文上限必须在 1 到 {MaxReadChars} 之间。");

        var entry = Discover().FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"规则 {id} 不存在或当前无法读取；目录不保证完整。");
        LoadedRule loaded;
        try { loaded = Resolve(entry); }
        catch (Exception ex) when (IsReadFailure(ex))
        {
            throw new InvalidDataException($"无法读取规则 {id}：{ex.Message}", ex);
        }
        var content = SelectStage(loaded.Content, stage, out var actualStage);
        if (offset > content.Length)
            throw new ArgumentOutOfRangeException(nameof(offset), "正文偏移超过当前版本的正文长度，请核对版本后重读。");
        var length = Math.Min(maxChars, content.Length - offset);
        var nextOffset = offset + length;
        // 不完整的旧格式保守回退全文；明确阶段也必须携带全局说明和清理约束。
        return new(loaded.Descriptor.Id, loaded.Descriptor.Version, loaded.Descriptor.Digest, actualStage,
            content.Substring(offset, length), nextOffset < content.Length, nextOffset,
            loaded.Preserve, loaded.Excludes);
    }

    private static string SelectStage(string content, string stage, out string actualStage)
    {
        if (!content.Contains("<!-- diskpilot-analysis-stages:v1 -->", StringComparison.Ordinal))
        { actualStage = "legacy-full"; return content; }
        var global = new StringBuilder();
        var sections = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);
        string? current = null;
        string? fence = null;
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~"))
            {
                var marker = trimmed[..3];
                fence = fence == marker ? null : fence ?? marker;
            }
            else if (fence is null && trimmed.StartsWith("## "))
            {
                current = trimmed[3..].Trim().ToLowerInvariant() switch
                {
                    "discovery" or "发现／扫描" or "发现/扫描" => "discovery",
                    "analysis" or "分析" => "analysis",
                    "handling" or "处理建议" => "handling",
                    "constraints" or "清理约束" => "constraints",
                    _ => null
                };
                if (current is not null) sections.TryAdd(current, new());
            }
            (current is null ? global : sections[current]).AppendLine(line);
        }
        // 只有完整、明确的四阶段格式才拆分；不猜测旧标题或省略未知全局约束。
        if (new[] { "discovery", "analysis", "handling", "constraints" }.Any(x => !sections.ContainsKey(x)))
        { actualStage = "legacy-full"; return content; }
        actualStage = stage;
        return global.ToString() + sections[stage] + (stage == "constraints" ? "" : sections["constraints"].ToString());
    }

    private IReadOnlyList<Entry> Discover()
    {
        Warning = "既有包目录 List 会跳过损坏或不可读包；无法保证目录完整，未列出不代表没有规则。";
        var entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var item in provider.GetSkillCatalog())
                entries.TryAdd(item.Id, new(item.Id, item, null));
        }
        catch (Exception ex) when (IsReadFailure(ex))
        {
            Warning += $" Provider 目录读取失败：{ex.Message}";
        }
        try
        {
            foreach (var package in packages.List())
            {
                if (entries.TryGetValue(package.Id, out var entry))
                    entries[package.Id] = entry with { Package = package };
                else entries.Add(package.Id, new(package.Id, null, package));
            }
        }
        catch (Exception ex) when (IsReadFailure(ex))
        {
            Warning += $" 包目录读取失败：{ex.Message}";
        }
        return entries.Values.OrderBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private LoadedRule Resolve(Entry entry)
    {
        var package = entry.Package;
        var content = entry.Item is not null ? provider.GetRawContent(entry.Id) : package?.SkillMarkdown;
        if (content is null) throw new InvalidDataException("规则仍在目录中，但正文不可读取。");

        // FileSkillPromptProvider 先选 Markdown，再以此固定格式投影包。仅匹配其真实包投影时合并包元数据，
        // 否则必须保留 provider 已选中的 Markdown，不能借同 ID 的包冒用版本、适用路径或约束。
        if (package is not null && entry.Item is not null &&
            content != $"# {package.Title}\n\n## Purpose\n{package.Description}\n\n{package.SkillMarkdown}")
            package = null;

        if (package is null)
        {
            // 旧 Markdown 没有可信的显式版本，直接用实际读取内容的 SHA256 作为内容版本。
            var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
            return new(new(entry.Id, entry.Item?.DisplayName ?? entry.Id, entry.Item?.Description ?? "",
                digest, digest, "markdown", [], false, true, null), content, [], []);
        }

        var preserve = package.Rules.SelectMany(rule => rule.Preserve).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var excludes = package.Rules.SelectMany(rule => rule.Excludes).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var hints = package.Rules.Select(rule => rule.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        // 正文还须带上规则的风险、说明和限制；仅输出 Markdown 会丢失结构化清理约束。绝不包含检测源码。
        if (package.Rules.Count > 0)
            content += "\n\n## 结构化规则与清理约束\n" + JsonSerializer.Serialize(package.Rules, RuleJson);
        return new(new(entry.Id, entry.Item?.DisplayName ?? package.Title, entry.Item?.Description ?? package.Description,
            package.Version, package.Digest, "package", hints, !string.IsNullOrWhiteSpace(package.Source), true, null),
            content, preserve, excludes);
    }

    private static bool IsReadFailure(Exception ex) => ex is IOException or InvalidDataException or
        UnauthorizedAccessException or ArgumentException or JsonException or System.Xml.XmlException or System.Security.SecurityException;

    private sealed record Entry(string Id, AiSkillCatalogItem? Item, CleanupSkillPackage? Package);
    private sealed record LoadedRule(AnalysisRuleDescriptor Descriptor, string Content,
        IReadOnlyList<string> Preserve, IReadOnlyList<string> Excludes);
}