using System.Windows;
using DiskPilotX.App.Controls;
using DiskPilotX.App.Converters;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Cleanup;
using DiskPilotX.Core.Services.FileTree;
using Microsoft.Extensions.DependencyInjection;

namespace DiskPilotX.App;

public partial class MainWindow
{
    private long _cleanupSettingsRevision;

    internal static bool IsFileDeletionApprovalCurrent(ScanSession approvedSession, ScanSession? currentSession,
        FileEntry entry, bool isScanning, DeletionMode approvedMode, DeletionMode currentMode,
        long approvedRevision, long currentRevision) =>
        ReferenceEquals(approvedSession, currentSession) && !isScanning && !entry.IsDeleted
        && approvedMode == currentMode && approvedRevision == currentRevision;

    private async Task DeleteEntryAsync(FileEntry entry)
    {
        if (entry.IsDeleted || _cleanupRunning || DataContext is not MainViewModel main || main.IsScanning
            || main.IsCopilotBusy || main.CurrentSession is not { } session) return;
        if (!ReferenceEquals(new FileTreeQueryService().FindByPath(session, entry.Path), entry)) return;
        _cleanupRunning = true;
        main.IsCopilotBusy = true;
        var results = new List<CleanupAction>();
        try
        {
            var mode = CurrentDeletionMode;
            var revision = Interlocked.Read(ref _cleanupSettingsRevision);
            void EnsureApprovalCurrent()
            {
                if (!IsFileDeletionApprovalCurrent(session, main.CurrentSession, entry, main.IsScanning, mode,
                        CurrentDeletionMode, revision, Interlocked.Read(ref _cleanupSettingsRevision)))
                    throw new InvalidOperationException("确认期间扫描、删除方式或保护设置已改变，请重新确认。");
            }
            var recommendation = new CleanupRecommendation
            {
                Id = Guid.NewGuid().ToString(), TargetPath = entry.Path,
                Entry = entry, Size = entry.Size, IsAccepted = true
            };
            var plan = await Task.Run(() => CleanupPlan.Capture(session, [recommendation], [recommendation.Id], mode));
            await Task.Run(() => ValidateCleanupItems(plan, session, false));
            if (await ModalHost.ShowCleanupConfirmationAsync("确认删除", plan.Items, mode, "确认删除") != ModalResult.Positive) return;
            EnsureApprovalCurrent();
            await Task.Run(() =>
            {
                ValidateCleanupItems(plan, session, false);
                if (!plan.IsTargetCurrent(plan.Items[0])) throw new InvalidOperationException("确认期间目标内容已改变，请重新确认。");
            });
            // 异步遍历结束后再次核对，不能让复核期间的上下文变化沿用旧授权。
            EnsureApprovalCurrent();
            results = await App.Services!.GetRequiredService<ICleanupService>().ExecuteCleanupAsync(plan.Items.ToList(), mode,
                new Progress<CleanupProgress>(p => main.ScanProgressText = $"正在删除 {p.CurrentItemPath}"), CancellationToken.None);
            ApplyCleanupResults(results, main, session);
            var failure = results.FirstOrDefault(result => result.Result is not (CleanupResult.Success or CleanupResult.AlreadyRemoved));
            if (failure is not null) await ShowAppMessageAsync(failure.FailureReason ?? "删除未完成。", "删除失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "右键删除失败");
            await ShowAppMessageAsync(ex.Message, "删除失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            try { App.Services!.GetRequiredService<DiskPilotX.Core.Services.Assistant.DiskAssistantService>().RecordCleanupUsage(results); }
            catch (Exception ex) { Serilog.Log.Debug(ex, "删除使用记录未保存"); }
            _cleanupRunning = false;
            main.IsCopilotBusy = _chatViewModel?.IsOperationRunning == true;
        }
    }
}
