using FluentAssertions;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Scanning;

namespace DiskPilotX.App.Tests;

public class ScanExecutionObservabilityTests
{
    [Fact]
    public async Task 非管理员扫描前提醒且拒绝不会覆盖旧结果或启动扫描()
    {
        var calls = 0;
        var vm = new MainViewModel(new Scanner((_, _, _) => { calls++; return Task.FromResult(new ScanSession()); }));
        var old = new ScanSession { Execution = new(Path.GetTempPath(), ScanMethod.Mft, ScanExecutionStatus.Completed) };
        vm.CurrentSession = old;
        var approved = await vm.ConfirmAssistantScanAsync(Path.GetTempPath(), false, (title, description, impact, ct) =>
        { (title + description + impact).Should().Contain("管理员").And.Contain("MFT").And.Contain("普通遍历"); return Task.FromResult(false); }, CancellationToken.None);
        approved.Should().BeFalse(); calls.Should().Be(0);
        vm.CurrentSession.Should().BeSameAs(old);
        vm.LastScanExecution!.Status.Should().Be(ScanExecutionStatus.CancelledBeforeStart);
        vm.LastScanExecution.Method.Should().Be(ScanMethod.Unknown);
        vm.LastScanExecution.StartedAt.Should().BeNull();
    }

    [Fact]
    public async Task 中途取消保留实际进入的扫描路线而不把旧结果当作新成功()
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scanner = new Scanner(async (path, progress, ct) =>
        {
            progress.Report(new("MFT", 0, 0) { Execution = new(path, ScanMethod.Mft, ScanExecutionStatus.Running, DateTime.Now) });
            reached.SetResult(); await Task.Delay(Timeout.Infinite, ct); return new();
        });
        var vm = new MainViewModel(scanner) { CurrentSession = new ScanSession() };
        var old = vm.CurrentSession;
        var run = vm.ScanForAssistantAsync(Path.GetTempPath(), CancellationToken.None);
        await reached.Task; vm.CancelScanCommand.Execute(null);
        await Assert.ThrowsAsync<DiskPilotX.Core.Services.Assistant.OperationCancelledByUserException>(() => run);
        vm.LastScanExecution!.Method.Should().Be(ScanMethod.Mft);
        vm.LastScanExecution.Status.Should().Be(ScanExecutionStatus.Cancelled);
        vm.CurrentSession.Should().BeSameAs(old);
    }

    [Fact]
    public async Task 管理员无已有扫描时不要求额外确认()
    {
        var vm = new MainViewModel(new Scanner((_, _, _) => Task.FromResult(new ScanSession())));
        var approved = await vm.ConfirmAssistantScanAsync(Path.GetTempPath(), true, (_, _, _, _) => throw new InvalidOperationException("不应弹窗"), CancellationToken.None);
        approved.Should().BeTrue();
    }

    [Fact]
    public async Task 停止确认等待也记为扫描前取消且不替换已有结果()
    {
        var vm = new MainViewModel(new Scanner((_, _, _) => throw new InvalidOperationException("不得扫描"))) { CurrentSession = new ScanSession() };
        var old = vm.CurrentSession;
        await Assert.ThrowsAsync<OperationCanceledException>(() => vm.ConfirmAssistantScanAsync(Path.GetTempPath(), false,
            (_, _, _, _) => throw new OperationCanceledException(), CancellationToken.None));
        vm.LastScanExecution!.Status.Should().Be(ScanExecutionStatus.CancelledBeforeStart);
        vm.CurrentSession.Should().BeSameAs(old);
    }

    [Fact]
    public async Task 无头扫描同样记录路线但不会替换主视图()
    {
        var path = Path.GetTempPath();
        var scanner = new Scanner((target, progress, ct) => Task.FromResult(new ScanSession
        { TargetPath = target, RootEntry = new() { Path = target, IsDirectory = true }, Execution = new(target, ScanMethod.DirectoryTraversal, ScanExecutionStatus.Completed) }));
        var vm = new MainViewModel(scanner) { CurrentSession = new ScanSession() };
        var old = vm.CurrentSession;
        (await vm.ConfirmAssistantScanAsync(path, false, (_, _, impact, _) =>
        { impact.Should().Contain("不替换"); return Task.FromResult(true); }, CancellationToken.None, replacesWorkspace: false)).Should().BeTrue();
        await vm.ScanHeadlessAsync(path, CancellationToken.None);
        vm.LastScanExecution!.Method.Should().Be(ScanMethod.DirectoryTraversal);
        vm.LastScanExecution.Status.Should().Be(ScanExecutionStatus.Completed);
        vm.CurrentSession.Should().BeSameAs(old);
    }

    [Fact]
    public async Task 无头扫描完成后取消新请求仍保留最近完成路线()
    {
        var path = Path.GetTempPath();
        var scanner = new Scanner((target, _, _) => Task.FromResult(new ScanSession
        { TargetPath = target, RootEntry = new() { Path = target, IsDirectory = true }, Execution = new(target, ScanMethod.DirectoryTraversal, ScanExecutionStatus.Completed) }));
        var vm = new MainViewModel(scanner) { CurrentSession = new ScanSession { Execution = new("旧主视图", ScanMethod.Mft, ScanExecutionStatus.Completed) } };
        var completed = await vm.ScanHeadlessAsync(path, CancellationToken.None);
        await vm.ConfirmAssistantScanAsync(path, false, (_, _, _, _) => Task.FromResult(false), CancellationToken.None, replacesWorkspace: false);
        vm.LastScanExecution!.Status.Should().Be(ScanExecutionStatus.CancelledBeforeStart);
        vm.LastCompletedScanExecution.Should().BeSameAs(completed.Execution);
        vm.LastCompletedScanExecution!.Method.Should().Be(ScanMethod.DirectoryTraversal);
        vm.CurrentSession.Execution!.Method.Should().Be(ScanMethod.Mft);
    }

    private sealed class Scanner(Func<string, IProgress<ScanProgress>, CancellationToken, Task<ScanSession>> scan) : IFileScanner
    {
        public bool IsReady => true;
        public event Action? IsReadyChanged { add { } remove { } }
        public Task<ScanSession> ScanAsync(string path, IProgress<ScanProgress> progress, CancellationToken ct) => scan(path, progress, ct);
    }
}
