using DiskPilotX.Core.Services.Copilot;

namespace DiskPilotX.Core.Tests;

public sealed class CleanupSkillProviderTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "cleanup-provider-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void RouterDiscoversSkillCreatedAfterFirstRoute()
    {
        var provider = new FileSkillPromptProvider([root]);
        var router = new AiSkillRouter(provider);
        Assert.Empty(router.Route("@new-cleanup", null, null, false).Skills);
        Assert.True(provider.CreateOrUpdateSkill("new-cleanup", "新清理", "清理测试", "只读检查", false).Success);
        Assert.Single(router.Route("@new-cleanup", null, null, false).Skills);
    }

    [Fact]
    public void ProviderProtectsCreatorEvenWhenNoBuiltinFileExists()
    {
        var provider = new FileSkillPromptProvider([root]);
        Assert.False(provider.CreateOrUpdateSkill("skill-creator", "创建器", "内置", "禁止覆盖", true).Success);
    }

    [Theory]
    [InlineData("帮我创建 npm 缓存清理规则")]
    [InlineData("修改清理技能，保留下载文件")]
    [InlineData("Create a cleanup skill for npm")]
    [InlineData("Update my cleanup rules")]
    public void ExplicitCreatorIntentSelectsOnlyCreator(string message)
    {
        Directory.CreateDirectory(Path.Combine(root, "skill-creator"));
        File.WriteAllText(Path.Combine(root, "skill-creator", "SKILL.md"), "# 创建器\n## Purpose\n创建清理技能");
        var router = new AiSkillRouter(new FileSkillPromptProvider([root]));
        Assert.Equal("skill-creator", Assert.Single(router.Route(message, null, null, false).SelectedSkillIds));
        Assert.Empty(router.Route("请清理 npm 缓存", null, null, false).Skills);
        Assert.Empty(router.Route("如何创建新的文件夹", null, null, false).Skills);
    }

    [Fact]
    public void ProviderReadsStructuredUserPackageAndBuiltinWins()
    {
        var builtin = Path.Combine(root, "builtin"); var user = Path.Combine(root, "user");
        Directory.CreateDirectory(Path.Combine(builtin, "skill-creator"));
        File.WriteAllText(Path.Combine(builtin, "skill-creator", "SKILL.md"), "# 内置创建器");
        var store = new DiskPilotX.Core.Services.Skills.CleanupSkillStore(user, [builtin]);
        store.Save(new() { Id = "structured", Title = "结构化包", Description = "用户技能", SkillMarkdown = "# 内容", Rules = [new() { Id = "cache", Path = "cache", Risk = "可再生", Description = "缓存", Sources = ["用户"] }] });
        var provider = new FileSkillPromptProvider([builtin], user);
        Assert.Contains(provider.GetSkillCatalog(), s => s.Id == "structured");
        Assert.Contains("结构化包", provider.GetRawContent("structured"));
        Assert.False(provider.DeleteSkill("skill-creator").Success);
        Assert.False(provider.DeleteSkill("structured").Success);
        Assert.Single(store.List());
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
