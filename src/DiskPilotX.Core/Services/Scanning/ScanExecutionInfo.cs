using System.Security.Principal;
using DiskPilotX.Core.Models;

namespace DiskPilotX.Core.Services.Scanning;

public enum ScanMethod { Unknown, Mft, UsnIncremental, DirectoryTraversal }
public enum ScanExecutionStatus { Running, Completed, Cancelled, CancelledBeforeStart, Failed }

/// <summary>记录实际执行路线；确认前取消与已有扫描结果互不覆盖。</summary>
public sealed record ScanExecutionInfo(string TargetPath, ScanMethod Method, ScanExecutionStatus Status,
    DateTime? StartedAt = null, DateTime? CompletedAt = null, IReadOnlyList<string>? FallbackReasons = null)
{
    public object ToPublicData() => new
    {
        path = TargetPath,
        method = Method switch { ScanMethod.Mft => "mft", ScanMethod.UsnIncremental => "usn_incremental", ScanMethod.DirectoryTraversal => "directory_traversal", _ => "unknown" },
        status = Status switch { ScanExecutionStatus.Running => "running", ScanExecutionStatus.Completed => "completed", ScanExecutionStatus.Cancelled => "cancelled", ScanExecutionStatus.CancelledBeforeStart => "cancelled_before_start", _ => "failed" },
        started = StartedAt.HasValue && Status != ScanExecutionStatus.CancelledBeforeStart,
        started_at = StartedAt, completed_at = CompletedAt,
        fallback_reasons = FallbackReasons ?? []
    };

    public static ScanExecutionInfo? FromSession(ScanSession? session) => session is null ? null : session.Execution
        ?? new(session.TargetPath, ScanMethod.Unknown, session.IsCancelled ? ScanExecutionStatus.Cancelled : ScanExecutionStatus.Completed,
            session.StartTime == default ? null : session.StartTime, session.EndTime);
}

public static class ProcessPrivileges
{
    /// <summary>检查当前进程令牌，不能把管理员组成员身份当成已经提权。</summary>
    public static bool IsElevated
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
