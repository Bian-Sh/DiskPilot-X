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
            _chatViewModel.SkillWindowRequested += OpenSkillWindow;
        }
        if (_chatViewModel is not null && DataContext is MainViewModel vm)
            _chatViewModel.PropertyChanged += (_, e) => { if(e.PropertyName == nameof(ChatViewModel.IsOperationRunning)) vm.IsCopilotBusy=_chatViewModel.IsOperationRunning||_cleanupRunning; };
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
            case "scan_path":
            {
                var target=ScanPathResolver.Resolve(args?["path"]?.GetValue<string>()??"");
                if(string.IsNullOrWhiteSpace(target)||Hidden(target)||matcher.IsExcluded(target,settings.ScanWhitelist)) return AssistantError("扫描路径为空或已排除。");
                if(main.IsScanning || _cleanupRunning) return AssistantError("当前磁盘操作尚未完成。");
                if (_chatViewModel is null) return AssistantError("扫描确认界面尚未准备好。");
                if (!await main.ConfirmAssistantScanAsync(target, ProcessPrivileges.IsElevated,
                    (title, description, impact, token) => _chatViewModel.ConfirmAsync(title, description, impact, token), ct))
                    return new JsonObject { ["ok"] = false, ["cancelled"] = true, ["started"] = false, ["scan_execution"] = JsonSerializer.SerializeToNode(main.LastScanExecution?.ToPublicData()) };
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
                bool hit = TreemapView.FocusPath(target, highlight);
                return hit
                    ? new JsonObject{["ok"]=true,["path"]=target,["focus_path"]=_treemapViewModel?.SelectedNode?.Entry.Path ?? target,["current_path"]=_treemapViewModel?.CurrentRoot?.Path}
                    : AssistantError("无法在地图上定位该路径。");
            }
            case "set_view":
                return new JsonObject{["ok"]=SetAssistantView(args?["view"]?.GetValue<string>()),["view"]=TreeViewControl.Visibility==Visibility.Visible?"tree":"treemap"};
            case "analyze_paths":
                return await AnalyzePathsAsync(args,ct,main,settings,matcher,Hidden);
            case "publish_recommendations":
            {
                if(main.IsScanning || _cleanupRunning) return AssistantError("扫描结果已改变或磁盘正在操作。");
                var requestedScanId=args?["scan_id"]?.GetValue<string>();
                var mergeMode=string.Equals(args?["mode"]?.GetValue<string>(),"merge",StringComparison.OrdinalIgnoreCase);
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
                        var owned=OwnedCandidates(raw,scope.Root.Path);
                        var scoped=engine.ValidateCandidates(scope.Session,owned,scope.Root);
                        missing+=scoped.Diagnostics.MissingEntryCount;
                        protectedFiltered+=scoped.Diagnostics.ProtectedFilteredCount;
                        foreach(var item in scoped.Recommendations)
                        {
                            item.SourceScope=scope.Label;
                            merged.Add(item);
                        }
                    }
                    merged=DeduplicateByPath(merged);
                    if(!analysisMode)
                        merged=merged.Where(item=>!WouldAffectExcludedPath(item.TargetPath,settings.AiConversationWhitelist)).ToList();
                    if(merged.Count==0) return AssistantError("没有通过校验的清理候选。");
                    var diagnostics=new AnalysisDiagnostics
                    {
                        TargetPath=batch.Scopes.Count==1?batch.Scopes[0].Path:batch.Note??"多个位置",
                        ScopePath=string.Join(" ; ",batch.Scopes.Select(scope=>scope.Path)),
                        IsFocusedScope=true,
                        ParsedRecommendationCount=merged.Count,
                        MissingEntryCount=missing,
                        ProtectedFilteredCount=protectedFiltered
                    };
                    var final=mergeMode?_recommendationsViewModel.Recommendations.Concat(merged).ToList():merged;
                    for(var index=0;index<final.Count;index++) final[index].Id=(index+1).ToString();
                    _recommendationsViewModel.SetExternalRecommendations(final,diagnostics);
                    _assistantAnalysisSession=batch.ToSession();
                    EnsureBottomPanelVisible(); ShowRecommendationsPanel();
                    return RecommendationState(_assistantAnalysisSession,settings,analysisMode,originLabel:batch.Note??(batch.Scopes.Count==1?batch.Scopes[0].Label:$"{batch.Scopes.Count} 个位置"));
                }
                if(main.CurrentSession?.Id.ToString()!=requestedScanId) return AssistantError("扫描结果已改变，请重新读取上下文。");
                var originalId=main.CurrentSession!.Id;
                if(!mergeMode && _recommendationsViewModel.HasAnyRecommendations && _chatViewModel is not null
                    && !await _chatViewModel.ConfirmAsync("替换当前清理建议","将用本轮校验后的建议替换列表。","此操作不会删除文件。",ct)) return new JsonObject{["ok"]=false,["cancelled"]=true};
                if(main.CurrentSession?.Id!=originalId) return AssistantError("扫描结果已改变，请重新分析。");
                var focusPath=args?["focus_path"]?.GetValue<string>();
                var localFocus=new FileTreeQueryService().FindByPath(main.CurrentSession!,focusPath??main.CurrentSession!.RootEntry!.Path);
                if(localFocus is null)return AssistantError("分析范围已失效。");
                var analysis=engine.ValidateCandidates(main.CurrentSession!,raw,localFocus);
                analysis.Recommendations=analysis.Recommendations.Where(r=>analysisMode||!WouldAffectExcludedPath(r.TargetPath,settings.AiConversationWhitelist)).ToList();
                // 确认窗口期间若规则、偏好或隐私条件改变，原列表保留并要求重新分析，避免用旧结论覆盖。
                if (!App.Services!.GetRequiredService<AssistantWorkspace>().ValidateAnalysisCommit(args)) return AssistantError("确认期间规则、偏好或隐私条件改变，原列表已保留，请重新分析。");
                ct.ThrowIfCancellationRequested();
                var published=mergeMode?_recommendationsViewModel.Recommendations.Concat(analysis.Recommendations).ToList():analysis.Recommendations;
                for(var index=0;index<published.Count;index++) published[index].Id=(index+1).ToString();
                _recommendationsViewModel.SetExternalRecommendations(published,analysis.Diagnostics);
                _assistantAnalysisSession=main.CurrentSession;
                EnsureBottomPanelVisible(); ShowRecommendationsPanel();
                return RecommendationState(main.CurrentSession!,settings,analysisMode);
            }
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
                return main.CurrentSession is null?AssistantError("扫描结果失效。"):RecommendationState(main.CurrentSession,settings);
            }
            case "cleanup_recommendations":
                return await ExecuteSharedCleanupAsync(ReadIds(args),args?["version"]?.GetValue<string>(),true,ct);
            case "confirm_skill_change":
            {
                if(_chatViewModel is null)return AssistantError("无法显示技能变更确认。");
                var package=args?["package"];
                var description=package is null ? $"操作：{args?["operation"]}；技能：{args?["id"]}\n\n{args?["body_markdown"]}" : $"规则：{package["title"]}\n说明：{package["description"]}\n\n规则明细：\n{package["rules"]?.ToJsonString(new(){WriteIndented=true})}\n\n{package["skill_markdown"]}";
                var confirmed=await _chatViewModel.ConfirmAsync("保存这份清理规则？",description,"保存后可从对话空态和 @ 菜单使用。保存不会清理文件；自定义源码执行还需单独授权。",ct);
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
    /// 返回按范围分段的真实证据，供模型一次性给出全部候选。主工作区视图不受影响。
    /// </summary>
    private async Task<JsonNode> AnalyzePathsAsync(JsonNode? args,CancellationToken ct,MainViewModel main,AppSettings settings,
        IPathWhitelistMatcher matcher,Func<string?,bool> hidden)
    {
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
                return new JsonObject { ["ok"] = false, ["cancelled"] = true, ["started"] = false, ["scan_execution"] = JsonSerializer.SerializeToNode(main.LastScanExecution?.ToPublicData()) };
        }
        var batch=new AssistantAnalysisBatch { Note=TrimTo(args?["note"]?.GetValue<string>(),400) };
        foreach(var path in paths)
        {
            ct.ThrowIfCancellationRequested();
            var resolved=ScanPathResolver.Resolve(path);
            try
            {
                var progress=new Progress<ScanProgress>(p=>main.ScanProgressText=
                    $"正在无头扫描 {p.CurrentPath}（{p.FileCount} 个文件 / {p.FolderCount} 个文件夹）");
                var session=await main.ScanHeadlessAsync(resolved,ct,progress);
                batch.Scopes.Add(new AssistantAnalysisScope { Path=resolved, Label=ScopeLabel(resolved), Session=session });
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            { batch.Errors.Add($"{resolved}：{ex.Message}"); }
        }
        if(batch.Scopes.Count==0)
            return AssistantError("这些路径都没能完成扫描：" + string.Join("；", batch.Errors));
        _analysisBatch=batch;
        main.ScanProgressText=$"无头扫描完成：{batch.Scopes.Count} 个范围，共 {FileSizeConverter.FormatSize(batch.Scopes.Sum(scope=>scope.Session.TotalSize))}";
        var scopes=new JsonArray();
        foreach(var scope in batch.Scopes)
            scopes.Add(new JsonObject { ["path"]=scope.Path,["label"]=scope.Label,
                ["files"]=scope.Session.TotalFiles,["folders"]=scope.Session.TotalFolders,["size_bytes"]=scope.Session.TotalSize });
        var errors=new JsonArray();
        foreach(var error in batch.Errors) errors.Add(error);
        return new JsonObject
        {
            ["ok"]=true,["scan_id"]=batch.Id.ToString(),["batch"]=true,["scope_count"]=batch.Scopes.Count,
            ["scopes"]=scopes,["errors"]=errors,
            // 回显给工具的一面保留完整证据长度（有上限，多个范围时自动收敛）。
            ["evidence"]=AssistantAnalysisEvidence.Build(App.Services!.GetRequiredService<RecommendationEngine>(),batch,12_000,36_000)
        };
    }

    /// <summary>把候选按路径前缀归属到某个范围；范围之间互不重叠。</summary>
    private static JsonElement OwnedCandidates(JsonElement raw,string rootPath)
    {
        var array=new JsonArray();
        if(raw.ValueKind==JsonValueKind.Object && raw.TryGetProperty("recommendations",out var items) && items.ValueKind==JsonValueKind.Array)
            foreach(var item in items.EnumerateArray())
                if(item.ValueKind==JsonValueKind.Object && item.TryGetProperty("path",out var pathNode)
                    && pathNode.ValueKind==JsonValueKind.String && IsUnder(pathNode.GetString(),rootPath))
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
            if(string.IsNullOrWhiteSpace(value)) continue;
            string full;
            try { full=Path.GetFullPath(value.Trim()); } catch(Exception ex) when(ex is ArgumentException or IOException or NotSupportedException) { continue; }
            if(!result.Any(existing=>string.Equals(existing,full,StringComparison.OrdinalIgnoreCase))) result.Add(full);
            if(result.Count>=6) break;
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
    /// 气泡里一律只放一张入口卡；surface=auto 时额外立即弹出完整视图窗口，
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
                return new JsonObject { ["ok"] = false, ["error"] = "视图契约无效或为空。kind 必须是 poster_grid/stat_row/table/bar_chart/text。" };
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