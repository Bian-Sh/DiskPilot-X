using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using DiskPilotX.App.Services;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Analysis;
using DiskPilotX.Core.Services.FileTree;
using DiskPilotX.Core.Services.Settings;

namespace DiskPilotX.App;

public partial class MainWindow
{
    private bool _recommendationPublishing;
    private readonly Dictionary<string,double> _publishTimings = [];
    private async Task<JsonNode> PublishRecommendationsAsync(JsonNode? args, CancellationToken ct, MainViewModel main, AppSettings settings)
    {
        if (_recommendationPublishing) return new JsonObject { ["ok"]=false, ["code"]="publish_busy", ["error"]="上一批推荐尚未提交完成，请等待回执。" };
        _recommendationPublishing = true;
        _publishTimings.Clear();
        var timing = System.Diagnostics.Stopwatch.StartNew();
        void Mark(string stage) { _publishTimings[stage] = timing.Elapsed.TotalMilliseconds; timing.Restart(); }
        try
        {

                if(main.IsScanning || _cleanupRunning) return CommitBoundaryError("scan_busy");
                if (args?["recommendations"] is not JsonArray candidates || candidates.Count > 100)
                    return new JsonObject { ["ok"]=false, ["code"]="batch_too_large", ["error"]="每批最多100项，请分批发布；本次未修改清单。", ["limits"]=ScanBudgetData() };
                var initialPolicy = PublishPolicyStamp();
                var initialList = RecommendationStamp();
                var initialBatch = _analysisBatch;
                var initialSession = main.CurrentSession?.Id;
                string Preferences() => JsonSerializer.Serialize((_analysisBatch?.Scopes.Select(scope=>scope.Path)
                    ?? new[]{main.CurrentSession?.TargetPath??""}).Select(path=>App.Services!.GetRequiredService<AnalysisContextStore>().GetPreferences(path)));
                var initialPreferences = Preferences(); Mark("capture");
                JsonNode? Changed() => initialBatch != _analysisBatch ? CommitBoundaryError("batch_changed")
                    : initialSession != main.CurrentSession?.Id ? CommitBoundaryError("scan_changed")
                    : initialPolicy != PublishPolicyStamp() ? CommitBoundaryError(_lastPolicyChange)
                    : initialList != RecommendationStamp() ? CommitBoundaryError("recommendations_changed")
                    : initialPreferences != Preferences() ? CommitBoundaryError("preferences_changed") : null;
                var requestedScanId=args?["scan_id"]?.GetValue<string>();
                var mergeMode=!string.Equals(args?["mode"]?.GetValue<string>(),"replace",StringComparison.OrdinalIgnoreCase);
                var analysisMode=args?["analysis_mode"]?.GetValue<bool>()==true;
                var raw=JsonSerializer.SerializeToElement(args);
                var engine=App.Services!.GetRequiredService<RecommendationEngine>();
                // 命中无头批次：每个范围用自己的会话校验，再合并成一份清单，避免逐个发布互相覆盖。
                var batch=_analysisBatch is { } pending && pending.Id.ToString()==requestedScanId ? pending : null;
                if(batch is not null)
                {
                    if(batch.Scopes.Count==0) return AssistantError("分析批次里没有可发布的范围。");
                    var merged=new List<CleanupRecommendation>();
                    var missing=0;
                    var protectedFiltered=0;
                    foreach(var scope in batch.Scopes)
                    {
                        ct.ThrowIfCancellationRequested();
                        var owned=OwnedCandidates(raw,scope.Root.Path,batch);
                        if (owned.GetProperty("recommendations").GetArrayLength() == 0) continue;
                        var scoped=await Task.Run(() => engine.ValidateCandidates(scope.Session,owned,scope.Root),ct);
                        Mark("validate_background");
                        if (Changed() is { } changedScope) return changedScope;
                        missing+=scoped.Diagnostics.MissingEntryCount;
                        protectedFiltered+=scoped.Diagnostics.ProtectedFilteredCount;
                        foreach(var item in scoped.Recommendations)
                        {
                            item.SourceScope=scope.Label;
                            merged.Add(item);
                        }
                    }
                    merged=DeduplicateByPath(merged);
                    var beforePrivacy=merged.Count;
                    if(!analysisMode)
                        merged=merged.Where(item=>!WouldAffectExcludedPath(item.TargetPath,settings.AiConversationWhitelist)).ToList();
                    var submitted=raw.TryGetProperty("recommendations",out var inputCandidates)&&inputCandidates.ValueKind==JsonValueKind.Array?inputCandidates.GetArrayLength():0;
                    var unowned=inputCandidates.ValueKind==JsonValueKind.Array?inputCandidates.EnumerateArray().Count(item=>!item.TryGetProperty("path",out var path)||path.ValueKind!=JsonValueKind.String||batch.FindScope(path.GetString()!) is null):0;
                    var diagnostics=new AnalysisDiagnostics
                    {
                        TargetPath=batch.Scopes.Count==1?batch.Scopes[0].Path:batch.Note??"多个位置",
                        ScopePath=string.Join(" ; ",batch.Scopes.Select(scope=>scope.Path)),
                        IsFocusedScope=true,
                        ParsedRecommendationCount=merged.Count,
                        InputRecommendationCount=submitted,
                        MissingEntryCount=missing+unowned,
                        ProtectedFilteredCount=protectedFiltered,
                        HostFilteredCount=beforePrivacy-merged.Count
                    };
                    if(merged.Count==0) return RejectedPublish(diagnostics);
                    if (await ValidatePublishBoundaryAsync(args, mergeMode, ct) is { } batchFailure) return batchFailure;
                    if (Changed() is { } changedBatch) return changedBatch;
                    Mark("boundary");
                    var final=mergeMode?MergePublishedRecommendations(merged):merged;
                    RegisterRecommendationScopes(batch.Scopes.Select(scope=>scope.Session),mergeMode);
                    for(var index=0;index<final.Count;index++) final[index].Id=(index+1).ToString();
                    Mark("merge_and_register");
                    _recommendationRevision++;
                    _recommendationsViewModel!.SetExternalRecommendations(final,diagnostics);
                    Mark("apply_cells");
                    _assistantAnalysisSession=RebuildReferencedRecommendationSession()
                        ?? throw new InvalidOperationException("推荐清单缺少可用的扫描来源，请重新扫描后再发布。");
                    EnsureBottomPanelVisible(); ShowRecommendationsPanel();
                    var receipt = RecommendationState(_assistantAnalysisSession,settings,analysisMode,originLabel:batch.Note??(batch.Scopes.Count==1?batch.Scopes[0].Label:$"{batch.Scopes.Count} 个位置"));
                    Mark("receipt");
                    return receipt;
                }
                if(main.CurrentSession?.Id.ToString()!=requestedScanId) return AnalysisBatchError("scan_expired", "扫描标识无效，请使用在册批次或重新扫描。");
                var originalId=main.CurrentSession!.Id;
                if(main.CurrentSession?.Id!=originalId) return CommitBoundaryError("scan_changed");
                var focusPath=args?["focus_path"]?.GetValue<string>();
                var localFocus=new FileTreeQueryService().FindByPath(main.CurrentSession!,focusPath??main.CurrentSession!.RootEntry!.Path);
                if(localFocus is null)return AssistantError("分析范围已失效。");
                var scannedSession = main.CurrentSession!;
                var analysis=await Task.Run(() => engine.ValidateCandidates(scannedSession,raw,localFocus),ct);
                if (Changed() is { } changedScan) return changedScan;
                var beforePrivacyFilter=analysis.Recommendations.Count;
                analysis.Recommendations=analysis.Recommendations.Where(r=>analysisMode||!WouldAffectExcludedPath(r.TargetPath,settings.AiConversationWhitelist)).ToList();
                analysis.Diagnostics.HostFilteredCount+=beforePrivacyFilter-analysis.Recommendations.Count;
                // 确认窗口期间若规则、偏好或隐私条件改变，原列表保留并要求重新分析，避免用旧结论覆盖。
                ct.ThrowIfCancellationRequested();
                if (analysis.Recommendations.Count == 0) return RejectedPublish(analysis.Diagnostics);
                if (await ValidatePublishBoundaryAsync(args, mergeMode, ct) is { } failure) return failure;
                if (main.CurrentSession?.Id != originalId) return CommitBoundaryError("scan_changed");
                if (Changed() is { } changedFinal) return changedFinal;
                var published=mergeMode?MergePublishedRecommendations(analysis.Recommendations):analysis.Recommendations;
                RegisterRecommendationScopes([main.CurrentSession!],mergeMode);
                for(var index=0;index<published.Count;index++) published[index].Id=(index+1).ToString();
                _recommendationRevision++;
                    _recommendationsViewModel!.SetExternalRecommendations(published,analysis.Diagnostics);
                _assistantAnalysisSession=RebuildReferencedRecommendationSession()
                        ?? throw new InvalidOperationException("推荐清单缺少可用的扫描来源，请重新扫描后再发布。");
                EnsureBottomPanelVisible(); ShowRecommendationsPanel();
                return RecommendationState(_assistantAnalysisSession!,settings,analysisMode);
            
        }
        finally { _recommendationPublishing = false; RefreshScanSnapshotEntries(); }
    }

    private JsonNode ScanBudgetData() => JsonSerializer.SerializeToNode(new
    {
        max_paths_per_call=6, max_recommendations_per_call=100, recommended_recommendations_per_call=25,
        scan_concurrency=1, snapshot_budget=_scanSnapshots.GetBudget(),
        on_capacity_exhausted="暂停并说明未完成范围；不得自动删除推荐或释放活动证据。"
    })!;

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
    private List<CleanupRecommendation> MergePublishedRecommendations(IEnumerable<CleanupRecommendation> incoming) =>
        RecommendationEngine.MergeRecommendations(_recommendationsViewModel!.Recommendations, incoming);
    private ScanSession RegisterRecommendationScopes(IEnumerable<ScanSession> sessions,bool merge)
    {
        var admitted=sessions.ToArray();
        // 默认合并保留旧建议；同根新扫描不能先覆盖仍在引用的旧证据，拒绝必须早于任何清单/ID/owner修改。
        if (merge)
            foreach (var session in admitted) ValidateManualRecommendationScopes(session, _publishedScopes.Values);
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
        return RebuildPublishedRecommendationSession()
            ?? throw new InvalidOperationException("没有可用于推荐清理的扫描来源。");
    }
    /// <summary>注册与裁剪共享同一组合版本计算；没有范围时不构造空批次。</summary>
    private ScanSession? RebuildPublishedRecommendationSession()
    {
        var stamp=string.Join("|",_publishedScopes.OrderBy(pair=>pair.Key,StringComparer.OrdinalIgnoreCase).Select(pair=>pair.Value.Id.ToString()));
        if(_publishedScopeStamp!=stamp) { _publishedScopeStamp=stamp; _publishedCompositeId=Guid.NewGuid(); }
        if(_publishedScopes.Count==0) return null;
        var combined=new AssistantAnalysisBatch();
        foreach(var session in _publishedScopes.Values) combined.AddOrReplace(new() {Path=session.TargetPath,Session=session,Label=ScopeLabel(session.TargetPath)});
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
        if (diagnostics.InputRecommendationCount == 0 && diagnostics.ParseError is null)
            return new JsonObject { ["ok"]=true, ["applied"]=false, ["empty_batch"]=true, ["accepted_count"]=0,
                ["message"]="本批没有候选，原推荐清单保持不变；是否完整分析仍须据实报告。" };

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
