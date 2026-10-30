using System.Text.Json;
using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Settings;
using DiskPilotX.Core.Services.Skills;
using TinyHarness;

namespace DiskPilotX.Core.Tests;

public sealed class ReadableUserJsonTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "DiskPilot-X-中文写入-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void 不保留自动迁移类型或迁移专用授权入口()
    {
        Assert.Null(typeof(AppCompatibility).Assembly.GetType("DiskPilotX.Core.AppDataMigration"));
        Assert.Null(typeof(ConversationStore).GetMethod("RefreshMigratedMetadata",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));
    }

    [Fact]
    public void 保存会话只写新版目录且不触碰旧根或生成迁移文件()
    {
        var legacy = Path.Combine(root, "SpaceMonger");
        Directory.CreateDirectory(legacy);
        var original = Path.Combine(legacy, "保留原件.txt");
        File.WriteAllText(original, "用户已手动复制，不自动迁移");
        var bytes = File.ReadAllBytes(original);
        var writeTime = File.GetLastWriteTimeUtc(original);
        var current = AppCompatibility.ResolveDataPath(root, "Conversations");
        var snapshot = new ConversationSnapshot(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow,
            "中文签名", AgentSessionState.Completed, [TinyHarness.ChatMessage.User("中文会话")],
            [SavedChatMessage.Capture(new DiskPilotX.Core.Models.ChatMessage { Sender = ChatSender.User, Text = "中文会话" })]);
        var store = new ConversationStore(current);
        store.Save(snapshot);
        Assert.Contains("中文会话", File.ReadAllText(Path.Combine(current, snapshot.Id + ".json")));
        Assert.Equal(bytes, File.ReadAllBytes(original));
        Assert.Equal(writeTime, File.GetLastWriteTimeUtc(original));
        Assert.Single(Directory.GetFiles(legacy, "*", SearchOption.AllDirectories));
        Assert.DoesNotContain(Directory.GetFiles(Path.Combine(root, "DiskPilot-X"), "*", SearchOption.AllDirectories),
            path => Path.GetFileName(path).StartsWith(".data-home", StringComparison.Ordinal));
    }

    [Fact]
    public void 会话正文及授权元数据中文直写且可回读()
    {
        var store = new ConversationStore(root);
        var snapshot = new ConversationSnapshot(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow,
            "中文隐私签名", AgentSessionState.Completed, [TinyHarness.ChatMessage.User("分析第 1 个磁盘")],
            [SavedChatMessage.Capture(new DiskPilotX.Core.Models.ChatMessage { Sender = ChatSender.User, Text = "分析第 1 个磁盘" })]);
        store.Save(snapshot);
        Assert.Contains("分析第 1 个磁盘", File.ReadAllText(Path.Combine(root, snapshot.Id + ".json")));
        Assert.Contains("中文隐私签名", File.ReadAllText(Path.Combine(root, snapshot.Id + ".metadata")));
        Assert.Equal(snapshot.Messages[0].Text, store.Load(snapshot.Id).Messages[0].Text);
        Assert.NotNull(store.ReadMetadata(snapshot.Id));
    }

    [Fact]
    public void 设置中文直写且加密凭据字节不变()
    {
        var file = Path.Combine(root, "settings.dat");
        var service = new SettingsService(file);
        var settings = new AppSettings { AnthropicBaseUrl = "https://测试.example/v1", EncryptedApiKey = service.EncryptApiKey("测试密钥") };
        var cipher = settings.EncryptedApiKey.ToArray();
        service.SaveSettings(settings);
        Assert.Contains("测试.example", File.ReadAllText(file));
        Assert.Equal(cipher, service.LoadSettings().EncryptedApiKey);
        Assert.Equal("测试密钥", service.GetApiKey(service.LoadSettings()));
    }

    [Fact]
    public void 分析偏好中文直写()
    {
        var context = new DiskPilotX.Core.Services.Analysis.AnalysisContextStore(root);
        context.SavePreference(root, "不要删除中文资料");
        Assert.Contains("不要删除中文资料", File.ReadAllText(Path.Combine(root, "analysis-context.json")));
        Assert.Single(context.ListPreferences());

    }

    [Fact]
    public void 技能清单和规则中文直写且规范摘要稳定()
    {
        var package = new CleanupSkillPackage { Id = "readable-test", Title = "缓存检查", Description = "只读检查", SkillMarkdown = "# 缓存检查", Rules = [new() { Id = "cache", Path = "cache", Description = "可以再生的缓存", Risk = "重新下载", Sources = ["用户指定"] }] };
        var canonicalOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = true };
        var expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(package with { Digest = "" }, canonicalOptions)))).ToLowerInvariant();
        var store = new CleanupSkillStore(root, []);
        var saved = store.Save(package);
        Assert.Equal(expected, saved.Digest);
        Assert.Equal(saved.Digest, store.Load(saved.Id).Digest);
        var revision = Path.Combine(root, package.Id, "versions", saved.Digest);
        Assert.Contains("缓存检查", File.ReadAllText(Path.Combine(revision, "manifest.json")));
        Assert.Contains("可以再生的缓存", File.ReadAllText(Path.Combine(revision, "rules.json")));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
