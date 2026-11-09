using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Data;
using System.Windows.Threading;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;
using DiskPilotX.App.Logging;
using DiskPilotX.App.Controls;
using DiskPilotX.App.Diagnostics;
using DiskPilotX.App.Helpers;
using DiskPilotX.App.Localization;
using DiskPilotX.App.Converters;
using DiskPilotX.App.ViewModels;
using DiskPilotX.App.Views;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Cleanup;

namespace DiskPilotX.App;

public partial class MainWindow
{
    private ScrollViewer? _consoleScroll;
    private bool _consoleFollowing = true, _consoleUpdating, _consoleScrollScheduled;
    private long? _consoleAnchor;
    private string _consoleQuery = "";
    private int _consoleBatchCount;

    private bool MatchesConsoleFilter(object entry) => entry is UiLogEntry logEntry && _visibleLogLevels.Includes(logEntry.Level) && MatchesConsoleSearch(logEntry);

    private bool MatchesConsoleSearch(UiLogEntry logEntry) => _consoleQuery.Length == 0 || logEntry.DisplayText.Contains(_consoleQuery, StringComparison.OrdinalIgnoreCase)
            || logEntry.SourceContext.Contains(_consoleQuery, StringComparison.OrdinalIgnoreCase);

    private sealed record ConsoleLevelSummary(string Label, string Icon, Brush Brush, int Count);

    private void UpdateConsoleLevelCounts()
    {
        var counts = new int[6];
        foreach (var entry in AppLog.Entries)
            if (MatchesConsoleSearch(entry)) counts[(int)entry.Level]++;
        string[] icons = ["\uE8A5", "\uE943", "\uE946", "\uE7BA", "\uEA39", "\uE814"];
        ConsoleLevelCounts.ItemsSource = Enum.GetValues<LogEventLevel>().Where(level => _visibleLogLevels.Includes(level)).Select(level =>
        {
            var sample = new UiLogEntry(default, level, "", null);
            return new ConsoleLevelSummary(sample.LevelText.Trim('[', ']'), icons[(int)level], sample.LevelBrush, counts[(int)level]);
        }).ToArray();
    }

    private void ConsoleFrame_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_logEntriesView is null) return;
        AppLog.UiSink.SetActive(ConsoleFrame.IsVisible);
        if (ConsoleFrame.IsVisible) ConsoleBatchUpdated();
    }

    private void ConsoleBatchUpdating()
    {
        _consoleUpdating = true;
        if (!_consoleFollowing && _consoleScroll is not null && ConsoleItemsControl.Items.Count > 0)
        {
            var index = Math.Clamp((int)_consoleScroll.VerticalOffset, 0, ConsoleItemsControl.Items.Count - 1);
            _consoleAnchor = (ConsoleItemsControl.Items[index] as UiLogEntry)?.Sequence;
        }
    }

    private void ConsoleBatchUpdated()
    {
        _consoleBatchCount++;
        UpdateConsoleLevelCounts();
        ConsoleLatestButton.Visibility = _consoleFollowing ? Visibility.Collapsed : Visibility.Visible;
        if (!ConsoleFrame.IsVisible || _consoleScrollScheduled) return;
        _consoleScrollScheduled = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            _consoleScrollScheduled = false;
            if (!ConsoleFrame.IsVisible) { _consoleUpdating = false; return; }
            _consoleScroll ??= FindConsoleScroll(ConsoleItemsControl);
            if (_consoleFollowing) ScrollLogToEnd();
            else if (_consoleScroll is not null && _consoleAnchor is { } sequence)
            {
                var index = 0;
                while (index < ConsoleItemsControl.Items.Count && ConsoleItemsControl.Items[index] is UiLogEntry entry && entry.Sequence < sequence) index++;
                _consoleScroll.ScrollToVerticalOffset(Math.Min(index, Math.Max(0, ConsoleItemsControl.Items.Count - 1)));
            }
            _consoleUpdating = false;
        });
    }

    private static ScrollViewer? FindConsoleScroll(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer scroll) return scroll;
            if (FindConsoleScroll(child) is { } nested) return nested;
        }
        return null;
    }

    private void ConsoleScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.OriginalSource is not ScrollViewer scroll || _consoleUpdating || e.ExtentHeightChange != 0) return;
        _consoleScroll = scroll;
        if (e.VerticalChange != 0) _consoleFollowing = scroll.VerticalOffset >= scroll.ScrollableHeight - 1;
        ConsoleLatestButton.Visibility = _consoleFollowing ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ConsoleLatest_Click(object sender, RoutedEventArgs e) { _consoleFollowing = true; _consoleAnchor = null; ConsoleBatchUpdated(); }
    private void ConsoleSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_logEntriesView is null) return;
        _consoleQuery = ConsoleSearchBox.Text.Trim();
        RefreshLogView();
    }

    private void ConsoleSearchClear_Click(object sender, RoutedEventArgs e)
    {
        ConsoleSearchBox.Clear();
        ConsoleSearchBox.Focus();
    }

    private void ConsoleOpenLogs_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(AppLog.LogDirectory) { UseShellExecute = true }); }
        catch (Exception error) { Log.Warning(error, "无法打开日志目录"); }
    }

    private void ConsoleItems_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.C || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        var text = string.Join(Environment.NewLine, ConsoleItemsControl.SelectedItems.Cast<UiLogEntry>().OrderBy(entry => entry.Sequence).Select(entry => entry.DisplayText));
        if (text.Length > 0) { try { Clipboard.SetText(text); } catch (Exception error) { Log.Warning(error, "日志复制失败"); } }
        e.Handled = true;
    }
    private void BottomPanelShadow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is FrameworkElement shadow && e.NewSize.Width > 0 && e.NewSize.Height > 0)
            shadow.Clip = CreateBottomPanelShadowClip(e.NewSize);
    }

    private static Geometry CreateBottomPanelShadowClip(Size size)
    {
        // 排除整个内部矩形，不让底板的投影渗入半透明文字区；圆角也不会向内部漏影。
        var clip = new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(new Rect(-24, -24, size.Width + 48, size.Height + 48)),
            new RectangleGeometry(new Rect(size)));
        clip.Freeze();
        return clip;
    }

    private void WriteConsoleLog(string message, LogEventLevel level = LogEventLevel.Information)
    {
        Log.Write(level, "{Message}", message);
    }

    private void RefreshLogView()
    {
        ConsoleBatchUpdating();
        _logEntriesView.Refresh();
        ConsoleBatchUpdated();
    }

    private void ScrollLogToEnd()
    {
        if (_consoleFollowing && ConsoleItemsControl.Items.Count > 0)
        {
            _consoleScroll ??= FindConsoleScroll(ConsoleItemsControl);
            ConsoleItemsControl.ScrollIntoView(ConsoleItemsControl.Items[^1]);
        }
    }

    private void LogLevelFilter_Click(object sender, RoutedEventArgs e)
    {
        _visibleLogLevels = AppLogLevelFilter.None;

        if (ConsoleLevelVerboseMenuItem.IsChecked)
            _visibleLogLevels |= AppLogLevelFilter.Verbose;
        if (ConsoleLevelDebugMenuItem.IsChecked)
            _visibleLogLevels |= AppLogLevelFilter.Debug;
        if (ConsoleLevelInfoMenuItem.IsChecked)
            _visibleLogLevels |= AppLogLevelFilter.Information;
        if (ConsoleLevelWarningMenuItem.IsChecked)
            _visibleLogLevels |= AppLogLevelFilter.Warning;
        if (ConsoleLevelErrorMenuItem.IsChecked)
            _visibleLogLevels |= AppLogLevelFilter.Error;
        if (ConsoleLevelFatalMenuItem.IsChecked)
            _visibleLogLevels |= AppLogLevelFilter.Fatal;

        RefreshLogView();
    }

    private void ConsoleFilterButton_Click(object sender, RoutedEventArgs e)
    {
        ConsoleFilterButton.ContextMenu.PlacementTarget = ConsoleFilterButton;
        ConsoleFilterButton.ContextMenu.IsOpen = true;
        e.Handled = true;
    }

    private void ConsoleClearMenuItem_Click(object sender, RoutedEventArgs e)
    {
        AppLog.UiSink.Clear();
    }

    private void ConsoleCopyButton_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: UiLogEntry entry } && !string.IsNullOrWhiteSpace(entry.DisplayText))
        {
            try
            {
                Clipboard.SetText(entry.DisplayText);
            }
            catch
            {
            }
        }

        e.Handled = true;
    }

    private void AppendAnalysisDiagnostics(AnalysisDiagnostics? diagnostics)
    {
        if (diagnostics is null)
        {
            WriteConsoleLog("DIAG: no diagnostics were produced; request likely failed before response parsing.");
            return;
        }

        WriteConsoleLog("DIAG: target=" + diagnostics.TargetPath);
        WriteConsoleLog("DIAG: scope=" + diagnostics.ScopePath + " focused=" + diagnostics.IsFocusedScope);
        WriteConsoleLog("DIAG: metadata_chars=" + diagnostics.MetadataLength + " response_chars=" + diagnostics.ResponseLength + " extracted_json_chars=" + diagnostics.ExtractedJsonLength);
        WriteConsoleLog("DIAG: parsed_recs=" + diagnostics.ParsedRecommendationCount + " protected_filtered=" + diagnostics.ProtectedFilteredCount + " missing_entry=" + diagnostics.MissingEntryCount + " malformed=" + diagnostics.MalformedRecommendationCount + " missing_fields=" + diagnostics.MissingFieldRecommendationCount);

        if (!string.IsNullOrWhiteSpace(diagnostics.ParseError))
        {
            WriteConsoleLog("DIAG: parse_error=" + diagnostics.ParseError, LogEventLevel.Warning);
        }

        if (!string.IsNullOrWhiteSpace(diagnostics.RawResponsePath))
        {
            WriteConsoleLog("DIAG: raw_response_path=" + diagnostics.RawResponsePath);
        }

        if (!string.IsNullOrWhiteSpace(diagnostics.ResponseEnvelopePath))
        {
            WriteConsoleLog("DIAG: response_envelope_path=" + diagnostics.ResponseEnvelopePath);
        }

        if (!string.IsNullOrWhiteSpace(diagnostics.StopReason))
        {
            WriteConsoleLog("DIAG: stop_reason=" + diagnostics.StopReason);
        }

        if (!string.IsNullOrWhiteSpace(diagnostics.ThinkingPath))
        {
            WriteConsoleLog("DIAG: thinking_path=" + diagnostics.ThinkingPath);
        }
    }

    private async void OnAnalyzeRequested()
    {
        if (_recommendationsViewModel is null || _chatViewModel is null || _recommendationsViewModel.IsAnalyzing || _chatViewModel.IsOperationRunning || _cleanupRunning || (DataContext as MainViewModel)?.IsScanning == true)
            return;

        var mainVm = DataContext as MainViewModel;
        if (mainVm?.CurrentSession?.RootEntry is null)
        {
            await ShowAppModalAsync(
                L.Text("AnalyzeNoScanTitle"),
                L.Text("AnalyzeNoScanMessage"),
                ModalMessageType.Info,
                ModalButtonFlags.Positive);
            return;
        }

        // Retrieve the API key from settings (checks actual saved key, not just validation flag)
        var settingsService = App.Services!.GetRequiredService<DiskPilotX.Core.Services.Settings.ISettingsService>();
        var loadedSettings = settingsService.LoadSettings();

        if (string.IsNullOrWhiteSpace(settingsService.GetApiKey(loadedSettings)))
        {
            var result = await ShowAppMessageAsync(
                L.Text("ApiKeyRequiredMessage"),
                L.Text("ApiKeyRequiredTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                OpenSettingsDialog();
            }

            // Re-check after settings dialog
            loadedSettings = settingsService.LoadSettings();
            if (string.IsNullOrWhiteSpace(settingsService.GetApiKey(loadedSettings)))
                return;
        }

        // If the user has drilled into a folder, scope the analysis to that subtree.
        // At the top level (CurrentRoot == scan root), analyze the whole drive.
        FileEntry? focusEntry = null;
        if (_treemapViewModel?.CurrentRoot is not null
            && _treemapViewModel.CurrentRoot != mainVm.CurrentSession.RootEntry)
        {
            focusEntry = _treemapViewModel.CurrentRoot;
        }

        // Show the panel immediately so the user sees the recommendations area
        EnsureBottomPanelVisible();
        ShowRecommendationsPanel();
        DebugBreakpoints.Hit("analyze-click");

        var focusPath = focusEntry?.Path ?? mainVm.CurrentSession.TargetPath;
        WriteConsoleLog(focusEntry is not null
            ? $"Analysis scope: {focusEntry.Path}"
            : $"Analysis scope: {mainVm.CurrentSession.TargetPath}");

        // 分析统一走对话：应用代发一条显式溯源的消息，证据仍由 AI 通过工具读取；
        // 覆盖已有建议的知情同意由 publish_recommendations 的聊天内确认卡片承担。
        var prompt = L.Format("AnalyzeChatPrompt", focusEntry?.Name ?? mainVm.CurrentSession.TargetPath);
        _recommendationsViewModel.BeginAnalysis();
        _recommendationsViewModel.IsAnalyzing = true;
        DebugBreakpoints.Hit("analyze-context-ready");
        try
        {
            var runResult = await _chatViewModel.SendAppAnalysisAsync(prompt, focusPath);
            DebugBreakpoints.Hit("analyze-command-returned");
            AppendAnalysisDiagnostics(runResult?.Analysis?.Diagnostics);
            if (runResult?.Task is { } task) _recommendationsViewModel.SetAnalysisTask(task);
            if (runResult is null)
            {
                WriteConsoleLog("Analysis request was rejected before the run started.", LogEventLevel.Warning);
                _recommendationsViewModel.SetAnalysisTask(new(Guid.NewGuid(), mainVm.CurrentSession.Id, focusPath,
                    DiskPilotX.Core.Services.Analysis.AnalysisTaskStatus.Failed, DiskPilotX.Core.Services.Analysis.AnalysisOutcome.Unknown, 0,
                    ["本轮分析未能启动，请查看聊天面板的提示。"], new Dictionary<string, string>()));
            }
        }
        finally
        {
            _recommendationsViewModel.IsAnalyzing = false;
        }
    }

}
