using System.Text;
using System.Globalization;
using DiskPilotX.Core.Services.Skills;
using System.Text.RegularExpressions;

namespace DiskPilotX.Core.Services.Copilot;

public sealed class FileSkillPromptProvider : ISkillPromptProvider
{
    private static readonly Regex SkillIdRegex = new("^[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.Compiled);
    private readonly IReadOnlyList<string>? _skillDirectories;
    private readonly bool _legacyWritableRoot;
    private readonly Func<string> _cultureProvider = () => CultureInfo.CurrentUICulture.Name;
    private readonly string _userRoot = CleanupSkillStore.DefaultUserRoot;
    private readonly IReadOnlyList<string> _builtInRoots = CleanupSkillStore.DefaultBuiltInRoots().ToArray();

    public FileSkillPromptProvider()
    {
    }

    public FileSkillPromptProvider(Func<string> cultureProvider) => _cultureProvider = cultureProvider;

    public FileSkillPromptProvider(IEnumerable<string> skillDirectories)
    {
        _skillDirectories = skillDirectories.ToArray();
        _legacyWritableRoot = true;
        // 兼容旧的隔离目录构造器：第一个目录是用户目录，额外目录是只读来源。
        _userRoot = Path.GetFullPath(_skillDirectories.FirstOrDefault() ?? CleanupSkillStore.DefaultUserRoot);
        _builtInRoots = _skillDirectories.Skip(1).ToArray();
    }

    public FileSkillPromptProvider(IEnumerable<string> builtInRoots, string userSkillRoot, Func<string>? cultureProvider = null)
    {
        _cultureProvider = cultureProvider ?? _cultureProvider;
        _builtInRoots = builtInRoots.Select(Path.GetFullPath).ToArray();
        _skillDirectories = _builtInRoots;
        _userRoot = Path.GetFullPath(userSkillRoot);
    }

    public IReadOnlyList<AiSkillCatalogItem> GetSkillCatalog() => LoadSkills().Catalog;

    public string GetPrompt(string skillId, string fallback)
    {
        if (LoadSkills().Prompts.TryGetValue(skillId, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return fallback;
    }

    public string? GetRawContent(string skillId)
    {
        return LoadSkills().Prompts.TryGetValue(skillId, out var value) ? value : null;
    }

    /// <summary>按需披露随技能发布的离线契约；不允许任意本地文件读取。</summary>
    public string? GetReference(string skillId, string relativePath)
    {
        if (!IsValidSkillId(skillId) || relativePath.Length > 200 ||
            !Regex.IsMatch(relativePath, @"^references/[a-zA-Z0-9_-]+\.md$")) return null;
        foreach (var root in _builtInRoots.Concat(GetSkillDirectories(_skillDirectories)).Append(_userRoot).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var directory = Path.Combine(root, skillId);
            if (!File.Exists(Path.Combine(directory, "SKILL.md"))) continue;
            try
            {
                var file = CleanupSkillSafety.SafePath(directory, relativePath);
                return File.Exists(file) && new FileInfo(file).Length <= 64 * 1024 ? File.ReadAllText(file, Encoding.UTF8) : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        }
        return null;
    }
    public AiSkillStoreResult CreateOrUpdateSkill(string skillId, string title, string description, string bodyMarkdown, bool overwrite)
    {
        skillId = skillId.Trim();
        if (!IsValidSkillId(skillId))
        {
            return new AiSkillStoreResult(false, "Skill id must use lowercase letters, digits, and hyphens only.", skillId, ErrorCode: "invalid_skill_id");
        }

        if (IsProtected(skillId)) return new(false, "内置技能不可修改。", skillId, ErrorCode: "protected_skill");
        if (File.Exists(Path.Combine(GetSafeSkillDirectory(skillId), "current.json"))) return new(false, "结构化清理包请通过 manage_cleanup_skill 更新。", skillId, ErrorCode: "structured_package");

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(description) || string.IsNullOrWhiteSpace(bodyMarkdown))
        {
            return new AiSkillStoreResult(false, "Skill title, description, and body are required.", skillId, ErrorCode: "missing_fields");
        }

        var skillDirectory = GetSafeSkillDirectory(skillId);
        var skillFile = Path.Combine(skillDirectory, "SKILL.md");
        var existed = File.Exists(skillFile);
        if (existed && !overwrite)
        {
            return new AiSkillStoreResult(false, "Skill already exists. Set overwrite=true to update it.", skillId, ErrorCode: "already_exists");
        }

        Directory.CreateDirectory(skillDirectory);
        var content = BuildSkillMarkdown(skillId, title, description, bodyMarkdown);
        var temporary = Path.Combine(skillDirectory, ".skill-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(temporary, content, Encoding.UTF8);
            File.Move(temporary, skillFile, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return new AiSkillStoreResult(true, existed ? "Skill updated." : "Skill created.", skillId, content);
    }

    public AiSkillStoreResult DeleteSkill(string skillId)
    {
        skillId = skillId.Trim();
        if (!IsValidSkillId(skillId))
        {
            return new AiSkillStoreResult(false, "Skill id must use lowercase letters, digits, and hyphens only.", skillId, ErrorCode: "invalid_skill_id");
        }

        if (IsProtected(skillId)) return new(false, "内置技能不可删除。", skillId, ErrorCode: "protected_skill");
        var skillDirectory = GetSafeSkillDirectory(skillId);
        if (File.Exists(Path.Combine(skillDirectory, "current.json"))) return new(false, "此工具不能删除结构化清理包。", skillId, ErrorCode: "structured_package");
        var skillFile = Path.Combine(skillDirectory, "SKILL.md");
        if (!File.Exists(skillFile))
        {
            return new AiSkillStoreResult(false, "Skill does not exist.", skillId, ErrorCode: "not_found");
        }

        Directory.Delete(skillDirectory, recursive: true);
        return new AiSkillStoreResult(true, "Skill deleted.", skillId);
    }

    private bool IsProtected(string id) => new CleanupSkillStore(_userRoot, _builtInRoots).IsProtected(id);

    private LoadedSkills LoadSkills()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var kinds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var texts = new Dictionary<string, SkillTextCatalog>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in _builtInRoots.Concat(GetSkillDirectories(_skillDirectories)).Append(_userRoot).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(directory)) continue;
            try
            {
                CleanupSkillSafety.EnsureNoReparsePoints(directory);
                foreach (var child in Directory.EnumerateDirectories(directory).Order(StringComparer.OrdinalIgnoreCase))
                {
                    var id = Path.GetFileName(child);
                    if (id.StartsWith('.') || result.ContainsKey(id)) continue;
                    var file = Path.Combine(child, "SKILL.md");
                    if (!File.Exists(file) || File.Exists(Path.Combine(child, "current.json"))) continue;
                    // 默认用户目录不能遮盖保留的内置 ID。旧隔离构造器仍可读取测试用内置目录。
                    if (directory.Equals(_userRoot, StringComparison.OrdinalIgnoreCase) && !_legacyWritableRoot && IsProtected(id)) continue;
                    try
                    {
                        CleanupSkillSafety.EnsureNoReparsePoints(file);
                        if (new FileInfo(file).Length <= 1024 * 1024)
                        {
                            result[id] = File.ReadAllText(file, Encoding.UTF8);
                            kinds[id] = DetectDirectoryKind(child);
                            if (kinds[id] != "rules" && Directory.Exists(Path.Combine(child,"i18n")))
                            {
                                try { texts[id] = new SkillTextCatalog(CleanupSkillStore.ReadBuiltInLocalizations(child)); }
                                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException) { }
                            }
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        foreach (var package in new CleanupSkillStore(_userRoot, _builtInRoots).List())
        {
            if (!result.ContainsKey(package.Id))
            {
                result[package.Id] = $"# {package.Title}\n\n## Purpose\n{package.Description}\n\n{package.SkillMarkdown}";
                kinds[package.Id] = DetectSourceKind(package.Source);
                if(kinds[package.Id] != "rules" && package.Localizations is not null) texts[package.Id] = new SkillTextCatalog(package.Localizations);
            }
        }

        var catalog = result
            .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(item =>
            {
                var title = ExtractDisplayName(item.Key,item.Value);
                var description = ExtractDescription(item.Value);
                if (texts.TryGetValue(item.Key,out var catalog))
                {
                    var culture = _cultureProvider();
                    var name = catalog.Get("skill.name",culture);var detail = catalog.Get("skill.description",culture);
                    if(name != "skill.name") title = name;
                    if(detail != "skill.description") description = detail;
                }
                return new AiSkillCatalogItem(item.Key,title,description,kinds.TryGetValue(item.Key,out var kind)?kind:"rules");
            })
            .ToList();

        return new LoadedSkills(result, catalog);
    }

    /// <summary>按目录里的源码文件名判断契约；两个契约互斥，Provider 优先。</summary>
    private static string DetectDirectoryKind(string skillDirectory)
    {
        try
        {
            if (File.Exists(Path.Combine(skillDirectory, "src", "Provider.cs"))) return "provider";
            if (File.Exists(Path.Combine(skillDirectory, "src", "Detector.cs"))) return "detector";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return "rules";
    }

    private static string DetectSourceKind(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return "rules";
        if (source.Contains("IAppDataProvider", StringComparison.Ordinal)) return "provider";
        if (source.Contains("ICleanupSkillDetector", StringComparison.Ordinal)) return "detector";
        return "rules";
    }

    private static IEnumerable<string> GetSkillDirectories(IReadOnlyList<string>? configuredDirectories)
    {
        if (configuredDirectories is { Count: > 0 })
        {
            return configuredDirectories;
        }

        var baseDirectory = AppContext.BaseDirectory;
        var solutionRoot = LocateSolutionRoot(baseDirectory);
        return
        [
            Path.Combine(baseDirectory, "skills"),
            Path.Combine(solutionRoot ?? baseDirectory, "skills"),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "skills"))
        ];
    }

    private static string ExtractDisplayName(string skillId, string content)
    {
        foreach (var line in ReadLines(content))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("# ", StringComparison.Ordinal))
            {
                return trimmed[2..].Trim();
            }
        }

        return skillId;
    }

    private static string ExtractDescription(string content)
    {
        var lines = ReadLines(content).ToArray();
        var frontmatterDescription = ExtractFrontmatterValue(lines, "description");
        if (!string.IsNullOrWhiteSpace(frontmatterDescription))
        {
            return frontmatterDescription;
        }

        for (var index = 0; index < lines.Length; index++)
        {
            if (!string.Equals(lines[index].Trim(), "## Purpose", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var description = ReadParagraph(lines, index + 1);
            if (!string.IsNullOrWhiteSpace(description))
            {
                return description;
            }
        }

        return ReadParagraph(lines, 0);
    }

    private static string ReadParagraph(IReadOnlyList<string> lines, int startIndex)
    {
        var paragraph = new List<string>();
        for (var index = startIndex; index < lines.Count; index++)
        {
            var trimmed = lines[index].Trim();
            if (trimmed.StartsWith("#", StringComparison.Ordinal) && paragraph.Count == 0)
            {
                continue;
            }

            if (trimmed.StartsWith("## ", StringComparison.Ordinal) && paragraph.Count > 0)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                if (paragraph.Count > 0)
                {
                    break;
                }

                continue;
            }

            paragraph.Add(trimmed);
        }

        return string.Join(" ", paragraph);
    }

    private static IEnumerable<string> ReadLines(string content)
    {
        using var reader = new StringReader(content);
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }

    private static string? LocateSolutionRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            if (directory.EnumerateFiles("*.sln", SearchOption.TopDirectoryOnly).Any())
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private string GetWritableSkillRoot() => _userRoot;

    private string GetSafeSkillDirectory(string skillId)
    {
        var root = GetWritableSkillRoot();
        var skillDirectory = Path.GetFullPath(Path.Combine(root, skillId));
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!skillDirectory.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Skill path escaped the configured skills directory.");
        }

        CleanupSkillSafety.EnsureNoReparsePoints(skillDirectory);
        return skillDirectory;
    }

    private static bool IsValidSkillId(string skillId)
    {
        try { CleanupSkillSafety.ValidateId(skillId); return SkillIdRegex.IsMatch(skillId); }
        catch (ArgumentException) { return false; }
    }

    private static string BuildSkillMarkdown(string skillId, string title, string description, string bodyMarkdown)
    {
        var cleanTitle = NormalizeSingleLine(title);
        var cleanDescription = NormalizeSingleLine(description);
        var body = bodyMarkdown.Trim();
        return $"""
            ---
            name: {skillId}
            description: {cleanDescription}
            ---

            # {cleanTitle}

            ## Purpose
            {cleanDescription}

            {body}
            """;
    }

    private static string NormalizeSingleLine(string value) => value.Replace("\r", " ").Replace("\n", " ").Trim();

    private static string? ExtractFrontmatterValue(IReadOnlyList<string> lines, string key)
    {
        if (lines.Count < 3 || lines[0].Trim() != "---")
        {
            return null;
        }

        var prefix = key + ":";
        for (var index = 1; index < lines.Count; index++)
        {
            var line = lines[index].Trim();
            if (line == "---")
            {
                return null;
            }

            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return line[prefix.Length..].Trim().Trim('"');
            }
        }

        return null;
    }

    private sealed record LoadedSkills(IReadOnlyDictionary<string, string> Prompts, IReadOnlyList<AiSkillCatalogItem> Catalog);
}
