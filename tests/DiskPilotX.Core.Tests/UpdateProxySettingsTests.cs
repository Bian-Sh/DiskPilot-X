using System.Text.Json;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Settings;

namespace DiskPilotX.Core.Tests;

public sealed class UpdateProxySettingsTests
{
    [Theory]
    [InlineData(true, "custom", "custom")]
    [InlineData(true, "system", "system")]
    [InlineData(true, "direct", "direct")]
    [InlineData(false, "custom", "direct")]
    [InlineData(true, null, "system")]
    public void 旧更新开关首次迁移为实际使用的独立模式(bool enabled, string? aiMode, string expected)
    {
        var path = SettingsPath();
        File.WriteAllText(path, JsonSerializer.Serialize(new { UseProxyForUpdates = enabled, ProxyMode = aiMode, ProxyHost = "10.1.2.3", ProxyPort = 8910, ProxyUsername = "旧用户", EncryptedProxyPassword = new byte[] { 1, 2, 3 }, Language = "zh-TW" }));
        var service = new SettingsService(path);
        var saved = service.LoadSettings();
        var json = JsonSerializer.SerializeToElement(saved);
        Assert.Equal(expected, json.GetProperty("UpdateProxyMode").GetString());
        Assert.Equal("10.1.2.3", json.GetProperty("UpdateProxyHost").GetString());
        Assert.Equal(8910, json.GetProperty("UpdateProxyPort").GetInt32());
        Assert.Equal("旧用户", json.GetProperty("UpdateProxyUsername").GetString());
        Assert.Equal(new byte[] { 1, 2, 3 }, json.GetProperty("EncryptedUpdateProxyPassword").GetBytesFromBase64());
        Assert.Equal("zh-TW", saved.Language);
        using var persisted = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(expected, persisted.RootElement.GetProperty("UpdateProxyMode").GetString());
    }

    [Fact]
    public void 迁移完成后AI代理修改不会再次覆盖更新代理()
    {
        var path = SettingsPath();
        File.WriteAllText(path, "{\"ProxyMode\":\"custom\",\"ProxyHost\":\"10.1.2.3\",\"ProxyPort\":8910}");
        var service = new SettingsService(path);
        var settings = service.LoadSettings();
        settings.ProxyMode = "direct"; settings.ProxyHost = "ai-only.example"; settings.UseProxyForUpdates = false;
        service.SaveSettings(settings);
        var json = JsonSerializer.SerializeToElement(service.LoadSettings());
        Assert.Equal("custom", json.GetProperty("UpdateProxyMode").GetString());
        Assert.Equal("10.1.2.3", json.GetProperty("UpdateProxyHost").GetString());
        Assert.Equal(8910, json.GetProperty("UpdateProxyPort").GetInt32());
    }

    [Fact]
    public void 已有独立配置不被历史开关或AI字段覆盖()
    {
        var path = SettingsPath();
        File.WriteAllText(path, "{\"UseProxyForUpdates\":false,\"ProxyMode\":\"custom\",\"ProxyHost\":\"ai.example\",\"UpdateProxyMode\":\"system\",\"UpdateProxyHost\":\"update.example\",\"UpdateProxyPort\":8001}");
        var json = JsonSerializer.SerializeToElement(new SettingsService(path).LoadSettings());
        Assert.Equal("system", json.GetProperty("UpdateProxyMode").GetString());
        Assert.Equal("update.example", json.GetProperty("UpdateProxyHost").GetString());
        Assert.Equal(8001, json.GetProperty("UpdateProxyPort").GetInt32());
    }

    [Fact]
    public void 旧自定义代理缺少地址时迁移为本地回环地址而不编造端口()
    {
        var path = SettingsPath();
        File.WriteAllText(path, "{\"ProxyMode\":\"custom\",\"ProxyHost\":\"   \"}");
        var json = JsonSerializer.SerializeToElement(new SettingsService(path).LoadSettings());
        Assert.Equal("127.0.0.1", json.GetProperty("UpdateProxyHost").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("UpdateProxyPort").ValueKind);
    }

    [Fact]
    public void 两套代理口令独立加密解密且清空更新口令不影响AI()
    {
        var path = SettingsPath(); var service = new SettingsService(path);
        var settings = new AppSettings { UpdateProxyMode = "custom", EncryptedProxyPassword = service.EncryptProxyPassword("AI测试口令"), EncryptedUpdateProxyPassword = service.EncryptProxyPassword("更新测试口令") };
        service.SaveSettings(settings);
        var saved = service.LoadSettings();
        Assert.Equal("AI测试口令", service.GetProxyPassword(saved));
        Assert.Equal("更新测试口令", service.GetUpdateProxyPassword(saved));
        var json = File.ReadAllText(path);
        Assert.DoesNotContain("AI测试口令", json); Assert.DoesNotContain("更新测试口令", json);
        saved.EncryptedUpdateProxyPassword = null; service.SaveSettings(saved);
        Assert.Null(service.GetUpdateProxyPassword(service.LoadSettings()));
        Assert.Equal("AI测试口令", service.GetProxyPassword(service.LoadSettings()));
    }

    private static string SettingsPath()
    {
        var folder = Path.Combine(Path.GetTempPath(), "DiskPilotX-独立更新代理", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "settings.json");
    }
}
