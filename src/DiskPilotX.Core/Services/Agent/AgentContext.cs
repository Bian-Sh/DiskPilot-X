using DiskPilotX.Core.Models;

namespace DiskPilotX.Core.Services.Agent;

public sealed record AgentContext(
    ScanSession? Session,
    FileEntry? CurrentViewRoot,
    FileEntry? LinkedEntry,
    CleanupRecommendation? LinkedRecommendation,
    bool HasExistingRecommendations = false,
    bool? IsElevated = null,
    DiskPilotX.Core.Services.Scanning.ScanExecutionInfo? LastScanExecution = null,
    DiskPilotX.Core.Services.Scanning.ScanExecutionInfo? LastCompletedScanExecution = null,
    IReadOnlyList<string>? MultiSelection = null);
