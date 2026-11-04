using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using DiskPilotX.App.Diagnostics;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Settings;
using DiskPilotX.Core.Services.Whitelist;

namespace DiskPilotX.App;
public partial class MainWindow
{
    private JsonObject? _snapshotProbe;
    private string? _snapshotProbeBatch;
    internal object AcceptanceScanSnapshots(string? action)
    {
        var settingsPath=Core.AppCompatibility.GetEnvironmentVariable("DISKPILOT_X_ACCEPTANCE_SETTINGS");
        if(!AcceptanceAutomationServer.IsEnabled() || string.IsNullOrWhiteSpace(settingsPath))throw new InvalidOperationException("需要独立验收环境。");
        if(action=="history")
        {
            if(_chatViewModel is null)throw new InvalidOperationException("聊天未就绪。");
            var service=App.Services!.GetRequiredService<DiskAssistantService>();
            if(_chatViewModel.Messages.Count>0)_chatViewModel.ClearConversationCommand.Execute(null);
            var summary=service.GetConversations().FirstOrDefault();
            if(summary is not null)_chatViewModel.LoadConversationCommand.Execute(summary);
            RefreshScanSnapshotEntries(); return AcceptanceScanSnapshotState();
        }
        if(action=="repeat-root")
        {
            _snapshotProbe=new JsonObject {["running"]=true};_=RepeatRoot();return _snapshotProbe;
        }
        if(action=="fixture")
        {
            _snapshotProbe=new JsonObject { ["running"]=true };
            _=RunFixture(); return _snapshotProbe;
        }
        if(action?.StartsWith("publish",StringComparison.Ordinal)==true)
        {
            _snapshotProbe=new JsonObject { ["running"]=true };
            _=PublishFixture(action); return _snapshotProbe;
        }
        if(int.TryParse(action,out var index))
        {
            var reference=_chatViewModel?.Messages.SelectMany(message=>message.ScanSnapshots).ElementAtOrDefault(index);
            if(reference is null)throw new InvalidOperationException("入口索引无效。");
            _=OpenScanSnapshotAsync(reference); return AcceptanceScanSnapshotState();
        }
        return AcceptanceScanSnapshotState();
        async Task RunFixture()
        {
            try
            {
                var root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settingsPath))!,"snapshot-fixtures",Guid.NewGuid().ToString("N"));
                var paths=new JsonArray();
                for(var n=0;n<3;n++)
                {
                    var path=Path.Combine(root,"范围"+n);Directory.CreateDirectory(path);File.WriteAllBytes(Path.Combine(path,"cache.bin"),new byte[256+n*128]);paths.Add(path);
                }
                if(_chatViewModel is null || DataContext is not MainViewModel main)throw new InvalidOperationException("工作区未就绪。");
                _chatViewModel.Messages.Add(new() {Sender=ChatSender.User,Text="验收：三个只读目录无头扫描",Timestamp=DateTime.Now});
                App.Services!.GetRequiredService<DiskAssistantService>().SaveConversation(_chatViewModel.Messages);
                var settings=App.Services!.GetRequiredService<ISettingsService>().LoadSettings();
                var matcher=App.Services!.GetRequiredService<IPathWhitelistMatcher>();
                var before=_treemapViewModel?.CurrentRoot?.Path;
                var result=await AnalyzePathsAsync(new JsonObject {["paths"]=paths,["note"]="三范围发布验收"},CancellationToken.None,main,settings,matcher,path=>matcher.IsExcluded(path,settings.AiConversationWhitelist));
                _snapshotProbeBatch=result["scan_id"]?.GetValue<string>();
                _snapshotProbe=new JsonObject { ["running"]=false,["result"]=result,["treemap_before"]=before,["treemap_after"]=_treemapViewModel?.CurrentRoot?.Path };
            }
            catch(Exception ex) { _snapshotProbe=new JsonObject {["running"]=false,["error"]=ex.ToString()}; }
        }
        async Task RepeatRoot()
        {
            try
            {
                if(_analysisBatch?.Scopes.FirstOrDefault() is not {} scope || DataContext is not MainViewModel main)throw new InvalidOperationException("先建立本任务范围。");
                var allowed=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settingsPath))!,"snapshot-fixtures"))+Path.DirectorySeparatorChar;
                var target=Path.GetFullPath(scope.Path);
                if(!target.StartsWith(allowed,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("不是本任务创建的夹具范围。");
                var oldId=scope.Session.Id;
                File.WriteAllBytes(Path.Combine(target,"cache.bin"),new byte[8192]);File.WriteAllBytes(Path.Combine(target,"new.bin"),new byte[4096]);
                var settings=App.Services!.GetRequiredService<ISettingsService>().LoadSettings();var matcher=App.Services!.GetRequiredService<IPathWhitelistMatcher>();
                var result=await AnalyzePathsAsync(new JsonObject {["paths"]=new JsonArray(target)},CancellationToken.None,main,settings,matcher,path=>matcher.IsExcluded(path,settings.AiConversationWhitelist));
                var fresh=_analysisBatch!.Scopes.Single(item=>item.Path==target);
                _snapshotProbe=new JsonObject {["running"]=false,["result"]=result,["path"]=target,["old_id"]=oldId.ToString(),["new_id"]=fresh.Session.Id.ToString()};
            }
            catch(Exception ex){_snapshotProbe=new JsonObject {["running"]=false,["error"]=ex.ToString()};}
        }
        async Task PublishFixture(string mode)
        {
            try
            {
                var candidates=new JsonArray();
                if(mode!="publish-empty" && _analysisBatch is {} batch)
                    foreach(var scope in batch.Scopes)
                        candidates.Add(new JsonObject { ["path"]=Path.Combine(scope.Path,"cache.bin"),["category"]="BuildCache",["safety_rating"]="ReviewFirst",["explanation"]="已授权只读夹具候选；不执行删除。" });
                var result=await ExecuteAssistantOperationAsync("publish_recommendations",new JsonObject {["scan_id"]=_snapshotProbeBatch,["mode"]=mode=="publish-replace"?"replace":"append",["recommendations"]=candidates},CancellationToken.None);
                _snapshotProbe=new JsonObject {["running"]=false,["result"]=result};
            }
            catch(Exception ex){_snapshotProbe=new JsonObject {["running"]=false,["error"]=ex.ToString()};}
        }
    }
    internal object AcceptanceScanSnapshotState()
    {
        RefreshScanSnapshotEntries();
        return new { ProcessId=Environment.ProcessId,probe=_snapshotProbe,current_root=_treemapViewModel?.CurrentRoot?.Path,
            main_scan_id=(DataContext as MainViewModel)?.CurrentSession?.Id,main_files=(DataContext as MainViewModel)?.CurrentSession?.TotalFiles,main_size=(DataContext as MainViewModel)?.CurrentSession?.TotalSize,conversation_id=_chatViewModel?.CurrentConversationId,
            batch_id=_analysisBatch?.Id,error_message=_chatViewModel?.ErrorMessage,is_operation_running=_chatViewModel?.IsOperationRunning,can_change_conversation=_chatViewModel?.CanChangeConversation,recommendation_count=_recommendationsViewModel?.Recommendations.Count??0,
            tree_root=(TreeViewControl.DataContext as TreeViewModel)?.RootItems.FirstOrDefault()?.Entry.Path,
            recommendations=_recommendationsViewModel?.Recommendations.Select(item=>new {item.TargetPath,item.SourceScanId,item.Size}).ToArray(),
            cards=_chatViewModel?.Messages.SelectMany(message=>message.ScanSnapshots).Select(reference=>new {reference.Id,reference.Path,reference.State,reference.StatusText,reference.ActionText,reference.ScannedAt,reference.Files,reference.SizeBytes}).ToArray() };
    }
}
