using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using DiskPilotX.Core.Services.Settings;
using DiskPilotX.Core.Services.Llm;

namespace DiskPilotX.App.Services;

public class UpdateService : IUpdateService
{
    private const string RepoOwner = "Bian-Sh";
    private const string RepoName = "DiskPilot-X";
    private const int CheckTimeoutSeconds = 10;
    private static readonly string DefaultCacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DiskPilot-X", "updates");
    private readonly string CacheDir;
    private string CacheMetaPath => Path.Combine(CacheDir, "update-cache.json");

    private readonly Func<HttpClient> _client;
    private UpdateCheckResult? _lastCheckedRelease;

    public UpdateService() : this(new HttpClient(), DefaultCacheDir) { }

    public UpdateService(HttpClient client, string cacheDirectory) : this(() => client, cacheDirectory) { }

    public UpdateService(ISettingsService settingsService, ModelHttpClientFactory clients, string? cacheDirectory = null)
        : this(() =>
        {
            var settings = settingsService.LoadSettings();
            if (settings.EnsureIndependentUpdateProxy()) settingsService.SaveSettings(settings);
            var mode = LlmContracts.ParseProxyKind(settings.UpdateProxyMode);
            if (mode == ProxyKind.Custom && settings.UpdateProxyPort is not (> 0 and < 65536))
                throw new InvalidOperationException("自定义更新代理端口必须是 1–65535 的整数。");
            return clients.Create(mode, settings.UpdateProxyHost,
                settings.UpdateProxyPort, settings.UpdateProxyUsername, settingsService.GetUpdateProxyPassword(settings));
        }, cacheDirectory) { }

    public UpdateService(Func<HttpClient> client, string? cacheDirectory = null)
    {
        _client = client;
        CacheDir = cacheDirectory ?? DefaultCacheDir;
    }

    private HttpRequestMessage Request(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("DiskPilotX-UpdateChecker");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        return request;
    }

    public async Task<IReadOnlyList<UpdateRelease>> GetReleasesAsync(int page, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(CheckTimeoutSeconds));
        using var request = Request($"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases?per_page=20&page={page}");
        using var response = await _client().SendAsync(request, timeout.Token);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(timeout.Token);
        var releases = JsonSerializer.Deserialize<GithubRelease[]>(json) ?? [];
        return releases.Select(r => new UpdateRelease
        {
            Version = r.TagName?.TrimStart('v', 'V') ?? "",
            ReleaseNotes = r.Body ?? "",
            PublishedAt = DateTime.TryParse(r.PublishedAt, out var date) ? date.ToString("yyyy-MM-dd") : "",
            HtmlUrl = r.HtmlUrl ?? ""
        }).ToArray();
    }

    public string GetCurrentVersion()
    {
        var infoAttr = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        var version = infoAttr?.InformationalVersion ?? "0.0.0";
        var plusIdx = version.IndexOf('+');
        return plusIdx > 0 ? version[..plusIdx] : version;
    }

    public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken ct = default, bool forceRefresh = false)
    {
        try
        {
            var cached = forceRefresh ? null : TryGetCachedResult();
            if (cached is not null)
            {
                _lastCheckedRelease = cached;
                return cached;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(CheckTimeoutSeconds));

            var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
            using var request = Request(url);
            using var response = await _client().SendAsync(request, cts.Token);
            if (!response.IsSuccessStatusCode)
                return UpdateCheckResult.Failed;

            var json = await response.Content.ReadAsStringAsync(cts.Token);
            var release = JsonSerializer.Deserialize<GithubRelease>(json);
            if (release is null)
                return UpdateCheckResult.Failed;

            var tagName = release.TagName?.TrimStart('v', 'V') ?? "";
            var currentVersion = GetCurrentVersion();

            var msiAsset = FindInstallerAsset(release.Assets);

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
                MsiFileSize = msiAsset?.Size ?? 0,
                InstallerSha256 = ParseSha256(msiAsset?.Digest)
            };

            // 缓存写入失败也保留本次可信响应，避免下载时跳过已知摘要。
            _lastCheckedRelease = result;
            CacheResult(result);
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return UpdateCheckResult.Failed; }
    }

    public string? GetCachedInstallerPath(string version)
    {
        var meta = LoadCacheMeta();
        if (meta?.MsiFileName is null) return null;
        var path = Path.Combine(CacheDir, InstallerFileName(version, meta.MsiFileName));
        if (!File.Exists(path)) return null;

        if (meta?.LatestVersion != version || meta.MsiFileSize <= 0 || new FileInfo(path).Length != meta.MsiFileSize)
            return null;

        if (meta.InstallerSha256 is { Length: 64 } expected)
        {
            using var input = File.OpenRead(path);
            if (!Convert.ToHexString(SHA256.HashData(input)).Equals(expected, StringComparison.OrdinalIgnoreCase)) return null;
        }
        return path;
    }

    public async Task<string> DownloadMsiAsync(
        string url, string version, long expectedSize,
        IProgress<(long downloaded, long total)> progress,
        CancellationToken ct)
    {
        Directory.CreateDirectory(CacheDir);
        if (version.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || version.Contains('/') || version.Contains('\\'))
            throw new IOException("更新版本号不是有效的文件名。");
        var filePath = Path.Combine(CacheDir, InstallerFileName(version, new Uri(url).AbsolutePath));
        var partialPath = filePath + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            using var request = Request(url);
            using var response = await _client().SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            var contentLength = response.Content.Headers.ContentLength ?? 0;
            var totalBytes = expectedSize > 0 ? expectedSize : contentLength;
            long totalRead = 0;
            await using (var input = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = new FileStream(partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                int count;
                while ((count = await input.ReadAsync(buffer, ct)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, count), ct);
                    totalRead += count;
                    progress.Report((totalRead, totalBytes));
                }
            }
            if (totalRead == 0 || (expectedSize > 0 && totalRead != expectedSize) || (contentLength > 0 && totalRead != contentLength))
                throw new IOException("更新安装包下载不完整，请重试。");
            var meta = LoadCacheMeta();
            var knownDigest = _lastCheckedRelease is { } release && release.LatestVersion == version && release.MsiDownloadUrl == url
                ? release.InstallerSha256 : meta?.LatestVersion == version ? meta.InstallerSha256 : null;
            if (knownDigest is { Length: 64 } expected)
            {
                await using var input = File.OpenRead(partialPath);
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(input, ct));
                if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new IOException("安装包SHA256校验失败，请重新下载。");
            }
            ct.ThrowIfCancellationRequested();
            File.Move(partialPath, filePath, true);
            return filePath;
        }
        finally
        {
            if (File.Exists(partialPath)) File.Delete(partialPath);
        }
    }

    public void LaunchInstaller(string installerPath)
    {
        var process = Process.Start(CreateInstallerStartInfo(installerPath));
        if (process is null) throw new IOException("安装程序未能启动。");
        Application.Current?.Shutdown();
    }

    public static ProcessStartInfo CreateInstallerStartInfo(string installerPath)
    {
        var fullPath = Path.GetFullPath(installerPath);
        var extension = Path.GetExtension(fullPath);
        if (!extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".msi", StringComparison.OrdinalIgnoreCase))
            throw new IOException("不支持的更新安装包类型。");
        return new ProcessStartInfo
        {
            FileName = extension.Equals(".msi", StringComparison.OrdinalIgnoreCase) ? "msiexec.exe" : fullPath,
            Arguments = extension.Equals(".msi", StringComparison.OrdinalIgnoreCase) ? $"/i \"{fullPath}\"" : "",
            WorkingDirectory = Path.GetDirectoryName(fullPath),
            UseShellExecute = true,
            Verb = "runas"
        };
    }

    private static string InstallerFileName(string version, string source)
    {
        var extension = Path.GetExtension(source).ToLowerInvariant();
        if (extension is not (".exe" or ".msi")) throw new IOException("更新资产必须为安装程序。");
        return extension == ".msi" ? $"DiskPilot-X-{version}-win64.msi" : $"DiskPilot-X-{version}-setup.exe";
    }

    private static GithubAsset? FindInstallerAsset(GithubAsset[]? assets)
    {
        if (assets is null) return null;
        var candidates = assets.Where(a => a.Name is { } name
            && !name.Contains("arm64", StringComparison.OrdinalIgnoreCase)
            && !name.Contains("x86", StringComparison.OrdinalIgnoreCase)).ToArray();
        return candidates.FirstOrDefault(a => a.Name?.EndsWith("-setup.exe", StringComparison.OrdinalIgnoreCase) == true)
            ?? candidates.FirstOrDefault(a => a.Name?.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static string? ParseSha256(string? digest)
    {
        if (digest is null) return null;
        if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) || digest.Length != 71 || !digest[7..].All(Uri.IsHexDigit))
            throw new IOException("发布资产摘要格式无效。");
        return digest[7..];
    }

    private static bool IsNewerVersion(string latest, string current)
    {
        if (!Version.TryParse(latest, out var latestVer))
            return false;
        if (!Version.TryParse(current, out var currentVer))
            return false;
        return latestVer > currentVer;
    }

    private UpdateCheckResult? TryGetCachedResult()
    {
        try
        {
            if (!File.Exists(CacheMetaPath))
                return null;

            var json = File.ReadAllText(CacheMetaPath);
            var meta = JsonSerializer.Deserialize<CacheMeta>(json);
            if (meta is null || meta.Date != DateTime.Now.ToString("yyyy-MM-dd") || meta.CurrentVersion != GetCurrentVersion())
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
                MsiFileSize = meta.MsiFileSize,
                InstallerSha256 = meta.InstallerSha256
            };
        }
        catch
        {
            return null;
        }
    }

    private void CacheResult(UpdateCheckResult result)
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
                MsiFileSize = result.MsiFileSize,
                InstallerSha256 = result.InstallerSha256
            };
            var json = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(CacheMetaPath, json);
        }
        catch
        {
            // 缓存写入失败就不管了
        }
    }

    private CacheMeta? LoadCacheMeta()
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

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("assets")]
        public GithubAsset[]? Assets { get; set; }
    }

    private class GithubAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }

        [JsonPropertyName("digest")]
        public string? Digest { get; set; }

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
    public string? InstallerSha256 { get; set; }
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
    public string? InstallerSha256 { get; set; }
}
