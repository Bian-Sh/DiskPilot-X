namespace DiskPilotX.Core.Services.Skills;

/// <summary>宿主扫描快照中的一条条目；skill 可据此避免重复遍历磁盘。</summary>
public sealed record SkillHostEntry(string Path, long SizeBytes, string Kind, DateTime LastWriteTime);

/// <summary>
/// 宿主注入给可信 skill 源码的只读能力桥（对应 x-hub 扩展体系的 window.xhub 桥 API）。
/// <para>
/// 源码编译时不引用 Registry / Process / 网络程序集，一切本机读取都必须经由本桥，
/// 由宿主统一审计、限流与降级。桥不是安全沙箱：源码仍以当前用户权限运行，
/// 因此执行前必须由宿主展示源码并征得明确授权。
/// </para>
/// </summary>
public interface ISkillHostBridge
{
    /// <summary>读取当前本地化状态；不依赖技能运行线程的文化，不授予修改宿主语言的能力。</summary>
    SkillLocalizationState GetLocalizationState() => SkillLocalizationState.FromCulture(System.Globalization.CultureInfo.CurrentUICulture);

    /// <summary>订阅语言状态变化；调用方必须释放返回值。旧宿主默认提供空订阅。</summary>
    IDisposable SubscribeLocalizationChanged(Action<SkillLocalizationState> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        return new SkillLocalizationSubscription();
    }

    /// <summary>当前执行的 skill 标识。</summary>
    string SkillId { get; }

    /// <summary>任务面板表单提交的只读参数（已由宿主校验数量与长度）。</summary>
    IReadOnlyDictionary<string, string> Parameters { get; }

    /// <summary>写入宿主诊断日志，供用户在控制台查看。</summary>
    void Log(string message);

    /// <summary>只读读取注册表字符串值；键不存在或无权限返回 null，不抛异常。</summary>
    string? ReadRegistry(string keyPath, string valueName);

    /// <summary>枚举子目录；路径不存在返回空集合。</summary>
    IReadOnlyList<string> EnumerateDirectories(string path);

    /// <summary>枚举文件；路径不存在返回空集合。</summary>
    IReadOnlyList<string> EnumerateFiles(string path, string searchPattern = "*");

    bool DirectoryExists(string path);

    bool FileExists(string path);

    /// <summary>读取文本文件内容；失败返回 null。</summary>
    string? ReadTextFile(string path);

    /// <summary>统计目录占用字节数（复用宿主的遍历与容错逻辑）。</summary>
    long GetDirectorySize(string path);

    /// <summary>取目录最后修改时间（UTC）；常用于推断"上次使用/游玩"。路径不存在返回 null。</summary>
    DateTime? GetDirectoryLastWriteTimeUtc(string path);

    /// <summary>查询宿主已扫描的快照；rootPath 为空表示全量。</summary>
    IReadOnlyList<SkillHostEntry> QueryScan(string? rootPath = null, int maxCount = 20_000);
}
