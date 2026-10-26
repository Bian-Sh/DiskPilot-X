using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DiskPilotX.Core.Services.Skills;

/// <summary>用户清理包存储。完整版本先落盘，再原子替换 current.json；失败时旧指针仍有效。</summary>
public sealed class CleanupSkillStore
{
    public static string DefaultUserRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiskPilot-X", "cleanup-skills");
    public string UserRoot { get; }
    private readonly string[] builtInRoots;
    private const int MaxPackageBytes = 4 * 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = true
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

    internal static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Utf8.GetBytes(value)));
    private static string HashPackage(CleanupSkillPackage package) => Hash(JsonSerializer.Serialize(package with { Digest = "" }, JsonOptions));
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
