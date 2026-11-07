using DiskPilotX.App.Diagnostics;

namespace DiskPilotX.App;

public partial class MainWindow
{
    private object? _pingAcceptanceResult;
    internal object AcceptanceTreemapPing(string? path)
    {
        if (!AcceptanceAutomationServer.IsEnabled() || string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("验收入口未启用或路径无效。");
        _pingAcceptanceResult = new { Running = true, Path = path };
        _ = Run();
        return _pingAcceptanceResult;
        async Task Run()
        {
            try
            {
                SetAssistantView("treemap");
                bool hit = await TreemapView.FocusPathAsync(path, true);
                _pingAcceptanceResult = new { Running = false, Hit = hit, Path = path };
            }
            catch (Exception ex) { _pingAcceptanceResult = new { Running = false, Hit = false, Error = ex.Message }; }
        }
    }
    internal object? AcceptanceTreemapPingState() => _pingAcceptanceResult;
}
