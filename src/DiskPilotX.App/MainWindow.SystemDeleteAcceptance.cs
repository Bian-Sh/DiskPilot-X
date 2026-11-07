using System.IO;
using System.Text.Json.Nodes;
using DiskPilotX.App.Diagnostics;
using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Cleanup;

namespace DiskPilotX.App;

public partial class MainWindow
{
    private JsonObject? _systemDeleteProbe;
    private string? _systemDeleteFixture;
    private List<CleanupRecommendation>? _systemDeleteApproved;
    private DeletionMode _systemDeleteMode;

    internal object? AcceptanceSystemDeleteProgress(string? action)
    {
        var settings = Core.AppCompatibility.GetEnvironmentVariable("DISKPILOT_X_ACCEPTANCE_SETTINGS");
        if (!AcceptanceAutomationServer.IsEnabled() || string.IsNullOrWhiteSpace(settings))
            throw new InvalidOperationException("系统进度验收只允许独立验收设置目录。");
        if (action == "state") return _systemDeleteProbe?.DeepClone();
        if (_systemDeleteProbe?["running"]?.GetValue<bool>() == true || _cleanupRunning)
            throw new InvalidOperationException("上一项系统删除验收尚未结束。");
        if (action is "prepare-permanent" or "prepare-recycle")
        {
            _systemDeleteMode = action == "prepare-recycle" ? DeletionMode.MoveToRecycleBin : DeletionMode.PermanentDelete;
            _systemDeleteProbe = new JsonObject { ["running"] = true, ["phase"] = "preparing", ["count"] = 50000 };
            _ = Prepare();
        }
        else if (action == "run")
        {
            if (_systemDeleteApproved is null || _systemDeleteFixture is null)
                throw new InvalidOperationException("请先创建本任务系统进度夹具。");
            _systemDeleteProbe = new JsonObject { ["running"] = true, ["phase"] = "deleting", ["root"] = _systemDeleteFixture,
                ["mode"] = _systemDeleteMode.ToString(), ["count"] = 50000 };
            _ = Run();
        }
        else throw new InvalidOperationException("仅支持创建、执行及读取本任务系统进度夹具。");
        return _systemDeleteProbe?.DeepClone();

        async Task Prepare()
        {
            try
            {
                var parent = Path.GetFullPath(Path.GetDirectoryName(Path.GetFullPath(settings))!);
                var fixture = Path.Combine(parent, "system-progress-fixtures", Guid.NewGuid().ToString("N"));
                if (!fixture.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("系统进度夹具路径越界。");
                _systemDeleteFixture = Path.Combine(fixture, "本任务可删除的进度夹具");
                await Task.Run(() =>
                {
                    Directory.CreateDirectory(fixture);
                    File.WriteAllText(Path.Combine(fixture, "必须保留.txt"), "本任务未批准删除的邻接文件。");
                    var payload = new byte[256];
                    for (var directory = 0; directory < 200; directory++)
                    {
                        var folder = Path.Combine(_systemDeleteFixture, directory.ToString("D3"));
                        Directory.CreateDirectory(folder);
                        for (var file = 0; file < 250; file++) File.WriteAllBytes(Path.Combine(folder, file.ToString("D3") + ".bin"), payload);
                    }
                    // 用户已授权本任务生成的验收夹具；仍捕获指纹，执行前由服务再次核对。
                    var item = new CleanupRecommendation { Id = "progress-1", TargetPath = _systemDeleteFixture };
                    _systemDeleteApproved = CleanupPlan.Capture(new ScanSession(), [item], [item.Id], _systemDeleteMode).Items.ToList();
                });
                _systemDeleteProbe = new JsonObject { ["running"] = false, ["phase"] = "prepared", ["root"] = _systemDeleteFixture,
                    ["mode"] = _systemDeleteMode.ToString(), ["count"] = 50000 };
            }
            catch (Exception error) { _systemDeleteProbe = new JsonObject { ["running"] = false, ["phase"] = "failed", ["error"] = error.ToString() }; }
        }
        async Task Run()
        {
            _cleanupRunning = true;
            try
            {
                var results = await new CleanupService().ExecuteCleanupAsync(_systemDeleteApproved!, _systemDeleteMode,
                    new Progress<CleanupProgress>(), CancellationToken.None);
                _systemDeleteProbe!["results"] = System.Text.Json.JsonSerializer.SerializeToNode(results.Select(r => new
                    { result = r.Result.ToString(), r.FailureReason, r.ActualSizeFreed }));
                _systemDeleteProbe["phase"] = "completed";
            }
            catch (OperationCanceledException) { _systemDeleteProbe!["phase"] = "cancelled"; }
            catch (Exception error) { _systemDeleteProbe!["phase"] = "failed"; _systemDeleteProbe["error"] = error.ToString(); }
            finally
            {
                _cleanupRunning = false;
                _systemDeleteProbe!["running"] = false;
                _systemDeleteProbe["target_exists"] = Directory.Exists(_systemDeleteFixture);
                var kept = Path.Combine(Path.GetDirectoryName(_systemDeleteFixture!)!, "必须保留.txt");
                _systemDeleteProbe["kept_file"] = File.Exists(kept);
                _systemDeleteApproved = null;
            }
        }
    }
}