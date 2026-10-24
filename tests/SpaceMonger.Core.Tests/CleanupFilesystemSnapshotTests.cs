using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using FluentAssertions;
using SpaceMonger.Core.Enums;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Cleanup;

namespace SpaceMonger.Core.Tests;

public class CleanupFilesystemSnapshotTests
{
    [Fact]
    public async Task UnreadableDescendantInvalidatesApprovalAndBlocksDirectServiceCall()
    {
        using var directory = new CleanupTestDirectory();
        var file = directory.WriteFile("nested/deeper/file.bin", "12345");
        var item = new CleanupRecommendation { Id = "1", TargetPath = directory.Path };
        var session = new ScanSession();
        var plan = CleanupPlan.Capture(session, [item], ["1"], DeletionMode.PermanentDelete);
        var blocked = new DirectoryInfo(System.IO.Path.GetDirectoryName(file)!);
        var original = blocked.GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorBinaryForm();
        var denied = blocked.GetAccessControl(AccessControlSections.Access);
        denied.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!,
            FileSystemRights.ListDirectory, AccessControlType.Deny));
        try
        {
            blocked.SetAccessControl(denied);
            plan.MatchesCurrentList(session, [item], DeletionMode.PermanentDelete).Should().BeTrue();
            plan.IsCurrent(session, [item], DeletionMode.PermanentDelete).Should().BeFalse();
            plan.IsTargetCurrent(plan.Items[0]).Should().BeFalse();
            var results = await new CleanupService().ExecuteCleanupAsync([item], DeletionMode.PermanentDelete,
                new Progress<CleanupProgress>(), CancellationToken.None);

            results.Single().Result.Should().Be(CleanupResult.Skipped);
            results.Single().ActualSizeFreed.Should().Be(0);
        }
        finally
        {
            var restored = new DirectorySecurity();
            restored.SetSecurityDescriptorBinaryForm(original, AccessControlSections.Access);
            blocked.SetAccessControl(restored);
        }
        File.Exists(file).Should().BeTrue();
    }

    [Fact]
    public void SnapshotCanReadMetadataWithoutOpeningLockedFileContents()
    {
        using var directory = new CleanupTestDirectory();
        var file = directory.WriteFile("nested/file.bin", "12345");
        using var locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None);
        var item = new CleanupRecommendation { Id = "1", TargetPath = directory.Path };
        var session = new ScanSession();

        var plan = CleanupPlan.Capture(session, [item], ["1"], DeletionMode.PermanentDelete);

        plan.Items.Single().Size.Should().Be(5);
        plan.IsCurrent(session, [item], DeletionMode.PermanentDelete).Should().BeTrue();
    }

    [Fact]
    public async Task NewlyAddedJunctionInvalidatesApprovalAndIsNotFollowed()
    {
        using var directory = new CleanupTestDirectory();
        var file = directory.WriteFile("real/file.bin");
        var item = new CleanupRecommendation { Id = "1", TargetPath = directory.Path };
        var session = new ScanSession();
        var plan = CleanupPlan.Capture(session, [item], ["1"], DeletionMode.PermanentDelete);
        var junction = System.IO.Path.Combine(directory.Path, "alias");
        var target = System.IO.Path.Combine(directory.Path, "real");
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add($"New-Item -ItemType Junction -Path '{junction.Replace("'", "''")}' -Target '{target.Replace("'", "''")}' -ErrorAction Stop");
        try
        {
            using var process = Process.Start(startInfo)!;
            var error = process.StandardError.ReadToEndAsync();
            await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            process.ExitCode.Should().Be(0, await error);
            File.GetAttributes(junction).Should().HaveFlag(FileAttributes.ReparsePoint);

            plan.IsCurrent(session, [item], DeletionMode.PermanentDelete).Should().BeFalse();
            Action capture = () => CleanupPlan.Capture(session, [item], ["1"], DeletionMode.PermanentDelete);
            capture.Should().Throw<InvalidOperationException>();
            Action throughJunction = () => CleanupPlan.Capture(session,
                [new() { Id = "1", TargetPath = System.IO.Path.Combine(junction, "file.bin") }], ["1"], DeletionMode.PermanentDelete);
            throughJunction.Should().Throw<InvalidOperationException>();

            var results = await new CleanupService().ExecuteCleanupAsync([item], DeletionMode.PermanentDelete,
                new Progress<CleanupProgress>(), CancellationToken.None);
            results.Single().Result.Should().Be(CleanupResult.Skipped);
            File.Exists(file).Should().BeTrue();
        }
        finally
        {
            // 只移除本次创建且经校验的链接，避免清理时触及链接目标。
            if (System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(junction)) != directory.Path)
                throw new InvalidOperationException("测试链接超出本次临时目录。");
            if (Directory.Exists(junction) && (File.GetAttributes(junction) & FileAttributes.ReparsePoint) != 0)
                Directory.Delete(junction);
        }
    }
}
