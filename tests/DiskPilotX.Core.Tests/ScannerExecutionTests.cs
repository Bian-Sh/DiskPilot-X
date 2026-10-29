using FluentAssertions;
using DiskPilotX.Core.Services.Scanning;

namespace DiskPilotX.Core.Tests;

public class ScannerExecutionTests
{
    [Fact]
    public async Task 普通遍历和快速扫描均从实际引擎分支记录路线()
    {
        var path = Path.Combine(Path.GetTempPath(), "DiskPilotX-ScanExecution-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(path, "本测试文件.txt"), "只用于本次扫描路线测试");
            var direct = await new FileScanner().ScanAsync(path, new Progress<ScanProgress>(), CancellationToken.None);
            direct.Execution!.Method.Should().Be(ScanMethod.DirectoryTraversal);
            direct.Execution.Status.Should().Be(ScanExecutionStatus.Completed);
            var scanner = new IncrementalFileScanner(new FileScanner());
            var first = await scanner.ScanAsync(path, new Progress<ScanProgress>(), CancellationToken.None);
            first.Execution!.Method.Should().Be(ScanMethod.DirectoryTraversal);
            (first.Execution.FallbackReasons ?? []).Should().BeEmpty();
            var previousMethod = first.Execution.Method;
            var second = await scanner.ScanAsync(path, new Progress<ScanProgress>(), CancellationToken.None);
            second.Execution!.Method.Should().BeOneOf(ScanMethod.Mft, ScanMethod.DirectoryTraversal, ScanMethod.UsnIncremental);
            second.Id.Should().NotBe(first.Id);
            first.Execution.Method.Should().Be(previousMethod);
        }
        finally { Directory.Delete(path, recursive: true); }
    }
}
