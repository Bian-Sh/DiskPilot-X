using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpaceMonger.Core.Enums;
using SpaceMonger.Core.Models;
namespace SpaceMonger.App.ViewModels;

public sealed record CleanupRuleShortcut(string Id, string Title, string Description, string Prompt);
public partial class ChatViewModel
{
    [ObservableProperty] private ObservableCollection<CleanupRuleShortcut> _cleanupRules = [];
    public event Action<SkillTaskCard>? SkillTaskRequested;
    public void RefreshSkills()
    {
        var catalog = _skillRouter.GetSkillCatalog();
        SkillMentionSuggestions = new(catalog.Select(s => new ChatSkillSuggestion("@" + s.Id, s.DisplayName, s.Description)));
        FilteredSkillMentionSuggestions = new(BuildFilteredSkillMentionSuggestions(InputText));
        CleanupRules = new(catalog.Where(s => s.Id is not ("skill-creator" or "app-guide" or "disk-management" or "path-cleanup-recommendation"))
            .Select(s => new CleanupRuleShortcut(s.Id, s.Id == "unity-project-cleanup" ? "检查 Unity 项目缓存" : s.DisplayName,
                s.Description, "@" + s.Id + " 请按这条清理规则检查，先让我选择范围并预览，保留用户资料，清理前再确认。")));
    }
    [RelayCommand] private async Task ExplorePromptAsync(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt) || IsOperationRunning || HasPendingInteractionCard || PendingAskUser is not null) return;
        InputText = prompt;
        await SubmitOrStopAsync();
    }
    public void AddSkillTask(SkillTaskCard card)
    {
        var target = displayMessage ?? Messages.LastOrDefault(m => m.Sender == ChatSender.Assistant);
        if (target is null) { target = new() { Sender = ChatSender.Assistant, Timestamp = DateTime.Now }; Messages.Add(target); }
        if (!target.SkillTasks.Any(t => t.TaskId == card.TaskId)) target.SkillTasks.Add(card);
    }
    public void OpenSkillTask(SkillTaskCard card) => SkillTaskRequested?.Invoke(card);
}
