using System.Text.Json;
using DiskPilotX.Core.Models.Theme;
namespace DiskPilotX.Core.Tests;
public sealed class KeepBackgroundGlassTests
{
    [Fact]
    public void 旧主题默认不保持后台通透且新选项可以保存复制()
    {
        var old=JsonSerializer.Deserialize<ThemeProfile>("{}")!;
        using var saved=JsonDocument.Parse(JsonSerializer.Serialize(old));
        Assert.True(saved.RootElement.TryGetProperty("KeepGlassWhenInactive",out var value),"主题缺少后台保持通透配置。");
        Assert.False(value.GetBoolean());
        var profile=JsonSerializer.Deserialize<ThemeProfile>("""{"GlassEnabled":true,"GlassOpacity":0.25,"KeepGlassWhenInactive":true}""")!;
        using var clone=JsonDocument.Parse(JsonSerializer.Serialize(profile.Clone()));
        Assert.True(clone.RootElement.GetProperty("KeepGlassWhenInactive").GetBoolean());
        Assert.Equal(0.25,clone.RootElement.GetProperty("GlassOpacity").GetDouble());
    }
}