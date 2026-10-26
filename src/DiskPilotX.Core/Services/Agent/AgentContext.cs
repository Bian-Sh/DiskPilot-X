using DiskPilotX.Core.Models;

namespace DiskPilotX.Core.Services.Agent;

public sealed record AgentContext(
    ScanSession? Session,
    FileEntry? CurrentViewRoot,
    FileEntry? LinkedEntry,
    CleanupRecommendation? LinkedRecommendation,
    bool HasExistingRecommendations = false);
