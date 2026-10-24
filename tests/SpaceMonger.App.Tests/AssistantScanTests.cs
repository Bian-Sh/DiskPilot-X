using FluentAssertions;
using SpaceMonger.App.ViewModels;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Scanning;
namespace SpaceMonger.App.Tests;
public class AssistantScanTests
{
    [Fact]
    public async Task SharedScanReplacesSessionOnlyAfterActualCompletion()
    {
        var released=new TaskCompletionSource<ScanSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        var scanner=new Scanner((_,_,_)=>released.Task);
        var vm=new MainViewModel(scanner){CurrentSession=new ScanSession()};var old=vm.CurrentSession;
        var pending=vm.ScanForAssistantAsync(Path.GetTempPath(),CancellationToken.None);
        vm.IsScanning.Should().BeTrue();vm.CurrentSession.Should().BeSameAs(old);
        var scan=new ScanSession{TargetPath=Path.GetTempPath(),RootEntry=new(){Path=Path.GetTempPath(),IsDirectory=true}};
        released.SetResult(scan);(await pending).Should().BeSameAs(scan);vm.IsScanning.Should().BeFalse();
    }
    [Fact]
    public async Task CancelledScanCannotReportPreviousSessionAsNewSuccess()
    {
        var scanner=new Scanner(async(_,_,ct)=>{await Task.Delay(Timeout.Infinite,ct);return new();});
        var vm=new MainViewModel(scanner){CurrentSession=new ScanSession()};var old=vm.CurrentSession;
        using var cts=new CancellationTokenSource();var scan=vm.ScanForAssistantAsync(Path.GetTempPath(),cts.Token);cts.Cancel();
        Func<Task> action=async()=>await scan;await action.Should().ThrowAsync<OperationCanceledException>();
        vm.CurrentSession.Should().BeSameAs(old);vm.IsScanning.Should().BeFalse();
    }
    [Fact]
    public void CopilotBusyDisablesManualScanCommand()
    {
        var vm=new MainViewModel(new Scanner((_,_,_)=>Task.FromResult(new ScanSession()))){SelectedPath=Path.GetTempPath()};
        vm.ScanCommand.CanExecute(null).Should().BeTrue();vm.IsCopilotBusy=true;vm.ScanCommand.CanExecute(null).Should().BeFalse();
    }
    private sealed class Scanner(Func<string,IProgress<ScanProgress>,CancellationToken,Task<ScanSession>> scan):IFileScanner
    {
        public bool IsReady=>true;public event Action? IsReadyChanged{add{}remove{}}
        public Task<ScanSession> ScanAsync(string path,IProgress<ScanProgress> progress,CancellationToken ct)=>scan(path,progress,ct);
    }
}
