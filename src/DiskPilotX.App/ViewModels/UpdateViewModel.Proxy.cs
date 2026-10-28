using CommunityToolkit.Mvvm.ComponentModel;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Llm;

namespace DiskPilotX.App.ViewModels;

public partial class UpdateViewModel
{
    [ObservableProperty] private ProxyKind _selectedProxyKind = ProxyKind.System;
    [ObservableProperty] private string? _proxyHost;
    [ObservableProperty] private string? _proxyPortText;
    [ObservableProperty] private string? _proxyUsername;
    [ObservableProperty] private string? _proxyPassword;
    public IReadOnlyList<ProxyKindOption> ProxyKindOptions => AiOptions.ProxyKinds();
    public bool IsCustomProxyConfigurable => SelectedProxyKind == ProxyKind.Custom;

    private void LoadProxySettings(AppSettings? settings)
    {
        if (settings is null) return;
        if (settings.EnsureIndependentUpdateProxy()) _settingsService?.SaveSettings(settings);
        SelectedProxyKind = LlmContracts.ParseProxyKind(settings.UpdateProxyMode);
        ProxyHost = settings.UpdateProxyHost;
        ProxyPortText = settings.UpdateProxyPort?.ToString();
        ProxyUsername = settings.UpdateProxyUsername;
    }

    partial void OnSelectedProxyKindChanged(ProxyKind value)
    {
        CommitProxySettings();
        PersistProxySettings(settings => settings.UpdateProxyMode = LlmContracts.FormatProxyKind(value));
        OnPropertyChanged(nameof(IsCustomProxyConfigurable));
    }
    partial void OnProxyHostChanged(string? value) => PersistProxySettings(settings => settings.UpdateProxyHost = string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    partial void OnProxyPortTextChanged(string? value) => PersistProxySettings(settings => settings.UpdateProxyPort = int.TryParse(value?.Trim(), out var port) && port is > 0 and < 65536 ? port : null);
    partial void OnProxyUsernameChanged(string? value) => PersistProxySettings(settings => settings.UpdateProxyUsername = string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    partial void OnProxyPasswordChanged(string? value) => PersistProxySettings(settings => settings.EncryptedUpdateProxyPassword = string.IsNullOrEmpty(value) ? null : _settingsService!.EncryptProxyPassword(value));

    public void CommitProxySettings()
    {
        // 只在切换、加载或提交时补齐，不能让默认值打断用户清空后重新输入。
        if (!_initializing && SelectedProxyKind == ProxyKind.Custom && string.IsNullOrWhiteSpace(ProxyHost))
            ProxyHost = LlmContracts.DefaultProxyHost;
    }

    private void PersistProxySettings(Action<AppSettings> update)
    {
        if (_initializing || _settingsService is null) return;
        var settings = _settingsService.LoadSettings();
        update(settings);
        _settingsService.SaveSettings(settings);
    }
}
