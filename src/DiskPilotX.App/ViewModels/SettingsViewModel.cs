using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskPilotX.App.Localization;
using DiskPilotX.App.Services;
using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Models.Theme;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Llm;
using DiskPilotX.Core.Services.Settings;
using DiskPilotX.Core.Services.Whitelist;
using TinyHarness;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiskPilotX.App.ViewModels;

public enum ValidationState
{
    None,
    Validating,
    Valid,
    Invalid
}

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly ModelConnectionTester _llmClient;
    private readonly ThemeManager _themeManager;
    private readonly ModelCatalogService? _modelCatalog;
    private readonly ILogger<SettingsViewModel> _logger;
    private bool _proxyPasswordEdited;
    private readonly IPathWhitelistMatcher _whitelistMatcher;
    private bool _isLoadingSettings;
    private bool _suppressPersistence;

    public event Action? SettingsChanged;

    public Func<string, Task>? ShowModelDiscoveryFailureAsync { get; set; }

    [ObservableProperty]
    private string? _apiKey;

    partial void OnApiKeyChanged(string? value)
    {
        if (_isLoadingSettings)
            return;

        InvalidateModelDiscovery();
        _suppressPersistence = true;
        try
        {
            IsApiKeyValid = false;
            ValidationState = ValidationState.None;
            ValidationMessage = null;
        }
        finally
        {
            _suppressPersistence = false;
        }

        PersistAppSettings(settings =>
        {
            settings.EncryptedApiKey = string.IsNullOrEmpty(value)
                ? null
                : _settingsService.EncryptApiKey(value);
            settings.IsApiKeyValid = false;
        });
    }

    [ObservableProperty]
    private string? _anthropicBaseUrl;

    partial void OnAnthropicBaseUrlChanged(string? value)
    {
        InvalidateModelDiscovery();
        PersistAppSettings(settings =>
            settings.AnthropicBaseUrl = string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }

    [ObservableProperty]
    private string? _modelsUrl;

    partial void OnModelsUrlChanged(string? value)
    {
        InvalidateModelDiscovery();
        PersistAppSettings(settings => settings.ModelsUrl = string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }

    [ObservableProperty]
    private string? _chatModelName;

    partial void OnChatModelNameChanged(string? value)
    {
        PersistAppSettings(settings =>
            settings.ChatModelName = string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }

    [ObservableProperty]
    private ThinkingLevel _selectedThinkingLevel = ThinkingLevel.Off;

    partial void OnSelectedThinkingLevelChanged(ThinkingLevel value)
    {
        PersistAppSettings(settings => settings.ThinkingLevel = LlmContracts.FormatThinkingLevel(value));
    }

    [ObservableProperty]
    private LlmApiFormat _selectedApiFormat = LlmApiFormat.AnthropicMessages;

    partial void OnSelectedApiFormatChanged(LlmApiFormat value)
    {
        InvalidateModelDiscovery();
        PersistAppSettings(settings => settings.ApiFormat = LlmContracts.FormatApiFormat(value));
    }

    [ObservableProperty]
    private CliIdentityKind _selectedCliIdentity = CliIdentityKind.None;

    partial void OnSelectedCliIdentityChanged(CliIdentityKind value)
    {
        InvalidateModelDiscovery();
        PersistAppSettings(settings => settings.CliIdentity = LlmContracts.FormatCliIdentity(value));
        OnPropertyChanged(nameof(CliIdentitySummary));
        OnPropertyChanged(nameof(CliIdentityFingerprint));
    }

    [ObservableProperty]
    private ProxyKind _selectedProxyKind = ProxyKind.System;

    partial void OnSelectedProxyKindChanged(ProxyKind value)
    {
        InvalidateModelDiscovery();
        PersistAppSettings(settings => settings.ProxyMode = LlmContracts.FormatProxyKind(value));
        OnPropertyChanged(nameof(IsCustomProxyConfigurable));
    }

    [ObservableProperty]
    private string? _proxyHost;

    partial void OnProxyHostChanged(string? value)
    {
        InvalidateModelDiscovery();
        PersistAppSettings(settings => settings.ProxyHost = string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }

    [ObservableProperty]
    private string? _proxyPortText = "7890";

    partial void OnProxyPortTextChanged(string? value)
    {
        InvalidateModelDiscovery();
        PersistAppSettings(settings => settings.ProxyPort = int.TryParse(value?.Trim(), out var port) && port > 0 && port < 65536 ? port : null);
    }

    [ObservableProperty]
    private string? _proxyUsername;

    partial void OnProxyUsernameChanged(string? value)
    {
        InvalidateModelDiscovery();
        PersistAppSettings(settings => settings.ProxyUsername = string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }

    [ObservableProperty]
    private string? _proxyPassword;

    partial void OnProxyPasswordChanged(string? value)
    {
        InvalidateModelDiscovery();
        if (!_isLoadingSettings) _proxyPasswordEdited = true;
        PersistAppSettings(settings => settings.EncryptedProxyPassword = string.IsNullOrEmpty(value) ? null : _settingsService.EncryptProxyPassword(value));
    }

    public bool IsCustomProxyConfigurable => SelectedProxyKind == ProxyKind.Custom;

    public ObservableCollection<string> ModelCandidates { get; } = [];

    /// <summary>列表顺序即聊天模型菜单顺序，只在有效移动时持久化一次。</summary>
    public bool MoveModel(string? id, int targetIndex)
    {
        if (IsRefreshingModels || id is null) return false;
        var sourceIndex = ModelCandidates.IndexOf(id);
        if (sourceIndex < 0 || targetIndex < 0 || targetIndex >= ModelCandidates.Count || sourceIndex == targetIndex) return false;
        ModelCandidates.Move(sourceIndex, targetIndex);
        PersistAppSettings(settings => settings.DiscoveredModels = ModelCandidates.ToList());
        return true;
    }

    [RelayCommand]
    private void RemoveModel(string? id)
    {
        if (IsRefreshingModels || id is null || !ModelCandidates.Remove(id)) return;
        // 不替用户改选其他模型，也不让旧的设置页选择在保存时复活已移除项。
        if (string.Equals(ChatModelName, id, StringComparison.OrdinalIgnoreCase))
        {
            _suppressPersistence = true;
            try { ChatModelName = null; }
            finally { _suppressPersistence = false; }
        }
        PersistAppSettings(settings =>
        {
            settings.DiscoveredModels = ModelCandidates.ToList();
            if (string.Equals(settings.ChatModelName, id, StringComparison.OrdinalIgnoreCase)) settings.ChatModelName = null;
        });
    }

    [ObservableProperty]
    private bool _isRefreshingModels;

    [ObservableProperty]
    private string? _modelRefreshMessage;

    public IReadOnlyList<ThinkingLevelOption> ThinkingLevelOptions => AiOptions.ThinkingLevels();
    public IReadOnlyList<ApiFormatOption> ApiFormatOptions => AiOptions.ApiFormats();
    private IReadOnlyList<CliIdentityOption> _cliIdentityOptions = AiOptions.CliIdentities();
    public IReadOnlyList<CliIdentityOption> CliIdentityOptions
    {
        get
        {
            foreach (var option in _cliIdentityOptions) option.RefreshCommand = RefreshCliIdentityCommand;
            return _cliIdentityOptions;
        }
    }
    public string? CliIdentitySummary => CliIdentityService.BuildHeaders(SelectedCliIdentity).GetValueOrDefault("User-Agent");
    public string CliIdentityFingerprint => string.Join("\n", CliIdentityService.BuildHeaders(SelectedCliIdentity).Select(header => $"{header.Key}: {header.Value}"));
    public IReadOnlyList<ProxyKindOption> ProxyKindOptions => AiOptions.ProxyKinds();

    [ObservableProperty]
    private string _language = "zh-CN";

    [ObservableProperty]
    private string? _saveToastMessage;

    [ObservableProperty]
    private bool _isSaveToastVisible;

    [ObservableProperty]
    private ValidationState _validationState = ValidationState.None;

    [ObservableProperty]
    private DeletionMode _selectedDeletionMode = DeletionMode.MoveToRecycleBin;

    partial void OnSelectedDeletionModeChanged(DeletionMode value)
    {
        PersistAppSettings(settings => settings.DeletionMode = value);
    }

    [ObservableProperty]
    private bool _isApiKeyValid;

    partial void OnIsApiKeyValidChanged(bool value)
    {
        PersistAppSettings(settings => settings.IsApiKeyValid = value);
    }

    [ObservableProperty]
    private string? _validationMessage;

    [ObservableProperty]
    private string _validationStatusText = string.Empty;

    public ObservableCollection<PathWhitelistEntry> ScanWhitelist { get; } = [];
    public ObservableCollection<PathWhitelistEntry> CleanupRecommendationWhitelist { get; } = [];
    public ObservableCollection<PathWhitelistEntry> AiConversationWhitelist { get; } = [];

    [ObservableProperty]
    private string? _newScanWhitelistPath;

    [ObservableProperty]
    private string? _newCleanupWhitelistPath;

    [ObservableProperty]
    private string? _newAiWhitelistPath;

    [ObservableProperty]
    private string? _whitelistErrorMessage;


    // ── Theme Properties ─────────────────────────────────────────

    [ObservableProperty]
    private ThemeProfile _selectedThemeProfile = ThemeProfile.VisionProDark;

    [ObservableProperty]
    private Color _themeBackgroundColor = Color.FromArgb(255, 52, 52, 64);

    partial void OnThemeBackgroundColorChanged(Color value)
    {
        if (!_isLoadingSettings)
            SyncAndSaveTheme();
    }

    [ObservableProperty]
    private bool _glassEnabled;

    partial void OnGlassEnabledChanged(bool value) => ApplyGlassSettings();

    [ObservableProperty]
    private double _glassOpacityPercent = 60;

    partial void OnGlassOpacityPercentChanged(double value) => ApplyGlassSettings();

    private void ApplyGlassSettings()
    {
        if (_isLoadingSettings) return;
        var profile = _themeManager.CurrentProfile.Clone();
        profile.GlassEnabled = GlassEnabled;
        profile.GlassOpacity = GlassOpacityPercent / 100;
        _themeManager.ApplyTheme(profile);
        SettingsChanged?.Invoke();
    }

    [ObservableProperty]
    private bool _autoTextContrast;

    partial void OnAutoTextContrastChanged(bool value)
    {
        if (!_isLoadingSettings) SyncAndSaveTheme();
    }

    public static IReadOnlyList<ThemeProfile> AvailableThemePresets { get; } = ThemeProfile.BuiltInPresets;

    public SettingsViewModel(ISettingsService settingsService, ModelConnectionTester llmClient, ThemeManager themeManager,
        ModelCatalogService? modelCatalog = null, IPathWhitelistMatcher? whitelistMatcher = null, ILogger<SettingsViewModel>? logger = null)
    {
        _settingsService = settingsService;
        _llmClient = llmClient;
        _themeManager = themeManager;
        _modelCatalog = modelCatalog;
        _logger = logger ?? NullLogger<SettingsViewModel>.Instance;
        _whitelistMatcher = whitelistMatcher ?? new PathWhitelistMatcher();
        L.LanguageChanged += RefreshValidationStatusText;
        L.LanguageChanged += RefreshOptionLabels;
        CliIdentityService.VersionChanged += RefreshCliIdentityDisplay;

        LoadSettings();
    }

    private void RefreshCliIdentityDisplay(CliIdentityKind identity)
    {
        void Refresh()
        {
            foreach (var option in CliIdentityOptions.Where(option => option.Value == identity)) option.RefreshDisplay();
            OnPropertyChanged(nameof(CliIdentitySummary));
            OnPropertyChanged(nameof(CliIdentityFingerprint));
        }
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) Refresh();
        else dispatcher.Invoke(Refresh);
    }

    private void RefreshOptionLabels()
    {
        OnPropertyChanged(nameof(ThinkingLevelOptions));
        OnPropertyChanged(nameof(ApiFormatOptions));
        _cliIdentityOptions = AiOptions.CliIdentities();
        OnPropertyChanged(nameof(CliIdentityOptions));
        OnPropertyChanged(nameof(CliIdentitySummary));
        OnPropertyChanged(nameof(ProxyKindOptions));
    }

    partial void OnValidationStateChanged(ValidationState value)
    {
        RefreshValidationStatusText();
    }

    partial void OnLanguageChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        var language = value.Trim();
        L.SetLanguage(language);

        if (!_isLoadingSettings)
        {
            SaveLanguagePreference(language);
        }
    }

    private void SaveLanguagePreference(string language)
    {
        PersistAppSettings(settings => settings.Language = language);
    }

    private void PersistAppSettings(Action<DiskPilotX.Core.Models.AppSettings> update)
    {
        if (_isLoadingSettings || _suppressPersistence)
            return;

        var settings = _settingsService.LoadSettings();
        update(settings);
        _settingsService.SaveSettings(settings);
        SettingsChanged?.Invoke();
    }

    private void RefreshValidationStatusText()
    {
        ValidationStatusText = ValidationState switch
        {
            ValidationState.Validating => L.Text("SettingsValidationValidating"),
            ValidationState.Valid => L.Text("SettingsValidationValid"),
            ValidationState.Invalid => L.Text("SettingsValidationInvalid"),
            _ => string.Empty
        };
    }

    public static IReadOnlyList<LanguageOption> LanguageOptions { get; } =
    [
        new("zh-CN", "简体中文"),
        new("zh-TW", "繁體中文"),
        new("en", "English"),
        new("auto", "Auto")
    ];

    // ── Theme Actions ─────────────────────────────────────────────

    [RelayCommand]
    private void ApplyThemePreset(ThemeProfile? preset)
    {
        if (preset == null) return;

        var profile = preset.Clone();
        profile.GlassEnabled = GlassEnabled;
        profile.GlassOpacity = _themeManager.CurrentProfile.GlassOpacity;
        _themeManager.ApplyTheme(profile);
        _themeManager.Persist();

        _isLoadingSettings = true;
        try
        {
            LoadThemeFromCurrent();
        }
        finally
        {
            _isLoadingSettings = false;
        }

        SettingsChanged?.Invoke();
    }

    private void LoadThemeFromCurrent()
    {
        var p = _themeManager.CurrentProfile;
        SelectedThemeProfile = p;
        ThemeBackgroundColor = ThemeManager.ParseColor(p.BackgroundColor);
        GlassEnabled = p.GlassEnabled;
        GlassOpacityPercent = 100 * p.GlassOpacity;
        AutoTextContrast = p.AutoTextContrast;
    }

    // ── Existing ──────────────────────────────────────────────────

    [RelayCommand]
    private async Task ValidateAsync()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            ValidationState = ValidationState.Invalid;
            ValidationMessage = L.Text("ApiKeyEmptyMessage");
            IsApiKeyValid = false;
            return;
        }

        ValidationState = ValidationState.Validating;
        ValidationMessage = null;
        var version = _catalogVersion;
        var catalogTask = CompleteModelConfigurationAsync(force: true);

        try
        {
            var endpoint = BuildEndpoint();
            var isValid = await _llmClient.ValidateAsync(endpoint, CancellationToken.None);
            if (version != _catalogVersion) return;

            if (isValid)
            {
                ValidationState = ValidationState.Valid;
                IsApiKeyValid = true;
                ValidationMessage = null;
            }
            else
            {
                ValidationState = ValidationState.Invalid;
                IsApiKeyValid = false;
                ValidationMessage = L.Text("ApiKeyInvalidMessage");
            }
        }
        catch (Exception ex)
        {
            if (version != _catalogVersion) return;
            ValidationState = ValidationState.Invalid;
            IsApiKeyValid = false;
            LogSettingsException("模型连接验证失败", ex);
            ValidationMessage = L.Format("ValidationFailedMessage", "模型连接验证失败，请检查地址、模型与凭据。");
        }
        finally { await catalogTask; }
    }

    /// <summary>用当前编辑中的字段构建端点配置；代理口令优先取输入框，未改动时解密存量值。</summary>
    private ModelEndpointConfig BuildEndpoint()
    {
        var settings = _settingsService.LoadSettings();
        if (SelectedProxyKind == ProxyKind.Custom
            && (!int.TryParse(ProxyPortText?.Trim(), out var port) || port is <= 0 or >= 65536))
            throw new InvalidOperationException("自定义代理端口必须是 1–65535 的整数。");
        var proxyPassword = _proxyPasswordEdited ? ProxyPassword : _settingsService.GetProxyPassword(settings);
        return ModelEndpointConfig.FromSettings(settings, ApiKey ?? string.Empty,
            ModelEndpointResolver.ResolveModel(ChatModelName, AnthropicBaseUrl),
            proxyPassword) with
        {
            BaseUrl = string.IsNullOrWhiteSpace(AnthropicBaseUrl) ? null : AnthropicBaseUrl.Trim(),
            ModelsUrl = string.IsNullOrWhiteSpace(ModelsUrl) ? null : ModelsUrl.Trim(),
            ProxyHost = string.IsNullOrWhiteSpace(ProxyHost) ? null : ProxyHost.Trim(),
            ProxyPort = int.TryParse(ProxyPortText?.Trim(), out var currentPort) ? currentPort : null,
            ProxyUsername = string.IsNullOrWhiteSpace(ProxyUsername) ? null : ProxyUsername.Trim(),
            ApiFormat = SelectedApiFormat,
            CliIdentity = SelectedCliIdentity,
            ThinkingLevel = SelectedThinkingLevel,
            ProxyMode = SelectedProxyKind,
        };
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task RefreshCliIdentityAsync(CliIdentityOption? option)
    {
        if (option is null || !option.CanRefresh) return;
        option.IsRefreshing = true;
        option.Status = L.Text("SettingsCliIdentityRefreshRunning");
        try
        {
            var refreshed = await CliIdentityService.RefreshVersionAsync(option.Value);
            option.Status = L.Text(refreshed ? "SettingsCliIdentityRefreshSuccess"
                : option.Value == CliIdentityKind.Grok ? "SettingsCliIdentityBuiltIn" : "SettingsCliIdentityRefreshFailed");
            if (refreshed)
                PersistAppSettings(settings => settings.CliIdentityVersions[LlmContracts.FormatCliIdentity(option.Value)] = CliIdentityService.GetVersion(option.Value));
            option.RefreshDisplay();
            OnPropertyChanged(nameof(CliIdentitySummary));
            OnPropertyChanged(nameof(CliIdentityFingerprint));
        }
        catch (Exception ex)
        {
            LogSettingsException("客户端指纹刷新失败", ex);
            option.Status = L.Text("SettingsCliIdentityRefreshFailed");
        }
        finally { option.IsRefreshing = false; }
    }

    [RelayCommand]
    private Task RefreshModelsAsync() => RequestModelCatalog(force: true, automatic: false);

    private ModelEndpointConfig DiagnosticConfig() => new()
    {
        ApiKey = ApiKey ?? string.Empty,
        ProxyPassword = ProxyPassword,
        Model = string.Empty,
    };

    private void LogSettingsException(string operation, Exception exception, ModelEndpointConfig? endpoint = null) =>
        _logger.LogError("{Operation}：{ExceptionType} {Detail}", operation, exception.GetType().Name,
            ModelCatalogService.Sanitize(endpoint ?? DiagnosticConfig(), exception.ToString()));

    [RelayCommand]
    public void Save()
    {
        var settings = _settingsService.LoadSettings();

        if (!string.IsNullOrEmpty(ApiKey))
        {
            settings.EncryptedApiKey = _settingsService.EncryptApiKey(ApiKey);
        }

        settings.IsApiKeyValid = IsApiKeyValid;
        settings.DeletionMode = SelectedDeletionMode;
        settings.AnthropicBaseUrl = string.IsNullOrWhiteSpace(AnthropicBaseUrl)
            ? null
            : AnthropicBaseUrl.Trim();
        settings.ModelsUrl = string.IsNullOrWhiteSpace(ModelsUrl) ? null : ModelsUrl.Trim();
        settings.ChatModelName = string.IsNullOrWhiteSpace(ChatModelName)
            ? null
            : ChatModelName.Trim();
        settings.ThinkingLevel = LlmContracts.FormatThinkingLevel(SelectedThinkingLevel);
        settings.ApiFormat = LlmContracts.FormatApiFormat(SelectedApiFormat);
        settings.CliIdentity = LlmContracts.FormatCliIdentity(SelectedCliIdentity);
        settings.ProxyMode = LlmContracts.FormatProxyKind(SelectedProxyKind);
        settings.ProxyHost = string.IsNullOrWhiteSpace(ProxyHost) ? null : ProxyHost.Trim();
        settings.ProxyPort = int.TryParse(ProxyPortText?.Trim(), out var proxyPort) && proxyPort > 0 && proxyPort < 65536 ? proxyPort : null;
        settings.ProxyUsername = string.IsNullOrWhiteSpace(ProxyUsername) ? null : ProxyUsername.Trim();
        if (!string.IsNullOrEmpty(ProxyPassword))
        {
            settings.EncryptedProxyPassword = _settingsService.EncryptProxyPassword(ProxyPassword);
        }
        settings.Language = string.IsNullOrWhiteSpace(Language) ? "zh-CN" : Language.Trim();
        L.SetLanguage(settings.Language);

        settings.ThemeProfile = _themeManager.CurrentProfile.Clone();

        _settingsService.SaveSettings(settings);
        SettingsChanged?.Invoke();
    }


    [RelayCommand]
    private void AddScanWhitelistPath()
    {
        AddWhitelistPath(ScanWhitelist, NewScanWhitelistPath);
        NewScanWhitelistPath = null;
    }

    [RelayCommand]
    private void AddCleanupWhitelistPath()
    {
        AddWhitelistPath(CleanupRecommendationWhitelist, NewCleanupWhitelistPath);
        NewCleanupWhitelistPath = null;
    }

    [RelayCommand]
    private void AddAiWhitelistPath()
    {
        AddWhitelistPath(AiConversationWhitelist, NewAiWhitelistPath);
        NewAiWhitelistPath = null;
    }

    [RelayCommand]
    private void BrowseScanWhitelistPath()
    {
        BrowseWhitelistPath(ScanWhitelist);
    }

    [RelayCommand]
    private void BrowseCleanupWhitelistPath()
    {
        BrowseWhitelistPath(CleanupRecommendationWhitelist);
    }

    [RelayCommand]
    private void BrowseAiWhitelistPath()
    {
        BrowseWhitelistPath(AiConversationWhitelist);
    }

    [RelayCommand]
    private void RemoveWhitelistEntry(PathWhitelistEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        if (ScanWhitelist.Remove(entry) || CleanupRecommendationWhitelist.Remove(entry) || AiConversationWhitelist.Remove(entry))
        {
            PersistWhitelists();
        }
    }

    [RelayCommand]
    private void CopyWhitelist(string? kind)
    {
        var source = GetWhitelist(kind);
        if (source is null)
        {
            return;
        }

        var json = JsonSerializer.Serialize(source.Select(CloneWhitelistEntry).ToList(), new JsonSerializerOptions { WriteIndented = true });
        Clipboard.SetText(json);
        WhitelistErrorMessage = null;
    }

    [RelayCommand]
    private void PasteWhitelist(string? kind)
    {
        var target = GetWhitelist(kind);
        if (target is null)
        {
            return;
        }

        try
        {
            var text = Clipboard.GetText();
            var incoming = JsonSerializer.Deserialize<List<PathWhitelistEntry>>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (incoming is null)
            {
                throw new JsonException("empty whitelist json");
            }

            ReplaceCollection(target, _whitelistMatcher.MergeEntries(target, incoming));
            WhitelistErrorMessage = null;
            PersistWhitelists();
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            LogSettingsException("导入白名单失败", ex);
            WhitelistErrorMessage = L.Text("SettingsWhitelistPasteInvalidJson");
        }
    }

    [RelayCommand]
    private void WhitelistEntryChanged()
    {
        PersistWhitelists();
    }

    private void AddWhitelistPath(ObservableCollection<PathWhitelistEntry> target, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        ReplaceCollection(target, _whitelistMatcher.MergeEntries(target, [new PathWhitelistEntry { Path = path.Trim() }]));
        WhitelistErrorMessage = null;
        PersistWhitelists();
    }

    private void BrowseWhitelistPath(ObservableCollection<PathWhitelistEntry> target)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog();
        if (dialog.ShowDialog() == true)
        {
            AddWhitelistPath(target, dialog.FolderName);
        }
    }

    private ObservableCollection<PathWhitelistEntry>? GetWhitelist(string? kind)
    {
        return kind switch
        {
            "scan" => ScanWhitelist,
            "cleanup" => CleanupRecommendationWhitelist,
            "ai" => AiConversationWhitelist,
            _ => null
        };
    }

    private void PersistWhitelists()
    {
        PersistAppSettings(settings =>
        {
            settings.ScanWhitelist = ScanWhitelist.Select(CloneWhitelistEntry).ToList();
            settings.CleanupRecommendationWhitelist = CleanupRecommendationWhitelist.Select(CloneWhitelistEntry).ToList();
            settings.AiConversationWhitelist = AiConversationWhitelist.Select(CloneWhitelistEntry).ToList();
        });
    }

    private static PathWhitelistEntry CloneWhitelistEntry(PathWhitelistEntry entry)
    {
        return new PathWhitelistEntry
        {
            Path = entry.Path.Trim(),
            Description = string.IsNullOrWhiteSpace(entry.Description) ? null : entry.Description.Trim()
        };
    }

    private static void ReplaceCollection(ObservableCollection<PathWhitelistEntry> target, IEnumerable<PathWhitelistEntry>? entries)
    {
        target.Clear();
        if (entries is null)
        {
            return;
        }

        foreach (var entry in entries.Where(entry => !string.IsNullOrWhiteSpace(entry.Path)))
        {
            target.Add(new PathWhitelistEntry
            {
                Path = entry.Path.Trim(),
                Description = string.IsNullOrWhiteSpace(entry.Description) ? null : entry.Description.Trim()
            });
        }
    }

    private void SyncThemeToManager()
    {
        var bgHex = ThemeManager.ToHex(ThemeBackgroundColor);
        var bgAlt = ComputeBgAlt(ThemeBackgroundColor);
        var textColors = ComputeTextColors(ThemeBackgroundColor);
        var accent = ComputeHarmoniousAccent(ThemeBackgroundColor);
        var accentHover = Lighten(accent, 20);

        var profile = new ThemeProfile
        {
            Name = "Custom",
            IsBuiltIn = false,
            AccentColor = ThemeManager.ToHex(accent),
            AccentHoverColor = ThemeManager.ToHex(accentHover),
            BackgroundColor = bgHex,
            BackgroundAltColor = ThemeManager.ToHex(bgAlt),
            TextPrimaryColor = textColors.primary,
            TextSecondaryColor = textColors.secondary,
            TextTertiaryColor = textColors.tertiary,
            DangerColor = "#FFFF453A",
            DangerPressedColor = "#FFCC1A1A",
            SuccessColor = "#FF30D158",
            WarningColor = "#FFFFD60A",
            GlassEnabled = GlassEnabled,
            GlassOpacity = GlassOpacityPercent / 100,
            AutoTextContrast = AutoTextContrast,
            SurfaceAlpha = _themeManager.CurrentProfile.SurfaceAlpha,
            SurfaceHoverAlpha = _themeManager.CurrentProfile.SurfaceHoverAlpha,
            SurfaceActiveAlpha = _themeManager.CurrentProfile.SurfaceActiveAlpha,
            SettingsCardAlpha = _themeManager.CurrentProfile.SettingsCardAlpha,
            CardAlpha = _themeManager.CurrentProfile.CardAlpha,
            BorderLightAlpha = _themeManager.CurrentProfile.BorderLightAlpha,
            BorderSubtleAlpha = _themeManager.CurrentProfile.BorderSubtleAlpha,
            OverlayAlpha = _themeManager.CurrentProfile.OverlayAlpha,
        };
        _themeManager.ApplyTheme(profile);
    }

    private static Color ComputeHarmoniousAccent(Color bg)
    {
        ColorToHSL(bg, out _, out var s, out var l);
        // Pick a vivid accent with good contrast against the background
        double accentL = l > 0.5 ? 0.45 : 0.55;
        double accentS = Math.Max(s, 0.5);
        // Shift hue by ~200 degrees for a complementary accent
        ColorToHSL(bg, out var h, out _, out _);
        double accentH = (h + 200) % 360;
        return HSLToColor(accentH, accentS, accentL);
    }

    private static Color ComputeBgAlt(Color bg)
    {
        ColorToHSL(bg, out var h, out var s, out var l);
        // Slightly lighter or darker than background
        double altL = l > 0.5 ? Math.Min(1, l + 0.05) : Math.Max(0, l + 0.06);
        return HSLToColor(h, s, altL);
    }

    private static (string primary, string secondary, string tertiary) ComputeTextColors(Color bg)
    {
        double luminance = (0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B) / 255.0;
        if (luminance > 0.5)
        {
            return ("#FF1C1C1E", "#FF6E6E73", "#FF8E8E93");
        }
        else
        {
            return ("#FFFFFFFF", "#FFC7C7CC", "#FFAEAEB2");
        }
    }

    private static Color Lighten(Color c, int amount)
    {
        return Color.FromArgb(c.A,
            (byte)Math.Min(255, c.R + amount),
            (byte)Math.Min(255, c.G + amount),
            (byte)Math.Min(255, c.B + amount));
    }

    private static void ColorToHSL(Color c, out double h, out double s, out double l)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        h = 0;
        if (delta > 0)
        {
            if (max == r) h = 60 * (((g - b) / delta) % 6);
            else if (max == g) h = 60 * ((b - r) / delta + 2);
            else h = 60 * ((r - g) / delta + 4);
        }
        if (h < 0) h += 360;

        l = (max + min) / 2;
        s = delta > 0 ? delta / (1 - Math.Abs(2 * l - 1)) : 0;
    }

    private static Color HSLToColor(double h, double s, double l)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
        double m = l - c / 2;

        double r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return Color.FromArgb(255,
            (byte)Math.Clamp((r + m) * 255, 0, 255),
            (byte)Math.Clamp((g + m) * 255, 0, 255),
            (byte)Math.Clamp((b + m) * 255, 0, 255));
    }

    private void SyncAndSaveTheme()
    {
        SyncThemeToManager();
        _themeManager.Persist();
        SettingsChanged?.Invoke();
    }

    public void HideSaveToast()
    {
        IsSaveToastVisible = false;
    }

    public void LoadSettings()
    {
        InvalidateModelDiscovery();
        var settings = _settingsService.LoadSettings();

        _isLoadingSettings = true;
        try
        {
            if (settings.EncryptedApiKey is not null)
            {
                ApiKey = _settingsService.GetApiKey(settings);
            }

            AnthropicBaseUrl = settings.AnthropicBaseUrl;
            ModelsUrl = settings.ModelsUrl;
            CliIdentityService.RestoreVersions(settings.CliIdentityVersions);
            foreach (var option in CliIdentityOptions) option.RefreshDisplay();
            ChatModelName = settings.ChatModelName;
            SelectedThinkingLevel = ModelEndpointConfig.ResolveThinkingLevel(settings);
            SelectedApiFormat = LlmContracts.ParseApiFormat(settings.ApiFormat);
            SelectedCliIdentity = LlmContracts.ParseCliIdentity(settings.CliIdentity);
            SelectedProxyKind = LlmContracts.ParseProxyKind(settings.ProxyMode);
            ProxyHost = settings.ProxyHost;
            ProxyPortText = settings.ProxyPort?.ToString() ?? "7890";
            ProxyUsername = settings.ProxyUsername;
            ProxyPassword = null;
            _proxyPasswordEdited = false;
            ModelCandidates.Clear();
            foreach (var id in settings.DiscoveredModels.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase))
                ModelCandidates.Add(id);
            ModelRefreshMessage = null;
            OnPropertyChanged(nameof(IsCustomProxyConfigurable));
            Language = string.IsNullOrWhiteSpace(settings.Language) ? "zh-CN" : settings.Language;
            SelectedDeletionMode = settings.DeletionMode;
            IsApiKeyValid = settings.IsApiKeyValid;
            ReplaceCollection(ScanWhitelist, settings.ScanWhitelist);
            ReplaceCollection(CleanupRecommendationWhitelist, settings.CleanupRecommendationWhitelist);
            ReplaceCollection(AiConversationWhitelist, settings.AiConversationWhitelist);
            WhitelistErrorMessage = null;

            ValidationState = IsApiKeyValid ? ValidationState.Valid : ValidationState.None;

            LoadThemeFromCurrent();
        }
        finally
        {
            _isLoadingSettings = false;
        }
    }
}

public sealed record LanguageOption(string Code, string DisplayName);


