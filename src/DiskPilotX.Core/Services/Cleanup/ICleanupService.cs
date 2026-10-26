using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Models;

namespace DiskPilotX.Core.Services.Cleanup;

public interface ICleanupService
{
    Task<List<CleanupAction>> ExecuteCleanupAsync(
        List<CleanupRecommendation> accepted,
        DeletionMode mode,
        IProgress<CleanupProgress> progress,
        CancellationToken cancellationToken);
}
