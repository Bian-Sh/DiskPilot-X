namespace DiskPilotX.Core.Services.Copilot;

public interface ISkillPromptProvider
{
    IReadOnlyList<AiSkillCatalogItem> GetSkillCatalog();
    string GetPrompt(string skillId, string fallback);
    string? GetRawContent(string skillId);
    AiSkillStoreResult CreateOrUpdateSkill(string skillId, string title, string description, string bodyMarkdown, bool overwrite);
    AiSkillStoreResult DeleteSkill(string skillId);
}
