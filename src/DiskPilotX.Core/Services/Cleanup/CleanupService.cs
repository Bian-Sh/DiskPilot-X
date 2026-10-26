using Microsoft.VisualBasic.FileIO;
using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Models;

namespace DiskPilotX.Core.Services.Cleanup;

public class CleanupService : ICleanupService
{
    public async Task<List<CleanupAction>> ExecuteCleanupAsync(
        List<CleanupRecommendation> accepted,
        DeletionMode mode,
        IProgress<CleanupProgress> progress,
        CancellationToken cancellationToken)
    {
        var results = new List<CleanupAction>();

        for (int i = 0; i < accepted.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var recommendation = accepted[i];

            var action = new CleanupAction
            {
                Recommendation = recommendation,
                ActionType = mode,
                Timestamp = DateTime.Now
            };

            progress.Report(new CleanupProgress(
                recommendation.TargetPath,
                i,
                accepted.Count));

            try
            {
                var snapshot = await Task.Run(() =>
                {
                    var targetPath = recommendation.TargetPath;
                    var expected = recommendation.ExpectedCleanupFingerprint;
                    var current = CleanupFilesystemSnapshot.Capture(targetPath, cancellationToken);
                    if (!current.Exists)
                        return current;
                    if (expected is not null && current.Fingerprint != expected)
                        throw new InvalidOperationException("清理目标已改变，必须重新批准。");
                    cancellationToken.ThrowIfCancellationRequested();
                    bool isDirectory = current.IsDirectory;

                    switch (mode)
                    {
                        case DeletionMode.PermanentDelete:
                            if (isDirectory)
                            {
                                Directory.Delete(targetPath, recursive: true);
                            }
                            else
                            {
                                File.Delete(targetPath);
                            }
                            break;

                        case DeletionMode.MoveToRecycleBin:
                            if (isDirectory)
                            {
                                FileSystem.DeleteDirectory(
                                    targetPath,
                                    UIOption.OnlyErrorDialogs,
                                    RecycleOption.SendToRecycleBin);
                            }
                            else
                            {
                                FileSystem.DeleteFile(
                                    targetPath,
                                    UIOption.OnlyErrorDialogs,
                                    RecycleOption.SendToRecycleBin);
                            }
                            break;
                    }
                    return current;
                }, cancellationToken).ConfigureAwait(false);

                action.Result = snapshot.Exists ? CleanupResult.Success : CleanupResult.AlreadyRemoved;
                action.ActualSizeFreed = snapshot.Exists ? snapshot.Size : 0;
            }
            catch (FileNotFoundException)
            {
                action.Result = CleanupResult.AlreadyRemoved;
            }
            catch (DirectoryNotFoundException)
            {
                action.Result = CleanupResult.AlreadyRemoved;
            }
            catch (InvalidOperationException ex)
            {
                action.Result = CleanupResult.Skipped;
                action.FailureReason = ex.Message;
            }
            catch (IOException ex)
            {
                action.Result = CleanupResult.Skipped;
                action.FailureReason = ex.Message;
            }
            catch (UnauthorizedAccessException ex)
            {
                action.Result = CleanupResult.Skipped;
                action.FailureReason = ex.Message;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                action.Result = CleanupResult.Failed;
                action.FailureReason = ex.Message;
            }

            results.Add(action);
        }

        return results;
    }
}
