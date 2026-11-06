using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DiskPilotX.Core.Services.Scanning;

public interface IScanRouteEnvironment
{
    ScanVolumeCapability Inspect(string volumeRoot);
    DateTime UtcNow { get; }
}

/// <summary>只查询卷能力与现有日志，不提升权限、不创建USN、不读取用户文件内容。</summary>
public sealed class ScanRouteEnvironment : IScanRouteEnvironment
{
    public DateTime UtcNow => DateTime.UtcNow;
    public ScanVolumeCapability Inspect(string root)
    {
        bool elevated = ProcessPrivileges.IsElevated;
        string key = Hash(root.ToUpperInvariant());
        try
        {
            var drive = new DriveInfo(root);
            if (drive.DriveType == DriveType.Network || root.StartsWith(@"\\"))
                return new(key, "network", elevated, false, "network_volume", "网络路径不支持当前NTFS卷索引接口，选择目录遍历。");
            if (!drive.IsReady) return new(key, "unknown", elevated, false, "volume_not_ready", "卷未就绪，不能验证快速索引能力。");
            string fs = drive.DriveFormat;
            if (OperatingSystem.IsWindows() && GetVolumeInformation(root, null, 0, out uint serial, out _, out _, null, 0))
                key = Hash(root.ToUpperInvariant() + ":" + serial.ToString("X8") + ":" + fs);
            if (!string.Equals(fs, "NTFS", StringComparison.OrdinalIgnoreCase))
                return new(key, fs, elevated, false, "unsupported_filesystem", $"文件系统为{fs}，当前快速卷索引仅支持NTFS，选择目录遍历。");
            if (!elevated) return new(key, fs, false, false, "not_elevated", "当前进程未提权，无法读取卷索引；使用目录遍历，不自动请求提权。");
            using var handle = NtfsUsnNative.OpenVolume(root);
            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                return new(key, fs, true, false, "volume_open_failed", $"已提权但打开卷失败，Win32错误{error}；使用目录遍历。", error);
            }
            bool ok = NtfsUsnNative.DeviceIoControl(handle, NtfsUsnNative.FSCTL_QUERY_USN_JOURNAL, 0, 0,
                out NtfsUsnNative.USN_JOURNAL_DATA_V1 _, Marshal.SizeOf<NtfsUsnNative.USN_JOURNAL_DATA_V1>(), out _, 0);
            if (!ok)
            {
                int error = Marshal.GetLastWin32Error();
                return new(key, fs, true, false, "journal_query_failed", $"已提权且NTFS，但现有USN日志查询失败，错误{error}；不创建日志，选择遍历。", error);
            }
            return new(key, fs, true, true, "mft_available", "进程已提权、卷为NTFS，卷句柄和现有USN日志访问检查通过。");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        { return new(key, "unknown", elevated, false, "capability_unavailable", "无法可靠检查卷能力，保留原范围执行目录遍历：" + e.Message); }
    }
    internal static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    [DllImport("kernel32.dll", EntryPoint="GetVolumeInformationW",CharSet=CharSet.Unicode,SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformation(string root,StringBuilder? name,int nameSize,out uint serial,
        out uint componentLength,out uint flags,StringBuilder? fileSystem,int fileSystemSize);
}

public interface IScanPerformanceStore
{
    IReadOnlyList<ScanPerformanceSample> Read();
    void Record(ScanPerformanceSample sample);
}

/// <summary>只持久化哈希标识与性能，不写目录名、文件名或整卷索引；有界保留成功样本。</summary>
public sealed class ScanPerformanceStore : IScanPerformanceStore
{
    private readonly string? _path;
    private readonly object _gate = new();
    private List<ScanPerformanceSample>? _samples;
    public ScanPerformanceStore(string? path = null, bool memoryOnly = false) => _path = memoryOnly ? null : path
        ?? AppCompatibility.ResolveDataPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "scan-performance.json");
    public IReadOnlyList<ScanPerformanceSample> Read()
    {
        lock (_gate)
        {
            if (_samples is null)
            {
                try { _samples = _path is not null && File.Exists(_path)
                    ? JsonSerializer.Deserialize<List<ScanPerformanceSample>>(File.ReadAllText(_path)) ?? [] : []; }
                catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { _samples = []; }
            }
            return _samples.ToArray();
        }
    }
    public void Record(ScanPerformanceSample sample)
    {
        if (sample.Entries <= 0 || !double.IsFinite(sample.ElapsedMilliseconds) || sample.ElapsedMilliseconds <= 0
            || !double.IsFinite(sample.IndexMilliseconds) || sample.IndexMilliseconds < 0
            || !double.IsFinite(sample.TreeMilliseconds) || sample.TreeMilliseconds < 0
            || !double.IsFinite(sample.SizeMilliseconds) || sample.SizeMilliseconds < 0) return;
        lock (_gate)
        {
            Read(); _samples!.Add(sample); _samples = _samples.OrderByDescending(s => s.RecordedAt).Take(128).ToList();
            if (_path is null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
                var temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { File.WriteAllText(temp, JsonSerializer.Serialize(_samples)); File.Move(temp, _path, true); }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { System.Diagnostics.Trace.TraceWarning("扫描性能记录写入失败：{0}", e.Message); }
        }
    }
}
