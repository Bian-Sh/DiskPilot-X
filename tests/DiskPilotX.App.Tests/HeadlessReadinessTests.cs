using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Scanning;
using FluentAssertions;

namespace DiskPilotX.App.Tests;
public class HeadlessReadinessTests
{
    [Fact]
    public async Task 多范围等待扫描器就绪且取消可中断等待()
    {
        var scanner = new DelayedScanner(); var vm = new MainViewModel(scanner);
        var run = vm.ScanHeadlessAsync(Path.GetTempPath(), CancellationToken.None);
        await Task.Delay(30); scanner.Calls.Should().Be(0);
        scanner.Ready = true;
        await run; scanner.Calls.Should().Be(1);
        scanner.Ready = false; using var cts = new CancellationTokenSource(30);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => vm.ScanHeadlessAsync(Path.GetTempPath(), cts.Token));
        scanner.Calls.Should().Be(1);
    }
    private sealed class DelayedScanner : IFileScanner
    {
        public bool Ready; public int Calls;
        public bool IsReady => Ready;
        public event Action? IsReadyChanged { add { } remove { } }
        public Task<ScanSession> ScanAsync(string path, IProgress<ScanProgress> p, CancellationToken ct)
        { Calls++; return Task.FromResult(new ScanSession { TargetPath = path, RootEntry = new() { Path = path, IsDirectory = true } }); }
    }
}
