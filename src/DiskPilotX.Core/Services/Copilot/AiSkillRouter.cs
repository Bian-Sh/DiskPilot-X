using System.Text.RegularExpressions;
using DiskPilotX.Core.Models;

namespace DiskPilotX.Core.Services.Copilot;

public sealed class AiSkillRouter : IAiSkillRouter
{
    private static readonly Regex SkillMentionRegex = new(@"(?<!\S)@([A-Za-z0-9][A-Za-z0-9._-]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // 只识别明确的清理规则创作请求；普通清理请求由助手主动读取技能目录。
    private static readonly Regex CreatorIntent = new(@"(?:创建|新建|编写|生成|修改|更新|编辑|调整)[^。！？\r\n]{0,80}(?:清理|清除)[^。！？\r\n]{0,40}(?:规则|技能|skill)|\b(?:create|build|write|update|edit|modify)\b[^.!?\r\n]{0,80}\bcleanup\b[^.!?\r\n]{0,40}\b(?:skill|rules?)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
    private readonly ISkillPromptProvider _skillPromptProvider;

    public AiSkillRouter() : this(new FileSkillPromptProvider()) { }
    public AiSkillRouter(ISkillPromptProvider skillPromptProvider) => _skillPromptProvider = skillPromptProvider;
    public string? GetSkillSource(string skillId) => _skillPromptProvider.GetRawContent(skillId);
    public IReadOnlyList<AiSkillCatalogItem> GetSkillCatalog() => _skillPromptProvider.GetSkillCatalog();

    public AiSkillRoutingResult Route(string userMessage, FileEntry? linkedEntry, FileEntry? currentViewRoot, bool hasExistingRecommendations, string? responseLanguage = null)
    {
        // 每轮使用一次新鲜目录快照；保存后立即可选，且不会在一次路由内混用两个版本。
        var catalog = GetSkillCatalog().ToDictionary(skill => skill.Id, StringComparer.OrdinalIgnoreCase);
        var message = userMessage.Trim();
        var selected = SkillMentionRegex.Matches(message).Select(match => match.Groups[1].Value).Where(catalog.ContainsKey).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (selected.Count == 0 && catalog.ContainsKey("skill-creator") && CreatorIntent.IsMatch(message)) selected.Add("skill-creator");
        var skills = selected.Select(id =>
        {
            var item = catalog[id];
            return new AiSkill(item.Id, item.Description, _skillPromptProvider.GetPrompt(item.Id, $"Skill {item.Id}: {item.Description}"));
        }).ToList();
        return new AiSkillRoutingResult(skills) { SelectedSkillIds = selected };
    }
}
