using DiskPilotX.Core.Models;
using System.Text.Json.Nodes;

namespace DiskPilotX.Core.Tests;

public sealed class RenameCompatibilityTests
{
    [Theory]
    [InlineData("SPACEMONGER_CREDENTIAL_旧引用")]
    [InlineData("DISKPILOT_X_CREDENTIAL_新引用")]
    public void 更名后仍拒绝新旧版凭据引用(string value)
    {
        var request = UserDecisionRequest.Parse(JsonNode.Parse("""{"title":"确认","question":"规则名称是什么？","fields":[{"id":"name","label":"凭据名称","type":"text","purpose":"credential_name"}]}"""));
        Assert.NotEmpty(UserDecisionValidator.ValidateSubmission(request, new Dictionary<string, object?> { ["name"] = value }));
    }

    [Fact]
    public void 环境变量兼容旧名称并优先采用新名称()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var current = "DISKPILOT_X_TEST_" + suffix;
        var legacy = "SPACEMONGER_TEST_" + suffix;
        try
        {
            Environment.SetEnvironmentVariable(legacy, "旧配置");
            Assert.Equal("旧配置", AppCompatibility.GetEnvironmentVariable(current));
            Environment.SetEnvironmentVariable(current, "新配置");
            Assert.Equal("新配置", AppCompatibility.GetEnvironmentVariable(current));
            Environment.SetEnvironmentVariable(current, " ");
            Assert.Equal("旧配置", AppCompatibility.GetEnvironmentVariable(current));
        }
        finally
        {
            Environment.SetEnvironmentVariable(current, null);
            Environment.SetEnvironmentVariable(legacy, null);
        }
    }

    [Fact]
    public void 未保存新版设置也不回退旧版数据目录()
    {
        var root = Path.Combine(Path.GetTempPath(), "diskpilotx-rename-" + Guid.NewGuid().ToString("N"));
        try
        {
            var legacy = Path.Combine(root, "SpaceMonger", "settings.dat");
            Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
            File.WriteAllText(legacy, "旧版设置占位");
            Assert.Equal(Path.Combine(root, "DiskPilot-X", "settings.dat"), AppCompatibility.ResolveDataPath(root, "settings.dat"));
            var current = Path.Combine(root, "DiskPilot-X", "settings.dat");
            Directory.CreateDirectory(Path.GetDirectoryName(current)!);
            Assert.Equal(Path.Combine(root, "DiskPilot-X", "settings.dat"), AppCompatibility.ResolveDataPath(root, "settings.dat"));
            File.WriteAllText(current, "新版设置占位");
            Assert.Equal(current, AppCompatibility.ResolveDataPath(root, "settings.dat"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("Conversations")]
    [InlineData("cleanup-skills")]
    public void 旧版对话和规则存在时路径仍固定新版(string directory)
    {
        var root = Path.Combine(Path.GetTempPath(), "diskpilotx-rename-" + Guid.NewGuid().ToString("N"));
        try
        {
            var current = Path.Combine(root, "DiskPilot-X", directory);
            Assert.Equal(current, AppCompatibility.ResolveDataPath(root, directory));
            var legacy = Path.Combine(root, "SpaceMonger", directory);
            Directory.CreateDirectory(legacy);
            Assert.Equal(current, AppCompatibility.ResolveDataPath(root, directory));
            Directory.CreateDirectory(current);
            Assert.Equal(current, AppCompatibility.ResolveDataPath(root, directory));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
