using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SpaceMonger.Core.Models;

namespace SpaceMonger.Core.Services.Settings;

public class SettingsService : ISettingsService
{
    private readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SpaceMonger",
        "settings.dat");

    public SettingsService(string? settingsFilePath = null)
    {
        if(!string.IsNullOrWhiteSpace(settingsFilePath))SettingsFilePath=Path.GetFullPath(settingsFilePath);
    }

    public AppSettings LoadSettings()
    {
        if (!File.Exists(SettingsFilePath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(SettingsFilePath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    public void SaveSettings(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(SettingsFilePath)!;
        Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(settings);
        File.WriteAllText(SettingsFilePath, json);
    }

    public string? GetApiKey(AppSettings settings)
    {
        if (settings.EncryptedApiKey is null)
        {
            return null;
        }

        try
        {
            var decryptedBytes = ProtectedData.Unprotect(
                settings.EncryptedApiKey,
                null,
                DataProtectionScope.CurrentUser);

            return Encoding.UTF8.GetString(decryptedBytes);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public byte[] EncryptApiKey(string apiKey)
    {
        var bytes = Encoding.UTF8.GetBytes(apiKey);

        return ProtectedData.Protect(
            bytes,
            null,
            DataProtectionScope.CurrentUser);
    }
}
