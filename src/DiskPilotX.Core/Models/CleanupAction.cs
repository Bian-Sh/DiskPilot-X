namespace DiskPilotX.Core.Models;

using DiskPilotX.Core.Enums;

/// <summary>
/// Represents a cleanup action that was executed on a recommendation.
/// </summary>
public class CleanupAction
{
    /// <summary>
    /// Gets or sets the unique identifier for this cleanup action.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the cleanup recommendation that prompted this action.
    /// </summary>
    public CleanupRecommendation Recommendation { get; set; } = new();

    /// <summary>
    /// Gets or sets the deletion mode used for this action.
    /// </summary>
    public DeletionMode ActionType { get; set; }

    /// <summary>
    /// Gets or sets the outcome of the cleanup action.
    /// </summary>
    public CleanupResult Result { get; set; }

    /// <summary>
    /// Gets or sets the failure reason if the action failed or was skipped.
    /// </summary>
    public string? FailureReason { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when this cleanup action was executed.
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Gets or sets the actual number of bytes freed by this action.
    /// </summary>
    public long ActualSizeFreed { get; set; }
}

/// <summary>
/// Represents the progress of an ongoing cleanup operation.
/// </summary>
public record CleanupProgress(
    string CurrentItemPath,
    int CompletedCount,
    int TotalCount
);
