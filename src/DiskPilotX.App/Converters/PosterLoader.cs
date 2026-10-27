using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;

namespace DiskPilotX.App.Converters;

/// <summary>
/// 为 Image 提供异步海报加载：设置 SourceUrl 附加属性后，
/// 后台下载并缓存到本地，完成后回填 ImageSource；失败时触发 Failed 事件。
/// </summary>
public static class PosterLoader
{
    public static readonly DependencyProperty SourceUrlProperty =
        DependencyProperty.RegisterAttached("SourceUrl", typeof(string), typeof(PosterLoader),
            new PropertyMetadata(null, OnSourceUrlChanged));

    public static readonly DependencyProperty FailedProperty =
        DependencyProperty.RegisterAttached("Failed", typeof(bool), typeof(PosterLoader),
            new PropertyMetadata(false));

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DiskPilot-X", "PosterCache");

    public static string? GetSourceUrl(DependencyObject obj) => (string?)obj.GetValue(SourceUrlProperty);
    public static void SetSourceUrl(DependencyObject obj, string? value) => obj.SetValue(SourceUrlProperty, value);
    public static bool GetFailed(DependencyObject obj) => (bool)obj.GetValue(FailedProperty);
    public static void SetFailed(DependencyObject obj, bool value) => obj.SetValue(FailedProperty, value);

    private static async void OnSourceUrlChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Image image) return;
        var url = e.NewValue as string;
        SetFailed(image, false);
        if (string.IsNullOrWhiteSpace(url)) return;

        image.Source = null;
        try
        {
            image.Source = await LoadAsync(url).ConfigureAwait(true);
            SetState(image, DiskPilotX.Core.Models.ImageLoadState.Loaded);
        }
        catch
        {
            SetFailed(image, true);
            SetState(image, DiskPilotX.Core.Models.ImageLoadState.Failed);
        }
    }

    private static void SetState(Image image, DiskPilotX.Core.Models.ImageLoadState state)
    {
        // 视图条目统一承载加载状态，气泡与窗口两种呈现面共享同一套绑定。
        if (image.DataContext is DiskPilotX.Core.Models.SkillUiItem item)
            item.ImageState = state;
    }

    private static async Task<ImageSource?> LoadAsync(string url)
    {
        var cacheFile = CachePath(url);
        if (File.Exists(cacheFile))
        {
            try { return Decode(cacheFile); }
            catch { /* 缓存损坏则重新下载 */ }
        }

        var bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
        Directory.CreateDirectory(CacheDir);
        await File.WriteAllBytesAsync(cacheFile, bytes).ConfigureAwait(false);
        return Decode(cacheFile);
    }

    private static ImageSource Decode(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static string CachePath(string url)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(url));
        var name = Convert.ToHexString(hash)[..32] + ".jpg";
        return Path.Combine(CacheDir, name);
    }
}
