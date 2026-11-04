using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace DiskPilotX.Core.Models;

/// <summary>可持久化的扫描入口；不包含文件树、控件或执行授权。</summary>
public sealed record ScanSnapshotReference(Guid Id, Guid CacheEpoch, string Path, DateTimeOffset ScannedAt,
    long SizeBytes, int Files, int Folders) : INotifyPropertyChanged
{
    private string state = "released";
    [JsonIgnore] public string State => state;
    [JsonIgnore] public string DisplayPath => state == "restricted" ? "扫描结果（当前访问受限）" : Path;
    [JsonIgnore] public string Title => state=="restricted" ? "扫描结果（访问受限）" : System.IO.Path.GetFileName(Path.TrimEnd('\\','/'));
    [JsonIgnore] public string CompactPath
    {
        get
        {
            if (state == "restricted" || Path.Length <= 48) return DisplayPath;
            var full = Path.TrimEnd('\\', '/');
            var parent = System.IO.Path.GetDirectoryName(full);
            return System.IO.Path.GetFileName(full) + " · " + System.IO.Path.GetPathRoot(full) + "…\\" +
                (string.IsNullOrEmpty(parent) ? "" : System.IO.Path.GetFileName(parent));
        }
    }
    public override string ToString() => DisplayPath;
    [JsonIgnore] public string Summary => $"{ScannedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss} · {SizeBytes:N0} 字节 · 扫描快照，非实时";
    [JsonIgnore] public string ActionText => "在 Treemap 查看";
    [JsonIgnore] public bool CanAct => state == "available";
    private string? progressText;
    [JsonIgnore] public bool IsPending => state is "pending" or "scanning";
    [JsonIgnore] public string ProgressText => progressText ?? StatusText;
    public void SetProgress(string value)
    {
        if (progressText == value) return;
        progressText = value;
        PropertyChanged?.Invoke(this, new(nameof(ProgressText)));
    }
    [JsonIgnore] public string StatusText => state switch
    {
        "available" => $"扫描完成 · {Files:N0} 文件 · {Folders:N0} 目录",
        "pending" => "待扫描",
        "scanning" => "扫描中，正在统计文件和目录。",
        "failed" => "扫描未完成，请查看失败原因。",
        "cancelled" => "扫描已取消，未生成有效快照。",
        "evicted" => "扫描信息已失效：内存结果已回收，需要重新扫描。",
        "missing" => "扫描信息已失效：路径不存在或无法访问。",
        "restricted" => "当前隐私或扫描设置不允许访问。",
        "busy" => "当前任务正在进行，请结束后重试。",
        _ => "扫描信息已失效：内存实例不存在，需要重新扫描。"
    };
    public event PropertyChangedEventHandler? PropertyChanged;
    public void SetState(string value)
    {
        if (state == value) return;
        state = value;
        progressText = null;
        foreach (var name in new[] { nameof(State), nameof(Title), nameof(DisplayPath), nameof(CompactPath), nameof(ActionText), nameof(CanAct), nameof(StatusText), nameof(ProgressText), nameof(IsPending) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
