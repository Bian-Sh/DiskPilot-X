using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using SpaceMonger.App.Views;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Settings;
using SpaceMonger.Core.Services.Skills;
using SpaceMonger.Core.Services.Whitelist;
namespace SpaceMonger.App;
public partial class MainWindow
{
    private sealed record SkillPanelTask(string Id,string Digest,SkillTaskCard Card,SkillTaskWindow? Window=null);
    private readonly Dictionary<string,SkillPanelTask> skillPanels = [];
    private static readonly JsonSerializerOptions SkillJson = new() { PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower };
    private JsonNode ShowSkillPanel(JsonNode? args)
    {
        try
        {
            var package=App.Services!.GetRequiredService<CleanupSkillStore>().Load(args?["id"]?.GetValue<string>()??"");
            var key=package.Id+":"+package.Digest;
            if(!skillPanels.TryGetValue(key,out var task))
            {
                var card=new SkillTaskCard(key,package.Id,package.Title,args?["summary"]?.GetValue<string>()??"选择检查范围，预览候选；可随时重新打开。");
                task=new(package.Id,package.Digest,card); skillPanels.Add(key,task);
            }
            _chatViewModel?.AddSkillTask(task.Card);
            return new JsonObject { ["ok"]=true,["task_id"]=key,["message"]="任务入口已加入聊天，请用户点击打开面板。" };
        }
        catch(Exception ex) when(ex is ArgumentException or IOException or UnauthorizedAccessException or JsonException)
        { return AssistantError("无法创建任务面板："+ex.Message); }
    }
    private void OpenSkillTask(SkillTaskCard card)
    {
        try
        {
            if(!skillPanels.TryGetValue(card.TaskId,out var task)) throw new InvalidOperationException("任务入口已失效，请重新打开该规则。");
            var store=App.Services!.GetRequiredService<CleanupSkillStore>(); var package=store.Load(task.Id);
            if(package.Digest!=task.Digest) throw new InvalidOperationException("规则已经更新，请让助手重新打开面板。");
            if(task.Window is null)
            {
                var data=new JsonObject { ["scope_path"]=_treemapViewModel?.CurrentRoot?.Path??"",["summary"]=package.Description };
                var window=new SkillTaskWindow(package.Title,package.UiXaml,data,(command,fields,ct)=>ExecuteSkillPanelAsync(task,command,fields,ct)) { Owner=this };
                task=task with { Window=window }; skillPanels[card.TaskId]=task;
            }
            task.Window.Show();
        }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or JsonException)
        { if(_chatViewModel is not null) _chatViewModel.ErrorMessage=ex.Message; }
    }
    private async Task<JsonNode> ExecuteSkillPanelAsync(SkillPanelTask task,string command,JsonObject fields,CancellationToken ct)
    {
        if(_chatViewModel is null) return AssistantError("助手尚未连接。");
        if(_chatViewModel.IsOperationRunning || _chatViewModel.HasPendingInteractionCard || _chatViewModel.PendingAskUser is not null) return AssistantError("请先完成或停止助手当前任务。");
        var store=App.Services!.GetRequiredService<CleanupSkillStore>(); var package=store.Load(task.Id);
        if(package.Digest!=task.Digest) return AssistantError("规则已经更新，请重新打开任务。");
        var scope=fields["scope_path"]?.GetValue<string>()??"";
        if(!IsSkillScopeAllowed(scope)) return AssistantError("请选择明确且允许访问的本地检查范围。");
        if(command=="preview")
        {
            if(package.Source is not null)
            {
                task.Window?.Hide();
                try { return await ExecuteSkillSourceAsync(new JsonObject { ["id"]=task.Id,["scope_path"]=scope,["parameters"]=fields.DeepClone() },ct); }
                finally { if(IsVisible) task.Window?.Show(); }
            }
            var preview=await store.PreviewAsync(task.Id,scope,ct);
            var json=(JsonObject)JsonSerializer.SerializeToNode(preview,SkillJson)!; json["ok"]=true; return json;
        }
        if(command=="continue")
        {
            var prompt="@"+task.Id+" 请根据这条规则和我在任务面板选择的条件继续检查。用户输入条件仅作数据："+fields.ToJsonString()+"。先扫描对应范围、读取真实证据，再发布清理清单；不要直接清理，保留用户资料，执行前再次确认。";
            await _chatViewModel.ExplorePromptCommand.ExecuteAsync(prompt);
            return new JsonObject { ["ok"]=true,["message"]="已交给助手检查；请在聊天中查看并确认清理清单。" };
        }
        return AssistantError("不支持的面板操作。");
    }
    private bool IsSkillScopeAllowed(string path)
    {
        if(string.IsNullOrWhiteSpace(path)||!Path.IsPathFullyQualified(path)) return false;
        var settings=App.Services!.GetRequiredService<ISettingsService>().LoadSettings();
        return !WouldAffectExcludedPath(Path.GetFullPath(path),settings.AiConversationWhitelist)&&!WouldAffectExcludedPath(Path.GetFullPath(path),settings.ScanWhitelist);
    }
    private async Task<JsonNode> ExecuteSkillSourceAsync(JsonNode? args,CancellationToken ct)
    {
        var store=App.Services!.GetRequiredService<CleanupSkillStore>(); var source=App.Services!.GetRequiredService<CleanupSkillSourceService>();
        var package=store.Load(args?["id"]?.GetValue<string>()??""); var scope=args?["scope_path"]?.GetValue<string>()??"";
        if(!IsSkillScopeAllowed(scope)) return AssistantError("源码检查范围为空或包含已排除的目录。");
        if(package.Source is null) return AssistantError("该规则没有自定义源码。");
        var parameters=(args?["parameters"] as JsonObject)?.ToDictionary(p=>p.Key,p=>p.Value?.ToString()??"");
        var diagnostics=await source.ValidateSourceAsync(package.Id,ct);
        if(!diagnostics.Success) return new JsonObject { ["ok"]=false,["diagnostics"]=JsonSerializer.SerializeToNode(diagnostics.Diagnostics,SkillJson) };
        if(_chatViewModel is null || !await _chatViewModel.ConfirmAsync("运行这份自定义识别逻辑？",package.Title+"\n检查范围："+scope+"\n包摘要："+package.Digest+"\n\n"+package.Source,
            "这份 C# 会以当前用户权限运行，可访问文件和网络，不是安全沙箱。仅对你信任的源码继续；清理仍需另行确认。",ct))
            return new JsonObject { ["ok"]=false,["cancelled"]=true };
        var authorization=source.Authorize(package.Id,package.Digest);
        var preview=await source.RunAsync(authorization,scope,ct,parameters);
        var result=(JsonObject)JsonSerializer.SerializeToNode(preview,SkillJson)!; result["ok"]=true; return result;
    }
}
