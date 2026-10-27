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
    private void WriteConsoleLog(string message, LogEventLevel level = LogEventLevel.Information)
    {
        Log.Write(level, "{Message}", message);
    }

    private void RefreshLogView()
    {
        _logEntriesView.Refresh();
        ScrollLogToEnd();
    }

    private void ScrollLogToEnd()
    {
        if (ConsoleScrollViewer is not null)
        {
            ConsoleScrollViewer.ScrollToEnd();
        }
    }

    private void LogLevelFilter_Click(object sender, RoutedEventArgs e)
    {
        _visibleLogLevels = AppLogLevelFilter.None;

        if (ConsoleLevelVerboseMenuItem.IsChecked)
            _visibleLogLevels |= AppLogLevelFilter.Verbose;
        if (ConsoleLevelInfoMenuItem.IsChecked)
            _visibleLogLevels |= AppLogLevelFilter.Information;
        if (ConsoleLevelWarningMenuItem.IsChecked)
            _visibleLogLevels |= AppLogLevelFilter.Warning;
        if (ConsoleLevelErrorMenuItem.IsChecked)
            _visibleLogLevels |= AppLogLevelFilter.Error | AppLogLevelFilter.Fatal;

        RefreshLogView();
    }

    private void ConsoleFilterButton_Click(object sender, RoutedEventArgs e)
    {
        ConsoleFilterButton.ContextMenu.PlacementTarget = ConsoleFilterButton;
        ConsoleFilterButton.ContextMenu.IsOpen = true;
        e.Handled = true;
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
