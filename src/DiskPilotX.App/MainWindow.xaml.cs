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

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
/// <summary>
/// Lightweight data object for breadcrumb dropdown items锛堥潰鍖呭睉涓嬫媺鏁版嵁椤癸級
/// </summary>
internal record BreadcrumbItem(string Name, string Path, FileEntry? Entry = null);

public partial class MainWindow : Window
{
    private const double DefaultRecommendationsHeight = 260;
    private const int WM_GETMINMAXINFO = 0x0024;

    private readonly ICollectionView _logEntriesView;
    private AppLogLevelFilter _visibleLogLevels = AppLogLevelFilter.Information | AppLogLevelFilter.Warning | AppLogLevelFilter.Error | AppLogLevelFilter.Fatal;
    private RecommendationsViewModel? _recommendationsViewModel;
    private TreemapViewModel? _treemapViewModel;
    private SettingsViewModel? _settingsViewModel;
    private ChatViewModel? _chatViewModel;
    private UpdateViewModel? _updateViewModel;
    private AcceptanceAutomationServer? _acceptanceAutomationServer;
    private readonly Dictionary<string, ScanSession> _aiScanSessionsByRoot = new(StringComparer.OrdinalIgnoreCase);
    private string? _displayPathOverride;
    private bool _suppressSelectedPathNavigation;
    private bool _closeConfirmed;
    private bool _closePromptShowing;

    public MainWindow()
    {
        InitializeComponent();
        _logEntriesView = CollectionViewSource.GetDefaultView(AppLog.Entries);
        _logEntriesView.Filter = entry => entry is UiLogEntry logEntry && _visibleLogLevels.Includes(logEntry.Level);
        ConsoleItemsControl.ItemsSource = _logEntriesView;
        AppLog.UiSink.EntryAdded += ScrollLogToEnd;
        Log.Information("MainWindow initialized; log directory: {LogDirectory}", AppLog.LogDirectory);
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        StateChanged += MainWindow_StateChanged;
        Closed += (_, _) =>
        {
            Log.Information("MainWindow closed");
            _chatViewModel?.CancelPendingInteractions();
            App.Services?.GetService<DiskPilotX.Core.Services.Assistant.DiskAssistantService>()?.CancelCurrentRun();
            foreach(var panel in skillPanels.Values) if(panel.Window is { } taskWindow) { taskWindow.Owner=null; taskWindow.Close(); }
            AppLog.UiSink.EntryAdded -= ScrollLogToEnd;
            _acceptanceAutomationServer?.Dispose();
        };
        TitleBar.CloseRequested += async (_, _) => await RequestCloseAsync();
        DiskPilotX.App.Localization.L.LanguageChanged += OnAppLanguageChanged;
    }


    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        Log.Information("MainWindow closing requested; confirmed={CloseConfirmed}", _closeConfirmed);
        if (_closeConfirmed)
            return;

        e.Cancel = true;
        _ = RequestCloseAsync();
    }

    private async Task RequestCloseAsync()
    {
        if (_closePromptShowing)
            return;

        _closePromptShowing = true;
        try
        {
            var result = await ShowAppModalAsync(
                L.Text("CloseAppTitle"),
                L.Text("CloseAppMessage"),
                ModalMessageType.Warning,
                ModalButtonFlags.Positive | ModalButtonFlags.Negative);

            Log.Information("Close confirmation result: {Result}", result);
            if (result == ModalResult.Positive)
            {
                _closeConfirmed = true;
                Close();
            }
        }
        finally
        {
            _closePromptShowing = false;
        }
    }
    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Log.Information("MainWindow loaded");
        // Enable DWM backdrop and dark mode
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        AcrylicHelper.EnableDarkMode(hwnd);
        AcrylicHelper.EnableMica(hwnd);

        // Hook WndProc for WM_GETMINMAXINFO (maximize bounds)
        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(WndProc);

        if (DataContext is MainViewModel mainVm)
        {
            mainVm.PropertyChanged += MainViewModel_PropertyChanged;
            TreemapView.SetScanningState(mainVm.IsScanning, mainVm.ScanTitleText, mainVm.ScanProgressText);

            // Set default path to first available drive
            if (string.IsNullOrEmpty(mainVm.SelectedPath))
            {
                var firstDrive = DriveInfo.GetDrives()
                    .Where(d => d.IsReady)
                    .Select(d => d.RootDirectory.FullName)
                    .FirstOrDefault();
                if (firstDrive is not null)
                    mainVm.SelectedPath = firstDrive;
            }

            // Always rebuild breadcrumb on startup 鈥?belt-and-suspenders with PropertyChanged
            RebuildBreadcrumbBar();
        }

        _acceptanceAutomationServer ??= AcceptanceAutomationServer.StartIfEnabled(this);
    }

}
