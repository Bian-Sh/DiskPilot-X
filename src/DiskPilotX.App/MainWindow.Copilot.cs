using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using DiskPilotX.App.Converters;
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
            return new AgentContext(main?.CurrentSession, _treemapViewModel?.CurrentRoot,
                _chatViewModel?.LinkedEntry ?? _treemapViewModel?.SelectedNode?.Entry, _chatViewModel?.LinkedRecommendation,
                _recommendationsViewModel?.HasAnyRecommendations == true);
        }).Task;
        workspace.Execute = (operation, args, ct) => Dispatcher.InvokeAsync(() => ExecuteAssistantOperationAsync(operation,args,ct)).Task.Unwrap();
        if (_chatViewModel is not null) _chatViewModel.SkillTaskRequested += OpenSkillTask;
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
                if(main.CurrentSession is not null && _chatViewModel is not null && !await _chatViewModel.ConfirmAsync("替换当前扫描结果",$"扫描 {target}","将更新 Treemap、文件树与当前分析上下文。",ct)) return new JsonObject{["ok"]=false,["cancelled"]=true};
                var session=await main.ScanForAssistantAsync(target,ct);
                session.Provenance = new(args?["source_rule_id"] is null ? "chat" : "rule", args?["conversation_id"]?.GetValue<string>(),
                    args?["message_id"]?.GetValue<string>(), args?["purpose"]?.GetValue<string>() ?? "用户请求扫描指定范围", args?["source_rule_id"]?.GetValue<string>());
                CacheAiScanSession(session);
                return new JsonObject{["ok"]=true,["scan_id"]=session.Id.ToString(),["path"]=session.TargetPath,["files"]=session.TotalFiles,["folders"]=session.TotalFolders};
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
            case "set_view":
                return new JsonObject{["ok"]=SetAssistantView(args?["view"]?.GetValue<string>()),["view"]=TreeViewControl.Visibility==Visibility.Visible?"tree":"treemap"};
            case "publish_recommendations":
            {
                if(main.IsScanning || _cleanupRunning || main.CurrentSession?.Id.ToString()!=args?["scan_id"]?.GetValue<string>()) return AssistantError("扫描结果已改变或磁盘正在操作。");
                var originalId=main.CurrentSession!.Id;
                if(_recommendationsViewModel.HasAnyRecommendations && _chatViewModel is not null
                    && !await _chatViewModel.ConfirmAsync("替换当前清理建议","将用本轮校验后的建议替换列表。","此操作不会删除文件。",ct)) return new JsonObject{["ok"]=false,["cancelled"]=true};
                if(main.CurrentSession?.Id!=originalId) return AssistantError("扫描结果已改变，请重新分析。");
                var analysisMode=args?["analysis_mode"]?.GetValue<bool>()==true;
                var focusPath=args?["focus_path"]?.GetValue<string>();
                var localFocus=new FileTreeQueryService().FindByPath(main.CurrentSession!,focusPath??main.CurrentSession!.RootEntry!.Path);
                if(localFocus is null)return AssistantError("分析范围已失效。");
                var raw=JsonSerializer.SerializeToElement(args);
                var analysis=App.Services!.GetRequiredService<RecommendationEngine>().ValidateCandidates(main.CurrentSession!,raw,localFocus);
                analysis.Recommendations=analysis.Recommendations.Where(r=>analysisMode||!WouldAffectExcludedPath(r.TargetPath,settings.AiConversationWhitelist)).ToList();
                if (!App.Services!.GetRequiredService<AssistantWorkspace>().ValidateAnalysisCommit(args)) return AssistantError("确认期间规则、偏好或隐私条件改变，原列表已保留，请重新分析。");
                ct.ThrowIfCancellationRequested();
                _recommendationsViewModel.SetExternalRecommendations(analysis.Recommendations,analysis.Diagnostics);
                EnsureBottomPanelVisible(); ShowRecommendationsPanel();
                return RecommendationState(main.CurrentSession!,settings,analysisMode);
            }
            case "list_recommendations":
                if(main.CurrentSession is null) return AssistantError("尚无扫描结果。");
                return RecommendationState(main.CurrentSession,settings);
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
            case "show_skill_panel":
                return ShowSkillPanel(args);
            default:return AssistantError("未知磁盘操作。");
        }
    }
    private bool SetAssistantView(string? view)
    {
        if(view=="tree"){TreeViewTabBtn.IsChecked=true;return true;}
        if(view=="treemap"){TreemapTabBtn.IsChecked=true;return true;}
        return false;
    }
    private JsonNode RecommendationState(ScanSession session,AppSettings settings,bool analysisMode=false)
    {
        var all=_recommendationsViewModel!.Recommendations.ToList();
        var matcher=App.Services!.GetRequiredService<IPathWhitelistMatcher>();
        return JsonSerializer.SerializeToNode(new{ok=true,scan_id=session.Id,version=CleanupPlan.ComputeVersion(session,all,CurrentDeletionMode),
            deletion_mode=CurrentDeletionMode.ToString(),recommendations=all.Where(r=>analysisMode||!matcher.IsExcluded(r.TargetPath,settings.AiConversationWhitelist))
                .Select(r=>new{id=r.Id,path=r.TargetPath,size_bytes=r.Size,safety_rating=r.SafetyRating.ToString(),explanation=r.Explanation,selected=r.IsAccepted})})!;
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
}
