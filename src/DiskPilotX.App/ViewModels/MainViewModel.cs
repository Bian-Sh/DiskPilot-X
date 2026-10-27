using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using DiskPilotX.App.Converters;
using DiskPilotX.App.Diagnostics;
using DiskPilotX.App.Localization;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Scanning;

namespace DiskPilotX.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IFileScanner _fileScanner;
    private readonly ILogger<MainViewModel> _logger;
    private CancellationTokenSource? _scanCts;
    private Action? _externalScanCancellation;
    private bool _lastScanCancelled;
    private string? _scanTitleResourceKey;
    private object?[] _scanTitleResourceArgs = [];

    [ObservableProperty]
    private string? _selectedPath;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isCopilotBusy;

    partial void OnIsCopilotBusyChanged(bool value) => ScanCommand.NotifyCanExecuteChanged();

    [ObservableProperty]
    private bool _isExternalScan;

    [ObservableProperty]
    private string? _scanTitleText;

    [ObservableProperty]
    private string? _scanProgressText;

    [ObservableProperty]
    private ScanSession? _currentSession;


    [ObservableProperty]
    private List<string> _driveList = new();

    [ObservableProperty]
    private string _driveCapacity = "--";

    [ObservableProperty]
    private string _usedSpace = "--";

    [ObservableProperty]
    private string _freeSpace = "--";

    [ObservableProperty]
    private string _fileCount = "--";

    [ObservableProperty]
    private string _folderCount = "--";

    /// <summary>
    /// Reference to the recommendations view model, set from MainWindow.SetViewModels.
    /// Used by the AnalyzeButton DataTrigger binding in MainWindow.xaml.
    /// </summary>
    [ObservableProperty]
    private RecommendationsViewModel? _recommendationsVM;

    /// <summary>
    /// Reference to the update view model, set from MainWindow.SetUpdateViewModel.
    /// Used for status bar version display binding.
    /// </summary>
    [ObservableProperty]
    private UpdateViewModel? _updateVM;

    public event Action<ScanSession>? ScanCompleted;

    public MainViewModel(IFileScanner fileScanner, ILogger<MainViewModel>? logger = null)
    {
        _fileScanner = fileScanner;
        _logger = logger ?? NullLogger<MainViewModel>.Instance;
        _logger.LogInformation("MainViewModel created");
        _fileScanner.IsReadyChanged += () =>
        {
            // Marshal to UI thread 鈥?the event may fire from a background thread.
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                ScanCommand.NotifyCanExecuteChanged);
        };

        DriveList = DriveInfo.GetDrives()
            .Where(d => d.IsReady)
            .Select(d => d.Name)
            .ToList();

        if (DriveList.Count > 0)
        {
            SelectedPath = DriveList[0];
        }

        L.LanguageChanged += OnLanguageChanged;
    }

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync(CancellationToken cancellationToken)
    {
        var scanTarget = SelectedPath;

        if (string.IsNullOrWhiteSpace(scanTarget))
            return;
        scanTarget = ScanPathResolver.Resolve(scanTarget);
        SelectedPath = scanTarget;


        _logger.LogInformation("Scan command started for {ScanTarget}", scanTarget);
        IsScanning = true;
        _lastScanCancelled = false;
        SetLocalizedScanTitle("ScanningStatus");
        ScanProgressText = string.Empty;
        _scanCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                ScanProgressText = p.FileCount > 0 || p.FolderCount > 0
                    ? L.Format("ScanProgressStatus", p.CurrentPath, p.FileCount, p.FolderCount)
                    : p.CurrentPath;
            });

            var session = await _fileScanner.ScanAsync(scanTarget, progress, _scanCts.Token);
            if (session.IsCancelled || _scanCts.IsCancellationRequested)
            {
                _logger.LogWarning("Scan cancelled for {ScanTarget}; hasRoot={HasRoot}, files={Files}, folders={Folders}", scanTarget, session.RootEntry is not null, session.TotalFiles, session.TotalFolders);
                _lastScanCancelled = true;
                ScanProgressText = L.Text("ScanCancelledStatus");
                return;
            }

            _logger.LogInformation("Scan completed for {ScanTarget}; files={Files}, folders={Folders}, hasRoot={HasRoot}", scanTarget, session.TotalFiles, session.TotalFolders, session.RootEntry is not null);
            DebugBreakpoints.Hit("scan-returned");
            session.Provenance ??= new("manual");
            CurrentSession = session;
            UpdateStatusBar(session);
            DebugBreakpoints.Hit("scan-session-set");
            ScanCompleted?.Invoke(session);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Scan cancelled by exception for {ScanTarget}", scanTarget);
            _lastScanCancelled = true;
            ScanProgressText = L.Text("ScanCancelledStatus");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("whitelist", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(ex, "Scan target blocked by whitelist: {ScanTarget}", scanTarget);
            ScanProgressText = L.Text("ScanTargetExcludedByWhitelist");
        }
        finally
        {
            _logger.LogInformation("Scan command finished for {ScanTarget}", scanTarget);
            IsScanning = false;
            _scanCts?.Dispose();
            _scanCts = null;
        }
    }

    public async Task<ScanSession> ScanForAssistantAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (IsScanning || !_fileScanner.IsReady) throw new InvalidOperationException("已有扫描任务或扫描器正在准备。");
        var previous = CurrentSession?.Id;
        SelectedPath = path;
        await ScanAsync(ct);
        ct.ThrowIfCancellationRequested();
        // 用户主动中断要作为独立的取消信号反馈给模型，不能与真实失败混为同一种异常。
        if (_lastScanCancelled || CurrentSession?.IsCancelled == true)
            throw new OperationCancelledByUserException("用户中断了扫描。");
        if (CurrentSession is null || CurrentSession.Id == previous)
            throw new InvalidOperationException("本次扫描没有产生完整结果。");
        return CurrentSession;
    }

    private bool CanScan() => !IsCopilotBusy && !IsScanning && SelectedPath is not null && _fileScanner.IsReady;

    partial void OnIsScanningChanged(bool value)
    {
        ScanCommand.NotifyCanExecuteChanged();
        CancelScanCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedPathChanged(string? value)
    {
        ScanCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanCancelScan))]
    private void CancelScan()
    {
        if (_scanCts is not null)
        {
            _logger.LogInformation("Scan cancellation requested");
            _scanCts.Cancel();
            return;
        }

        _logger.LogInformation("External scan cancellation requested");
        _externalScanCancellation?.Invoke();
    }

    private bool CanCancelScan() => IsScanning;

    public IDisposable BeginExternalScan(string titleText, string? progressText, Action cancel)
    {
        _logger.LogInformation("External scan started: {TitleText}", titleText);
        _externalScanCancellation = cancel;
        IsExternalScan = true;
        SetRawScanTitle(titleText);
        ScanProgressText = progressText ?? string.Empty;
        IsScanning = true;
        return new ExternalScanScope(this);
    }

    public void SetRawScanTitle(string? titleText)
    {
        _scanTitleResourceKey = null;
        _scanTitleResourceArgs = [];
        ScanTitleText = titleText;
    }

    public void SetLocalizedScanTitle(string resourceKey, params object?[] args)
    {
        _scanTitleResourceKey = resourceKey;
        _scanTitleResourceArgs = args;
        RefreshLocalizedScanTitle();
    }

    private void EndExternalScan()
    {
        _logger.LogInformation("External scan ended");
        _externalScanCancellation = null;
        IsExternalScan = false;
        SetRawScanTitle(null);
        IsScanning = false;
    }

    private void OnLanguageChanged()
    {
        RefreshLocalizedScanTitle();
    }

    private void RefreshLocalizedScanTitle()
    {
        if (_scanTitleResourceKey is null)
        {
            return;
        }

        ScanTitleText = _scanTitleResourceArgs.Length == 0
            ? L.Text(_scanTitleResourceKey)
            : L.Format(_scanTitleResourceKey, _scanTitleResourceArgs);
    }

    private sealed class ExternalScanScope(MainViewModel owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            owner.EndExternalScan();
        }
    }


    [RelayCommand]
    private Task BrowseAsync()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog();
        if (dialog.ShowDialog() == true)
        {
            SelectedPath = dialog.FolderName;
            _logger.LogInformation("Browse selected path {SelectedPath}", SelectedPath);
        }
        return Task.CompletedTask;
    }

    public void UpdateStatusBar(ScanSession session)
    {
        DriveCapacity = session.DriveCapacity is not null
            ? FileSizeConverter.FormatSize(session.DriveCapacity.Value)
            : "--";

        FreeSpace = session.DriveFreeSpace is not null
            ? FileSizeConverter.FormatSize(session.DriveFreeSpace.Value)
            : "--";

        UsedSpace = session.DriveCapacity is not null && session.DriveFreeSpace is not null
            ? FileSizeConverter.FormatSize(session.DriveCapacity.Value - session.DriveFreeSpace.Value)
            : "--";

        FileCount = session.TotalFiles.ToString("N0");
        FolderCount = session.TotalFolders.ToString("N0");
    }
}
