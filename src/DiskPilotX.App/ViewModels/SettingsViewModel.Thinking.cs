using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DiskPilotX.App.Localization;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Llm;

namespace DiskPilotX.App.ViewModels;

/// <summary>设置中的复选项声明模型支持集合，不修改对话正在使用的等级。</summary>
public sealed partial class SupportedThinkingLevelOption : ObservableObject
{
    public required ThinkingLevel Value { get; init; }
    public required string Label { get; init; }
    [ObservableProperty] private bool _isSelected;
}

public sealed record ThinkingControlModeOption(ThinkingControlMode Value, string Label);

public partial class SettingsViewModel
{
    private bool _refreshingThinking;
    public ObservableCollection<SupportedThinkingLevelOption> SupportedThinkingLevelOptions { get; } = [];
    [ObservableProperty] private string? _thinkingConfigurationModel;
    [ObservableProperty] private ThinkingControlMode _selectedThinkingControlMode;
    public bool IsAnthropicThinking => SelectedApiFormat == LlmApiFormat.AnthropicMessages;
    public bool HasThinkingConfigurationModel => !string.IsNullOrWhiteSpace(ThinkingConfigurationModel);
    public IReadOnlyList<ThinkingControlModeOption> ThinkingControlModeOptions =>
    [
        new(ThinkingControlMode.Budget, L.Text("ThinkingBudgetModeLabel")),
        new(ThinkingControlMode.Effort, L.Text("ThinkingEffortModeLabel")),
    ];
    public string ThinkingCapabilityHint => L.Text(IsAnthropicThinking && SelectedThinkingControlMode == ThinkingControlMode.Budget
        ? "SettingsThinkingBudgetHint" : "SettingsThinkingCapabilityHint");

    partial void OnThinkingConfigurationModelChanged(string? value)
    {
        OnPropertyChanged(nameof(HasThinkingConfigurationModel));
        if (!_isLoadingSettings) RefreshThinkingConfiguration();
    }

    private void RefreshThinkingModels()
    {
        if (!ModelCandidates.Contains(ThinkingConfigurationModel ?? string.Empty))
            ThinkingConfigurationModel = ModelCandidates.FirstOrDefault(id => string.Equals(id, ChatModelName, StringComparison.OrdinalIgnoreCase)) ?? ModelCandidates.FirstOrDefault();
    }

    private AppSettings ThinkingScope(AppSettings settings) => new()
    {
        ApiFormat = LlmContracts.FormatApiFormat(SelectedApiFormat),
        AnthropicBaseUrl = AnthropicBaseUrl,
        ChatModelName = ThinkingConfigurationModel,
        ThinkingLevel = settings.ThinkingLevel,
        EnableThinking = settings.EnableThinking,
        ModelThinkingConfigurations = settings.ModelThinkingConfigurations,
    };

    partial void OnSelectedThinkingControlModeChanged(ThinkingControlMode value)
    {
        if (_refreshingThinking || _isLoadingSettings || !HasThinkingConfigurationModel) return;
        PersistAppSettings(settings => ThinkingCapabilities.SetControlMode(ThinkingScope(settings), value));
        RefreshThinkingConfiguration();
    }

    private void RefreshThinkingConfiguration()
    {
        if (_refreshingThinking) return;
        _refreshingThinking = true;
        try
        {
            var scope = ThinkingScope(_settingsService.LoadSettings());
            SelectedThinkingControlMode = ThinkingCapabilities.GetControlMode(scope);
            var selected = ThinkingCapabilities.GetSupportedLevels(scope);
            SupportedThinkingLevelOptions.Clear();
            foreach (var option in AiOptions.ThinkingLevels(ThinkingCapabilities.GetCandidates(SelectedApiFormat, SelectedThinkingControlMode)))
            {
                var item = new SupportedThinkingLevelOption { Value = option.Value, Label = option.Label, IsSelected = selected.Contains(option.Value) };
                item.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName != nameof(item.IsSelected) || _refreshingThinking || _isLoadingSettings || !HasThinkingConfigurationModel) return;
                    var levels = SupportedThinkingLevelOptions.Where(candidate => candidate.IsSelected).Select(candidate => candidate.Value).ToArray();
                    if (levels.Length == 0) { item.IsSelected = true; return; }
                    PersistAppSettings(settings => ThinkingCapabilities.SetSupportedLevels(ThinkingScope(settings), levels));
                };
                SupportedThinkingLevelOptions.Add(item);
            }
            OnPropertyChanged(nameof(IsAnthropicThinking));
            OnPropertyChanged(nameof(ThinkingCapabilityHint));
            OnPropertyChanged(nameof(ThinkingControlModeOptions));
        }
        finally { _refreshingThinking = false; }
    }
}
