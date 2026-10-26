using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskPilotX.App.Localization;
using DiskPilotX.Core.Services.Llm;

namespace DiskPilotX.App.ViewModels;

public sealed record ThinkingLevelOption(ThinkingLevel Value, string Label);
public sealed record ApiFormatOption(LlmApiFormat Value, string Label);
public sealed partial class CliIdentityOption(CliIdentityKind value, string label) : ObservableObject
{
    public CliIdentityKind Value { get; } = value;
    public string Label { get; } = label;
    public string DisplayLabel => Value == CliIdentityKind.None ? Label : $"{Label} · {CliIdentityService.GetVersion(Value)}";
    public string Fingerprint => string.Join("\n", CliIdentityService.BuildHeaders(Value).Select(header => $"{header.Key}: {header.Value}"));
    public IAsyncRelayCommand? RefreshCommand { get; set; }
    public bool CanRefresh => Value != CliIdentityKind.None && !IsRefreshing;
    public string RefreshToolTip => $"{Status ?? L.Text("SettingsRefreshCliIdentityHint")}\n{Fingerprint}".Trim();
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanRefresh))] private bool _isRefreshing;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(RefreshToolTip))] private string? _status;
    public void RefreshDisplay()
    {
        OnPropertyChanged(nameof(DisplayLabel));
        OnPropertyChanged(nameof(Fingerprint));
        OnPropertyChanged(nameof(RefreshToolTip));
    }
}
public sealed record ProxyKindOption(ProxyKind Value, string Label);

/// <summary>AI 设置下拉选项的唯一来源；标签本地化，语言切换后需重新读取。</summary>
public static class AiOptions
{
    public static IReadOnlyList<ThinkingLevelOption> ThinkingLevels() =>
    [
        new(ThinkingLevel.Off, L.Text("ThinkingLevelOffLabel")),
        new(ThinkingLevel.Minimal, L.Text("ThinkingLevelMinimalLabel")),
        new(ThinkingLevel.Low, L.Text("ThinkingLevelLowLabel")),
        new(ThinkingLevel.Medium, L.Text("ThinkingLevelMediumLabel")),
        new(ThinkingLevel.High, L.Text("ThinkingLevelHighLabel")),
    ];

    public static IReadOnlyList<ApiFormatOption> ApiFormats() =>
    [
        new(LlmApiFormat.AnthropicMessages, L.Text("ApiFormatAnthropicLabel")),
        new(LlmApiFormat.OpenAiResponses, L.Text("ApiFormatResponsesLabel")),
        new(LlmApiFormat.OpenAiChatCompletions, L.Text("ApiFormatChatCompletionsLabel")),
    ];

    public static IReadOnlyList<CliIdentityOption> CliIdentities() =>
    [
        new(CliIdentityKind.None, L.Text("CliIdentityNoneLabel")),
        new(CliIdentityKind.ClaudeCode, L.Text("CliIdentityClaudeCodeLabel")),
        new(CliIdentityKind.Codex, L.Text("CliIdentityCodexLabel")),
        new(CliIdentityKind.Grok, L.Text("CliIdentityGrokLabel")),
    ];

    public static IReadOnlyList<ProxyKindOption> ProxyKinds() =>
    [
        new(ProxyKind.System, L.Text("ProxyModeSystemLabel")),
        new(ProxyKind.Direct, L.Text("ProxyModeDirectLabel")),
        new(ProxyKind.Custom, L.Text("ProxyModeCustomLabel")),
    ];
}
