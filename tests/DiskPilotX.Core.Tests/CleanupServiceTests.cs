using FluentAssertions;
using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Cleanup;

namespace DiskPilotX.Core.Tests;

public class CleanupServiceTests
{
    [Theory]
    [InlineData(DeletionMode.PermanentDelete)]
    [InlineData(DeletionMode.MoveToRecycleBin)]
    public async Task MissingFileIsAlreadyRemovedAndFreesNoSpace(DeletionMode mode)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"DiskPilot-X-missing-{Guid.NewGuid():N}.tmp");
        var service = new CleanupService();

        var results = await service.ExecuteCleanupAsync(
            [new() { TargetPath = path, Size = 123 }], mode,
            new Progress<CleanupProgress>(), CancellationToken.None);

        results.Should().ContainSingle().Which.Result.Should().Be(CleanupResult.AlreadyRemoved);
        results[0].ActualSizeFreed.Should().Be(0);
    }

    [Fact]
    public async Task RepeatedFileDeletionOnlyReportsFreedSpaceOnce()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"DiskPilot-X-cleanup-{Guid.NewGuid():N}.tmp");
        try
        {
            await System.IO.File.WriteAllBytesAsync(path, [1, 2, 3]);
            var recommendation = new CleanupRecommendation { TargetPath = path, Size = 3 };
            var results = await new CleanupService().ExecuteCleanupAsync(
                [recommendation, recommendation], DeletionMode.PermanentDelete,
                new Progress<CleanupProgress>(), CancellationToken.None);

            results.Select(action => action.Result).Should().Equal(CleanupResult.Success, CleanupResult.AlreadyRemoved);
            results.Select(action => action.ActualSizeFreed).Should().Equal(3, 0);
            System.IO.File.Exists(path).Should().BeFalse();
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Fact]
    public async Task ApprovedDirectoryWithAddedContentIsNotDeleted()
    {
        using var directory = new CleanupTestDirectory();
        directory.WriteFile("nested/existing.json");
        var item = new CleanupRecommendation { Id = "1", TargetPath = directory.Path };
        var plan = CleanupPlan.Capture(new ScanSession(), [item], ["1"], DeletionMode.PermanentDelete);
        directory.WriteFile("nested/new.json");

        var results = await new CleanupService().ExecuteCleanupAsync(plan.Items.ToList(),
            DeletionMode.PermanentDelete, new Progress<CleanupProgress>(), CancellationToken.None);

        Directory.Exists(directory.Path).Should().BeTrue();
        results.Single().Result.Should().Be(CleanupResult.Skipped);
        results.Single().ActualSizeFreed.Should().Be(0);
    }

    [Fact]
    public async Task DirectServiceCallRejectsNewProtectedDescendantMissingFromScanTree()
    {
        using var directory = new CleanupTestDirectory();
        var entry = new FileEntry { Path = directory.Path };
        var protectedFile = directory.WriteFile("nested/Program Files/important.json");
        var item = new CleanupRecommendation { TargetPath = directory.Path, Entry = entry };

        var results = await new CleanupService().ExecuteCleanupAsync([item], DeletionMode.PermanentDelete,
            new Progress<CleanupProgress>(), CancellationToken.None);

        File.Exists(protectedFile).Should().BeTrue();
        results.Single().Result.Should().Be(CleanupResult.Skipped);
        results.Single().ActualSizeFreed.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessfulCleanupReportsRealLogicalBytesInsteadOfRecommendationSize(bool isDirectory)
    {
        using var directory = new CleanupTestDirectory();
        var file = directory.WriteFile("nested/file.bin", "12345");
        var target = isDirectory ? directory.Path : file;
        var item = new CleanupRecommendation { TargetPath = target, Size = 999 };

        var results = await new CleanupService().ExecuteCleanupAsync([item], DeletionMode.PermanentDelete,
            new Progress<CleanupProgress>(), CancellationToken.None);

        results.Single().Result.Should().Be(CleanupResult.Success);
        results.Single().ActualSizeFreed.Should().Be(5);
    }

    [Fact]
    public async Task ApprovedDirectoryWithMissingDescendantIsSkipped()
    {
        using var directory = new CleanupTestDirectory();
        var file = directory.WriteFile("nested/existing.json");
        var item = new CleanupRecommendation { Id = "1", TargetPath = directory.Path };
        var plan = CleanupPlan.Capture(new ScanSession(), [item], ["1"], DeletionMode.PermanentDelete);
        File.Delete(file);

        var results = await new CleanupService().ExecuteCleanupAsync(plan.Items.ToList(), DeletionMode.PermanentDelete,
            new Progress<CleanupProgress>(), CancellationToken.None);

        Directory.Exists(directory.Path).Should().BeTrue();
        results.Single().Result.Should().Be(CleanupResult.Skipped);
        results.Single().ActualSizeFreed.Should().Be(0);
    }

    [Theory]
    [InlineData(DeletionMode.PermanentDelete)]
    [InlineData(DeletionMode.MoveToRecycleBin)]
    public async Task ApprovedTargetRemovedBeforeExecutionIsAlreadyRemoved(DeletionMode mode)
    {
        using var directory = new CleanupTestDirectory();
        var file = directory.WriteFile("existing.json");
        var item = new CleanupRecommendation { Id = "1", TargetPath = file };
        var plan = CleanupPlan.Capture(new ScanSession(), [item], ["1"], mode);
        File.Delete(file);

        var results = await new CleanupService().ExecuteCleanupAsync(plan.Items.ToList(), mode,
            new Progress<CleanupProgress>(), CancellationToken.None);

        results.Single().Result.Should().Be(CleanupResult.AlreadyRemoved);
        results.Single().ActualSizeFreed.Should().Be(0);
    }

    [Theory]
    [InlineData(DeletionMode.PermanentDelete)]
    [InlineData(DeletionMode.MoveToRecycleBin)]
    public async Task ProtectedDescendantAddedAfterApprovalIsNeverDeleted(DeletionMode mode)
    {
        using var directory = new CleanupTestDirectory();
        directory.WriteFile("nested/existing.json");
        var item = new CleanupRecommendation { Id = "1", TargetPath = directory.Path };
        var plan = CleanupPlan.Capture(new ScanSession(), [item], ["1"], mode);
        var file = directory.WriteFile("nested/Program Files/important.json");

        var results = await new CleanupService().ExecuteCleanupAsync(plan.Items.ToList(), mode,
            new Progress<CleanupProgress>(), CancellationToken.None);

        File.Exists(file).Should().BeTrue();
        results.Single().Result.Should().Be(CleanupResult.Skipped);
        results.Single().ActualSizeFreed.Should().Be(0);
    }

    [Fact]
    public async Task CancellationImmediatelyBeforeWorkerPreventsDeletion()
    {
        using var directory = new CleanupTestDirectory();
        var file = directory.WriteFile("file.bin");
        using var cancellation = new CancellationTokenSource();
        Func<Task> cleanup = () => new CleanupService().ExecuteCleanupAsync([new() { TargetPath = file }],
            DeletionMode.PermanentDelete, new CancelOnReport(cancellation), cancellation.Token);

        await cleanup.Should().ThrowAsync<OperationCanceledException>();
        File.Exists(file).Should().BeTrue();
    }

    [Fact]
    public async Task ApprovedUnchangedTargetReportsActualSnapshotSize()
    {
        using var directory = new CleanupTestDirectory();
        directory.WriteFile("nested/file.bin", "12345");
        var item = new CleanupRecommendation { Id = "1", TargetPath = directory.Path, Size = 999 };
        var plan = CleanupPlan.Capture(new ScanSession(), [item], ["1"], DeletionMode.PermanentDelete);
        var results = await new CleanupService().ExecuteCleanupAsync(plan.Items.ToList(), DeletionMode.PermanentDelete,
            new Progress<CleanupProgress>(), CancellationToken.None);

        results.Single().Result.Should().Be(CleanupResult.Success);
        results.Single().ActualSizeFreed.Should().Be(5);
        Directory.Exists(directory.Path).Should().BeFalse();
    }

    private sealed class CancelOnReport(CancellationTokenSource cancellation) : IProgress<CleanupProgress>
    {
        public void Report(CleanupProgress value) => cancellation.Cancel();
    }
}
