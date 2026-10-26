using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskPilotX.App.Localization;
using DiskPilotX.App.Services;

namespace DiskPilotX.App.ViewModels;

public partial class UpdateViewModel : ObservableObject
{
    private readonly UpdateService _updateService;

    public UpdateViewModel(UpdateService updateService)
    {
        _updateService = updateService;
        CurrentVersion = _updateService.GetCurrentVersion();
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

    private CancellationTokenSource? _downloadCts;

    partial void OnUpdateAvailableChanged(bool value) => UpdateStatusText();
    partial void OnLatestVersionChanged(string value) => UpdateStatusText();
    partial void OnIsDownloadingChanged(bool value) => UpdateStatusText();
    partial void OnDownloadProgressChanged(double value) => UpdateStatusText();
    partial void OnIsDownloadCompleteChanged(bool value) => UpdateStatusText();
    partial void OnCurrentVersionChanged(string value) => UpdateStatusText();

    public void UpdateStatusText()
    {
        if (IsDownloadComplete)
        {
            StatusText = L.Format("DownloadReadyFormat", LatestVersion);
        }
        else if (IsDownloading)
        {
            StatusText = L.Format("DownloadingFormat", LatestVersion, ((int)DownloadProgress).ToString());
        }
        else if (UpdateAvailable)
        {
            StatusText = L.Format("UpdateAvailableFormat", LatestVersion);
        }
        else
        {
            StatusText = $"v{CurrentVersion}";
        }
    }

    [RelayCommand]
    private async Task CheckForUpdateAsync()
    {
        try
        {
            var result = await _updateService.CheckForUpdateAsync();
            if (!result.Success)
                return;

            LatestVersion = result.LatestVersion;
            ReleaseNotes = result.ReleaseNotes;
            PublishedAt = FormatPublishedDate(result.PublishedAt);
            MsiDownloadUrl = result.MsiDownloadUrl;
            MsiFileName = result.MsiFileName;
            MsiFileSize = result.MsiFileSize;
            UpdateAvailable = result.UpdateAvailable;

            if (UpdateAvailable)
            {
                var cachedPath = _updateService.GetCachedInstallerPath(LatestVersion);
                if (cachedPath is not null)
                {
                    IsDownloadComplete = true;
                }
            }
        }
        catch
        {
            // 检查失败就静默
        }
        finally
        {
            CheckCompleted = true;
            UpdateStatusText();
        }
    }

    [RelayCommand]
    private async Task DownloadAsync()
    {
        if (string.IsNullOrEmpty(MsiDownloadUrl) || IsDownloading)
            return;

        IsDownloading = true;
        IsDownloadComplete = false;
        DownloadProgress = 0;
        _downloadCts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<(long downloaded, long total)>(p =>
            {
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    if (p.total > 0)
                        DownloadProgress = (double)p.downloaded / p.total * 100;
                });
            });

            await _updateService.DownloadMsiAsync(
                MsiDownloadUrl, LatestVersion, MsiFileSize,
                progress, _downloadCts.Token);

            IsDownloadComplete = true;
        }
        catch (OperationCanceledException)
        {
            // 用户取消
        }
        catch
        {
            // 下载失败
        }
        finally
        {
            IsDownloading = false;
            _downloadCts?.Dispose();
            _downloadCts = null;
        }
    }

    [RelayCommand]
    private void Install()
    {
        var path = _updateService.GetCachedInstallerPath(LatestVersion);
        if (path is not null)
        {
            _updateService.LaunchInstaller(path);
        }
    }

    [RelayCommand]
    private void CancelDownload()
    {
        _downloadCts?.Cancel();
    }

    [RelayCommand]
    private async Task ReDownloadAsync()
    {
        // 删除已缓存的文件，重新下载
        var path = _updateService.GetCachedInstallerPath(LatestVersion);
        if (path is not null)
        {
            try { File.Delete(path); } catch { }
        }
        IsDownloadComplete = false;
        await DownloadAsync();
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
