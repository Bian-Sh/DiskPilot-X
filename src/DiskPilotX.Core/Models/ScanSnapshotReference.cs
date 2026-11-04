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
    public override string ToString() => DisplayPath;
    [JsonIgnore] public string Summary => $"{ScannedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss} · {SizeBytes:N0} 字节 · 扫描快照，非实时";
    [JsonIgnore] public string ActionText => state == "available" ? "在磁盘地图中查看" : "重新扫描";
    [JsonIgnore] public bool CanAct => state is "available" or "released" or "evicted";
    [JsonIgnore] public string StatusText => state switch
    {
        "available" => "内存快照可查看，不会重新扫描或请求 AI。",
        "evicted" => "结果已按内存预算回收，需要重新扫描。",
        "missing" => "路径不存在或无法访问。",
        "restricted" => "当前隐私或扫描设置不允许访问。",
        "busy" => "当前任务正在进行，请结束后重试。",
        _ => "内存结果已释放，需要重新扫描。"
    };
    public event PropertyChangedEventHandler? PropertyChanged;
    public void SetState(string value)
    {
        if (state == value) return;
        state = value;
        foreach (var name in new[] { nameof(State), nameof(Title), nameof(DisplayPath), nameof(ActionText), nameof(CanAct), nameof(StatusText) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
