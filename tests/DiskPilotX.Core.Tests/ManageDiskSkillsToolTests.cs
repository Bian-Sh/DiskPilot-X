using System.Text.Json;
using FluentAssertions;
using DiskPilotX.Core.Services.Agent;
using DiskPilotX.Core.Services.Copilot;

namespace DiskPilotX.Core.Tests;

public class ManageDiskSkillsToolTests
{
    [Theory]
    [InlineData("references/bridge-api.md", true)]
    [InlineData("references/../SKILL.md", false)]
    [InlineData("../secret.md", false)]
    [InlineData("C:/secret.md", false)]
    [InlineData("references/missing.md", false)]
    public async Task ExecuteAsync_ReadReference_OnlyReadsBoundedSkillDocumentation(string referencePath, bool expected)
    {
        var root = Path.Combine(Path.GetTempPath(), "diskpilotx-reference-" + Guid.NewGuid().ToString("N"));
        try
        {
            var directory = Path.Combine(root, "sample-skill");
            Directory.CreateDirectory(Path.Combine(directory, "references"));
            File.WriteAllText(Path.Combine(directory, "SKILL.md"), "# 示例规则\n\n规则正文");
            File.WriteAllText(Path.Combine(directory, "references", "bridge-api.md"), "# 真实桥契约\n只读方法签名");
            var tool = new ManageDiskSkillsTool(new FileSkillPromptProvider([root]));
            var result = await tool.ExecuteAsync(EmptyContext(), Args(new { operation = "read", id = "sample-skill", reference_path = referencePath }), CancellationToken.None);
            result.GetProperty("ok").GetBoolean().Should().Be(expected);
            if (expected) result.GetProperty("content").GetString().Should().Contain("真实桥契约").And.NotContain("规则正文");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
    [Fact]
    public async Task ExecuteAsync_CanCreateReadListAndDeleteDiskSkill()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "diskpilotx-skill-crud-" + Guid.NewGuid().ToString("N"));
        try
        {
            var provider = new FileSkillPromptProvider([tempRoot]);
            var tool = new ManageDiskSkillsTool(provider);

            var create = await tool.ExecuteAsync(
                EmptyContext(),
                Args(new
                {
                    operation = "create",
                    id = "unity-cache-audit",
                    domain = "disk_management",
                    host_tools = new[] { "find_large_files", "scan_path" },
                    title = "Unity Cache Audit Skill",
                    description = "Use when auditing Unity-generated cache folders with DiskPilot-X scan evidence.",
                    body_markdown = "## Steps\n- Inspect scanned Unity cache folders.\n- Rank risk from skill evidence before proposing cleanup."
                }),
                CancellationToken.None);

            create.GetProperty("ok").GetBoolean().Should().BeTrue();
            create.TryGetProperty("content", out _).Should().BeFalse();
            provider.GetSkillCatalog().Should().ContainSingle(skill =>
                skill.Id == "unity-cache-audit" &&
                skill.DisplayName == "Unity Cache Audit Skill" &&
                skill.Description.Contains("Unity-generated cache"));

            var read = await tool.ExecuteAsync(EmptyContext(), Args(new { operation = "read", id = "unity-cache-audit" }), CancellationToken.None);
            read.GetProperty("content").GetString().Should().Contain("## Steps");

            var list = await tool.ExecuteAsync(EmptyContext(), Args(new { operation = "list" }), CancellationToken.None);
            list.GetProperty("skills").EnumerateArray().Should().Contain(skill => skill.GetProperty("id").GetString() == "unity-cache-audit");

            var delete = await tool.ExecuteAsync(EmptyContext(), Args(new { operation = "delete", id = "unity-cache-audit" }), CancellationToken.None);
            delete.GetProperty("ok").GetBoolean().Should().BeTrue();
            provider.GetSkillCatalog().Should().NotContain(skill => skill.Id == "unity-cache-audit");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ExecuteAsync_RejectsNonDiskSkillCreation()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "diskpilotx-skill-guard-" + Guid.NewGuid().ToString("N"));
        try
        {
            var tool = new ManageDiskSkillsTool(new FileSkillPromptProvider([tempRoot]));

            var result = await tool.ExecuteAsync(
                EmptyContext(),
                Args(new
                {
                    operation = "create",
                    id = "write-emails",
                    domain = "email",
                    host_tools = new[] { "scan_path" },
                    title = "Write Emails",
                    description = "Draft outgoing email replies.",
                    body_markdown = "## Steps\n- Draft email."
                }),
                CancellationToken.None);

            result.GetProperty("ok").GetBoolean().Should().BeFalse();
            result.GetProperty("error").GetProperty("code").GetString().Should().Be("unsupported_domain");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ExecuteAsync_RejectsSkillWithoutHostToolSupport()
    {
        var tool = new ManageDiskSkillsTool(new FileSkillPromptProvider([Path.Combine(Path.GetTempPath(), "diskpilotx-skill-no-tools-" + Guid.NewGuid().ToString("N"))]));

        var result = await tool.ExecuteAsync(
            EmptyContext(),
            Args(new
            {
                operation = "create",
                id = "unsupported-cleaner",
                domain = "disk_management",
                title = "Unsupported Cleaner",
                description = "Clean something the host cannot inspect.",
                body_markdown = "## Steps\n- Use an unavailable external cleaner."
            }),
            CancellationToken.None);

        result.GetProperty("ok").GetBoolean().Should().BeFalse();
        result.GetProperty("error").GetProperty("code").GetString().Should().Be("missing_host_tools");
    }

    private static AgentContext EmptyContext() => new(null, null, null, null, false);

    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);
}
