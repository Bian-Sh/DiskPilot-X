using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace DiskPilotX.App.Services;

public class UpdateService
{
    private const string RepoOwner = "Bian-Sh";
    private const string RepoName = "DiskPilot-X";
    private const int CheckTimeoutSeconds = 10;
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DiskPilot-X", "updates");
    private static readonly string CacheMetaPath = Path.Combine(CacheDir, "update-cache.json");

    private readonly HttpClient _httpClient;

    public UpdateService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("DiskPilotX-UpdateChecker");
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public string GetCurrentVersion()
    {
        var infoAttr = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        var version = infoAttr?.InformationalVersion ?? "0.0.0";
        var plusIdx = version.IndexOf('+');
        return plusIdx > 0 ? version[..plusIdx] : version;
    }

    public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken ct = default)
    {
        try
        {
            var cached = TryGetCachedResult();
            if (cached is not null)
                return cached;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(CheckTimeoutSeconds));

            var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
            using var response = await _httpClient.GetAsync(url, cts.Token);
            if (!response.IsSuccessStatusCode)
                return UpdateCheckResult.Failed;

            var json = await response.Content.ReadAsStringAsync(cts.Token);
            var release = JsonSerializer.Deserialize<GithubRelease>(json);
            if (release is null)
                return UpdateCheckResult.Failed;

            var tagName = release.TagName?.TrimStart('v', 'V') ?? "";
            var currentVersion = GetCurrentVersion();

            var msiAsset = FindMsiAsset(release.Assets);

            var result = new UpdateCheckResult
            {
                Success = true,
                LatestVersion = tagName,
                CurrentVersion = currentVersion,
                UpdateAvailable = IsNewerVersion(tagName, currentVersion),
                ReleaseNotes = release.Body ?? "",
                PublishedAt = release.PublishedAt ?? "",
                MsiDownloadUrl = msiAsset?.BrowserDownloadUrl,
                MsiFileName = msiAsset?.Name,
                MsiFileSize = msiAsset?.Size ?? 0
            };

            CacheResult(result);
            return result;
        }
        catch
        {
            return UpdateCheckResult.Failed;
        }
    }

    public string? GetCachedInstallerPath(string version)
    {
        var path = Path.Combine(CacheDir, $"DiskPilot-X-{version}-win64.msi");
        if (!File.Exists(path))
            return null;

        var meta = LoadCacheMeta();
        if (meta?.MsiFileName is null)
            return null;

        return path;
    }

    public async Task<string> DownloadMsiAsync(
        string url, string version, long expectedSize,
        IProgress<(long downloaded, long total)> progress,
        CancellationToken ct)
    {
        Directory.CreateDirectory(CacheDir);
        var filePath = Path.Combine(CacheDir, $"DiskPilot-X-{version}-win64.msi");

        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? expectedSize;
        var buffer = new byte[81920];

        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        var totalRead = 0L;
        int bytesRead;
        while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            totalRead += bytesRead;
            progress.Report((totalRead, totalBytes));
        }

        return filePath;
    }

    public void LaunchInstaller(string msiPath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "msiexec",
            Arguments = $"/i \"{msiPath}\" /qb",
            UseShellExecute = true,
            Verb = "runas"
        });
        Application.Current.Shutdown();
    }

    private static GithubAsset? FindMsiAsset(GithubAsset[]? assets)
    {
        if (assets is null) return null;

        var candidates = assets
            .Where(a => a.Name?.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();

        var win64 = candidates.FirstOrDefault(a =>
            a.Name?.Contains("win64", StringComparison.OrdinalIgnoreCase) == true ||
            a.Name?.Contains("windows_amd64", StringComparison.OrdinalIgnoreCase) == true);
        if (win64 is not null) return win64;

        var windows = candidates.FirstOrDefault(a =>
            a.Name?.Contains("windows", StringComparison.OrdinalIgnoreCase) == true);
        if (windows is not null) return windows;

        return candidates.FirstOrDefault();
    }

    private static bool IsNewerVersion(string latest, string current)
    {
        if (!Version.TryParse(latest, out var latestVer))
            return false;
        if (!Version.TryParse(current, out var currentVer))
            return false;
        return latestVer > currentVer;
    }

    private static UpdateCheckResult? TryGetCachedResult()
    {
        try
        {
            if (!File.Exists(CacheMetaPath))
                return null;

            var json = File.ReadAllText(CacheMetaPath);
            var meta = JsonSerializer.Deserialize<CacheMeta>(json);
            if (meta is null || meta.Date != DateTime.Now.ToString("yyyy-MM-dd"))
                return null;

            return new UpdateCheckResult
            {
                Success = true,
                LatestVersion = meta.LatestVersion ?? "",
                CurrentVersion = meta.CurrentVersion ?? "",
                UpdateAvailable = meta.UpdateAvailable,
                ReleaseNotes = meta.ReleaseNotes ?? "",
                PublishedAt = meta.PublishedAt ?? "",
                MsiDownloadUrl = meta.MsiDownloadUrl,
                MsiFileName = meta.MsiFileName,
                MsiFileSize = meta.MsiFileSize
            };
        }
        catch
        {
            return null;
        }
    }

    private static void CacheResult(UpdateCheckResult result)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            var meta = new CacheMeta
            {
                Date = DateTime.Now.ToString("yyyy-MM-dd"),
                LatestVersion = result.LatestVersion,
                CurrentVersion = result.CurrentVersion,
                UpdateAvailable = result.UpdateAvailable,
                ReleaseNotes = result.ReleaseNotes,
                PublishedAt = result.PublishedAt,
                MsiDownloadUrl = result.MsiDownloadUrl,
                MsiFileName = result.MsiFileName,
                MsiFileSize = result.MsiFileSize
            };
            var json = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(CacheMetaPath, json);
        }
        catch
        {
            // 缓存写入失败就不管了
        }
    }

    private static CacheMeta? LoadCacheMeta()
    {
        try
        {
            if (!File.Exists(CacheMetaPath)) return null;
            return JsonSerializer.Deserialize<CacheMeta>(File.ReadAllText(CacheMetaPath));
        }
        catch
        {
            return null;
        }
    }

    #region GitHub API Models

    private class GithubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("published_at")]
        public string? PublishedAt { get; set; }

        [JsonPropertyName("assets")]
        public GithubAsset[]? Assets { get; set; }
    }

    private class GithubAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }

    #endregion
}

public class UpdateCheckResult
{
    public static readonly UpdateCheckResult Failed = new() { Success = false };

    public bool Success { get; set; }
    public string LatestVersion { get; set; } = "";
    public string CurrentVersion { get; set; } = "";
    public bool UpdateAvailable { get; set; }
    public string ReleaseNotes { get; set; } = "";
    public string PublishedAt { get; set; } = "";
    public string? MsiDownloadUrl { get; set; }
    public string? MsiFileName { get; set; }
    public long MsiFileSize { get; set; }
}

internal class CacheMeta
{
    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("latestVersion")]
    public string? LatestVersion { get; set; }

    [JsonPropertyName("currentVersion")]
    public string? CurrentVersion { get; set; }

    [JsonPropertyName("updateAvailable")]
    public bool UpdateAvailable { get; set; }

    [JsonPropertyName("releaseNotes")]
    public string? ReleaseNotes { get; set; }

    [JsonPropertyName("publishedAt")]
    public string? PublishedAt { get; set; }

    [JsonPropertyName("msiDownloadUrl")]
    public string? MsiDownloadUrl { get; set; }

    [JsonPropertyName("msiFileName")]
    public string? MsiFileName { get; set; }

    [JsonPropertyName("msiFileSize")]
    public long MsiFileSize { get; set; }
}
