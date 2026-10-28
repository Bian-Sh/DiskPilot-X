using System.IO;
using System.Collections.ObjectModel;
using DiskPilotX.Core.Services.Settings;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskPilotX.App.Localization;
using DiskPilotX.App.Services;

namespace DiskPilotX.App.ViewModels;

public partial class UpdateViewModel : ObservableObject
{
    private readonly IUpdateService _updateService;
    private readonly ISettingsService? _settingsService;
    private bool _initializing = true;
    private string? _downloadedPath;
    private int _historyPage;

    public UpdateViewModel(IUpdateService updateService, ISettingsService? settingsService = null)
    {
        _updateService = updateService;
        _settingsService = settingsService;
        CurrentVersion = _updateService.GetCurrentVersion();
        var settings = settingsService?.LoadSettings();
        AutoCheckUpdates = settings?.AutoCheckUpdates ?? true;
        LoadProxySettings(settings);
        _initializing = false;
        CommitProxySettings();
        UpdateStatusText();
        L.LanguageChanged += () =>
        {
            OnPropertyChanged(nameof(ProxyKindOptions));
            OnPropertyChanged(nameof(SelectedProxyKind));
        };
    }

    [ObservableProperty]
    private string _currentVersion = "";

    [ObservableProperty]
    private bool _updateAvailable;

    [ObservableProperty]
    private string _latestVersion = "";

    [ObservableProperty]
    private string _releaseNotes = "";

    [ObservableProperty]
    private string _publishedAt = "";

    [ObservableProperty]
    private string? _msiDownloadUrl;

    [ObservableProperty]
    private string? _msiFileName;

    [ObservableProperty]
    private long _msiFileSize;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private bool _isDownloadComplete;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private bool _checkCompleted;

    [ObservableProperty] private bool _isChecking;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string _errorText = "";
    [ObservableProperty] private bool _autoCheckUpdates = true;
    [ObservableProperty] private bool _isHistoryLoading;
    [ObservableProperty] private bool _historyHasError;
    [ObservableProperty] private bool _canLoadMoreHistory;
    [ObservableProperty] private bool _historyLoaded;
    [ObservableProperty] private UpdateRelease? _selectedRelease;
    public ObservableCollection<UpdateRelease> Releases { get; } = [];
    public bool IsUpdateEntryVisible => IsChecking || HasError || UpdateAvailable || IsDownloading || IsDownloadComplete;
    public bool IsHistoryEmpty => HistoryLoaded && !IsHistoryLoading && !HistoryHasError && Releases.Count == 0;
    public string PrimaryActionText => IsDownloadComplete ? L.Text("InstallButton") : IsDownloading ? L.Text("CancelButton") : L.Text("UpdateEntry");
    public bool CanUsePrimaryAction => !IsChecking && (IsDownloadComplete || IsDownloading || !string.IsNullOrWhiteSpace(MsiDownloadUrl));
    public string UpdateEntryGlyph => HasError ? "\uE7BA" : IsDownloadComplete ? "\uE930" : IsDownloading || IsChecking ? "\uE895" : "\uE896";
    public string UpdateEntryHint => IsDownloading ? $"{DownloadProgress:F0}%"
        : IsChecking ? L.Text("UpdateChecking")
        : HasError ? L.Text("UpdateRetry")
        : IsDownloadComplete ? L.Text("InstallButton") : "";
    public bool CanCheck => !IsChecking && !IsDownloading && !IsDownloadComplete;
    private CancellationTokenSource? _downloadCts;

    partial void OnAutoCheckUpdatesChanged(bool value) => SavePreferences();
    private void SavePreferences()
    {
        if (_initializing || _settingsService is null) return;
        // 总是读取最新设置，避免覆盖其它设置分区刚保存的内容。
        var settings = _settingsService.LoadSettings();
        settings.AutoCheckUpdates = AutoCheckUpdates;
        _settingsService.SaveSettings(settings);
    }
    partial void OnUpdateAvailableChanged(bool value) => UpdateStatusText();
    partial void OnLatestVersionChanged(string value) => UpdateStatusText();
    partial void OnIsDownloadingChanged(bool value) => UpdateStatusText();
    partial void OnDownloadProgressChanged(double value) => UpdateStatusText();
    partial void OnIsDownloadCompleteChanged(bool value) => UpdateStatusText();
    partial void OnCurrentVersionChanged(string value) => UpdateStatusText();
    partial void OnIsCheckingChanged(bool value) => UpdateStatusText();
    partial void OnHasErrorChanged(bool value) => UpdateStatusText();
    partial void OnIsHistoryLoadingChanged(bool value) => OnPropertyChanged(nameof(IsHistoryEmpty));
    partial void OnHistoryLoadedChanged(bool value) => OnPropertyChanged(nameof(IsHistoryEmpty));
    partial void OnHistoryHasErrorChanged(bool value) => OnPropertyChanged(nameof(IsHistoryEmpty));
    partial void OnMsiDownloadUrlChanged(string? value) => OnPropertyChanged(nameof(CanUsePrimaryAction));

    public void UpdateStatusText()
    {
        StatusText = IsDownloading ? L.Format("DownloadingFormat", LatestVersion, ((int)DownloadProgress).ToString())
            : IsChecking ? L.Text("UpdateChecking")
            : HasError ? ErrorText
            : IsDownloadComplete ? L.Format("DownloadReadyFormat", LatestVersion)
            : UpdateAvailable ? L.Format("UpdateAvailableFormat", LatestVersion)
            : CheckCompleted ? L.Text("UpdateUpToDate") : $"v{CurrentVersion}";
        OnPropertyChanged(nameof(IsUpdateEntryVisible));
        OnPropertyChanged(nameof(PrimaryActionText));
        OnPropertyChanged(nameof(UpdateEntryGlyph));
        OnPropertyChanged(nameof(UpdateEntryHint));
        OnPropertyChanged(nameof(CanUsePrimaryAction));
        OnPropertyChanged(nameof(CanCheck));
    }

    public async Task CheckAtStartupAsync()
    {
        if (!AutoCheckUpdates) return;
        await CheckAsync(false);
        // 自动检查只在发现新版且没有完整缓存时后台下载，不自动启动安装器。
        if (UpdateAvailable && !HasError && !IsDownloadComplete && !string.IsNullOrWhiteSpace(MsiDownloadUrl))
            await DownloadCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private Task CheckForUpdateAsync() => CheckAsync(true);

    private async Task CheckAsync(bool forceRefresh)
    {
        if (!CanCheck) return;
        IsChecking = true;
        HasError = false;
        try
        {
            var result = await _updateService.CheckForUpdateAsync(default, forceRefresh);
            if (!result.Success) { SetError("UpdateCheckFailed"); return; }
            LatestVersion = result.LatestVersion;
            ReleaseNotes = result.ReleaseNotes;
            PublishedAt = FormatPublishedDate(result.PublishedAt);
            MsiDownloadUrl = result.MsiDownloadUrl;
            MsiFileName = result.MsiFileName;
            MsiFileSize = result.MsiFileSize;
            UpdateAvailable = result.UpdateAvailable;
            _downloadedPath = UpdateAvailable ? _updateService.GetCachedInstallerPath(LatestVersion) : null;
            IsDownloadComplete = !string.IsNullOrWhiteSpace(_downloadedPath);
        }
        catch { SetError("UpdateCheckFailed"); }
        finally { CheckCompleted = true; IsChecking = false; UpdateStatusText(); }
    }

    private void SetError(string key) { ErrorText = L.Text(key); HasError = true; }

    [RelayCommand]
    private async Task DownloadAsync()
    {
        if (string.IsNullOrEmpty(MsiDownloadUrl) || IsDownloading || IsChecking) return;
        HasError = false;
        IsDownloading = true;
        IsDownloadComplete = false;
        DownloadProgress = 0;
        using var cts = new CancellationTokenSource();
        _downloadCts = cts;
        try
        {
            var progress = new Progress<(long downloaded, long total)>(p =>
            {
                if (IsDownloading && ReferenceEquals(_downloadCts, cts) && p.total > 0)
                    DownloadProgress = Math.Clamp((double)p.downloaded / p.total * 100, 0, 100);
            });
            _downloadedPath = await _updateService.DownloadMsiAsync(MsiDownloadUrl, LatestVersion, MsiFileSize, progress, cts.Token);
            IsDownloadComplete = true;
            DownloadProgress = 100;
        }
        catch (OperationCanceledException) { }
        catch { SetError("UpdateDownloadFailed"); }
        finally { IsDownloading = false; _downloadCts = null; }
    }

    [RelayCommand]
    private void Install()
    {
        var path = _downloadedPath ?? _updateService.GetCachedInstallerPath(LatestVersion);
        if (path is null || !File.Exists(path)) { IsDownloadComplete = false; SetError("UpdateDownloadFailed"); return; }
        try { _updateService.LaunchInstaller(path); }
        catch { SetError("UpdateInstallFailed"); }
    }

    [RelayCommand] private void CancelDownload() => _downloadCts?.Cancel();
    [RelayCommand] private Task ReDownloadAsync() { IsDownloadComplete = false; return DownloadAsync(); }
    [RelayCommand]
    private async Task ActivateUpdateAsync()
    {
        if (IsChecking || IsDownloading) return;
        if (!UpdateAvailable && !IsDownloadComplete)
        {
            await CheckForUpdateCommand.ExecuteAsync(null);
            return;
        }
        if (!IsDownloadComplete) await DownloadCommand.ExecuteAsync(null);
        if (IsDownloadComplete) Install();
    }

    [RelayCommand]
    private void PrimaryAction()
    {
        if (IsChecking) return;
        if (IsDownloading) CancelDownload();
        else if (IsDownloadComplete) Install();
        else DownloadCommand.Execute(null);
    }

    [RelayCommand]
    private async Task LoadHistoryAsync()
    {
        if (IsHistoryLoading || (HistoryLoaded && !HistoryHasError)) return;
        await LoadHistoryPageAsync(1);
    }
    [RelayCommand]
    private async Task LoadMoreHistoryAsync()
    {
        if (IsHistoryLoading || !CanLoadMoreHistory) return;
        await LoadHistoryPageAsync(_historyPage + 1);
    }
    [RelayCommand]
    private Task RetryHistoryAsync() => IsHistoryLoading ? Task.CompletedTask : LoadHistoryPageAsync(_historyPage + 1);

    private async Task LoadHistoryPageAsync(int page)
    {
        IsHistoryLoading = true;
        HistoryHasError = false;
        try
        {
            var items = await _updateService.GetReleasesAsync(page);
            foreach (var item in items)
            {
                if (Releases.Any(r => r.Version == item.Version)) continue;
                item.IsLatest = Releases.Count == 0 && page == 1;
                item.IsCurrent = item.Version == CurrentVersion;
                Releases.Add(item);
            }
            SelectedRelease ??= Releases.FirstOrDefault();
            _historyPage = page;
            CanLoadMoreHistory = items.Count == 20;
            HistoryLoaded = true;
        }
        catch { HistoryHasError = true; }
        finally { IsHistoryLoading = false; OnPropertyChanged(nameof(IsHistoryEmpty)); }
    }

    private static string FormatPublishedDate(string isoDate)
    {
        if (DateTime.TryParse(isoDate, out var dt))
            return dt.ToString("yyyy-MM-dd");
        return isoDate;
    }

    public string FormatFileSize(long bytes)
    {
        return bytes switch
        {
            >= 1024 * 1024 => $"{bytes / 1024.0 / 1024.0:F1} MB",
            >= 1024 => $"{bytes / 1024.0:F0} KB",
            _ => $"{bytes} B"
        };
    }
}
