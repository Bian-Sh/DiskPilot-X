using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using DiskPilotX.App.Converters;
using DiskPilotX.App.Services;
using DiskPilotX.App.Views;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Settings;
using DiskPilotX.Core.Services.Skills;
using DiskPilotX.Core.Services.Whitelist;
namespace DiskPilotX.App;
public partial class MainWindow
{
    private sealed record SkillPanelTask(string Id,string Digest,SkillTaskCard Card,SkillTaskWindow? Window=null);
    private readonly Dictionary<string,SkillPanelTask> skillPanels = [];
    private static readonly JsonSerializerOptions SkillJson = new() { PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower };
    private static string SkillPackageText(CleanupSkillPackage package,string key,string fallback)
    {
        var text=new SkillTextCatalog(package.Localizations).Get(key,DiskPilotX.App.Localization.L.CurrentLanguageName);
        return text==key?fallback:SkillTextCatalog.Bind(key);
    }
    private JsonNode ShowSkillPanel(JsonNode? args)
    {
        try
        {
            var package=App.Services!.GetRequiredService<CleanupSkillStore>().LoadReadOnly(args?["id"]?.GetValue<string>()??"");
            var key=package.Id+":"+package.Digest;
            if(!skillPanels.TryGetValue(key,out var task))
            {
                var card=new SkillTaskCard(key,package.Id,SkillPackageText(package,"skill.name",package.Title),
                    args?["summary"]?.GetValue<string>()??SkillPackageText(package,"skill.description",package.Description),package.Localizations);
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
            var store=App.Services!.GetRequiredService<CleanupSkillStore>(); var package=store.LoadReadOnly(task.Id);
            if(package.Digest!=task.Digest) throw new InvalidOperationException("规则已经更新，请让助手重新打开面板。");
            if(task.Window is null)
            {
                var data=new JsonObject { ["scope_path"]=_treemapViewModel?.CurrentRoot?.Path??"",["summary"]=package.Description };
                var window=new SkillTaskWindow(SkillPackageText(package,"skill.name",package.Title),package.UiXaml,data,(command,fields,ct)=>ExecuteSkillPanelAsync(task,command,fields,ct),package.Localizations) { Owner=this };
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
        var store=App.Services!.GetRequiredService<CleanupSkillStore>(); var package=store.LoadReadOnly(task.Id);
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
        var package=store.LoadReadOnly(args?["id"]?.GetValue<string>()??""); var scope=args?["scope_path"]?.GetValue<string>()??"";
        if(package.Source is null) return AssistantError("该规则没有自定义源码。");
        var parameters=(args?["parameters"] as JsonObject)?.ToDictionary(p=>p.Key,p=>p.Value?.ToString()??"");
        var diagnostics=await source.ValidateSourceAsync(package.Id,ct);
        if(!diagnostics.Success) return new JsonObject { ["ok"]=false,["diagnostics"]=JsonSerializer.SerializeToNode(diagnostics.Diagnostics,SkillJson) };
        var isProvider=diagnostics.Contract=="provider";
        if(!isProvider)
        {
            if(!IsSkillScopeAllowed(scope)) return AssistantError("源码检查范围为空或包含已排除的目录。");
        }
        var purpose=isProvider?"运行这份应用数据发现逻辑？":"运行这份自定义识别逻辑？";
        var detail=isProvider?package.Title+"\n包摘要："+package.Digest:package.Title+"\n检查范围："+scope+"\n包摘要："+package.Digest;
        if(_chatViewModel is null || !await _chatViewModel.ConfirmAsync(purpose,detail+"\n\n"+package.Source,
            "这份 C# 会以当前用户权限运行，可访问文件和网络，不是安全沙箱。仅对你信任的源码继续；清理仍需另行确认。",ct))
            return new JsonObject { ["ok"]=false,["cancelled"]=true };
        var authorization=source.Authorize(package.Id,package.Digest);
        if(isProvider)
        {
            // 注入宿主能力桥：源码的一切本机读取都经由此处，便于审计与降级。
            var root=(DataContext as ViewModels.MainViewModel)?.CurrentSession?.RootEntry;
            var host=new DefaultSkillHostBridge(package.Id,root,parameters,message=>WriteConsoleLog(message));
            var discovery=await source.RunDiscoveryAsync(authorization,host,ct,parameters);
            if(discovery.View is {} view && _chatViewModel is not null)
            {
                // 气泡里永远只留一张入口卡；surface=auto 时再顺手把完整视图弹到新窗口。
                view = view with { Localizations = view.Localizations ?? package.Localizations };
                _chatViewModel.AddView(view);
                if(view.AutoOpensWindow) _chatViewModel.OpenSkillWindow(view);
            }
            return JsonSerializer.SerializeToNode(new{ok=true,contract="provider",count=discovery.Entries.Count,entries=discovery.Entries},SkillJson) as JsonObject ?? new JsonObject{["ok"]=true};
        }
        var preview=await source.RunAsync(authorization,scope,ct,parameters,DiskPilotX.App.Localization.L.GetState());
        var result=(JsonObject)JsonSerializer.SerializeToNode(preview,SkillJson)!; result["ok"]=true; return result;
    }

    /// <summary>在新窗口中打开按需视图；用户的勾选结果回传给助手继续处理。</summary>
    private void OpenSkillWindow(SkillUiSpec spec)
    {
        var window = new SkillWindow(spec) { Owner = this };
        window.ContinueRequested += (source, items) =>
        {
            var payload = DescribeSelection(items,source.Localizations);
            window.Close();
            if (_chatViewModel is null || items.Count == 0) return;
            // 多选一律由宿主先把全部范围无头分析掉，再让模型一次性发布：
            // 模型不一定自觉调 analyze_paths，逐个 scan_path 只会一次次覆盖同一份清单。
            if (CountablePaths(items) >= 2) { _ = BatchSelectedItemsAsync(items, previewOnly: false,source.Localizations); return; }
            _chatViewModel.ExplorePromptCommand.Execute(
                "我在这份视图里选中了以下条目，请据此继续；需要时进入清理分析，但清理前必须先让我确认清单：\n" + payload + MultiSelectionHint(items.Count));
        };
        window.PreviewRequested += (source, items) =>
        {
            var payload = DescribeSelection(items,source.Localizations);
            window.Close();
            if (_chatViewModel is null || items.Count == 0) return;
            if (CountablePaths(items) >= 2) { _ = BatchSelectedItemsAsync(items, previewOnly: true,source.Localizations); return; }
            _chatViewModel.ExplorePromptCommand.Execute(
                "请预览这些条目的清理候选，先读取真实扫描证据，不要直接清理：\n" + payload + MultiSelectionHint(items.Count));
        };

        // 条目的行内动作（现在主要是 Steam 卡的「卸载」）：由宿主执行，执行前必须拿到强确认。
        window.ItemActionRequested += (source, item, action) => _ = HandleItemActionAsync(item, action, window);
        window.Show();
    }

    /// <summary>
    /// 处理条目行内动作。卸载类走「强确认 → Steam 原生卸载 → 等待 → 把结果交给模型总结」这条闭环，
    /// 期间不做任何空间分析、也不把条目回传去做清理推荐：游戏卸载本来就该止步于卸载本身。
    /// </summary>
    private async Task HandleItemActionAsync(SkillUiItem item, SkillUiItemAction action, Window source)
    {
        if (_chatViewModel is null) return;
        var displayName = string.IsNullOrWhiteSpace(item.Title) ? ("应用 " + action.Target) : item.Title!;

        if (action.IsOpenPath)
        {
            OpenInExplorer(item.Path ?? action.Target);
            return;
        }
        if (!action.IsUninstall)
        {
            _chatViewModel.ErrorMessage = "暂不支持这个动作：" + action.Label;
            return;
        }

        var appId = string.IsNullOrWhiteSpace(action.Target) ? item.AppId : action.Target;
        if (string.IsNullOrWhiteSpace(appId))
        {
            _chatViewModel.ErrorMessage = "缺少应用编号，无法交给 Steam 卸载。";
            return;
        }

        var installer = new SteamGameUninstaller();
        var target = installer.Resolve(appId!, item.Path);
        if (target is null)
        {
            _chatViewModel.ErrorMessage = "在本机的 Steam 卸载记录里找不到《" + displayName + "》，请直接在 Steam 里卸载。";
            return;
        }

        if (action.ConfirmRequired)
        {
            var confirmed = await Dispatcher.InvokeAsync(() => UninstallConfirmWindow.Confirm(
                displayName, appId!, string.IsNullOrWhiteSpace(item.Value) ? FormatBytes(item.SizeBytes) : item.Value,
                target.InstallPath, source));
            if (!confirmed) { WriteConsoleLog("已取消卸载：" + displayName); return; }
        }

        WriteConsoleLog("正在卸载：" + displayName + "（appid " + appId + "）");
        SteamUninstallResult result;
        try { result = await installer.RunAsync(target, item.SizeBytes, message => WriteConsoleLog(message)); }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or UnauthorizedAccessException)
        {
            _chatViewModel.ErrorMessage = "卸载未能完成：" + ex.Message;
            return;
        }

        await _chatViewModel.ExplorePromptCommand.ExecuteAsync(BuildUninstallSummary(displayName, target, result));
    }

    /// <summary>卸载结果交给模型做总结；事实部分全部由宿主给出，模型只负责组织语言。</summary>
    private static string BuildUninstallSummary(string displayName, SteamUninstallTarget target, SteamUninstallResult result)
    {
        var freed = result.FreedBytes > 0 ? FileSizeConverter.FormatSize(result.FreedBytes) : "未知";
        var header = result.IsDone
            ? "我已经卸载了 Steam 游戏《" + displayName + "》（appid " + target.AppId + "）。"
            : "我请求卸载 Steam 游戏《" + displayName + "》（appid " + target.AppId + "），但没有确认完成。";
        var builder = new System.Text.StringBuilder(header);
        builder.Append("\n事实（由宿主给出，请直接采用，不要重新扫描或改写数字）：");
        builder.Append("\n- 结果：").Append(result.IsDone ? "Steam 已完成卸载" : result.Detail);
        builder.Append("\n- 卸载方式：Steam 原生卸载（").Append(target.Executable).Append(' ').Append(target.Arguments).Append(")");
        builder.Append("\n- 卸载前占用：").Append(result.BeforeBytes > 0 ? FileSizeConverter.FormatSize(result.BeforeBytes) : "未知");
        builder.Append("\n- 释放空间：").Append(freed);
        if (!string.IsNullOrWhiteSpace(result.Detail)) builder.Append("\n- 备注：").Append(result.Detail);
        builder.Append("\n\n请用一两句给我一个收尾总结：卸载了什么、释放了多少空间、是否需要我重新扫描一次才能看到最新占用。");
        builder.Append("不要再建议清理这个文件目录下的其他东西，也不要调用任何工具。");
        return builder.ToString();
    }

    private void OpenInExplorer(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _chatViewModel!.ErrorMessage = "无法打开位置：" + ex.Message;
        }
    }

    private static string FormatBytes(long bytes) => bytes > 0 ? FileSizeConverter.FormatSize(bytes) : "未统计";

    private static string DescribeSelection(IReadOnlyList<SkillUiItem> items,Dictionary<string,Dictionary<string,string>>? localizations = null)
    {
        if (items.Count == 0) return "（未选择任何条目）";
        return string.Join("\n", items.Select(item =>
            string.IsNullOrWhiteSpace(item.Path)
                ? $"- {DiskPilotX.App.Localization.SkillTextExtension.Resolve(item.Title ?? "",localizations)}"
                : $"- {DiskPilotX.App.Localization.SkillTextExtension.Resolve(item.Title ?? "",localizations)} → {item.Path}"));
    }

    private static int CountablePaths(IReadOnlyList<SkillUiItem> items) =>
        items.Count(item => !string.IsNullOrWhiteSpace(item.Path));

    /// <summary>
    /// 多选回传的确定性处理：宿主先跑一次无头多范围分析（不动 Treemap / 文件树），
    /// 再把「全部范围的证据 + 批次号」作为一条用户消息交给模型，模型只需一次
    /// publish_recommendations 就把候选合并进「推荐清理」。这样既不依赖模型自觉，
    /// 也不会出现先分析 A、分析 B 时又把 A 覆盖掉的诡异行为。
    /// </summary>
    private async Task BatchSelectedItemsAsync(IReadOnlyList<SkillUiItem> items, bool previewOnly, Dictionary<string,Dictionary<string,string>>? localizations = null)
    {
        if (_chatViewModel is null) return;
        var payload = DescribeSelection(items,localizations);
        AssistantAnalysisBatch? batch = null;
        string? evidence = null;
        try
        {
            if (DataContext is ViewModels.MainViewModel main && _recommendationsViewModel is not null)
            {
                var settings = App.Services!.GetRequiredService<ISettingsService>().LoadSettings();
                var matcher = App.Services!.GetRequiredService<IPathWhitelistMatcher>();
                bool Hidden(string? path) => matcher.IsExcluded(path, settings.AiConversationWhitelist);
                var arguments = new JsonObject
                {
                    ["note"] = "技能视图多选",
                    ["paths"] = new JsonArray([.. items.Select(item => item.Path)
                        .Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => (JsonNode)path!)])
                };
                var result = await AnalyzePathsAsync(arguments, CancellationToken.None, main, settings, matcher, Hidden);
                batch = _analysisBatch;
                if (result is JsonObject ok && ok["ok"]?.GetValue<bool>() == true)
                    evidence = ok["evidence"]?.GetValue<string>();
                if (batch is null || batch.Scopes.Count == 0)
                    WriteConsoleLog("无头多范围分析未完成：" + (result["error"]?.GetValue<string>() ?? "未知原因"));
            }
        }
        catch (Exception ex)
        {
            WriteConsoleLog($"无头多范围分析失败：{ex.Message}");
        }

        var prompt = batch is { Scopes.Count: > 0 } && evidence is not null
            ? BuildBatchPrompt(items, previewOnly, batch, evidence,localizations)
            : (previewOnly
                ? "请预览这些条目的清理候选，先读取真实扫描证据，不要直接清理：\n"
                : "我在这份视图里选中了以下条目，请据此继续；需要时进入清理分析，但清理前必须先让我确认清单：\n")
              + payload + MultiSelectionHint(items.Count);
        await _chatViewModel.ExplorePromptCommand.ExecuteAsync(prompt);
    }

    /// <summary>把「宿主已经分析好」的事实写进提问里，让模型直接发布而不是再扫一遍。</summary>
    private static string BuildBatchPrompt(IReadOnlyList<SkillUiItem> items, bool previewOnly,
        AssistantAnalysisBatch batch, string evidence,Dictionary<string,Dictionary<string,string>>? localizations = null)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append(previewOnly
            ? "我在这份视图里一次选中了多个条目，全部范围已经无头扫描完成。请预览它们的清理候选（不要清理）：\n"
            : "我在这份视图里一次选中了多个条目，全部范围已经无头扫描完成，进入清理分析；清理前必须先让我确认清单：\n");
        builder.Append(DescribeSelection(items,localizations)).Append("\n\n");
        builder.Append("这些范围已经一次性扫描完毕（批次 scan_id=").Append(batch.Id).Append("，共 ")
               .Append(batch.Scopes.Count).Append(" 个范围，互不覆盖）。下面是全部范围的真实证据。\n");
        builder.Append("请据此用 mode=replace 调用一次 publish_recommendations 发布全部候选；")
               .Append("不要再调用 analyze_paths 或 scan_path，也不要分多次 publish（第二次发布会覆盖第一次的结果）。\n");
        builder.Append("每个候选的 path 必须落在上面某个范围之内。\n\n");
        builder.Append(evidence);
        return builder.ToString();
    }

    /// <summary>
    /// 多选时的处理约定：推荐清理面板只有一份清单，逐个发布必然互相覆盖。
    /// 明确要求模型一次 analyze_paths 收全部路径、一次 publish_recommendations 发全部候选。
    /// </summary>
    private static string MultiSelectionHint(int count) => count <= 1
        ? string.Empty
        : $"\n\n（我一次选了 {count} 个条目：请调用一次 analyze_paths 传入上面全部路径，"
          + "拿到全部证据后再调用一次 publish_recommendations 发布全部候选。不要逐个扫描逐个发布，"
          + "推荐清理面板只有一份清单，第二次发布会覆盖第一次的结果。）";
}
