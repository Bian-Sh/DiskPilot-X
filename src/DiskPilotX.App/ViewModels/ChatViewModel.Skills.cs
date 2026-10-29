using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Copilot;
namespace DiskPilotX.App.ViewModels;

public sealed record CleanupRuleShortcut(string Id, string Title, string Description, string Prompt);
public partial class ChatViewModel
{
    [ObservableProperty] private ObservableCollection<CleanupRuleShortcut> _cleanupRules = [];
    public event Action<SkillTaskCard>? SkillTaskRequested;
    /// <summary>请求在新窗口中打开某个按需视图；由宿主负责创建窗口。</summary>
    public event Action<SkillUiSpec>? SkillWindowRequested;
    public void RefreshSkills()
    {
        var catalog = _skillRouter.GetSkillCatalog();
        SkillMentionSuggestions = new(catalog.Select(s => new ChatSkillSuggestion("@" + s.Id, s.DisplayName, s.Description)));
        FilteredSkillMentionSuggestions = new(BuildFilteredSkillMentionSuggestions(InputText));
        CleanupRules = new(catalog.Where(s => s.Id is not ("skill-creator" or "app-guide" or "disk-management" or "path-cleanup-recommendation"))
            .Select(s => new CleanupRuleShortcut(s.Id, s.DisplayName,
                s.Description, BuildShortcutPrompt(s))));
    }
    /// <summary>入口只表达运行意图；范围、参数、视图和回传方式由技能自己的契约决定。</summary>
    private static string BuildShortcutPrompt(AiSkillCatalogItem skill) => skill.Kind == "provider"
        ? "@" + skill.Id + " 请先读取并按这份应用数据发现规则运行。需要参数时打开规则自带的任务面板，或让我选择；不要擅自扩大范围。"
          + "源码返回视图后不要再调用 render_view 重复渲染；是否勾选回传或在面板中操作，由规则和我的需求决定。只读发现，不直接清理。"
        : "@" + skill.Id + " 请按这条清理规则检查，先让我选择范围并预览，保留用户资料，清理前再确认。";
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

    /// <summary>
    /// 把模型按需构建的视图追加到当前助手泡泡。泡泡里只放入口卡，因此同一条消息里
    /// 讲同一件事的视图只留第一份（技能源码先返回的那份数据最全），
    /// 后面模型补发的 render_view 不再叠一张卡。
    /// </summary>
    public void AddView(SkillUiSpec spec)
    {
        var target = displayMessage ?? Messages.LastOrDefault(m => m.Sender == ChatSender.Assistant);
        if (target is null) { target = new() { Sender = ChatSender.Assistant, Timestamp = DateTime.Now }; Messages.Add(target); }
        var normalized = SkillUiSpecContract.Normalize(spec);
        if (target.Views.Any(existing => string.Equals(existing.Signature, normalized.Signature, StringComparison.Ordinal)
            || existing.DescribesSameContent(normalized))) return;
        target.Views.Add(normalized);
    }

    /// <summary>在新窗口中打开按需视图，供用户深入观测与回传。</summary>
    public void OpenSkillWindow(SkillUiSpec spec) => SkillWindowRequested?.Invoke(SkillUiSpecContract.Normalize(spec));
}
