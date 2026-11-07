using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Models;

namespace DiskPilotX.App;

public partial class MainWindow
{
    private Task AddManualRecommendationsAsync(IReadOnlyList<FileEntry> entries)
    {
        if (TryShowBusyOperationToast("加入推荐清理")) return Task.CompletedTask;
        if (_recommendationPublishing)
        {
            ShowOperationToast("推荐清单正在更新，请等本次更新结束后再加入。");
            return Task.CompletedTask;
        }
        if (DataContext is not MainViewModel { CurrentSession: { } session } || _recommendationsViewModel is null)
        {
            ShowOperationToast("还没有可用的扫描结果，请先扫描文件后再加入推荐清理。");
            return Task.CompletedTask;
        }

        try
        {
            var additions = _recommendationsViewModel.PrepareManualRecommendations(session, entries);
            if (additions.Count == 0)
            {
                ShowOperationToast("这些项目或其父子目录已在推荐清理中，未重复加入。");
                return Task.CompletedTask;
            }
            ValidateManualRecommendationScopes(session, _publishedScopes.Values);
            // 登记及保护成功后才追加；清理继续使用原有 CleanupPlan、来源会话和安全复核。
            _assistantAnalysisSession = RegisterRecommendationScopes([session], merge: true);
            var count = _recommendationsViewModel.AppendManualRecommendations(additions);
            RefreshScanSnapshotEntries();
            EnsureBottomPanelVisible();
            ShowRecommendationsPanel();
            ShowOperationToast($"已手动加入 {count} 项推荐清理，尚未勾选；请核对后确认。");
        }
        catch (InvalidOperationException ex)
        {
            ShowOperationToast(ex.Message);
        }
        return Task.CompletedTask;
    }

    /// <summary>只在清单实际提交后裁剪无引用范围，保留其他 owner 及仍被建议使用的扫描版本。</summary>
    private ScanSession? RebuildReferencedRecommendationSession()
    {
        if (_recommendationsViewModel is null) return _assistantAnalysisSession;
        var items = _recommendationsViewModel.Recommendations.ToArray();
        var sessions = _publishedScopes.Values.ToArray();
        var referenced = new HashSet<Guid>();
        var query = new DiskPilotX.Core.Services.FileTree.FileTreeQueryService();
        foreach (var item in items)
        {
            if (item.SourceScanId is { } id)
            {
                referenced.Add(id);
                continue;
            }
            // 旧建议没有扫描标识时优先认真实实体；实体缺失或已脱离来源时保守保留路径所属范围。
            var sources = sessions.Where(session => item.Entry is not null
                && ReferenceEquals(query.FindByPath(session, item.TargetPath), item.Entry)).ToArray();
            if (sources.Length == 0)
                sources = sessions.Where(session => IsPathUnder(item.TargetPath, session.TargetPath)).ToArray();
            foreach (var source in sources) referenced.Add(source.Id);
        }
        var unused = _publishedScopes.Where(pair => !referenced.Contains(pair.Value.Id)).ToArray();
        foreach (var pair in unused)
        {
            _publishedScopes.Remove(pair.Key);
            // 仅释放推荐清单自己的保护；不释放 view / analysis，也不删除内存快照或真实文件。
            _scanSnapshots.Unprotect(pair.Value.Id, "recommendations");
        }
        if (unused.Length > 0) _recommendationRevision++;
        var session = RebuildPublishedRecommendationSession();
        RefreshScanSnapshotEntries();
        return session;
    }

    internal static void ValidateManualRecommendationScopes(ScanSession session, IEnumerable<ScanSession> published)
    {
        // 同一路径的新扫描不能替换旧建议的证据；重叠根会让 CleanupPlan 按路径取到另一版本。
        if (published.Any(old => !ReferenceEquals(old, session)
            && (IsPathUnder(session.TargetPath, old.TargetPath) || IsPathUnder(old.TargetPath, session.TargetPath))))
            throw new InvalidOperationException("推荐中仍有该范围的另一份扫描结果。请先移除旧范围的建议，再从当前扫描加入；原清单和勾选已保留。");
    }
}
