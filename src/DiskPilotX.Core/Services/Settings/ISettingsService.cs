using DiskPilotX.Core.Models;

namespace DiskPilotX.Core.Services.Settings;

public interface ISettingsService
{
    AppSettings LoadSettings();
    void SaveSettings(AppSettings settings);
    string? GetApiKey(AppSettings settings);
    byte[] EncryptApiKey(string apiKey);
    string? GetProxyPassword(AppSettings settings);
    string? GetUpdateProxyPassword(AppSettings settings);
    byte[] EncryptProxyPassword(string password);
}
