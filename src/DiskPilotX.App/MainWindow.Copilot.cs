using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using DiskPilotX.App.Converters;
using DiskPilotX.App.Services;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Agent;
using DiskPilotX.Core.Services.Analysis;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Cleanup;
using DiskPilotX.Core.Services.FileTree;
using DiskPilotX.Core.Services.Scanning;
using DiskPilotX.App.Localization;
using DiskPilotX.Core.Services.Settings;
using DiskPilotX.Core.Services.Whitelist;

namespace DiskPilotX.App;

public partial class MainWindow
{
    private void ConnectAssistantWorkspace()
    {
        var workspace = App.Services!.GetRequiredService<AssistantWorkspace>();
        App.Services!.GetRequiredService<DiskAssistantService>().AnalysisStateChanged += task =>
            _ = Dispatcher.InvokeAsync(() => _recommendationsViewModel?.SetAnalysisTask(task));
        workspace.IsAnalysisBatch = id => _analysisBatch is { Scopes.Count: > 0 } batch && batch.Id == id;
        workspace.ReadContext = ct => Dispatcher.InvokeAsync(() =>
        {
            ct.ThrowIfCancellationRequested();
            var main = DataContext as MainViewModel;
            var multiSelection = _treemapViewModel?.MultiSelection
                ?.Select(n => n.Entry.Path).ToList();
            return new AgentContext(main?.CurrentSession, _treemapViewModel?.CurrentRoot,
                _chatViewModel?.LinkedEntry ?? _treemapViewModel?.SelectedNode?.Entry, _chatViewModel?.LinkedRecommendation,
                _recommendationsViewModel?.HasAnyRecommendations == true,
                IsElevated: ProcessPrivileges.IsElevated, LastScanExecution: main?.LastScanExecution,
                LastCompletedScanExecution: main?.LastCompletedScanExecution, MultiSelection: multiSelection);
        }).Task;
        workspace.Execute = (operation, args, ct) => Dispatcher.InvokeAsync(() => ExecuteAssistantOperationAsync(operation,args,ct)).Task.Unwrap();
        if (_chatViewModel is not null)
        {
            _chatViewModel.SkillTaskRequested += OpenSkillTask;
            _chatViewModel.ScanSnapshotAction = OpenScanSnapshotAsync;
            _chatViewModel.ConversationReset += ResetAssistantBatch;
            _chatViewModel.SkillWindowRequested += OpenSkillWindow;
        }
        if (_chatViewModel is not null && DataContext is MainViewModel vm)
            _chatViewModel.PropertyChanged += (_, e) => { if(e.PropertyName == nameof(ChatViewModel.IsOperationRunning)) { vm.IsCopilotBusy=_chatViewModel.IsOperationRunning||_cleanupRunning; RefreshScanSnapshotEntries(); } };
    }

    private async Task<JsonNode> ExecuteAssistantOperationAsync(string operation, JsonNode? args, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (DataContext is not MainViewModel main || _recommendationsViewModel is null) return AssistantError("工作区尚未准备好。");
        var settingsService=App.Services!.GetRequiredService<ISettingsService>();
        var settings=settingsService.LoadSettings();
        var matcher=App.Services!.GetRequiredService<IPathWhitelistMatcher>();
        bool Hidden(string? path)=>matcher.IsExcluded(path,settings.AiConversationWhitelist);
        switch(operation)
        {
            case "set_recommendations_empty_state":
            {
                var applied = _recommendationsViewModel.SetEmptyStatePresentation(args?["title"]?.GetValue<string>(), args?["description"]?.GetValue<string>());
                return new JsonObject { ["ok"] = true, ["applied"] = applied, ["display_only"] = true,
                    ["visible"] = applied && !_recommendationsViewModel.IsAnalyzing && !_recommendationsViewModel.HasAnyRecommendations };
            }
            case "scan_path":
            {
                var target=ScanPathResolver.Resolve(args?["path"]?.GetValue<string>()??"");
                if(string.IsNullOrWhiteSpace(target)||Hidden(target)||matcher.IsExcluded(target,settings.ScanWhitelist)) return AssistantError("扫描路径为空或已排除。");
                if(main.IsScanning || _cleanupRunning || _recommendationPublishing) return AssistantError("当前磁盘操作尚未完成。");
                if (_chatViewModel is null) return AssistantError("扫描确认界面尚未准备好。");
                if (!await main.ConfirmAssistantScanAsync(target, ProcessPrivileges.IsElevated,
                    (title, description, impact, token) => _chatViewModel.ConfirmAsync(title, description, impact, token), ct))
                    return new JsonObject { ["ok"] = false, ["cancelled"] = true, ["started"] = false, ["unexecuted"]=JsonSerializer.SerializeToNode(new[]{target}.Where(IsSkillScopeAllowed)), ["scan_execution"] = JsonSerializer.SerializeToNode(main.LastScanExecution?.ToPublicData()) };
                var session=await main.ScanForAssistantAsync(target,ct);
                session.Provenance = new(args?["source_rule_id"] is null ? "chat" : "rule", args?["conversation_id"]?.GetValue<string>(),
                    args?["message_id"]?.GetValue<string>(), args?["purpose"]?.GetValue<string>() ?? "用户请求扫描指定范围", args?["source_rule_id"]?.GetValue<string>());
                CacheAiScanSession(session);
                // 工作区换了一份新扫描结果，清理基准跟着它走，避免沿用上一次的批次会话。
                _assistantAnalysisSession=session;
                return new JsonObject{["ok"]=true,["scan_id"]=session.Id.ToString(),["path"]=session.TargetPath,["files"]=session.TotalFiles,["folders"]=session.TotalFolders,["scan_execution"]=JsonSerializer.SerializeToNode(session.Execution?.ToPublicData())};
            }
            case "navigate_path":
            {
                var target=args?["path"]?.GetValue<string>();
                if(main.CurrentSession is null || string.IsNullOrWhiteSpace(target)||Hidden(target)
                    ||App.Services!.GetRequiredService<IFileTreeQueryService>().FindByPath(main.CurrentSession,target) is null) return AssistantError("路径未扫描或不允许 AI 访问。");
                NavigateToPathOrSelect(target);
                var view=args?["view"]?.GetValue<string>();
                if(view is not null && !SetAssistantView(view)) return AssistantError("视图只支持 treemap 或 tree。");
                return new JsonObject{["ok"]=true,["path"]=target,["current_path"]=_treemapViewModel?.CurrentRoot?.Path,["view"]=TreeViewControl.Visibility==Visibility.Visible?"tree":"treemap"};
            }
            case "focus_path":
            {
                var target=args?["path"]?.GetValue<string>();
                var highlight=args?["highlight"]?.GetValue<bool>() ?? true;
                if(main.CurrentSession is null || string.IsNullOrWhiteSpace(target)||Hidden(target)
                    ||App.Services!.GetRequiredService<IFileTreeQueryService>().FindByPath(main.CurrentSession,target) is null) return AssistantError("路径未扫描或不允许 AI 访问。");
                SetAssistantView("treemap");
                bool hit = await TreemapView.FocusPathAsync(target, highlight);
                return hit
                    ? new JsonObject{["ok"]=true,["path"]=target,["focus_path"]=_treemapViewModel?.SelectedNode?.Entry.Path ?? target,["current_path"]=_treemapViewModel?.CurrentRoot?.Path}
                    : AssistantError("无法在地图上定位该路径。");
            }
            case "set_view":
                return new JsonObject{["ok"]=SetAssistantView(args?["view"]?.GetValue<string>()),["view"]=TreeViewControl.Visibility==Visibility.Visible?"tree":"treemap"};
            case "describe_analysis_batch":
                return AnalysisBatchError("scan_expired", "扫描标识无效或批次已失效，请使用当前在册批次，或重新扫描所需范围。");
            case "get_analysis_evidence":
            {
                if (_analysisBatch is not { } active || active.Id.ToString() != args?["scan_id"]?.GetValue<string>())
                    return AnalysisBatchError("scan_expired", "批次已失效，请重新扫描范围。");
                var requested=args?["path"]?.GetValue<string>();
                var scope=string.IsNullOrWhiteSpace(requested)?active.Scopes.FirstOrDefault():active.FindScope(requested);
                if (scope is null || !IsSkillScopeAllowed(scope.Path)) return AnalysisBatchError("scope_unavailable", "该范围无有效证据或当前设置不允许访问，请缩小范围或重新扫描。");
                var focus=new FileTreeQueryService().FindByPath(scope.Session,requested??scope.Path);
                if(focus is null) return AnalysisBatchError("not_scanned", "路径不在该范围的扫描树中，请扫描确切目标后再分析。");
                var evidence=App.Services!.GetRequiredService<RecommendationEngine>().BuildEvidence(scope.Session,focus);
                return new JsonObject { ["ok"]=true,["scan_id"]=active.Id.ToString(),["snapshot_id"]=scope.Session.Id.ToString(),["evidence"]=evidence.Length>12000?evidence[..12000]+"\n（证据已截断，请缩小范围继续读取）":evidence };
            }
            case "get_scan_budget":
                return new JsonObject { ["ok"]=true, ["limits"]=ScanBudgetData() };
            case "analyze_paths":
                return await AnalyzePathsAsync(args,ct,main,settings,matcher,Hidden);
            case "publish_recommendations":
                return await PublishRecommendationsAsync(args, ct, main, settings);
            case "list_recommendations":
            {
                // 清单可能建立在无头批次会话上（多范围分析），此时读它而不是工作区会话。
                var source=_assistantAnalysisSession??main.CurrentSession;
                if(source is null) return AssistantError("尚无扫描结果。");
                return RecommendationState(source,settings);
            }
            case "select_recommendations":
            {
                var ids=ReadIds(args);var selected=args?["selected"]?.GetValue<bool>()??true;
                var items=_recommendationsViewModel.Recommendations.Where(r=>ids.Contains(r.Id)&&!Hidden(r.TargetPath)).ToList();
                if(items.Count!=ids.Count)return AssistantError("建议 ID 不存在或已被排除。");
                foreach(var item in items){item.IsAccepted=selected;item.IsDismissed=!selected;}
                _recommendationsViewModel.RefreshFilteredRecommendations();
                var source=_assistantAnalysisSession??main.CurrentSession;
                return source is null?AssistantError("扫描结果失效。"):RecommendationState(source,settings);
            }
            case "cleanup_recommendations":
                return await ExecuteSharedCleanupAsync(ReadIds(args),args?["version"]?.GetValue<string>(),true,ct);
            case "confirm_skill_change":
            {
                if(_chatViewModel is null)return AssistantError("无法显示技能变更确认。");
                var package=args?["package"];
                var skillOperation=args?["operation"]?.GetValue<string>()??"save";
                var skillId=args?["id"]?.GetValue<string>()??package?["id"]?.GetValue<string>()??"";
                var description=skillOperation=="delete"
                    ? $"将从技能库删除规则「{skillId}」。"
                    : package is null ? $"操作：{skillOperation}；技能：{skillId}\n\n{args?["body_markdown"]}" : $"规则：{package["title"]}\n说明：{package["description"]}\n\n规则明细：\n{package["rules"]?.ToJsonString(new(){WriteIndented=true})}\n\n{package["skill_markdown"]}";
                var title=skillOperation switch
                {
                    "delete"=>$"删除规则「{skillId}」？",
                    "update"=>$"更新规则「{skillId}」？",
                    "create"=>$"新建规则「{skillId}」？",
                    _=>$"保存规则「{skillId}」？"
                };
                var impact=skillOperation=="delete"
                    ? "只删除规则文件，不动磁盘上的任何数据；删除后它从对话空态和 @ 菜单消失。内置规则不可删除。"
                    : "保存后可从对话空态和 @ 菜单使用。保存不会清理文件；自定义源码执行还需单独授权。";
                var confirmed=await _chatViewModel.ConfirmAsync(title,description,impact,ct);
                return new JsonObject{["ok"]=confirmed,["cancelled"]=!confirmed};
            }
            case "confirm_analysis_memory":
            case "confirm_analysis_history":
            {
                if (_chatViewModel is null) return AssistantError("无法显示分析上下文确认。");
                var description = args?["description"]?.GetValue<string>() ?? args?.ToJsonString() ?? "";
                var confirmed = await _chatViewModel.ConfirmAsync(operation == "confirm_analysis_history" ? "读取关联会话的这一条消息？" : "更新本地分析偏好或使用记录？", description,
                    "这不会清理文件，也不会继承历史清理授权；你可以拒绝。", ct);
                return new JsonObject { ["ok"] = confirmed, ["cancelled"] = !confirmed };
            }
            case "queue_rule_creation":
            {
                if (_chatViewModel is null || main.CurrentSession?.Id.ToString() != args?["scan_id"]?.GetValue<string>()) return AssistantError("原分析范围已失效，请重新分析。");
                var scope = args?["scope_path"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(scope) || !AnalysisTaskContext.IsWithin(scope, main.CurrentSession!.TargetPath)) return AssistantError("规则创建范围不属于本次扫描。");
                _chatViewModel.PrepareRuleCreationDraft(main.CurrentSession!.Id, scope, args?["reason"]?.GetValue<string>() ?? "补充专用检查规则");
                return new JsonObject { ["ok"] = true, ["queued"] = true, ["created"] = false, ["next"] = "本轮结束后可点击填写规则创建请求，审核输入后由用户发送；规则完成后可回到同一范围继续分析。" };
            }
            case "ask_user":
                if(_chatViewModel is null)return AssistantError("无法显示输入请求。");
                return await _chatViewModel.AskUserAsync(args,ct);
            case "run_skill_source":
                return await ExecuteSkillSourceAsync(args,ct);
            case "refresh_skill_catalog":
                _chatViewModel?.RefreshSkills(); return new JsonObject { ["ok"]=true };
            case "get_localization_state":
                return JsonSerializer.SerializeToNode(DiskPilotX.App.Localization.L.GetState(), SkillJson)!;
            case "show_skill_panel":
                return ShowSkillPanel(args);
            case "render_view":
                return RenderView(args);
            default:return AssistantError("未知磁盘操作。");
        }
    }
    private bool SetAssistantView(string? view)
    {
        if(view=="tree"){TreeViewTabBtn.IsChecked=true;return true;}
        if(view=="treemap"){TreemapTabBtn.IsChecked=true;return true;}
        return false;
    }

    /// <summary>
    /// 多范围无头分析：一次性扫描多个用户选定的路径，各范围保有自己的扫描会话，
    /// 返回按范围分段的真实证据，供模型按批取证并分批追加候选。主工作区视图不受影响。
    /// </summary>
    private async Task<JsonNode> AnalyzePathsAsync(JsonNode? args,CancellationToken ct,MainViewModel main,AppSettings settings,
        IPathWhitelistMatcher matcher,Func<string?,bool> hidden)
    {
        if(args?["paths"] is JsonArray input && input.Count>6)
            return new JsonObject { ["ok"]=false, ["code"]="batch_too_large", ["error"]="每次最多扫描6个路径；本次未执行，请拆批。", ["limits"]=ScanBudgetData() };
        var paths=ReadPaths(args);
        if(paths.Count==0) return AssistantError("请提供至少一个本地绝对路径。");
        var rejected=paths.FirstOrDefault(path=>hidden(path)||matcher.IsExcluded(path,settings.ScanWhitelist));
        if(rejected is not null) return AssistantError($"路径不允许用于 AI 分析：{rejected}");
        if (!ProcessPrivileges.IsElevated)
        {
            if (_chatViewModel is null) return AssistantError("扫描确认界面尚未准备好。");
            if (!await main.ConfirmAssistantScanAsync(paths[0], false,
                (title, description, impact, token) => _chatViewModel.ConfirmAsync(title,
                    description + "\n本次只读范围：" + string.Join("；", paths), impact, token), ct, replacesWorkspace: false))
                return new JsonObject { ["ok"] = false, ["cancelled"] = true, ["started"] = false, ["unexecuted"]=JsonSerializer.SerializeToNode(paths.Where(IsSkillScopeAllowed)), ["scan_execution"] = JsonSerializer.SerializeToNode(main.LastScanExecution?.ToPublicData()) };
        }
        if(main.IsScanning || _cleanupRunning || _recommendationPublishing) return AssistantError("已有磁盘操作正在进行，请完成后重试。");
        using var scanCancellation=CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var scanActivity=main.BeginExternalScan("无头扫描（结果仅保留在内存）",null,scanCancellation.Cancel);
        var scanToken=scanCancellation.Token;
        var batch=_analysisBatch ??= new AssistantAnalysisBatch { Note=TrimTo(args?["note"]?.GetValue<string>(),400) };
        var requestPolicy = PublishPolicyStamp();
        var ownerConversation=_chatViewModel?.CurrentConversationId;
        batch.Errors.Clear();
        var unexecuted = new JsonArray();
        var completedPaths=new List<string>();
        var cancelled = false;
        var queue = paths.ToDictionary(path => path, path => new ScanSnapshotReference(Guid.NewGuid(), _scanSnapshots.Epoch,
            path, DateTimeOffset.Now, 0, 0, 0), StringComparer.OrdinalIgnoreCase);
        foreach (var entry in queue.Values) { entry.SetState("pending"); _chatViewModel?.AddScanSnapshot(entry); }
        foreach(var path in paths)
        {
            var row = queue[path];
            if (scanToken.IsCancellationRequested) { cancelled = true; row.SetState("cancelled"); unexecuted.Add(path); continue; }
            var resolved=ScanPathResolver.Resolve(path);
            try
            {
                if (!IsSkillScopeAllowed(resolved)) throw new InvalidOperationException("当前范围已受隐私或扫描设置限制。");
                var budget = _scanSnapshots.GetBudget();
                if (budget.ProtectedSnapshots >= budget.MaximumSnapshots)
                    throw new InvalidOperationException("快照容量被活动证据占满；请暂停分批任务，不要自动释放清单。");
                row.SetState("scanning");
                var progress=new Progress<ScanProgress>(p =>
                {
                    if (row.State != "scanning") return;
                    var text = ScanProgressFormatter.Format(p);
                    main.ScanProgressText = text;
                    row.SetProgress(text);
                });
                var session=await main.ScanHeadlessAsync(resolved,scanToken,progress);
                if(!ReferenceEquals(_analysisBatch,batch) || ownerConversation is not null && _chatViewModel?.CurrentConversationId!=ownerConversation)
                { scanCancellation.Cancel(); throw new OperationCanceledException(scanToken); }
                if (requestPolicy != PublishPolicyStamp() || !IsSkillScopeAllowed(resolved))
                    throw new InvalidOperationException("扫描期间隐私或保护条件变化，请重新确认范围。");
                var previous=batch.Scopes.FirstOrDefault(scope=>string.Equals(scope.Path,resolved,StringComparison.OrdinalIgnoreCase));
                RegisterScanSnapshot(session,analysis:true);
                ownerConversation??=_chatViewModel?.CurrentConversationId;
                batch.AddOrReplace(new AssistantAnalysisScope { Path=resolved, Label=ScopeLabel(resolved), Session=session });
                completedPaths.Add(resolved);
                if (previous is not null) _scanSnapshots.Unprotect(previous.Session.Id,"analysis");
            }
            catch(OperationCanceledException) { cancelled = true; row.SetState("cancelled"); unexecuted.Add(path); }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or TimeoutException)
            { row.SetState("failed"); row.SetProgress(ex.Message); batch.Errors.Add($"{resolved}：{ex.Message}"); }
        }
        RefreshScanSnapshotEntries();
        main.ScanProgressText=$"无头扫描批次结束：{batch.Scopes.Count} 个范围，共 {FileSizeConverter.FormatSize(batch.Scopes.Sum(scope=>scope.Session.TotalSize))}";
        var scopes=new JsonArray();
        foreach(var scope in batch.Scopes.Where(scope=>IsSkillScopeAllowed(scope.Path)))
            scopes.Add(new JsonObject { ["path"]=scope.Path,["label"]=scope.Label,["snapshot_id"]=scope.Session.Id.ToString(),
                ["files"]=scope.Session.TotalFiles,["folders"]=scope.Session.TotalFolders,["size_bytes"]=scope.Session.TotalSize });
        return new JsonObject
        {
            ["ok"]=completedPaths.Count>0 && !cancelled && batch.Errors.Count==0,["partial"]=completedPaths.Count>0 && (cancelled || batch.Errors.Count>0),["limits"]=ScanBudgetData(),["registered_this_call"]=completedPaths.Count,["cancelled"]=cancelled,["scan_id"]=batch.Id.ToString(),["batch"]=true,["scope_count"]=scopes.Count,
            ["scopes"]=scopes,["errors"]=JsonSerializer.SerializeToNode(batch.Errors.Where(error=>paths.Any(path=>error.StartsWith(path,StringComparison.OrdinalIgnoreCase)&&IsSkillScopeAllowed(path)))),["unexecuted"]=JsonSerializer.SerializeToNode(unexecuted.Select(n=>n!.GetValue<string>()).Where(IsSkillScopeAllowed)),
            ["next_step"]="仅对本批成功范围取证分析，随后分批发布推荐；首批明确replace，后续merge，等待实际回执再继续。容量不足时停止并报告未完成范围，禁止自动清空清单。"
        };
    }

    /// <summary>把候选按路径前缀归属到某个范围；范围之间互不重叠。</summary>
    private static JsonElement OwnedCandidates(JsonElement raw,string rootPath,AssistantAnalysisBatch batch)
    {
        var array=new JsonArray();
        if(raw.ValueKind==JsonValueKind.Object && raw.TryGetProperty("recommendations",out var items) && items.ValueKind==JsonValueKind.Array)
            foreach(var item in items.EnumerateArray())
                if(item.ValueKind==JsonValueKind.Object && item.TryGetProperty("path",out var pathNode)
                    && pathNode.ValueKind==JsonValueKind.String && IsUnder(pathNode.GetString(),rootPath) && batch.FindScope(pathNode.GetString()!)?.Path == rootPath)
                    array.Add(JsonNode.Parse(item.GetRawText()));
        return JsonSerializer.SerializeToElement(new JsonObject { ["recommendations"]=array });
    }

    private static bool IsUnder(string? path,string? rootPath)
    {
        if(string.IsNullOrWhiteSpace(path)||string.IsNullOrWhiteSpace(rootPath)) return false;
        string full;
        try { full=Path.GetFullPath(path); } catch(Exception ex) when(ex is ArgumentException or IOException or NotSupportedException) { return false; }
        return IsPathUnder(full,rootPath);
    }

    /// <summary>跨范围去重：同一条路径或父子路径只保留体积更大的那条。</summary>
    private static List<CleanupRecommendation> DeduplicateByPath(List<CleanupRecommendation> items)
    {
        var ordered=items.OrderByDescending(item=>item.Size).ToList();
        var kept=new List<CleanupRecommendation>();
        foreach(var item in ordered)
            if(!kept.Any(existing=>IsUnder(item.TargetPath,existing.TargetPath)||IsUnder(existing.TargetPath,item.TargetPath)))
                kept.Add(item);
        return kept;
    }

    private static List<string> ReadPaths(JsonNode? args)
    {
        var result=new List<string>();
        if(args?["paths"] is not JsonArray array) return result;
        foreach(var entry in array)
        {
            var value=entry?.GetValue<string>();
            if(string.IsNullOrWhiteSpace(value)||!Path.IsPathFullyQualified(value.Trim())) continue;
            string full;
            try { full=Path.GetFullPath(value.Trim()); } catch(Exception ex) when(ex is ArgumentException or IOException or NotSupportedException) { continue; }
            if(!result.Any(existing=>string.Equals(existing,full,StringComparison.OrdinalIgnoreCase))) result.Add(full);

        }
        return result;
    }

    private static string ScopeLabel(string path)
    {
        var trimmed=path.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        var name=Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(name)?trimmed:name;
    }

    private static string? TrimTo(string? value,int maxLength)
    {
        if(string.IsNullOrWhiteSpace(value)) return null;
        var text=value.Trim();
        return text.Length<=maxLength?text:text[..maxLength];
    }

    private JsonNode RecommendationState(ScanSession session,AppSettings settings,bool analysisMode=false,string? originLabel=null)
    {
        var all=_recommendationsViewModel!.Recommendations.ToList();
        var matcher=App.Services!.GetRequiredService<IPathWhitelistMatcher>();
        return JsonSerializer.SerializeToNode(new{ok=true,scan_id=session.Id,version=CleanupPlan.ComputeVersion(session,all,CurrentDeletionMode),
            deletion_mode=CurrentDeletionMode.ToString(),origin_label=originLabel??string.Empty,
            recommendations=all.Where(r=>analysisMode||!matcher.IsExcluded(r.TargetPath,settings.AiConversationWhitelist))
                .Select(r=>new{id=r.Id,path=r.TargetPath,size_bytes=r.Size,safety_rating=r.SafetyRating.ToString(),explanation=r.Explanation,source_scope=r.SourceScope,selected=r.IsAccepted})})!;
    }
    private static bool WouldAffectExcludedPath(string target,IEnumerable<PathWhitelistEntry> rules)
    {
        var matcher=new PathWhitelistMatcher();
        var list=rules.ToList();
        return matcher.IsExcluded(target,list)||list.Any(rule=>matcher.IsExcluded(rule.Path,[new PathWhitelistEntry{Path=target}]));
    }
    private static List<string> ReadIds(JsonNode? args)=>args?["ids"] is JsonArray ids?ids.Select(n=>n?.GetValue<string>()??"").ToList():[];
    private static JsonNode AssistantError(string message)=>new JsonObject{["ok"]=false,["error"]=message};
    internal static string? BuildRecommendationSummary(IEnumerable<CleanupRecommendation> recommendations)
    {
        var lines=recommendations.OrderByDescending(r=>r.Size).Take(3).Select(r=>$"- {Path.GetFileName(r.TargetPath.TrimEnd(Path.DirectorySeparatorChar))}: {FileSizeConverter.FormatSize(r.Size)}").ToArray();
        return lines.Length==0?null:"主要清理候选："+Environment.NewLine+string.Join(Environment.NewLine,lines);
    }

    /// <summary>
    /// 解析并渲染模型按需构建的视图契约。
    /// 泡泡里一律只放一张入口卡；surface=auto 时额外立即弹出完整视图窗口，
    /// surface=entry（默认）时只放入口卡，等用户点击再开窗。
    /// </summary>
    private JsonNode RenderView(JsonNode? args)
    {
        if (_chatViewModel is null) return AssistantError("聊天面板尚未就绪。");
        if (args is not JsonObject) return AssistantError("render_view 需要 sections 数组。");
        try
        {
            var spec = SkillUiSpecContract.Parse(JsonSerializer.SerializeToElement(args));
            if (spec.Sections.Count == 0 && spec.Surface != SkillUiSurface.Window)
                return new JsonObject { ["ok"] = false, ["error"] = "视图契约无效或为空。kind 必须是 poster_grid/stat_row/table/bar_chart/pie_chart/donut_chart/text。" };
            _chatViewModel.AddView(spec);
            if (spec.AutoOpensWindow) _chatViewModel.OpenSkillWindow(spec);
            return new JsonObject { ["ok"] = true, ["surface"] = spec.Surface.ToString().ToLowerInvariant(), ["sections"] = spec.Sections.Count };
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return AssistantError("视图契约解析失败：" + ex.Message);
        }
    }
}