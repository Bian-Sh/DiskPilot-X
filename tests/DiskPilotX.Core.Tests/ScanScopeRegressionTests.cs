using System.Reflection;
using System.Collections;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Scanning;
using FluentAssertions;

namespace DiskPilotX.Core.Tests;

public class ScanScopeRegressionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 子目录首次扫描只遍历目标且不伪造MFT回退(bool trailingSeparator)
    {
        var directory = Path.Combine(Path.GetTempPath(), "DiskPilotX-扫描范围-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "子目录"));
        var scanner = new IncrementalFileScanner(new FileScanner());
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(directory, "文件.bin"), new byte[17]);
            await File.WriteAllBytesAsync(Path.Combine(directory, "子目录", "文件.bin"), new byte[23]);
            var reports = new List<ScanProgress>();
            var target = trailingSeparator ? directory + Path.DirectorySeparatorChar : directory;
            var result = await scanner.ScanAsync(target, new ImmediateProgress(reports.Add), CancellationToken.None);

            result.Execution!.Method.Should().Be(ScanMethod.DirectoryTraversal);
            (result.Execution.FallbackReasons ?? []).Should().BeEmpty("子目录主动选择遍历，不是 MFT 失败回退");
            reports.Should().NotContain(p => p.Execution != null && p.Execution.Method == ScanMethod.Mft);
            result.TotalFiles.Should().Be(2);
            result.TotalFolders.Should().Be(2, "保持包含目标根目录的口径");
            result.TotalSize.Should().Be(40);
            result.RootEntry!.Children.Should().HaveCount(2);

            var second = await scanner.ScanAsync(target, new ImmediateProgress(_ => { }), CancellationToken.None);
            second.Execution!.Method.Should().BeOneOf(ScanMethod.UsnIncremental, ScanMethod.DirectoryTraversal);
            second.TotalFiles.Should().Be(2);
            second.TotalSize.Should().Be(40);
            second.Id.Should().NotBe(result.Id);
        }
        finally
        {
            for (var i = 0; !scanner.IsReady && i < 500; i++) await Task.Delay(10);
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData(@"D:\", true)]
    [InlineData(@"D:/", true)]
    [InlineData(@"D:\目录\..\", true)]
    [InlineData(@"D:\目录", false)]
    [InlineData(@"D:\目录\", false)]
    public void 卷根与子目录判定不因分隔符而混淆(string target, bool expected)
    {
        var method = typeof(IncrementalFileScanner).GetMethod("IsWholeVolumeRoot", BindingFlags.NonPublic | BindingFlags.Static)!;
        method.Invoke(null, [target]).Should().Be(expected);
    }

    [Fact]
    public async Task 已取消的子目录扫描不产生成功结果()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var scanner = new IncrementalFileScanner(new FileScanner());
        var action = async () => await scanner.ScanAsync(Path.GetTempPath(), new ImmediateProgress(_ => { }), cancellation.Token);
        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task 卷根保留MFT路线并能在读取前取消()
    {
        using var cancellation = new CancellationTokenSource();
        var reports = new List<ScanProgress>();
        var scanner = new IncrementalFileScanner(new FileScanner());
        var progress = new ImmediateProgress(p =>
        {
            reports.Add(p);
            if (p.Execution?.Method == ScanMethod.Mft) cancellation.Cancel();
        });
        var action = async () => await scanner.ScanAsync(Path.GetPathRoot(Path.GetTempPath())!, progress, cancellation.Token);
        await action.Should().ThrowAsync<OperationCanceledException>();
        reports.Should().Contain(p => p.Execution != null && p.Execution.Method == ScanMethod.Mft);
        reports.Should().NotContain(p => p.Execution != null && p.Execution.Method == ScanMethod.DirectoryTraversal);
    }

    [Fact]
    public async Task 已有同路径水位时仍优先选择USN增量路线()
    {
        var target = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        var scanner = new IncrementalFileScanner(new FileScanner());
        var stateType = typeof(IncrementalFileScanner).GetNestedType("VolumeScanState", BindingFlags.NonPublic)!;
        var state = Activator.CreateInstance(stateType, nonPublic: true)!;
        stateType.GetProperty("ScannedPath")!.SetValue(state, Path.GetFullPath(target));
        stateType.GetProperty("Watermark")!.SetValue(state, new UsnWatermark(0, 0, Path.GetPathRoot(target)!));
        var states = (IDictionary)typeof(IncrementalFileScanner).GetField("_volumeStates", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(scanner)!;
        states[Path.GetPathRoot(target)!] = state;
        using var cancellation = new CancellationTokenSource();
        var reports = new List<ScanProgress>();
        var progress = new ImmediateProgress(p =>
        {
            reports.Add(p);
            if (p.Execution?.Method == ScanMethod.UsnIncremental) cancellation.Cancel();
        });
        var action = async () => await scanner.ScanAsync(target, progress, cancellation.Token);
        await action.Should().ThrowAsync<OperationCanceledException>();
        reports.Should().Contain(p => p.Execution != null && p.Execution.Method == ScanMethod.UsnIncremental);
        reports.Should().NotContain(p => p.Execution != null && p.Execution.Method == ScanMethod.Mft);
    }

    [Fact]
    public void 大小采集阶段只从传入目标树计算进度数量()
    {
        var directory = Path.Combine(Path.GetTempPath(), "DiskPilotX-大小进度-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "目标.bin"), new byte[73]);
            var tree = new FileEntry { Name = "目标", Path = directory, IsDirectory = true };
            tree.Children.Add(new FileEntry { Name = "目标.bin", Path = Path.Combine(directory, "目标.bin") });
            var reports = new List<ScanProgress>();
            var collector = typeof(IncrementalFileScanner).Assembly.GetType("DiskPilotX.Core.Services.Scanning.ParallelSizeCollector")!;
            var method = collector.GetMethod("CollectSizes", BindingFlags.NonPublic | BindingFlags.Static)!;
            var counts = ((int files, int folders))method.Invoke(null, [tree, new ImmediateProgress(reports.Add), CancellationToken.None])!;
            counts.Should().Be((1, 1));
            reports.Should().NotBeEmpty().And.OnlyContain(p => p.FileCount == 1 && p.FolderCount == 1 && !p.IsVolumeIndex);
            tree.Size.Should().Be(73);
        }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class ImmediateProgress(Action<ScanProgress> report) : IProgress<ScanProgress>
    {
        public void Report(ScanProgress value) => report(value);
    }
}
