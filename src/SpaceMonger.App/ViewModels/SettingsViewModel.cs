using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpaceMonger.App.Localization;
using SpaceMonger.App.Services;
using SpaceMonger.Core.Enums;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Models.Theme;
using SpaceMonger.Core.Services.Assistant;
using SpaceMonger.Core.Services.Llm;
using SpaceMonger.Core.Services.Settings;
using SpaceMonger.Core.Services.Whitelist;
using TinyHarness;

namespace SpaceMonger.App.ViewModels;

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
    private readonly IPathWhitelistMatcher _whitelistMatcher;
    private bool _isLoadingSettings;
    private bool _suppressPersistence;

    public event Action? SettingsChanged;

    [ObservableProperty]
    private string? _apiKey;

    partial void OnApiKeyChanged(string? value)
    {
        if (_isLoadingSettings)
            return;

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
        PersistAppSettings(settings =>
            settings.AnthropicBaseUrl = string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }

    [ObservableProperty]
    private string? _analysisModelName;

    partial void OnAnalysisModelNameChanged(string? value)
    {
        PersistAppSettings(settings =>
            settings.AnalysisModelName = string.IsNullOrWhiteSpace(value) ? null : value.Trim());
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
        PersistAppSettings(settings => settings.ApiFormat = LlmContracts.FormatApiFormat(value));
    }

    [ObservableProperty]
    private CliIdentityKind _selectedCliIdentity = CliIdentityKind.None;

    partial void OnSelectedCliIdentityChanged(CliIdentityKind value)
    {
        PersistAppSettings(settings => settings.CliIdentity = LlmContracts.FormatCliIdentity(value));
    }

    [ObservableProperty]
    private ProxyKind _selectedProxyKind = ProxyKind.System;

    partial void OnSelectedProxyKindChanged(ProxyKind value)
    {
        PersistAppSettings(settings => settings.ProxyMode = LlmContracts.FormatProxyKind(value));
        OnPropertyChanged(nameof(IsCustomProxyConfigurable));
    }

    [ObservableProperty]
    private string? _proxyHost;

    partial void OnProxyHostChanged(string? value)
    {
        PersistAppSettings(settings => settings.ProxyHost = string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }

    [ObservableProperty]
    private string? _proxyPortText = "7890";

    partial void OnProxyPortTextChanged(string? value)
    {
        PersistAppSettings(settings => settings.ProxyPort = int.TryParse(value?.Trim(), out var port) && port > 0 && port < 65536 ? port : null);
    }

    [ObservableProperty]
    private string? _proxyUsername;

    partial void OnProxyUsernameChanged(string? value)
    {
        PersistAppSettings(settings => settings.ProxyUsername = string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }

    [ObservableProperty]
    private string? _proxyPassword;

    partial void OnProxyPasswordChanged(string? value)
    {
        PersistAppSettings(settings => settings.EncryptedProxyPassword = string.IsNullOrEmpty(value) ? null : _settingsService.EncryptProxyPassword(value));
    }

    public bool IsCustomProxyConfigurable => SelectedProxyKind == ProxyKind.Custom;

    public ObservableCollection<string> ModelCandidates { get; } = [];

    [ObservableProperty]
    private bool _isRefreshingModels;

    [ObservableProperty]
    private string? _modelRefreshMessage;

    public IReadOnlyList<ThinkingLevelOption> ThinkingLevelOptions => AiOptions.ThinkingLevels();
    public IReadOnlyList<ApiFormatOption> ApiFormatOptions => AiOptions.ApiFormats();
    public IReadOnlyList<CliIdentityOption> CliIdentityOptions => AiOptions.CliIdentities();
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

    partial void OnGlassEnabledChanged(bool value)
    {
        if (!_isLoadingSettings) SyncAndSaveTheme();
    }

    [ObservableProperty]
    private int _glassBackdropType;

    partial void OnGlassBackdropTypeChanged(int value)
    {
        if (!_isLoadingSettings) SyncAndSaveTheme();
    }

    [ObservableProperty]
    private double _blurRadius;

    partial void OnBlurRadiusChanged(double value)
    {
        if (!_isLoadingSettings) SyncAndSaveTheme();
    }

    [ObservableProperty]
    private double _glassOpacity = 0.85;

    partial void OnGlassOpacityChanged(double value)
    {
        if (!_isLoadingSettings) SyncAndSaveTheme();
    }

    [ObservableProperty]
    private bool _autoTextContrast;

    partial void OnAutoTextContrastChanged(bool value)
    {
        if (!_isLoadingSettings) SyncAndSaveTheme();
    }

    public static IReadOnlyList<ThemeProfile> AvailableThemePresets { get; } = ThemeProfile.BuiltInPresets;

    public SettingsViewModel(ISettingsService settingsService, ModelConnectionTester llmClient, ThemeManager themeManager,
        ModelCatalogService? modelCatalog = null, IPathWhitelistMatcher? whitelistMatcher = null)
    {
        _settingsService = settingsService;
        _llmClient = llmClient;
        _themeManager = themeManager;
        _modelCatalog = modelCatalog;
        _whitelistMatcher = whitelistMatcher ?? new PathWhitelistMatcher();
        L.LanguageChanged += RefreshValidationStatusText;
        L.LanguageChanged += RefreshOptionLabels;

        LoadSettings();
    }

    private void RefreshOptionLabels()
    {
        OnPropertyChanged(nameof(ThinkingLevelOptions));
        OnPropertyChanged(nameof(ApiFormatOptions));
        OnPropertyChanged(nameof(CliIdentityOptions));
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

    private void PersistAppSettings(Action<SpaceMonger.Core.Models.AppSettings> update)
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
        new("en", "English"),
        new("auto", "Auto")
    ];

    // ── Theme Actions ─────────────────────────────────────────────

    [RelayCommand]
    private void ApplyThemePreset(ThemeProfile? preset)
    {
        if (preset == null) return;

        _themeManager.ApplyTheme(preset);
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
        GlassBackdropType = p.GlassBackdropType;
        BlurRadius = p.BlurRadius;
        GlassOpacity = p.GlassOpacity;
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

        try
        {
            var endpoint = BuildEndpoint(analysis: false);
            var isValid = await _llmClient.ValidateAsync(endpoint, CancellationToken.None);

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
            ValidationState = ValidationState.Invalid;
            IsApiKeyValid = false;
            ValidationMessage = L.Format("ValidationFailedMessage", "模型连接验证失败，请检查地址、模型与凭据。");
        }
    }

    /// <summary>用当前编辑中的字段构建端点配置；代理口令优先取输入框，未改动时解密存量值。</summary>
    private ModelEndpointConfig BuildEndpoint(bool analysis)
    {
        var settings = _settingsService.LoadSettings();
        var proxyPassword = string.IsNullOrEmpty(ProxyPassword) ? _settingsService.GetProxyPassword(settings) : ProxyPassword;
        return ModelEndpointConfig.FromSettings(settings, ApiKey ?? string.Empty,
            ModelEndpointResolver.ResolveModel(analysis ? AnalysisModelName : ChatModelName, AnthropicBaseUrl, analysis),
            proxyPassword) with
        {
            ApiFormat = SelectedApiFormat,
            CliIdentity = SelectedCliIdentity,
            ThinkingLevel = SelectedThinkingLevel,
            ProxyMode = SelectedProxyKind,
        };
    }

    [RelayCommand]
    private async Task RefreshModelsAsync()
    {
        if (_modelCatalog is null || IsRefreshingModels) return;
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            ModelRefreshMessage = L.Text("ApiKeyEmptyMessage");
            return;
        }

        IsRefreshingModels = true;
        ModelRefreshMessage = L.Text("SettingsModelRefreshRunning");
        try
        {
            var endpoint = BuildEndpoint(analysis: false);
            var models = await Task.Run(() => _modelCatalog.FetchAsync(endpoint, CancellationToken.None));
            var ids = models.Select(model => model.Id).ToList();
            PersistAppSettings(settings => settings.DiscoveredModels = ids);
            ModelCandidates.Clear();
            foreach (var id in ids) ModelCandidates.Add(id);
            ModelRefreshMessage = L.Format("SettingsModelRefreshSuccessFormat", $"{ids.Count}");
        }
        catch (Exception ex) when (ex is ModelClientException or InvalidOperationException or HttpRequestException)
        {
            ModelRefreshMessage = L.Text("SettingsModelRefreshFailed");
        }
        finally
        {
            IsRefreshingModels = false;
        }
    }

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
        settings.AnalysisModelName = string.IsNullOrWhiteSpace(AnalysisModelName)
            ? null
            : AnalysisModelName.Trim();
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

        SyncThemeToManager();

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
            GlassBackdropType = GlassBackdropType,
            BlurRadius = BlurRadius,
            GlassOpacity = GlassOpacity,
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
        var settings = _settingsService.LoadSettings();

        _isLoadingSettings = true;
        try
        {
            if (settings.EncryptedApiKey is not null)
            {
                ApiKey = _settingsService.GetApiKey(settings);
            }

            AnthropicBaseUrl = settings.AnthropicBaseUrl;
            AnalysisModelName = settings.AnalysisModelName;
            ChatModelName = settings.ChatModelName;
            SelectedThinkingLevel = ModelEndpointConfig.ResolveThinkingLevel(settings);
            SelectedApiFormat = LlmContracts.ParseApiFormat(settings.ApiFormat);
            SelectedCliIdentity = LlmContracts.ParseCliIdentity(settings.CliIdentity);
            SelectedProxyKind = LlmContracts.ParseProxyKind(settings.ProxyMode);
            ProxyHost = settings.ProxyHost;
            ProxyPortText = settings.ProxyPort?.ToString() ?? "7890";
            ProxyUsername = settings.ProxyUsername;
            ProxyPassword = null;
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


