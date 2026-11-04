using System.IO;
using System.Net.Http;
using CommunityToolkit.Mvvm.Input;
using DiskPilotX.App.Localization;
using DiskPilotX.Core.Services.Llm;
using Microsoft.Extensions.Logging;
using TinyHarness;

namespace DiskPilotX.App.ViewModels;

public partial class SettingsViewModel
{
    private long _validationVersion;
    private string? _validationModel;
    private string? _validationFailureKey;
    private int? _validationHttpStatus;
    private string? _validationUpstreamMessage;

    /// <summary>连接选择变化只撤销验证结果，不使模型目录重复获取。</summary>
    private void InvalidateConnectionValidation()
    {
        _validationVersion++;
        _validationModel = null;
        _validationFailureKey = null;
        _validationHttpStatus = null;
        _validationUpstreamMessage = null;
        ValidationState = ValidationState.None;
        IsApiKeyValid = false;
        RefreshValidationStatusText();
    }

    private void RefreshValidationStatusText()
    {
        var failure = _validationFailureKey is null ? L.Text("SettingsConnectionErrorUnknown") : L.Text(_validationFailureKey);
        var status = ValidationState switch
        {
            ValidationState.Validating => _validationModel is null ? L.Text("SettingsConnectionFetchingModels")
                : L.Format("SettingsConnectionTestingModel", _validationModel),
            ValidationState.Valid => _validationModel is null ? L.Text("SettingsConnectionPreviouslyValid")
                : L.Format("SettingsConnectionModelValid", _validationModel),
            ValidationState.Invalid or ValidationState.Failed => _validationModel is null ? failure
                : L.Format("SettingsConnectionModelFailed", _validationModel, failure),
            _ => string.Empty,
        };
        if (ValidationState is ValidationState.Invalid or ValidationState.Failed)
        {
            if (_validationHttpStatus is { } code) status += $" (HTTP {code})";
            if (!string.IsNullOrWhiteSpace(_validationUpstreamMessage)) status += "\n" + _validationUpstreamMessage;
        }
        ValidationStatusText = status;
        ValidationMessage = string.IsNullOrEmpty(status) ? null : status;
    }

    [RelayCommand]
    private async Task ValidateAsync()
    {
        try { EnsureProxyHost(); InvalidateConnectionValidation(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { FailValidationSettings(error); return; }
        var version = _validationVersion;
        var catalogVersion = _catalogVersion;
        if (!TryReadValidationSelection(out var selectedModel)) return;
        bool IsCurrent()
        {
            if (version != _validationVersion || catalogVersion != _catalogVersion) return false;
            // 聊天区直接保存模型选择，不经过设置 VM；两处 await 后都核对真实存储。
            if (!TryReadValidationSelection(out var currentModel)) return false;
            if (string.Equals(selectedModel, currentModel, StringComparison.Ordinal)) return true;
            InvalidateConnectionValidation();
            return false;
        }
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            _validationFailureKey = "ApiKeyEmptyMessage";
            ValidationState = ValidationState.Failed;
            RefreshValidationStatusText();
            return;
        }
        ValidationState = ValidationState.Validating;
        ModelEndpointConfig? endpoint = null;
        try
        {
            // 先等本次目录成功，绝不拿旧缓存、旧模型或隐藏默认模型抢先发生成请求。
            endpoint = BuildEndpoint() with { Model = string.Empty, ThinkingLevel = ThinkingLevel.Off };
            await CompleteModelConfigurationAsync(force: true);
            if (!IsCurrent()) return;
            if (_lastCatalogError is not null) throw _lastCatalogError;
            var catalogEndpoint = BuildEndpoint() with { Model = string.Empty, ThinkingLevel = ThinkingLevel.Off };
            if (_modelCatalog is null || _lastCatalogSuccess != catalogEndpoint || _lastDiscoveredModels.Count == 0)
            {
                _validationFailureKey = "SettingsConnectionNoModels";
                ValidationState = ValidationState.Failed;
                RefreshValidationStatusText();
                return;
            }
            var model = _lastDiscoveredModels.FirstOrDefault(id => string.Equals(id, selectedModel?.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? _lastDiscoveredModels[0];
            endpoint = BuildEndpoint(model);
            _validationModel = ModelCatalogService.Sanitize(endpoint, model);
            RefreshValidationStatusText();
            // 临时测试模型只属于本次请求，不回写聊天选择或思考配置。
            var valid = await _llmClient.ValidateAsync(endpoint, CancellationToken.None);
            if (!IsCurrent()) return;
            _validationFailureKey = valid ? null : "SettingsConnectionErrorIncomplete";
            ValidationState = valid ? ValidationState.Valid : ValidationState.Failed;
            IsApiKeyValid = valid;
            RefreshValidationStatusText();
        }
        catch (Exception error)
        {
            if (!IsCurrent()) return;
            var source = error is ModelCatalogExhaustedException { InnerException: { } inner } ? inner : error;
            var kind = source is ModelClientException modelError ? modelError.Kind
                : source is OperationCanceledException ? ModelErrorKind.Timeout
                : source is HttpRequestException or IOException ? ModelErrorKind.ConnectionFailed : ModelErrorKind.Unknown;
            _validationFailureKey = error is ModelCatalogExhaustedException && kind == ModelErrorKind.Unknown
                ? "SettingsConnectionNoModels" : "SettingsConnectionError" + kind;
            _validationHttpStatus = (source as ModelClientException)?.StatusCode;
            var config = endpoint ?? DiagnosticConfig();
            _validationUpstreamMessage = (source as ModelClientException)?.UpstreamMessage is { } message
                ? ModelCatalogService.Sanitize(config, message) : null;
            ValidationState = kind == ModelErrorKind.Authentication ? ValidationState.Invalid : ValidationState.Failed;
            IsApiKeyValid = false;
            LogSettingsException("模型连接验证失败", error, endpoint);
            RefreshValidationStatusText();
        }
    }

    private bool TryReadValidationSelection(out string? model)
    {
        try { model = _settingsService.LoadSettings().ChatModelName; return true; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            model = null;
            FailValidationSettings(error);
            return false;
        }
    }

    /// <summary>配置读写不可用时只撤销内存结果，不能再次访问存储而掩盖原异常。</summary>
    private void FailValidationSettings(Exception error)
    {
        var suppress = _suppressPersistence;
        _suppressPersistence = true;
        try
        {
            InvalidateConnectionValidation();
            _validationFailureKey = "SettingsConnectionSettingsUnavailable";
            ValidationState = ValidationState.Failed;
            RefreshValidationStatusText();
        }
        finally { _suppressPersistence = suppress; }
        LogSettingsException("读取验证配置失败", error);
    }
    private void LogSettingsException(string operation, Exception exception, ModelEndpointConfig? endpoint = null)
    {
        var config = endpoint ?? DiagnosticConfig();
        string Safe(string? value) => ModelCatalogService.Sanitize(config, value ?? string.Empty);
        var catalogueAddress = exception.Data["ModelCatalogEndpoint"] as string;
        var address = catalogueAddress ?? config.BaseUrl;
        try
        {
            if (catalogueAddress is null && endpoint is not null) address = config.ApiFormat switch
            {
                LlmApiFormat.OpenAiResponses => OpenAiOptions.GetEndpointUri("/responses", config.BaseUrl).AbsoluteUri,
                LlmApiFormat.OpenAiChatCompletions => OpenAiOptions.GetEndpointUri("/chat/completions", config.BaseUrl).AbsoluteUri,
                _ => AnthropicOptions.GetMessagesUri(config.BaseUrl).AbsoluteUri,
            };
        }
        catch (InvalidOperationException) { /* 无效地址仍按脱敏配置记录，不掩盖原始失败。 */ }
        var source = exception is ModelCatalogExhaustedException { InnerException: { } inner } ? inner : exception;
        var error = source as ModelClientException;
        _logger.LogError("{Operation}：{ExceptionType}；端点 {Endpoint}；模型 {Model}；协议 {ApiFormat}；代理 {ProxyMode}；HTTP {StatusCode}；类别 {Kind}；代码 {ErrorCode}；上游 {UpstreamMessage}；详情 {Detail}",
            operation, exception.GetType().Name, Safe(address), Safe(config.Model), config.ApiFormat, config.ProxyMode,
            error?.StatusCode, error?.Kind ?? (source is OperationCanceledException ? ModelErrorKind.Timeout : source is HttpRequestException or IOException ? ModelErrorKind.ConnectionFailed : ModelErrorKind.Unknown), Safe(error?.ErrorCode), Safe(error?.UpstreamMessage), Safe(exception.ToString()));
    }
}
