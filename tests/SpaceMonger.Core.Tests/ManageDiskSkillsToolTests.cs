using System.Text.Json;
using FluentAssertions;
using SpaceMonger.Core.Services.Agent;
using SpaceMonger.Core.Services.Copilot;

namespace SpaceMonger.Core.Tests;

public class ManageDiskSkillsToolTests
{
    [Fact]
    public async Task ExecuteAsync_CanCreateReadListAndDeleteDiskSkill()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "spacemonger-skill-crud-" + Guid.NewGuid().ToString("N"));
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
                    description = "Use when auditing Unity-generated cache folders with SpaceMonger scan evidence.",
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
        var tempRoot = Path.Combine(Path.GetTempPath(), "spacemonger-skill-guard-" + Guid.NewGuid().ToString("N"));
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
        var tool = new ManageDiskSkillsTool(new FileSkillPromptProvider([Path.Combine(Path.GetTempPath(), "spacemonger-skill-no-tools-" + Guid.NewGuid().ToString("N"))]));

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
