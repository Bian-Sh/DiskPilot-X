namespace DiskPilotX.App.Services;

public interface IUpdateService
{
    string GetCurrentVersion();
    Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken ct = default, bool forceRefresh = false);
    Task<IReadOnlyList<UpdateRelease>> GetReleasesAsync(int page, CancellationToken ct = default);
    string? GetCachedInstallerPath(string version);
    Task<string> DownloadMsiAsync(string url, string version, long expectedSize, IProgress<(long downloaded, long total)> progress, CancellationToken ct);
    void LaunchInstaller(string msiPath);
}

public sealed class UpdateRelease
{
    public string Version { get; init; } = "";
    public string ReleaseNotes { get; init; } = "";
    public string PublishedAt { get; init; } = "";
    public string HtmlUrl { get; init; } = "";
    public bool IsCurrent { get; set; }
    public bool IsLatest { get; set; }
}
