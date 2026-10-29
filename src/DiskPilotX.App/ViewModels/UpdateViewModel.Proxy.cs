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
    partial void OnProxyPortTextChanged(string? value) => PersistProxySettings(settings =>
    {
        // 空输入使用默认端口；非空非法输入保存为无效值，避免实际请求悄悄改用默认端口。
        settings.UpdateProxyPort = string.IsNullOrWhiteSpace(value)
            ? SelectedProxyKind == ProxyKind.Custom ? Services.UpdateService.DefaultProxyPort : null
            : int.TryParse(value.Trim(), out var port) && port is > 0 and < 65536 ? port : 0;
    });
    partial void OnProxyUsernameChanged(string? value) => PersistProxySettings(settings => settings.UpdateProxyUsername = string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    partial void OnProxyPasswordChanged(string? value) => PersistProxySettings(settings => settings.EncryptedUpdateProxyPassword = string.IsNullOrEmpty(value) ? null : _settingsService!.EncryptProxyPassword(value));

    public void CommitProxySettings()
    {
        // 只在切换、加载或提交时补齐，不能让默认值打断用户清空后重新输入。
        if (_initializing || SelectedProxyKind != ProxyKind.Custom) return;
        if (string.IsNullOrWhiteSpace(ProxyHost)) ProxyHost = LlmContracts.DefaultProxyHost;
        if (string.IsNullOrWhiteSpace(ProxyPortText)) ProxyPortText = Services.UpdateService.DefaultProxyPort.ToString();
    }

    private void PersistProxySettings(Action<AppSettings> update)
    {
        if (_initializing || _settingsService is null) return;
        var settings = _settingsService.LoadSettings();
        update(settings);
        _settingsService.SaveSettings(settings);
    }
}
