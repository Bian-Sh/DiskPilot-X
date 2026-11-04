using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using DiskPilotX.App.Services;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Settings;

namespace DiskPilotX.App;

public partial class MainWindow
{
    private readonly Dictionary<string, ScanSession> _publishedScopes = new(StringComparer.OrdinalIgnoreCase);
    private string? _publishedScopeStamp;
    private Guid _publishedCompositeId;
    private string? _publishPolicyContent;
    private long _publishPolicyRevision;
    private long _recommendationRevision;
    private (string Privacy,string Scan,string Protection,string Mode)? _policyParts;
    private string _lastPolicyChange="policy_changed";
    private string PublishPolicyStamp()
    {
        var settings=App.Services!.GetRequiredService<ISettingsService>().LoadSettings();
        var parts=(Privacy:JsonSerializer.Serialize(settings.AiConversationWhitelist),Scan:JsonSerializer.Serialize(settings.ScanWhitelist),
            Protection:JsonSerializer.Serialize(settings.CleanupRecommendationWhitelist),Mode:CurrentDeletionMode.ToString());
        if(_policyParts is {} old) _lastPolicyChange=parts.Privacy!=old.Privacy?"privacy_changed":parts.Scan!=old.Scan?"scan_policy_changed":parts.Protection!=old.Protection?"protection_changed":parts.Mode!=old.Mode?"deletion_mode_changed":_lastPolicyChange;
        _policyParts=parts;
        var content=JsonSerializer.Serialize(new { settings.ScanWhitelist, settings.AiConversationWhitelist, settings.CleanupRecommendationWhitelist, mode=CurrentDeletionMode });
        if(_publishPolicyContent!=content) { _publishPolicyContent=content; _publishPolicyRevision++; }
        return _publishPolicyRevision+"|"+content;
    }
    private string RecommendationStamp() => _recommendationRevision+"|"+JsonSerializer.Serialize(_recommendationsViewModel!.Recommendations.Select(r=>new { r.Id,r.TargetPath,r.Size,r.IsAccepted }));
    private async Task<JsonNode?> ValidatePublishBoundaryAsync(JsonNode? args, bool merge, CancellationToken ct)
    {
        var policy=PublishPolicyStamp(); var list=RecommendationStamp(); var batch=_analysisBatch;
        var preferenceScopes=(batch?.Scopes.Select(scope=>scope.Path)??new[]{(DataContext as MainViewModel)?.CurrentSession?.TargetPath??""}).ToArray();
        var preferences=App.Services!.GetRequiredService<Core.Services.Analysis.AnalysisContextStore>();
        string PreferenceStamp()=>JsonSerializer.Serialize(preferenceScopes.Select(path=>preferences.GetPreferences(path)));
        var preferenceStamp=PreferenceStamp();
        var workspace=App.Services!.GetRequiredService<AssistantWorkspace>();
        if (!workspace.ValidateAnalysisCommit(args)) return CommitBoundaryError(workspace.DescribeAnalysisCommitFailure(args)??"conditions_changed");
        if (!merge && _recommendationsViewModel!.HasAnyRecommendations)
        {
            if (_chatViewModel is null || !await _chatViewModel.ConfirmAsync("替换当前清理建议", "将用本轮通过校验的建议替换当前列表。", "此操作不会删除文件。", ct))
                return new JsonObject { ["ok"]=false,["cancelled"]=true };
            if(batch!=_analysisBatch)return CommitBoundaryError("batch_changed");
            if(list!=RecommendationStamp())return CommitBoundaryError("recommendations_changed");
            if(preferenceStamp!=PreferenceStamp())return CommitBoundaryError("preferences_changed");
            if(policy!=PublishPolicyStamp())return CommitBoundaryError(_lastPolicyChange);
        }
        ct.ThrowIfCancellationRequested();
        if ((DataContext as MainViewModel)?.IsScanning==true || _cleanupRunning)return CommitBoundaryError("scan_busy");
        if(!workspace.ValidateAnalysisCommit(args))return CommitBoundaryError(workspace.DescribeAnalysisCommitFailure(args)??"conditions_changed");
        if (_analysisBatch is { } current && current.Scopes.Any(scope=>!IsSkillScopeAllowed(scope.Path)))
            return AnalysisBatchError("privacy_changed", "批次中存在当前不允许的范围，原清单保留，请缩小范围并重新扫描。");
        return null;
    }
    private List<CleanupRecommendation> MergePublishedRecommendations(IEnumerable<CleanupRecommendation> incoming)
    {
        var previous=_recommendationsViewModel!.Recommendations.ToDictionary(r=>Path.GetFullPath(r.TargetPath),StringComparer.OrdinalIgnoreCase);
        foreach(var item in incoming)
        {
            var path=Path.GetFullPath(item.TargetPath);
            if(previous.TryGetValue(path,out var old)) { item.IsAccepted=old.IsAccepted; item.IsDismissed=old.IsDismissed; }
            previous[path]=item;
        }
        return DeduplicateByPath(previous.Values.ToList());
    }
    private ScanSession RegisterRecommendationScopes(IEnumerable<ScanSession> sessions,bool merge)
    {
        var admitted=sessions.ToArray();
        // 先统一准入；失败不能先替换用户清单或撤销旧证据。
        foreach(var session in admitted)
        {
            _scanSnapshots.Add(session);
            if((DataContext as MainViewModel)?.CurrentSession?.Id==session.Id) _scanSnapshots.Protect(session.Id,"view");
        }
        if(!merge) { _publishedScopes.Clear(); _scanSnapshots.ReleaseOwner("recommendations"); }
        foreach(var session in admitted)
        {
            if(_publishedScopes.TryGetValue(session.TargetPath,out var old)) _scanSnapshots.Unprotect(old.Id,"recommendations");
            _publishedScopes[session.TargetPath]=session; _scanSnapshots.Protect(session.Id,"recommendations");
        }
        var combined=new AssistantAnalysisBatch();
        foreach(var session in _publishedScopes.Values) combined.AddOrReplace(new() {Path=session.TargetPath,Session=session,Label=ScopeLabel(session.TargetPath)});
        var stamp=string.Join("|",_publishedScopes.OrderBy(pair=>pair.Key,StringComparer.OrdinalIgnoreCase).Select(pair=>pair.Value.Id.ToString()));
        if(_publishedScopeStamp!=stamp) { _publishedScopeStamp=stamp; _publishedCompositeId=Guid.NewGuid(); }
        return combined.Scopes.Count==1?combined.Scopes[0].Session:combined.ToSession(_publishedCompositeId);
    }
    private JsonNode CommitBoundaryError(string code)
    {
        var message=code switch {"rules_changed"=>"本次读取的规则已变化。","preferences_changed"=>"当前范围的偏好已变化。",
            "privacy_changed"=>"隐私允许范围已变化。","scan_policy_changed"=>"扫描排除条件已变化。","protection_changed"=>"清理保护范围已变化。",
            "deletion_mode_changed"=>"删除方式已变化。","task_expired"=>"当前分析任务标识已失效。","scan_changed"=>"扫描版本已变化。",
            "batch_changed"=>"当前会话的分析批次已变化。","recommendations_changed"=>"当前推荐清单或选择已变化。","scan_busy"=>"扫描或磁盘操作正在进行。",_=>"当前分析条件已变化。"};
        var error=(JsonObject)AnalysisBatchError(code,message+"本次未覆盖当前清单。");
        error["next_step"]=code=="rules_changed"?"请重新读取相关规则并重新分析后再发布。":code=="preferences_changed"?"请读取当前偏好并重新评估候选后再发布。":code=="recommendations_changed"?"请重新读取清单及版本，再确认需要的操作。":"请重新读取上下文，按当前允许范围取证并重新确认；不要直接重复旧请求。";
        return error;
    }
    private JsonNode RejectedPublish(AnalysisDiagnostics diagnostics)
    {
        var reason=diagnostics.HostFilteredCount>0?"候选被当前隐私或范围限制过滤，请缩小到允许范围。":diagnostics.MissingEntryCount>0?"候选缺少扫描证据，请扫描缺失的确切范围。":diagnostics.ProtectedFilteredCount>0?"候选被本地保护规则排除，不能通过重试绕过。":diagnostics.InputRecommendationCount==0?"未提交有效候选。":"候选未通过校验。";
        var error=(JsonObject)AnalysisBatchError("no_accepted_candidates",reason+"原清单保留，不要重复提交相同列表。");
        error["validation"]=JsonSerializer.SerializeToNode(new {input_count=diagnostics.InputRecommendationCount,accepted_count=0,
            not_scanned=diagnostics.MissingEntryCount,protected_count=diagnostics.ProtectedFilteredCount,
            excluded_count=diagnostics.ExcludedFilteredCount,host_filtered=diagnostics.HostFilteredCount});
        return error;
    }
    private JsonNode AnalysisBatchError(string code,string message)
    {
        var visible=_analysisBatch?.Scopes.Where(scope=>IsSkillScopeAllowed(scope.Path)).ToArray()??[];
        return new JsonObject { ["ok"]=false,["code"]=code,["error"]=message,["accepted_count"]=0,
            ["next_step"]="请使用当前允许标识按范围读取证据，或重新扫描需要的范围。",
            ["current_batch_id"]=visible.Length>0?_analysisBatch!.Id.ToString():null,["scope_count"]=visible.Length };
    }
}
