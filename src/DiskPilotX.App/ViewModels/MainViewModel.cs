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
    private ScanExecutionInfo? _lastScanExecution;
    public ScanExecutionInfo? LastScanExecution => Volatile.Read(ref _lastScanExecution);
    private ScanExecutionInfo? _lastCompletedScanExecution;
    public ScanExecutionInfo? LastCompletedScanExecution => Volatile.Read(ref _lastCompletedScanExecution);
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
        var execution = new ScanExecutionInfo(scanTarget, ScanMethod.Unknown, ScanExecutionStatus.Running, DateTime.Now);
        Volatile.Write(ref _lastScanExecution, execution);

        try
        {
            var uiProgress = new Progress<ScanProgress>(p =>
            {
                ScanProgressText = p.FileCount > 0 || p.FolderCount > 0
                    ? L.Format("ScanProgressStatus", p.CurrentPath, p.FileCount, p.FolderCount)
                    : p.CurrentPath;
            });

            var progress = new ExecutionProgress(p =>
            {
                if (p.Execution is { } reported) { execution = reported; Volatile.Write(ref _lastScanExecution, reported); }
            }, uiProgress);
            var session = await _fileScanner.ScanAsync(scanTarget, progress, _scanCts.Token);
            session.Execution ??= execution with { Status = session.IsCancelled ? ScanExecutionStatus.Cancelled : ScanExecutionStatus.Completed, CompletedAt = session.EndTime ?? DateTime.Now };
            Volatile.Write(ref _lastScanExecution, session.Execution);
            if (session.IsCancelled || _scanCts.IsCancellationRequested)
            {
                _logger.LogWarning("Scan cancelled for {ScanTarget}; hasRoot={HasRoot}, files={Files}, folders={Folders}", scanTarget, session.RootEntry is not null, session.TotalFiles, session.TotalFolders);
                _lastScanCancelled = true;
                Volatile.Write(ref _lastScanExecution, session.Execution with { Status = ScanExecutionStatus.Cancelled });
                ScanProgressText = L.Text("ScanCancelledStatus");
                return;
            }

            _logger.LogInformation("Scan completed for {ScanTarget}; files={Files}, folders={Folders}, hasRoot={HasRoot}", scanTarget, session.TotalFiles, session.TotalFolders, session.RootEntry is not null);
            DebugBreakpoints.Hit("scan-returned");
            session.Provenance ??= new("manual");
            Volatile.Write(ref _lastCompletedScanExecution, session.Execution);
            CurrentSession = session;
            UpdateStatusBar(session);
            DebugBreakpoints.Hit("scan-session-set");
            ScanCompleted?.Invoke(session);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Scan cancelled by exception for {ScanTarget}", scanTarget);
            _lastScanCancelled = true;
            Volatile.Write(ref _lastScanExecution, execution with { Status = ScanExecutionStatus.Cancelled, CompletedAt = DateTime.Now });
            ScanProgressText = L.Text("ScanCancelledStatus");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("whitelist", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(ex, "Scan target blocked by whitelist: {ScanTarget}", scanTarget);
            ScanProgressText = L.Text("ScanTargetExcludedByWhitelist");
            Volatile.Write(ref _lastScanExecution, execution with { Status = ScanExecutionStatus.Failed, CompletedAt = DateTime.Now });
        }
        catch (Exception)
        {
            Volatile.Write(ref _lastScanExecution, execution with { Status = ScanExecutionStatus.Failed, CompletedAt = DateTime.Now });
            throw;
        }
        finally
        {
            _logger.LogInformation("Scan command finished for {ScanTarget}", scanTarget);
            IsScanning = false;
            _scanCts?.Dispose();
            _scanCts = null;
        }
    }

    private sealed class ExecutionProgress(Action<ScanProgress> observe, IProgress<ScanProgress> ui) : IProgress<ScanProgress>
    {
        public void Report(ScanProgress value) { observe(value); ui.Report(value); }
    }

    public async Task<bool> ConfirmAssistantScanAsync(string path, bool isElevated,
        Func<string, string, string?, CancellationToken, Task<bool>> confirm, CancellationToken ct, bool replacesWorkspace = true)
    {
        ct.ThrowIfCancellationRequested();
        if (isElevated && (!replacesWorkspace || CurrentSession is null)) return true;
        var warning = isElevated ? "" : "当前 APP 未以管理员方式启动，MFT 快速扫描可能不可用，回退普通遍历会明显更慢。建议先手动以管理员方式重启；管理员权限也不保证所有路径都能使用 MFT。";
        var title = isElevated ? "替换当前扫描结果" : "非管理员扫描提醒";
        var impact = !replacesWorkspace ? "继续将只读扫描所选范围，不替换当前扫描结果。"
            : CurrentSession is null ? "继续将开始扫描，不会自动提权或重启。" : "继续将更新 Treemap、文件树与当前分析上下文。";
        try
        {
            if (await confirm(title, $"扫描 {path}\n{warning}", impact, ct)) return true;
        }
        catch (OperationCanceledException)
        {
            Volatile.Write(ref _lastScanExecution, new(path, ScanMethod.Unknown, ScanExecutionStatus.CancelledBeforeStart, CompletedAt: DateTime.Now));
            throw;
        }
        Volatile.Write(ref _lastScanExecution, new(path, ScanMethod.Unknown, ScanExecutionStatus.CancelledBeforeStart, CompletedAt: DateTime.Now));
        return false;
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

    /// <summary>
    /// 无头扫描：只拿到一份真实的扫描会话用于分析，不替换主工作区的 Treemap / 文件树 / 状态栏。
    /// 多范围分析（例如用户一次勾了 2 个游戏）靠它同时容纳多份扫描结果，互不覆盖。
    /// </summary>
    public async Task<ScanSession> ScanHeadlessAsync(string path, CancellationToken ct, IProgress<ScanProgress>? progress = null)
    {
        ct.ThrowIfCancellationRequested();
        if (!_fileScanner.IsReady) throw new InvalidOperationException("扫描器正在准备，请稍后再试。");
        var target = ScanPathResolver.Resolve(path ?? string.Empty);
        if (string.IsNullOrWhiteSpace(target)) throw new ArgumentException("路径为空。");
        var execution = new ScanExecutionInfo(target, ScanMethod.Unknown, ScanExecutionStatus.Running, DateTime.Now);
        Volatile.Write(ref _lastScanExecution, execution);
        var observed = new ExecutionProgress(p =>
        {
            if (p.Execution is { } reported) { execution = reported; Volatile.Write(ref _lastScanExecution, reported); }
        }, progress ?? new Progress<ScanProgress>());
        try
        {
            var session = await _fileScanner.ScanAsync(target, observed, ct);
            session.Execution ??= execution with { Status = session.IsCancelled ? ScanExecutionStatus.Cancelled : ScanExecutionStatus.Completed, CompletedAt = session.EndTime ?? DateTime.Now };
            execution = session.Execution;
            Volatile.Write(ref _lastScanExecution, execution);
            if (session.IsCancelled || ct.IsCancellationRequested) throw new OperationCancelledByUserException("用户中断了扫描。");
            if (session.RootEntry is null) throw new InvalidOperationException($"无法读取 {target}。");
            Volatile.Write(ref _lastCompletedScanExecution, execution);
            return session;
        }
        catch (Exception ex)
        {
            var cancelled = ex is OperationCanceledException or OperationCancelledByUserException;
            Volatile.Write(ref _lastScanExecution, execution with { Status = cancelled ? ScanExecutionStatus.Cancelled : ScanExecutionStatus.Failed, CompletedAt = DateTime.Now });
            throw;
        }
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
