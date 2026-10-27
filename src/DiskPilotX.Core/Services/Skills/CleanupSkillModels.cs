namespace DiskPilotX.Core.Services.Skills;

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
public sealed record CleanupSkillSourceValidation(bool Success, string Digest, IReadOnlyList<CleanupSkillSourceDiagnostic> Diagnostics, string Contract = "none");
public sealed record CleanupSkillDetectionContext(string SkillId, string ScopePath, IReadOnlyList<CleanupSkillRule> Rules, IReadOnlyDictionary<string,string>? Parameters = null);

/// <summary>可信源码的固定检测契约。此接口和加载上下文均不提供安全沙箱；只能返回候选，宿主另行确认清理。</summary>
public interface ICleanupSkillDetector
{
    Task<IReadOnlyList<CleanupSkillCandidate>> DetectAsync(CleanupSkillDetectionContext context, CancellationToken cancellationToken);
}

/// <summary>应用数据发现结果条目（枚举/读取类 skill 的输出单元，如 Steam 游戏）。</summary>
public sealed record AppDataEntry(
    string AppId,
    string Name,
    string Path,
    long SizeBytes,
    string? Detail = null);

/// <summary>
/// 发现类 skill 的完整产出：数据条目 + 可选的按需视图契约。
/// 视图由源码声明（如海报网格 + 上次游玩时间），宿主用原生控件渲染。
/// </summary>
public sealed record AppDataDiscoveryResult(
    IReadOnlyList<AppDataEntry> Entries,
    DiskPilotX.Core.Models.SkillUiSpec? View = null);

/// <summary>
/// 应用数据发现的只读上下文。Parameters 来自宿主确认的任务面板表单；
/// Host 是宿主注入的能力桥，源码的一切本机读取都必须经由它。
/// </summary>
public sealed record AppDataDiscoveryContext(
    string SkillId,
    ISkillHostBridge Host,
    IReadOnlyDictionary<string, string>? Parameters = null);

/// <summary>
/// 可信源码的固定发现契约：枚举/读取本机应用数据（只读），不检测清理候选。
/// 与 ICleanupSkillDetector 互斥；此接口和加载上下文均不提供安全沙箱。
/// </summary>
public interface IAppDataProvider
{
    Task<AppDataDiscoveryResult> DiscoverAsync(AppDataDiscoveryContext context, CancellationToken cancellationToken);
}
