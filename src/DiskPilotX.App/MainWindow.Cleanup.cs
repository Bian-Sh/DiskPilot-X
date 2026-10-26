using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using DiskPilotX.App.Controls;
using DiskPilotX.App.Converters;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Analysis;
using DiskPilotX.Core.Services.Cleanup;
using DiskPilotX.Core.Services.FileTree;
using DiskPilotX.Core.Services.Settings;
using DiskPilotX.Core.Services.Whitelist;

namespace DiskPilotX.App;

public partial class MainWindow
{
    private bool _cleanupRunning;
    private DeletionMode CurrentDeletionMode=>_settingsViewModel?.SelectedDeletionMode??DeletionMode.MoveToRecycleBin;
    private async void OnCleanupRequested()
    {
        if(_recommendationsViewModel is null||_chatViewModel?.IsOperationRunning==true)return;
        var ids=_recommendationsViewModel.Recommendations.Where(r=>r.IsAccepted).Select(r=>r.Id).ToList();
        try
        {
            var result=await ExecuteSharedCleanupAsync(ids,null,false,CancellationToken.None);
            if(result["ok"]?.GetValue<bool>()==false && result["cancelled"]?.GetValue<bool>()!=true)
                await ShowAppMessageAsync(result["error"]?.GetValue<string>()??"清理未完成。","清理结果",MessageBoxButton.OK,MessageBoxImage.Information);
        }
        catch(Exception ex){Serilog.Log.Warning("手动清理未完成 {ExceptionType}",ex.GetType().Name);await ShowAppMessageAsync("清理未完成，请检查目标路径和访问权限。","清理结果",MessageBoxButton.OK,MessageBoxImage.Warning);}
    }
    private async Task<JsonNode> ExecuteSharedCleanupAsync(List<string> ids,string? expectedVersion,bool fromAssistant,CancellationToken ct)
    {
        if(_cleanupRunning||DataContext is not MainViewModel main||main.CurrentSession is null||main.IsScanning||_recommendationsViewModel is null||_recommendationsViewModel.IsAnalyzing)
            return AssistantError("当前有磁盘任务正在运行，或尚无可清理结果。");
        if(ids.Count==0)return AssistantError("请先选择具体清理项。");
        _cleanupRunning=true;main.IsCopilotBusy=true;
        try
        {
            var items=_recommendationsViewModel.Recommendations.ToList();
            var planSession=main.CurrentSession;
            var planMode=CurrentDeletionMode;
            var requestedVersion=CleanupPlan.ComputeVersion(planSession,items,planMode);
            main.ScanProgressText="正在复核清理目录的完整状态";
            var plan=await Task.Run(()=>CleanupPlan.Capture(planSession,items,ids,planMode,ct),ct);
            if(plan.RecommendationVersion!=requestedVersion || !plan.MatchesCurrentList(main.CurrentSession,_recommendationsViewModel.Recommendations.ToList(),CurrentDeletionMode))return AssistantError("清单或扫描状态已改变，请重新确认。");
            if(fromAssistant&&(string.IsNullOrWhiteSpace(expectedVersion)||plan.RecommendationVersion!=expectedVersion))return AssistantError("清理清单版本已改变，请重新读取并确认。");
            await Task.Run(()=>ValidateCleanupItems(plan,planSession,fromAssistant,ct:ct),ct);
            var description=string.Join(Environment.NewLine,plan.Items.Select(r=>$"{r.TargetPath}（{FileSizeConverter.FormatSize(r.Size)}）"));
            var impact=$"共 {plan.Items.Count} 项，{FileSizeConverter.FormatSize(plan.Items.Sum(r=>r.Size))}；方式：{(plan.Mode==DeletionMode.PermanentDelete?"永久删除":"移入回收站")}。";
            bool confirmed;
            if(fromAssistant&&_chatViewModel is not null)
            {
                if(_isChatCollapsed)ToggleChatPanel();
                confirmed=await _chatViewModel.ConfirmAsync("确认清理具体清单",description,impact,ct);
            }
            else confirmed=await ShowAppModalAsync("确认清理具体清单",description+Environment.NewLine+impact,ModalMessageType.Warning,ModalButtonFlags.Positive|ModalButtonFlags.Negative)==ModalResult.Positive;
            ct.ThrowIfCancellationRequested();
            if(!confirmed)return new JsonObject{["ok"]=false,["cancelled"]=true,["message"]="已取消，未删除任何项目。"};
            var approvedSession=main.CurrentSession;
            var approvedItems=_recommendationsViewModel.Recommendations.ToList();
            var approvedMode=CurrentDeletionMode;
            main.ScanProgressText="正在核对批准后的目录状态";
            var approvalCurrent=await Task.Run(()=>plan.IsCurrent(approvedSession,approvedItems,approvedMode,ct),ct);
            if(!approvalCurrent || !plan.MatchesCurrentList(main.CurrentSession,_recommendationsViewModel.Recommendations.ToList(),CurrentDeletionMode))return AssistantError("确认期间清单、选择、目录内容或删除方式已改变，请重新确认。");
            await Task.Run(()=>ValidateCleanupItems(plan,approvedSession!,fromAssistant,ct:ct),ct);
            var progress=new Progress<CleanupProgress>(p=>main.ScanProgressText=$"正在清理 {p.CompletedCount+1}/{p.TotalCount}");
            var results=new List<CleanupAction>();
            foreach(var item in plan.Items)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var itemSession=main.CurrentSession;
                    if(itemSession?.Id!=plan.ScanId || CurrentDeletionMode!=plan.Mode)throw new InvalidOperationException("扫描或删除方式改变。");
                    var currentItem=_recommendationsViewModel.Recommendations.FirstOrDefault(candidate=>candidate.Id==item.Id);
                    if(currentItem is null || currentItem.TargetPath!=item.TargetPath || currentItem.IsAccepted!=item.IsAccepted || currentItem.IsDismissed!=item.IsDismissed)throw new InvalidOperationException("清理选择改变。");
                    await Task.Run(()=> { ValidateCleanupItems(plan,itemSession,fromAssistant,item,ct); if(!plan.IsTargetCurrent(item,ct))throw new InvalidOperationException("目标内容或保护状态改变。"); },ct);
                    if(main.CurrentSession?.Id!=plan.ScanId || CurrentDeletionMode!=plan.Mode || currentItem.IsAccepted!=item.IsAccepted || currentItem.IsDismissed!=item.IsDismissed)throw new InvalidOperationException("执行前上下文改变。");
                    var result=await App.Services!.GetRequiredService<ICleanupService>().ExecuteCleanupAsync([item],plan.Mode,progress,ct);
                    results.AddRange(result);
                    // 每项完成立即同步，停止后也不会丢失已产生的真实结果。
                    ApplyCleanupResults(result,main);
                }
                catch(InvalidOperationException)
                {
                    results.Add(new CleanupAction{Recommendation=item,ActionType=plan.Mode,Result=CleanupResult.Skipped,FailureReason="执行前路径或扫描状态改变。"});
                }
            }
            main.ScanProgressText=$"本次已释放 {FileSizeConverter.FormatSize(results.Sum(r=>r.ActualSizeFreed))}";
            return JsonSerializer.SerializeToNode(new{ok=results.All(r=>r.Result is CleanupResult.Success or CleanupResult.AlreadyRemoved),freed_bytes=results.Sum(r=>r.ActualSizeFreed),
                results=results.Select(r=>new{id=r.Recommendation.Id,path=r.Recommendation.TargetPath,status=r.Result.ToString(),size_bytes=r.ActualSizeFreed})})!;
        }
        catch(InvalidOperationException){return AssistantError("清理项不在当前允许范围或目录状态已改变，请重新扫描核对后再确认。");}
        catch(IOException){return AssistantError("目录内容正在变化或无法完整读取，未继续清理。请关闭占用程序或重新扫描后重试。");}
        catch(UnauthorizedAccessException){return AssistantError("无法完整读取清理目录，未继续清理。请检查访问权限。");}
        finally{_cleanupRunning=false;main.IsCopilotBusy=_chatViewModel?.IsOperationRunning==true;}
    }
    private void ValidateCleanupItems(CleanupPlan plan,ScanSession session,bool fromAssistant,CleanupRecommendation? current=null,CancellationToken ct=default)
    {
        if(session.Id!=plan.ScanId)throw new InvalidOperationException("扫描版本改变。");
        var policy=App.Services!.GetRequiredService<RecommendationEngine>();
        var settings=App.Services!.GetRequiredService<ISettingsService>().LoadSettings();
        var matcher=App.Services!.GetRequiredService<IPathWhitelistMatcher>();
        var query=new FileTreeQueryService();
        foreach(var item in current is null?plan.Items:new[]{current})
        {
            ct.ThrowIfCancellationRequested();
            var entry=query.FindByPath(session,item.TargetPath);
            if(entry is null||entry.IsReparsePoint||entry.IsAccessDenied||RecommendationEngine.IsDeletionProtected(item.TargetPath)
                ||policy.IsRecommendationExcluded(item.TargetPath)||WouldAffectExcludedPath(item.TargetPath,settings.CleanupRecommendationWhitelist)||WouldAffectExcludedPath(item.TargetPath,settings.ScanWhitelist)
                ||fromAssistant&&WouldAffectExcludedPath(item.TargetPath,settings.AiConversationWhitelist))throw new InvalidOperationException("目标不允许清理。");
            item.Entry=entry;
            var pending=new Stack<FileEntry>();pending.Push(entry);
            while(pending.TryPop(out var child))
            {
                ct.ThrowIfCancellationRequested();
                if(child.IsReparsePoint||RecommendationEngine.IsDeletionProtected(child.Path))throw new InvalidOperationException("候选目录包含受保护项或链接。");
                foreach(var nested in child.Children)pending.Push(nested);
            }
            for(var directory=Directory.Exists(item.TargetPath)?new DirectoryInfo(item.TargetPath):Directory.GetParent(item.TargetPath);directory is not null;directory=directory.Parent)
                if(directory.Exists&&directory.Attributes.HasFlag(FileAttributes.ReparsePoint))throw new InvalidOperationException("禁止通过链接清理。");
            if(File.Exists(item.TargetPath)&&File.GetAttributes(item.TargetPath).HasFlag(FileAttributes.ReparsePoint))throw new InvalidOperationException("禁止清理链接。");
        }
    }
    private void ApplyCleanupResults(List<CleanupAction> results,MainViewModel main)
    {
        _treemapViewModel?.ApplyCleanupResults(results,main.CurrentSession!);
        if(TreeViewControl.DataContext is TreeViewModel tree) { if(main.CurrentSession?.RootEntry is {} root)tree.SetRoot(root,main.CurrentSession); else tree.RootItems.Clear(); }
        main.UpdateStatusBar(main.CurrentSession!);
        var completed=results.Where(r=>r.Result is CleanupResult.Success or CleanupResult.AlreadyRemoved).Select(r=>r.Recommendation.Id).ToHashSet();
        _recommendationsViewModel!.SetExternalRecommendations(_recommendationsViewModel.Recommendations.Where(r=>!completed.Contains(r.Id)));
        main.ScanProgressText=$"本次已释放 {FileSizeConverter.FormatSize(results.Sum(r=>r.ActualSizeFreed))}";
    }
}
