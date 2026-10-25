namespace SpaceMonger.Core.Services.Skills;

public sealed record CleanupSkillPackage
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string Version { get; init; } = "1.0";
    public int HostProtocolVersion { get; init; } = 1;
    public string SkillMarkdown { get; init; } = "";
    public IReadOnlyList<CleanupSkillRule> Rules { get; init; } = [];
    public IReadOnlyList<CleanupSkillReference> References { get; init; } = [];
    public string? UiXaml { get; init; }
    public string? Source { get; init; }
    public string Digest { get; init; } = "";
}

public sealed record CleanupSkillRule
{
    public string Id { get; init; } = "";
    // 路径相对于用户选定范围，支持 *、?、**；不展开环境变量或绝对路径。
    public string Path { get; init; } = "";
    public IReadOnlyList<string> Excludes { get; init; } = [];
    public IReadOnlyList<string> Preserve { get; init; } = [];
    public string Description { get; init; } = "";
    public string Risk { get; init; } = "";
    public IReadOnlyList<string> Sources { get; init; } = [];
}

public sealed record CleanupSkillReference(string Id, string Source, string Content, string Digest, DateTimeOffset RetrievedAt);
public sealed record CleanupSkillCandidate(string RuleId, string Path, long SizeBytes, string Description, string Risk, IReadOnlyList<string> Sources);
public sealed record CleanupSkillPreview(string Id, string Digest, string ScopePath, IReadOnlyList<CleanupSkillCandidate> Candidates, IReadOnlyList<string> Warnings);
public sealed record CleanupSkillSourceDiagnostic(string Code, string Message, string Severity, int? Line = null, int? Column = null);
public sealed record CleanupSkillSourceValidation(bool Success, string Digest, IReadOnlyList<CleanupSkillSourceDiagnostic> Diagnostics);
public sealed record CleanupSkillDetectionContext(string SkillId, string ScopePath, IReadOnlyList<CleanupSkillRule> Rules, IReadOnlyDictionary<string,string>? Parameters = null);

/// <summary>可信源码的固定检测契约。此接口和加载上下文均不提供安全沙箱；只能返回候选，宿主另行确认清理。</summary>
public interface ICleanupSkillDetector
{
    Task<IReadOnlyList<CleanupSkillCandidate>> DetectAsync(CleanupSkillDetectionContext context, CancellationToken cancellationToken);
}
