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
using SpaceMonger.App.Logging;
using SpaceMonger.App.Controls;
using SpaceMonger.App.Diagnostics;
using SpaceMonger.App.Helpers;
using SpaceMonger.App.Localization;
using SpaceMonger.App.Converters;
using SpaceMonger.App.ViewModels;
using SpaceMonger.App.Views;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Cleanup;

namespace SpaceMonger.App;

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
        if (_recommendationsViewModel is null || _settingsViewModel is null || _recommendationsViewModel.IsAnalyzing || _chatViewModel?.IsOperationRunning == true || _cleanupRunning || (DataContext as MainViewModel)?.IsScanning == true)
            return;

        var mainVm = DataContext as MainViewModel;
        if (mainVm?.CurrentSession is null)
        {
            await ShowAppModalAsync(
                L.Text("AnalyzeNoScanTitle"),
                L.Text("AnalyzeNoScanMessage"),
                ModalMessageType.Info,
                ModalButtonFlags.Positive);
            return;
        }

        // Retrieve the API key from settings (checks actual saved key, not just validation flag)
        var settingsService = App.Services!.GetRequiredService<SpaceMonger.Core.Services.Settings.ISettingsService>();
        var loadedSettings = settingsService.LoadSettings();
        var apiKey = settingsService.GetApiKey(loadedSettings);

        if (string.IsNullOrWhiteSpace(apiKey))
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
            apiKey = settingsService.GetApiKey(loadedSettings);
            if (string.IsNullOrWhiteSpace(apiKey))
                return;
        }

        // Warn if re-running analysis will replace the recommendations currently visible in the ScrollView.
        if (_recommendationsViewModel.HasAnyRecommendations)
        {
            var confirmResult = await ShowAppMessageAsync(
                L.Text("ConfirmReanalysisMessage"),
                L.Text("ConfirmReanalysisTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirmResult != MessageBoxResult.Yes)
                return;
        }

        // Show the panel immediately so the user sees the loading indicator
        EnsureBottomPanelVisible();
        ShowRecommendationsPanel();
        DebugBreakpoints.Hit("analyze-click");

        // If the user has drilled into a folder, scope the analysis to that subtree.
        // At the top level (CurrentRoot == scan root), analyze the whole drive.
        FileEntry? focusEntry = null;
        if (_treemapViewModel?.CurrentRoot is not null
            && _treemapViewModel.CurrentRoot != mainVm.CurrentSession.RootEntry)
        {
            focusEntry = _treemapViewModel.CurrentRoot;
        }

        _recommendationsViewModel.SetContext(
            mainVm.CurrentSession,
            SpaceMonger.Core.Services.Llm.ModelEndpointConfig.FromSettings(
                loadedSettings, apiKey!, loadedSettings.AnalysisModelName ?? string.Empty,
                settingsService.GetProxyPassword(loadedSettings)),
            loadedSettings.Language,
            focusEntry);
        DebugBreakpoints.Hit("analyze-context-ready");

        var scopeLabel = focusEntry is not null
            ? L.Format("AnalyzingFolderStatus", focusEntry.Name)
            : L.Text("AnalyzingScanResultsStatus");
        mainVm.ScanProgressText = scopeLabel;
        WriteConsoleLog(scopeLabel);
        WriteConsoleLog(focusEntry is not null
            ? $"Analysis scope: {focusEntry.Path}"
            : $"Analysis scope: {mainVm.CurrentSession.TargetPath}");
        await _recommendationsViewModel.AnalyzeCommand.ExecuteAsync(null);
        DebugBreakpoints.Hit("analyze-command-returned");

        if (_recommendationsViewModel.AnalysisError is not null)
        {
            mainVm.ScanProgressText = L.Format("AnalysisFailedStatus", _recommendationsViewModel.AnalysisError);
            WriteConsoleLog(mainVm.ScanProgressText, LogEventLevel.Error);
            AppendAnalysisDiagnostics(_recommendationsViewModel.LastDiagnostics);
        }
        else
        {
            var count = _recommendationsViewModel.Recommendations.Count;
            mainVm.ScanProgressText = L.Format("AnalysisCompleteStatus", count, count == 1 ? "" : "s");
            WriteConsoleLog(mainVm.ScanProgressText);
            AppendAnalysisDiagnostics(_recommendationsViewModel.LastDiagnostics);
            if (count == 0)
            {
                WriteConsoleLog("DIAG: zero final recommendations. Inspect parsed_recs/protected_filtered/parse_error above to determine whether this was an empty model result, parse failure, or post-filtering.");
            }
        }
    }

}
