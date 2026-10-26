using FluentAssertions;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Services.Cleanup;
namespace DiskPilotX.Core.Tests;
public class CleanupPlanTests
{
    [Fact]
    public void ApprovalVersionChanges_WhenPathModeOrSelectionChanges()
    {
        var item = new CleanupRecommendation { Id="1", TargetPath=@"D:\test\cache", Size=15, IsAccepted=true };
        var session = new ScanSession { TargetPath=@"D:\test" };
        var list = new List<CleanupRecommendation>{item};
        var plan = CleanupPlan.Capture(session, list, ["1"], DeletionMode.MoveToRecycleBin);
        plan.IsCurrent(session, list, DeletionMode.MoveToRecycleBin).Should().BeTrue();
        plan.IsCurrent(session, list, DeletionMode.PermanentDelete).Should().BeFalse();
        item.TargetPath = @"D:\test\other";
        plan.IsCurrent(session,list,DeletionMode.MoveToRecycleBin).Should().BeFalse();
        plan.Items.Single().TargetPath.Should().Be(@"D:\test\cache");
    }
    [Fact]
    public void CaptureRejectsUnknownIdsAndOverlappingPaths()
    {
        var session=new ScanSession();
        var items=new List<CleanupRecommendation>{new(){Id="1",TargetPath=@"D:\cache"},new(){Id="2",TargetPath=@"D:\cache\child"}};
        Action unknown=()=>CleanupPlan.Capture(session,items,["missing"],DeletionMode.MoveToRecycleBin);
        unknown.Should().Throw<InvalidOperationException>();
        Action overlapping=()=>CleanupPlan.Capture(session,items,["1","2"],DeletionMode.MoveToRecycleBin);
        overlapping.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void DeepFileChangeInvalidatesApprovalEvenWhenRootTimestampIsRestored()
    {
        using var directory = new CleanupTestDirectory();
        var file = directory.WriteFile("nested/deeper/existing.json", "1111");
        var item = new CleanupRecommendation { Id = "1", TargetPath = directory.Path, IsAccepted = true };
        var session = new ScanSession();
        var plan = CleanupPlan.Capture(session, [item], ["1"], DeletionMode.PermanentDelete);
        var rootTimestamp = Directory.GetLastWriteTimeUtc(directory.Path);
        File.WriteAllText(file, "2222");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(2));
        Directory.SetLastWriteTimeUtc(directory.Path, rootTimestamp);

        plan.IsCurrent(session, [item], DeletionMode.PermanentDelete).Should().BeFalse();
    }

    [Fact]
    public void NewlyProtectedDescendantInvalidatesApprovalDespiteStaleScanTree()
    {
        using var directory = new CleanupTestDirectory();
        directory.WriteFile("nested/existing.json");
        var item = new CleanupRecommendation { Id = "1", TargetPath = directory.Path,
            Entry = new FileEntry { Path = directory.Path } };
        var session = new ScanSession();
        var plan = CleanupPlan.Capture(session, [item], ["1"], DeletionMode.PermanentDelete);
        var timestamp = Directory.GetLastWriteTimeUtc(directory.Path);
        directory.WriteFile("nested/Program Files/important.json");
        Directory.SetLastWriteTimeUtc(directory.Path, timestamp);

        plan.IsCurrent(session, [item], DeletionMode.PermanentDelete).Should().BeFalse();
    }

    [Fact]
    public void CapturePreservesSelectionAndUsesRealLogicalSize()
    {
        using var directory = new CleanupTestDirectory();
        directory.WriteFile("nested/file.bin", "12345");
        var item = new CleanupRecommendation { Id = "1", TargetPath = directory.Path, Size = 999, IsAccepted = true };
        var plan = CleanupPlan.Capture(new ScanSession(), [item], ["1"], DeletionMode.PermanentDelete);

        plan.Items[0].IsAccepted.Should().BeTrue();
        plan.Items[0].IsDismissed.Should().BeFalse();
        plan.Items[0].Size.Should().Be(5);
        item.Size.Should().Be(999);
    }

    [Fact]
    public void LightweightVersionRemainsStableWhileApprovalFingerprintChanges()
    {
        using var directory = new CleanupTestDirectory();
        var file = directory.WriteFile("nested/file.bin", "1111");
        var item = new CleanupRecommendation { Id = "1", TargetPath = directory.Path, Size = 100 };
        var session = new ScanSession();
        var first = CleanupPlan.Capture(session, [item], ["1"], DeletionMode.PermanentDelete);
        File.WriteAllText(file, "2222");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(3));
        var second = CleanupPlan.Capture(session, [item], ["1"], DeletionMode.PermanentDelete);

        first.RecommendationVersion.Should().Be(CleanupPlan.ComputeVersion(session, [item], DeletionMode.PermanentDelete));
        first.RecommendationVersion.Should().Be(second.RecommendationVersion);
        first.Version.Should().NotBe(second.Version);
        first.MatchesCurrentList(session, [item], DeletionMode.PermanentDelete).Should().BeTrue();
        first.IsTargetCurrent(first.Items[0]).Should().BeFalse();
        second.IsCurrent(session, [item], DeletionMode.PermanentDelete).Should().BeTrue();
        second.Version.Should().Be(CleanupPlan.Capture(session, [item], ["1"], DeletionMode.PermanentDelete).Version);
    }

    [Fact]
    public void CaptureDoesNotTraverseUnselectedProtectedDirectory()
    {
        using var directory = new CleanupTestDirectory();
        var file = directory.WriteFile("selected/file.bin", "123");
        var protectedFile = directory.WriteFile("unselected/nested/Program Files/important.json");
        var selected = new CleanupRecommendation { Id = "1", TargetPath = file };
        var unselected = new CleanupRecommendation { Id = "2", TargetPath = System.IO.Path.Combine(directory.Path, "unselected") };
        var session = new ScanSession();
        var plan = CleanupPlan.Capture(session, [selected, unselected], ["1"], DeletionMode.PermanentDelete);
        File.WriteAllText(protectedFile, "未选目录中的变化");

        plan.IsCurrent(session, [selected, unselected], DeletionMode.PermanentDelete).Should().BeTrue();
        plan.Items.Should().ContainSingle().Which.Size.Should().Be(3);
    }

    [Fact]
    public void CaptureRejectsExistingProtectedDescendants()
    {
        using var directory = new CleanupTestDirectory();
        directory.WriteFile("nested/Program Files/important.json");
        var item = new CleanupRecommendation { Id = "1", TargetPath = directory.Path };

        Action capture = () => CleanupPlan.Capture(new ScanSession(), [item], ["1"], DeletionMode.PermanentDelete);

        capture.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MissingDescendantInvalidatesApprovalEvenWithRestoredRootTimestamp()
    {
        using var directory = new CleanupTestDirectory();
        var file = directory.WriteFile("nested/deeper/existing.json");
        var item = new CleanupRecommendation { Id = "1", TargetPath = directory.Path };
        var session = new ScanSession();
        var plan = CleanupPlan.Capture(session, [item], ["1"], DeletionMode.PermanentDelete);
        var timestamp = Directory.GetLastWriteTimeUtc(directory.Path);
        File.Delete(file);
        Directory.SetLastWriteTimeUtc(directory.Path, timestamp);

        plan.IsCurrent(session, [item], DeletionMode.PermanentDelete).Should().BeFalse();
        plan.IsTargetCurrent(plan.Items[0]).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CapturePreservesDismissedState(bool dismissed)
    {
        using var directory = new CleanupTestDirectory();
        var file = directory.WriteFile("file.bin");
        var item = new CleanupRecommendation { Id = "1", TargetPath = file, IsDismissed = dismissed };
        var plan = CleanupPlan.Capture(new ScanSession(), [item], ["1"], DeletionMode.PermanentDelete);

        plan.Items[0].IsDismissed.Should().Be(dismissed);
        plan.Items[0].IsAccepted.Should().BeFalse();
    }

    [Fact]
    public void MissingTargetCanBeCapturedWithZeroSize()
    {
        using var directory = new CleanupTestDirectory();
        var item = new CleanupRecommendation { Id = "1", TargetPath = System.IO.Path.Combine(directory.Path, "missing", "file.bin"), Size = 100 };
        var session = new ScanSession();
        var plan = CleanupPlan.Capture(session, [item], ["1"], DeletionMode.PermanentDelete);

        plan.Items[0].Size.Should().Be(0);
        plan.IsCurrent(session, [item], DeletionMode.PermanentDelete).Should().BeTrue();
        directory.WriteFile("missing/file.bin");
        plan.IsCurrent(session, [item], DeletionMode.PermanentDelete).Should().BeFalse();
    }

    [Fact]
    public void SnapshotOperationsPropagateCancellation()
    {
        using var directory = new CleanupTestDirectory();
        var file = directory.WriteFile("nested/file.bin");
        var item = new CleanupRecommendation { Id = "1", TargetPath = file };
        var session = new ScanSession();
        var plan = CleanupPlan.Capture(session, [item], ["1"], DeletionMode.PermanentDelete);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Action capture = () => CleanupPlan.Capture(session, [item], ["1"], DeletionMode.PermanentDelete, cancellation.Token);
        Action current = () => plan.IsCurrent(session, [item], DeletionMode.PermanentDelete, cancellation.Token);
        Action target = () => plan.IsTargetCurrent(plan.Items[0], cancellation.Token);

        capture.Should().Throw<OperationCanceledException>();
        current.Should().Throw<OperationCanceledException>();
        target.Should().Throw<OperationCanceledException>();
        File.Exists(file).Should().BeTrue();
    }

    [Fact]
    public void ListValidationRejectsChangedSelectionScanAndMode()
    {
        using var directory = new CleanupTestDirectory();
        var file = directory.WriteFile("file.bin");
        var item = new CleanupRecommendation { Id = "1", TargetPath = file, IsAccepted = true };
        var session = new ScanSession();
        var plan = CleanupPlan.Capture(session, [item], ["1"], DeletionMode.PermanentDelete);

        plan.MatchesCurrentList(new ScanSession(), [item], DeletionMode.PermanentDelete).Should().BeFalse();
        plan.MatchesCurrentList(null, [item], DeletionMode.PermanentDelete).Should().BeFalse();
        plan.MatchesCurrentList(session, [item], DeletionMode.MoveToRecycleBin).Should().BeFalse();
        item.IsDismissed = true;
        plan.MatchesCurrentList(session, [item], DeletionMode.PermanentDelete).Should().BeFalse();
    }

    [Fact]
    public void CaptureRejectsDiskRootAndDuplicateIds()
    {
        var item = new CleanupRecommendation { Id = "1", TargetPath = System.IO.Path.GetPathRoot(System.IO.Path.GetTempPath())! };
        var session = new ScanSession();
        Action root = () => CleanupPlan.Capture(session, [item], ["1"], DeletionMode.PermanentDelete);
        Action duplicates = () => CleanupPlan.Capture(session, [item], ["1", "1"], DeletionMode.PermanentDelete);

        root.Should().Throw<InvalidOperationException>();
        duplicates.Should().Throw<InvalidOperationException>();
    }
}
