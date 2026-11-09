using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DiskPilotX.App.Diagnostics;
using DiskPilotX.App.Logging;
using Serilog;

namespace DiskPilotX.App;

public partial class MainWindow
{
    private Task? _logFixtureTask;
    private long _logFixtureElapsed;
    private string? _logFixtureError;

    internal object AcceptanceLogFixture(string? text)
    {
        if (!AcceptanceAutomationServer.IsEnabled() || string.IsNullOrWhiteSpace(Core.AppCompatibility.GetEnvironmentVariable("DISKPILOT_X_ACCEPTANCE_SETTINGS")))
            throw new InvalidOperationException("日志夹具必须使用验收模式和独立设置目录。");
        if (_logFixtureTask is { IsCompleted: false }) throw new InvalidOperationException("上一批日志尚未完成。");
        using var request = JsonDocument.Parse(text ?? "{}");
        var count = request.RootElement.TryGetProperty("count", out var number) ? number.GetInt32() : 100_000;
        var length = request.RootElement.TryGetProperty("length", out var size) ? size.GetInt32() : 32;
        if (count is < 1 or > 200_000 || length is < 0 or > 200_000) throw new InvalidOperationException("日志夹具参数越界。");
        AppLog.UiSink.Clear();
        _logFixtureError = null;
        _logFixtureTask = Task.Run(() =>
        {
            var watch = Stopwatch.StartNew();
            try
            {
                var payload = new string('测', length);
                var logger = Log.ForContext("SourceContext", "日志验收");
                for (var index = 0; index < count; index++) logger.Information("验收日志 {Index} {Payload:l}", index, payload);
                try { throw new InvalidOperationException("异常\t列内Tab\r\n详情\"引号\""); }
                catch (Exception exception) { logger.Error(exception, "分列标记\t中文\r\n第二行\"引号\""); }
            }
            catch (Exception exception) { _logFixtureError = exception.ToString(); }
            finally { _logFixtureElapsed = watch.ElapsedMilliseconds; }
        });
        return AcceptanceLogState();
    }

    internal object AcceptanceLogState()
    {
        var state = AppLog.UiSink.State;
        var batch = AppLog.UiSink.BatchMetrics;
        using var process = Process.GetCurrentProcess();
        return new { running = _logFixtureTask is { IsCompleted: false }, error = _logFixtureError, elapsed_ms = _logFixtureElapsed,
            buffered_count = state.Count, text_characters = state.TextCharacters, version = state.Version, active = state.Active, scheduled = state.Scheduled,
            displayed = AppLog.Entries.Count, visible = ConsoleItemsControl.Items.Count, realized = LogDescendants(ConsoleItemsControl).OfType<ListBoxItem>().Count(),
            batch_count = _consoleBatchCount, batch_operations = batch.Operations, batch_ms = batch.DurationMilliseconds,
            maximum_batch_operations = batch.MaximumOperations, maximum_batch_ms = batch.MaximumDurationMilliseconds,
            batch_entry_limit = UiLogSink.MaxBatchEntries, batch_budget_ms = UiLogSink.BatchBudgetMilliseconds,
            following = _consoleFollowing, offset = _consoleScroll?.VerticalOffset, scrollable = _consoleScroll?.ScrollableHeight,
            level_counts = ConsoleLevelCounts.Items.Cast<ConsoleLevelSummary>().Select(item => new { level = item.Label, count = item.Count, icon = item.Icon }).ToArray(),
            search_ui = new { text = ConsoleSearchBox.Text, placeholder = ConsoleSearchPlaceholder.IsVisible, icon = ConsoleSearchIcon.IsVisible,
                clear = ConsoleSearchClearButton.IsVisible, focused = ConsoleSearchBox.IsFocused, radius = ConsoleSearchField.CornerRadius.TopLeft,
                width = ConsoleSearchField.ActualWidth,
                right = ConsoleSearchField.TransformToAncestor(this).Transform(new Point(ConsoleSearchField.ActualWidth, 0)).X,
                counts_left = ConsoleLevelCounts.TransformToAncestor(this).Transform(new Point(0, 0)).X },
            query = _consoleQuery, working_set = process.WorkingSet64, log_directory = AppLog.LogDirectory, diagnostics_directory = CrashDiagnostics.LogDirectory,
            first = (ConsoleItemsControl.Items.Count > 0 ? ConsoleItemsControl.Items[0] as UiLogEntry : null)?.Message,
            last = (ConsoleItemsControl.Items.Count > 0 ? ConsoleItemsControl.Items[^1] as UiLogEntry : null)?.Message };
    }

    internal object AcceptanceLogCrash(string? kind)
    {
        if (!AcceptanceAutomationServer.IsEnabled() || Core.AppCompatibility.GetEnvironmentVariable("DISKPILOT_X_ACCEPTANCE_LOG_CRASH") != "1"
            || string.IsNullOrWhiteSpace(Core.AppCompatibility.GetEnvironmentVariable("DISKPILOT_X_ACCEPTANCE_SETTINGS")))
            throw new InvalidOperationException("未授权此验收进程的可控异常或退出。");
        if (kind == "recoverable")
        {
            try { throw new InvalidOperationException("可恢复诊断\t字段\n第二行"); }
            catch (Exception exception) { CrashDiagnostics.CaptureException("日志验收可恢复异常", exception); }
        }
        else if (kind == "exit") { _closeConfirmed = true; Dispatcher.BeginInvoke(Close); }
        else if (kind == "crash")
        {
            _ = Task.Run(async () => { await Task.Delay(250); _ = Dispatcher.BeginInvoke(new Action(() => throw new InvalidOperationException("受控崩溃\t中文\r\n完整堆栈\"引号\""))); });
        }
        else throw new InvalidOperationException("未知日志验收动作。");
        return new { accepted = true, kind };
    }

    private static IEnumerable<DependencyObject> LogDescendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var nested in LogDescendants(child)) yield return nested;
        }
    }
}
