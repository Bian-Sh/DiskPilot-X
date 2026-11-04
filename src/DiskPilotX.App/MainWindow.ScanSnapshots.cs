using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Settings;

namespace DiskPilotX.App;

public partial class MainWindow
{
    private ScanSnapshotReference RegisterScanSnapshot(ScanSession session, bool analysis = false)
    {
        var reference = _scanSnapshots.Add(session);
        if((DataContext as MainViewModel)?.CurrentSession?.Id==session.Id) _scanSnapshots.Protect(reference.Id,"view");
        if (analysis) _scanSnapshots.Protect(reference.Id, "analysis");
        _chatViewModel?.AddScanSnapshot(reference);
        RefreshScanSnapshotEntries();
        return reference;
    }

    private void RefreshScanSnapshotEntries()
    {
        if (_chatViewModel is null) return;
        foreach (var reference in _chatViewModel.Messages.SelectMany(message => message.ScanSnapshots))
        {
            if (!IsSkillScopeAllowed(reference.Path)) { reference.SetState("restricted"); continue; }
            if (reference.IsPending || reference.State is "failed" or "cancelled") continue;
            var state = !IsSkillScopeAllowed(reference.Path) ? "restricted"
                : !Directory.Exists(reference.Path) ? "missing"
                : _cleanupRunning || _recommendationPublishing || _chatViewModel.IsOperationRunning || (DataContext as MainViewModel)?.IsScanning == true ? "busy"
                : reference.CacheEpoch != _scanSnapshots.Epoch ? "released"
                : _scanSnapshots.TryGet(reference, out _) ? "available" : "evicted";
            reference.SetState(state);
        }
    }

    private void ResetAssistantBatch()
    {
        _analysisBatch = null;
        _scanSnapshots.ReleaseOwner("analysis");
        RefreshScanSnapshotEntries();
    }

    private async Task OpenScanSnapshotAsync(ScanSnapshotReference reference)
    {
        RefreshScanSnapshotEntries();
        if (!reference.CanAct || _chatViewModel is null || DataContext is not MainViewModel main) return;
        if (reference.State == "available" && _scanSnapshots.TryGet(reference, out var session) && session?.RootEntry is not null)
        {
            _scanSnapshots.ReleaseOwner("view"); _scanSnapshots.Protect(reference.Id, "view");
            main.CurrentSession = session;
            main.UpdateStatusBar(session);
            _treemapViewModel?.SetRoot(session.RootEntry, session);
            if (TreeViewControl.DataContext is TreeViewModel tree) tree.SetRoot(session.RootEntry, session);
            // 查看只改变工作区事实，不恢复历史对话上下文或清理许可。
            _chatViewModel.SetContext(session, session.RootEntry);
            _suppressSelectedPathNavigation = true;
            try { main.SelectedPath = session.TargetPath; } finally { _suppressSelectedPathNavigation = false; }
            main.SetRawScanTitle($"扫描快照 · {reference.ScannedAt.LocalDateTime:MM-dd HH:mm:ss}");
            return;
        }
        using var cancellation = new CancellationTokenSource();
        var conversationId=_chatViewModel.CurrentConversationId;
        _chatViewModel.IsOperationRunning=true;
        try
        {
            if (!await main.ConfirmAssistantScanAsync(reference.Path, DiskPilotX.Core.Services.Scanning.ProcessPrivileges.IsElevated,
                (title, detail, impact, ct) => _chatViewModel.ConfirmAsync(title, detail, impact, ct), cancellation.Token, replacesWorkspace: false)) return;
            using var activity = main.BeginExternalScan("重新扫描历史范围", reference.Path, cancellation.Cancel);
            var scanned = await main.ScanHeadlessAsync(reference.Path, cancellation.Token);
            if (!IsSkillScopeAllowed(reference.Path)) throw new InvalidOperationException("扫描期间安全设置变化，结果未登记。");
            if(_chatViewModel.CurrentConversationId!=conversationId) throw new InvalidOperationException("会话已切换，扫描结果未附加到其他会话。");
            RegisterScanSnapshot(scanned);
        }
        catch (OperationCanceledException) { _chatViewModel.ErrorMessage = "重新扫描已停止，原历史记录保留。"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or TimeoutException)
        { _chatViewModel.ErrorMessage = "重新扫描未完成：" + ex.Message; }
        finally { _chatViewModel.IsOperationRunning=false; RefreshScanSnapshotEntries(); }
    }
}
