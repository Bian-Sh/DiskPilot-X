using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using DiskPilotX.App.Diagnostics;
using DiskPilotX.App.Services;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Assistant;

namespace DiskPilotX.App;

public partial class MainWindow
{
    private JsonObject? _cleanupFixtureProbe;
    private string? _cleanupFixtureRoot;
    private const int CleanupFixtureCount = 181;

    internal object AcceptanceCleanupFixture(string? mode)
    {
        var settingsPath = Core.AppCompatibility.GetEnvironmentVariable("DISKPILOT_X_ACCEPTANCE_SETTINGS");
        if (!AcceptanceAutomationServer.IsEnabled() || string.IsNullOrWhiteSpace(settingsPath)
            || mode is not ("manual" or "assistant" or "changed"))
            throw new InvalidOperationException("仅支持独立验收目录中的181项本任务清理夹具。");
        if (_cleanupRunning) throw new InvalidOperationException("上一项清理尚未结束。");
        _cleanupFixtureProbe = new JsonObject { ["preparing"]=true, ["mode"]=mode };
        _ = Prepare();
        return _cleanupFixtureProbe;

        async Task Prepare()
        {
            try
            {
                if (DataContext is not MainViewModel main || _recommendationsViewModel is null || _chatViewModel is null)
                    throw new InvalidOperationException("工作区未就绪。");
                var parent = Path.GetFullPath(Path.GetDirectoryName(Path.GetFullPath(settingsPath))!);
                var root = Path.Combine(parent, "cleanup-fixtures", Guid.NewGuid().ToString("N"));
                if (!root.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("夹具目录越界。");
                _cleanupFixtureRoot = root;
                await Task.Run(() =>
                {
                    Directory.CreateDirectory(root);
                    File.WriteAllText(Path.Combine(root, "必须保留.txt"), "本任务未选中的保留资料，不得删除。");
                    for (var index=0; index<CleanupFixtureCount; index++)
                    {
                        var path=Path.Combine(root,$"cache-{index:000}");
                        Directory.CreateDirectory(path);
                        File.WriteAllText(Path.Combine(path,"payload.bin"),$"本任务专用可删除夹具 {index:000}");
                    }
                });
                _recommendationsViewModel.BeginExternalRecommendationLoad();
                var session=await main.ScanHeadlessAsync(root,CancellationToken.None);
                var batch=new AssistantAnalysisBatch();
                batch.AddOrReplace(new(){Path=root,Label="181项清理回归夹具",Session=session});
                _analysisBatch=batch;RegisterScanSnapshot(session,analysis:true);
                if (_settingsViewModel is null) throw new InvalidOperationException("设置未就绪。");
                // 删除方式只写入独立验收设置；不访问用户设置或用户推荐目标。
                _settingsViewModel.SelectedDeletionMode=DeletionMode.PermanentDelete;
                for(var offset=0;offset<CleanupFixtureCount;offset+=100)
                {
                    var rows=new JsonArray();
                    for(var index=offset;index<Math.Min(CleanupFixtureCount,offset+100);index++)
                        rows.Add(new JsonObject { ["path"]=Path.Combine(root,$"cache-{index:000}"),
                            ["category"]="BuildCache",["safety_rating"]="ReviewFirst",["explanation"]="本任务新建的独立清理验收夹具，不是用户文件。" });
                    var published=await ExecuteAssistantOperationAsync("publish_recommendations",new JsonObject
                    { ["scan_id"]=batch.Id.ToString(),["mode"]="merge",["recommendations"]=rows },CancellationToken.None);
                    if(published["ok"]?.GetValue<bool>()!=true) throw new InvalidOperationException(published["error"]?.GetValue<string>()??"夹具发布失败。");
                }
                var ids=new JsonArray(_recommendationsViewModel.Recommendations.Select(item=>JsonValue.Create(item.Id)).ToArray());
                await ExecuteAssistantOperationAsync("select_recommendations",new JsonObject{["ids"]=ids,["selected"]=true},CancellationToken.None);
                _cleanupFixtureProbe=new JsonObject{["preparing"]=false,["prepared"]=true,["mode"]=mode,["root"]=root,["initial_count"]=CleanupFixtureCount};
                if (mode=="manual") return;
                var state=await ExecuteAssistantOperationAsync("list_recommendations",null,CancellationToken.None);
                var result=await ExecuteAssistantOperationAsync("cleanup_recommendations",new JsonObject
                { ["ids"]=ids.DeepClone(),["version"]=state["version"]?.DeepClone() },CancellationToken.None);
                _cleanupFixtureProbe["result"]=result;
            }
            catch(Exception error)
            { _cleanupFixtureProbe=new JsonObject{["preparing"]=false,["error"]=error.ToString()}; }
        }
    }

    internal object AcceptanceCleanupFixtureState()
    {
        if (!AcceptanceAutomationServer.IsEnabled()) throw new InvalidOperationException("验收未启用。");
        var root=_cleanupFixtureRoot;
        return new { probe=_cleanupFixtureProbe, running=_cleanupRunning,
            selected_count=_recommendationsViewModel?.Recommendations.Count(item=>item.IsAccepted),
            recommendation_count=_recommendationsViewModel?.Recommendations.Count,
            existing_targets=root is null?0:Enumerable.Range(0,CleanupFixtureCount).Count(index=>Directory.Exists(Path.Combine(root,$"cache-{index:000}"))),
            kept_file=root is not null && File.Exists(Path.Combine(root,"必须保留.txt")),
            kept_content=root is not null && File.Exists(Path.Combine(root,"必须保留.txt"))?File.ReadAllText(Path.Combine(root,"必须保留.txt")):null,
            deletion_mode=CurrentDeletionMode.ToString() };
    }
}
