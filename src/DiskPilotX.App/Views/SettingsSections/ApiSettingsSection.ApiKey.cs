using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using DiskPilotX.App.Localization;
using DiskPilotX.App.ViewModels;

namespace DiskPilotX.App.Views.SettingsSections;

public partial class ApiSettingsSection
{
    private SettingsViewModel? _apiKeyViewModel;
    private bool _syncingApiKeyMask;
    private bool _changingApiKeyVisibility;
    private bool _watchingApiKeyLanguage;
    private Action? _apiKeyLanguageChanged;

    private void InitializeApiKeyVisibility()
    {
        var weak = new WeakReference<ApiSettingsSection>(this);
        Action? languageChanged = null;
        languageChanged = () =>
        {
            if (weak.TryGetTarget(out var section)) section.UpdateApiKeyVisibilityLabel();
            else L.LanguageChanged -= languageChanged;
        };
        _apiKeyLanguageChanged = languageChanged;
        // Checked/Unchecked 同时覆盖鼠标、键盘和可访问性 Toggle，不依赖 Click。
        ApiKeyVisibilityToggle.Checked += ApiKeyVisibilityChanged;
        ApiKeyVisibilityToggle.Unchecked += ApiKeyVisibilityChanged;
        DataContextChanged += (_, _) => { WatchApiKeyViewModel(); ResetApiKeyVisibility(); };
        Unloaded += (_, _) =>
        {
            ResetApiKeyVisibility();
            if (_apiKeyViewModel is not null) _apiKeyViewModel.PropertyChanged -= ApiKeyViewModelChanged;
            _apiKeyViewModel = null;
            if (_watchingApiKeyLanguage) L.LanguageChanged -= _apiKeyLanguageChanged;
            _watchingApiKeyLanguage = false;
        };
        ResetApiKeyVisibility();
    }

    private void WatchApiKeyViewModel()
    {
        var vm = DataContext as SettingsViewModel;
        if (!ReferenceEquals(vm, _apiKeyViewModel))
        {
            if (_apiKeyViewModel is not null) _apiKeyViewModel.PropertyChanged -= ApiKeyViewModelChanged;
            _apiKeyViewModel = vm;
            if (vm is not null) vm.PropertyChanged += ApiKeyViewModelChanged;
        }
        if (IsLoaded && !_watchingApiKeyLanguage)
        {
            L.LanguageChanged += _apiKeyLanguageChanged;
            _watchingApiKeyLanguage = true;
        }
        SyncApiKeyMask();
    }

    private void ApiKeyViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SettingsViewModel.ApiKey) && !string.IsNullOrEmpty(e.PropertyName)) return;
        if (Dispatcher.CheckAccess()) SyncApiKeyMask();
        else Dispatcher.BeginInvoke(SyncApiKeyMask);
    }

    private void SyncApiKeyMask()
    {
        var value = _apiKeyViewModel?.ApiKey ?? string.Empty;
        if (ApiKeyMaskedInput.Password == value) return;
        _syncingApiKeyMask = true;
        try { ApiKeyMaskedInput.Password = value; }
        finally { _syncingApiKeyMask = false; }
    }

    private void ApiKeyMaskedInput_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_syncingApiKeyMask && DataContext is SettingsViewModel vm)
            vm.ApiKey = ApiKeyMaskedInput.Password;
    }

    private void ApiKeyVisibilityChanged(object sender, RoutedEventArgs e) => SetApiKeyVisibility(ApiKeyVisibilityToggle.IsChecked == true);

    private void ResetApiKeyVisibility()
    {
        if (ApiKeyVisibilityToggle is null) return;
        ApiKeyVisibilityToggle.IsChecked = false;
        SetApiKeyVisibility(false);
    }

    private void SetApiKeyVisibility(bool visible)
    {
        _changingApiKeyVisibility = true;
        try
        {
            ApiKeyInput.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            ApiKeyMaskedInput.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
            ApiKeyEyeSlash.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
            UpdateApiKeyVisibilityLabel();
        }
        finally { _changingApiKeyVisibility = false; }
    }

    private void UpdateApiKeyVisibilityLabel()
    {
        // 语言通知可能来自另一个 UI 调度器，不跨线程读取控件，也不强引用已关闭面板。
        if (!Dispatcher.CheckAccess())
        {
            if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(UpdateApiKeyVisibilityLabel);
            return;
        }
        var label = L.Text(ApiKeyVisibilityToggle.IsChecked == true ? "HideApiKey" : "ShowApiKey");
        ApiKeyVisibilityToggle.ToolTip = label;
        AutomationProperties.SetName(ApiKeyVisibilityToggle, label);
    }
}
