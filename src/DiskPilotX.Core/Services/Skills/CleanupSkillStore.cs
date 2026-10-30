using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DiskPilotX.Core.Services.Skills;

/// <summary>用户清理包存储。完整版本先落盘，再原子替换 current.json；失败时旧指针仍有效。</summary>
public sealed class CleanupSkillStore
{
    public static string DefaultUserRoot => AppCompatibility.ResolveDataPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "cleanup-skills");
    public string UserRoot { get; }
    private readonly string[] builtInRoots;
    private const int MaxPackageBytes = 4 * 1024 * 1024;
    private const int MaxLocalizationCultures = 32;
    private const int MaxLocalizationBytes = 128 * 1024;
    private const int MaxTotalLocalizationBytes = 512 * 1024;
    private const int MaxLocalizationEntries = 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = true, Encoder = UserDataJson.Options.Encoder
    };

    public CleanupSkillStore(string? userRoot = null, IEnumerable<string>? builtInRoots = null)
    {
        UserRoot = Path.GetFullPath(userRoot ?? DefaultUserRoot);
        this.builtInRoots = (builtInRoots ?? DefaultBuiltInRoots()).Select(Path.GetFullPath).ToArray();
    }

    internal static IEnumerable<string> DefaultBuiltInRoots()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "skills");
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (dir.EnumerateFiles("*.sln").Any()) { yield return Path.Combine(dir.FullName, "skills"); break; }
    }

    public bool IsProtected(string id) => CleanupSkillSafety.ReservedIds.Contains(id, StringComparer.OrdinalIgnoreCase) ||
        builtInRoots.Any(root => File.Exists(Path.Combine(root, id, "SKILL.md")));

    public IReadOnlyList<CleanupSkillPackage> List()
    {
        CleanupSkillSafety.EnsureNoReparsePoints(UserRoot);
        if (!Directory.Exists(UserRoot)) return [];
        var packages = new List<CleanupSkillPackage>();
        foreach (var dir in Directory.EnumerateDirectories(UserRoot).Order(StringComparer.OrdinalIgnoreCase))
        {
            var id = Path.GetFileName(dir);
            if (id.StartsWith('.') || IsProtected(id)) continue;
            try { packages.Add(Load(id)); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or JsonException or UnauthorizedAccessException or System.Xml.XmlException) { }
        }
        return packages;
    }

    public CleanupSkillPackage Load(string id)
    {
        CleanupSkillSafety.ValidateId(id);
        if (IsProtected(id)) throw new ArgumentException("不能以用户包读取或执行内置技能 ID。");
        var directory = CleanupSkillSafety.SafePath(UserRoot, id);
        var digest = JsonSerializer.Deserialize<string>(ReadText(directory, "current.json", 128), JsonOptions);
        if (digest is null || digest.Length != 64 || !digest.All(Uri.IsHexDigit)) throw new InvalidDataException("无效的包版本指针。");
        return LoadRevision(id, digest);
    }

    /// <summary>
    /// 只读载入：用户包走版本指针，内置技能则从内置根读取 SKILL.md 与 src/*.cs。
    /// 内置内容仍不可保存或删除，但必须<b>可运行</b>——随包发布的发现类技能否则毫无用处。
    /// </summary>
    public CleanupSkillPackage LoadReadOnly(string id)
    {
        CleanupSkillSafety.ValidateId(id);
        if (!IsProtected(id)) return Load(id);
        foreach (var root in builtInRoots)
        {
            var directory = Path.Combine(root, id);
            if (!File.Exists(Path.Combine(directory, "SKILL.md"))) continue;
            return LoadBuiltIn(directory, id);
        }
        throw new ArgumentException("内置技能缺少可读内容：" + id);
    }

    private static CleanupSkillPackage LoadBuiltIn(string directory, string id)
    {
        CleanupSkillSafety.EnsureNoReparsePoints(directory);
        var markdown = File.ReadAllText(Path.Combine(directory, "SKILL.md"), Utf8);
        var package = new CleanupSkillPackage
        {
            Id = id,
            Title = ReadHeading(markdown) ?? id,
            Description = ReadFrontmatter(markdown, "description") ?? "",
            Version = "1.0",
            HostProtocolVersion = 1,
            SkillMarkdown = markdown,
            Rules = [],
            References = [],
            UiXaml = ReadOptional(directory, "ui.xaml"),
            Source = ReadBuiltInSource(directory),
            Localizations = ReadBuiltInLocalizations(directory)
        };
        // 内置包不套用用户包的字段校验：纯规则技能没有源码也合法。
        if (package.Source is not null && Utf8.GetByteCount(package.Source) > 256 * 1024) throw new InvalidDataException("内置技能源码超过 256 KiB 限制。");
        if (package.UiXaml is not null) _ = DiskPilotX.Core.Models.SkillUiDocument.Parse(package.UiXaml);
        return package with { Digest = HashPackage(package) };
    }

    private static string? ReadBuiltInSource(string directory)
    {
        foreach (var name in BuiltInSourceNames)
        {
            var path = Path.Combine(directory, "src", name);
            if (File.Exists(path)) return File.ReadAllText(path, Utf8);
        }
        return null;
    }

    private static readonly string[] BuiltInSourceNames = ["Provider.cs", "Detector.cs"];

    private static string? ReadHeading(string markdown)
    {
        foreach (var line in markdown.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("# ", StringComparison.Ordinal)) return trimmed[2..].Trim();
        }
        return null;
    }

    private static string? ReadFrontmatter(string markdown, string key)
    {
        var lines = markdown.Split('\n');
        if (lines.Length < 3 || lines[0].Trim() != "---") return null;
        var prefix = key + ":";
        for (var index = 1; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line == "---") return null;
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return line[prefix.Length..].Trim().Trim('"');
        }
        return null;
    }

    private CleanupSkillPackage LoadRevision(string id, string digest)
    {
        var directory = CleanupSkillSafety.SafePath(UserRoot, id);
        var revision = CleanupSkillSafety.SafePath(directory, "versions/" + digest);
        var manifest = JsonSerializer.Deserialize<CleanupSkillPackage>(ReadText(revision, "manifest.json"), JsonOptions)
            ?? throw new InvalidDataException("缺少包清单。");
        if (manifest.References is null || manifest.References.Count > 32 || manifest.References.Any(r => r is null)) throw new InvalidDataException("包的资料清单无效。");
        var references = manifest.References.Select(reference =>
        {
            CleanupSkillSafety.ValidateId(reference.Id);
            return reference with { Content = ReadText(revision, "references/" + reference.Id + ".txt", CleanupSkillReferenceImporter.MaxBytes) };
        }).ToArray();
        var package = manifest with
        {
            SkillMarkdown = ReadText(revision, "SKILL.md"),
            Rules = JsonSerializer.Deserialize<CleanupSkillRule[]>(ReadText(revision, "rules.json"), JsonOptions) ?? [],
            References = references,
            UiXaml = ReadOptional(revision, "ui.xaml"), Source = ReadOptional(revision, "src/Detector.cs"), Digest = ""
        };
        Validate(package);
        if (package.Id != id || !string.Equals(HashPackage(package), digest, StringComparison.Ordinal))
            throw new InvalidDataException("包内容与摘要不一致，请重新保存并确认。");
        if (Directory.Exists(Path.Combine(revision,"i18n")))
        {
            var disk = ReadBuiltInLocalizations(revision);
            var expected = package.Localizations?.ToDictionary(table => System.Globalization.CultureInfo.GetCultureInfo(table.Key).Name,table => table.Value,StringComparer.OrdinalIgnoreCase);
            if(disk is null || expected is null || disk.Count != expected.Count || disk.Any(table =>
                !expected.TryGetValue(table.Key,out var values) || values.Count != table.Value.Count ||
                table.Value.Any(entry => !values.TryGetValue(entry.Key,out var value) || value != entry.Value)))
                throw new InvalidDataException("技能 JSON 文案与已确认版本不一致，请通过宿主保存新版本。");
        }
        return package with { Digest = digest };
    }

    public CleanupSkillPackage Save(CleanupSkillPackage package, bool overwrite = false)
    {
        ArgumentNullException.ThrowIfNull(package);
        Validate(package);
        if (IsProtected(package.Id)) throw new ArgumentException("内置技能不可覆盖，包括 skill-creator。");
        CleanupSkillSafety.EnsureNoReparsePoints(UserRoot);
        Directory.CreateDirectory(UserRoot);
        // 同一用户根使用文件锁协调多个 store 实例，指针替换不依赖先删除旧文件。
        using var held = new FileStream(CleanupSkillSafety.SafePath(UserRoot, ".write.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var target = CleanupSkillSafety.SafePath(UserRoot, package.Id);
        if ((File.Exists(Path.Combine(target, "current.json")) || File.Exists(Path.Combine(target, "SKILL.md"))) && !overwrite) throw new IOException("技能已存在，更新须指定 overwrite。");
        var canonical = package with { Digest = "" };
        var digest = HashPackage(canonical);
        var stage = CleanupSkillSafety.SafePath(UserRoot, ".stage-" + Guid.NewGuid().ToString("N"));
        var pointer = CleanupSkillSafety.SafePath(target, ".current-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(stage);
            WriteText(stage, "manifest.json", JsonSerializer.Serialize(canonical with
            {
                SkillMarkdown = "", Rules = [], UiXaml = null, Source = null,
                References = canonical.References.Select(r => r with { Content = "" }).ToArray()
            }, JsonOptions));
            if(canonical.Localizations is not null)
                foreach(var (culture,entries) in canonical.Localizations)
                    WriteText(stage,"i18n/" + System.Globalization.CultureInfo.GetCultureInfo(culture).Name + ".json",JsonSerializer.Serialize(entries,JsonOptions));
            WriteText(stage, "SKILL.md", canonical.SkillMarkdown);
            WriteText(stage, "rules.json", JsonSerializer.Serialize(canonical.Rules, JsonOptions));
            foreach (var reference in canonical.References) WriteText(stage, "references/" + reference.Id + ".txt", reference.Content);
            if (canonical.UiXaml is not null) WriteText(stage, "ui.xaml", canonical.UiXaml);
            if (canonical.Source is not null) WriteText(stage, "src/Detector.cs", canonical.Source);
            var versions = CleanupSkillSafety.SafePath(target, "versions");
            Directory.CreateDirectory(versions);
            // 摘要相同的旧版本也须重新校验，不能复用被外部改写的内容。
            var revision = CleanupSkillSafety.SafePath(versions, digest);
            if (Directory.Exists(revision))
            {
                _ = LoadRevision(package.Id, digest);
            }
            else Directory.Move(stage, revision);
            using (var stream = new FileStream(pointer, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = Utf8.GetBytes(JsonSerializer.Serialize(digest, JsonOptions));
                stream.Write(bytes); stream.Flush(flushToDisk: true);
            }
            File.Move(pointer, CleanupSkillSafety.SafePath(target, "current.json"), overwrite: true);
            return canonical with { Digest = digest };
        }
        finally
        {
            // 仅清理本次调用新建的临时产物，旧版本保持可恢复。
            if (Directory.Exists(stage)) Directory.Delete(stage, true);
            if (File.Exists(pointer)) File.Delete(pointer);
        }
    }

    public Task<CleanupSkillPreview> PreviewAsync(string id, string scopePath, CancellationToken cancellationToken = default) =>
        Task.Run(() => CleanupSkillPreviewer.Preview(Load(id), scopePath, cancellationToken), cancellationToken);

    public Task<CleanupSkillPreview> PreviewAsync(CleanupSkillPackage package, string scopePath, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(package);
        if (IsProtected(package.Id)) throw new ArgumentException("草稿不能使用内置技能 ID。");
        var snapshot = package with { Digest = HashPackage(package) };
        return CleanupSkillPreviewer.Preview(snapshot, scopePath, cancellationToken);
    }, cancellationToken);

    public Task<CleanupSkillReference> ImportReferenceAsync(string source, CancellationToken cancellationToken = default) =>
        CleanupSkillReferenceImporter.ImportAsync(source, cancellationToken);

    public static void Validate(CleanupSkillPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        CleanupSkillSafety.ValidateId(package.Id);
        ValidateLocalizations(package.Localizations);
        if (package.UiXaml is not null) _ = DiskPilotX.Core.Models.SkillUiDocument.Parse(package.UiXaml);
        if (string.IsNullOrWhiteSpace(package.Title) || string.IsNullOrWhiteSpace(package.Description) ||
            string.IsNullOrWhiteSpace(package.SkillMarkdown) || string.IsNullOrWhiteSpace(package.Version) || package.HostProtocolVersion != 1)
            throw new ArgumentException("包必须提供标题、说明、版本、SKILL.md 和宿主协议版本 1。");
        if (package.Rules is null || package.References is null || package.Rules.Count > 128 || package.References.Count > 32 || package.Rules.Any(r => r is null) || package.References.Any(r => r is null))
            throw new ArgumentException("规则或资料数量超过限制。");
        if (package.Source is not null && Utf8.GetByteCount(package.Source) > 256 * 1024) throw new ArgumentException("源码超过 256 KiB 限制。");
        if (package.Rules.Count == 0 && string.IsNullOrWhiteSpace(package.Source)) throw new ArgumentException("至少提供一条规则或检测源码。");
        if (package.Rules.Select(r => r.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != package.Rules.Count)
            throw new ArgumentException("规则 ID 重复。");
        foreach (var rule in package.Rules)
        {
            CleanupSkillSafety.ValidateId(rule.Id); CleanupSkillSafety.ValidatePattern(rule.Path);
            if (string.IsNullOrWhiteSpace(rule.Description) || string.IsNullOrWhiteSpace(rule.Risk) || rule.Sources is null || rule.Sources.Count == 0 || rule.Sources.Any(string.IsNullOrWhiteSpace))
                throw new ArgumentException("规则必须包含说明、风险和来源。");
            if (rule.Excludes is null || rule.Preserve is null || rule.Excludes.Count + rule.Preserve.Count > 128) throw new ArgumentException("排除项或保留项无效。");
            foreach (var pattern in rule.Excludes.Concat(rule.Preserve)) CleanupSkillSafety.ValidatePattern(pattern);
        }
        if (package.References.Select(r => r.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != package.References.Count)
            throw new ArgumentException("资料 ID 重复。");
        foreach (var reference in package.References)
        {
            CleanupSkillSafety.ValidateId(reference.Id);
            if (string.IsNullOrWhiteSpace(reference.Source) || reference.Content is null || Utf8.GetByteCount(reference.Content) > CleanupSkillReferenceImporter.MaxBytes || reference.Digest != Hash(reference.Content))
                throw new ArgumentException("资料内容摘要或来源无效。");
        }
        if (Utf8.GetByteCount(JsonSerializer.Serialize(package with { Digest = "" }, JsonOptions)) > MaxPackageBytes)
            throw new ArgumentException("技能包超过 4 MiB 限制。");
    }

    internal static Dictionary<string, Dictionary<string, string>>? ReadBuiltInLocalizations(string directory)
    {
        var root = CleanupSkillSafety.SafePath(directory, "i18n");
        if (!Directory.Exists(root)) return null;
        // 只读这一层且先限制枚举数量，不递归扫描、不跟随链接。
        var files = Directory.EnumerateFileSystemEntries(root).Take(MaxLocalizationCultures + 1).ToArray();
        if (files.Length > MaxLocalizationCultures) throw new InvalidDataException("内置技能 i18n 文案目录最多包含 32 个文化文件。");
        if (files.Length == 0) return null;
        var tables = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var totalBytes = 0;
        foreach (var file in files.Order(StringComparer.OrdinalIgnoreCase))
        {
            var relative = "i18n/" + Path.GetFileName(file);
            var path = CleanupSkillSafety.SafePath(directory, relative);
            if ((File.GetAttributes(path) & FileAttributes.Directory) != 0 ||
                !Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"文案 {relative} 必须是当前 i18n 目录下的文化 JSON 文件，不允许子目录。");
            var name = Path.GetFileNameWithoutExtension(path);
            if (!SkillTextCatalog.TryResolveCulture(name, out var culture))
                throw new InvalidDataException($"文案 {relative} 的文化名称无效。");
            try
            {
                var json = ReadText(directory, relative, MaxLocalizationBytes);
                totalBytes += Utf8.GetByteCount(json);
                if (totalBytes > MaxTotalLocalizationBytes) throw new InvalidDataException("文案总大小超过 512 KiB 限制。");
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("文案 JSON 根节点必须为键值对象。");
                var entries = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (entries.Count >= MaxLocalizationEntries) throw new InvalidDataException("单个文化的文案最多 1024 项。");
                    if (property.Value.ValueKind != JsonValueKind.String || !entries.TryAdd(property.Name, property.Value.GetString()!))
                        throw new InvalidDataException($"文案键 {property.Name} 重复或其值不是字符串。");
                }
                if (!tables.TryAdd(culture.Name, entries)) throw new InvalidDataException("文案文化重复。");
                ValidateLocalizations(tables);
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidDataException)
            {
                throw new InvalidDataException($"文案 {relative} 无效：{ex.Message}", ex);
            }
        }
        return tables;
    }

    private static void ValidateLocalizations(IReadOnlyDictionary<string, Dictionary<string, string>>? tables)
    {
        if (tables is null) return;
        if (tables.Count > MaxLocalizationCultures) throw new ArgumentException("文案最多包含 32 个文化。");
        var cultures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var totalBytes = 0;
        foreach (var (name, entries) in tables)
        {
            if (!SkillTextCatalog.TryResolveCulture(name, out var culture) || !cultures.Add(culture.Name))
                throw new ArgumentException("文案文化名称无效或重复：" + name);
            if (entries is null || entries.Count > MaxLocalizationEntries) throw new ArgumentException($"文案文化 {name} 必须为至多 1024 项的键值对象。");
            var rawBytes = 0;
            foreach (var (key, value) in entries)
            {
                if (string.IsNullOrWhiteSpace(key) || key.Length > 256 || key.Any(char.IsControl) ||
                    value is null || value.Length > 16384 || value.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')))
                    throw new ArgumentException($"文案文化 {name} 的键值无效：键最多 256 字符，值最多 16384 字符，禁止非法控制字符。");
                rawBytes += Utf8.GetByteCount(key) + Utf8.GetByteCount(value);
                if (rawBytes > MaxLocalizationBytes) throw new ArgumentException($"文案文化 {name} 超过 128 KiB 限制。");
            }
            // 同时限制实际序列化大小，包含 JSON 转义与缩进开销。
            var bytes = Utf8.GetByteCount(JsonSerializer.Serialize(entries, JsonOptions));
            if (bytes > MaxLocalizationBytes) throw new ArgumentException($"文案文化 {name} 超过 128 KiB 限制。");
            totalBytes += bytes;
            if (totalBytes > MaxTotalLocalizationBytes) throw new ArgumentException("文案总大小超过 512 KiB 限制。");
        }
    }
    internal static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Utf8.GetBytes(value)));
    // 摘要继续沿用旧编码规范，磁盘中文直写不应使既有版本和授权失效。
    private static readonly JsonSerializerOptions DigestJsonOptions = new(JsonOptions) { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Default };
    private static string HashPackage(CleanupSkillPackage package) => Hash(JsonSerializer.Serialize(package with { Digest = "" }, DigestJsonOptions));
    private static string? ReadOptional(string root, string relative) => File.Exists(CleanupSkillSafety.SafePath(root, relative)) ? ReadText(root, relative) : null;
    private static string ReadText(string root, string relative, int maxBytes = MaxPackageBytes)
    {
        var path = CleanupSkillSafety.SafePath(root, relative);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > maxBytes) throw new InvalidDataException("包文件超过大小限制。");
        using var reader = new StreamReader(stream, Utf8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
    private static void WriteText(string root, string relative, string content)
    {
        var path = CleanupSkillSafety.SafePath(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, Utf8);
    }
}
