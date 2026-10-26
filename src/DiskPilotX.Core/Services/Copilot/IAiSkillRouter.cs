using DiskPilotX.Core.Models;

namespace DiskPilotX.Core.Services.Copilot;

public interface IAiSkillRouter
{
    AiSkillRoutingResult Route(string userMessage, FileEntry? linkedEntry, FileEntry? currentViewRoot, bool hasExistingRecommendations, string? responseLanguage = null);
    string? GetSkillSource(string skillId);
    IReadOnlyList<AiSkillCatalogItem> GetSkillCatalog();
}
