using System.Diagnostics;
using DiskPilotX.App.Localization;
using DiskPilotX.Core.Services.Llm;

namespace DiskPilotX.App.ViewModels;

public partial class SettingsViewModel
{
    private CancellationTokenSource? _catalogCancellation;
    private Task? _catalogTask;
    private ModelEndpointConfig? _catalogEndpoint;
    private ModelEndpointConfig? _lastCatalogSuccess;
    private ModelEndpointConfig? _lastCatalogFailure;
    private long _catalogVersion;
    private long _catalogFailureTime;

    /// <summary>字符修改只使旧结果失效；请求必须由完成编辑事件发起。</summary>
    private void InvalidateModelDiscovery()
    {
        if (_isLoadingSettings) return;
        _catalogVersion++;
        if (ValidationState == ValidationState.Validating)
        {
            ValidationState = ValidationState.None;
            ValidationMessage = null;
        }
        _catalogCancellation?.Cancel();
        _catalogCancellation = null;
        _catalogTask = null;
        _catalogEndpoint = null;
        IsRefreshingModels = false;
        ModelRefreshMessage = null;
    }

    /// <summary>打开设置时仅为空目录补齐一次，不因每次进入页面重复请求。</summary>
    public Task EnsureModelCatalogAsync() => ModelCandidates.Count == 0
        && !string.IsNullOrWhiteSpace(AnthropicBaseUrl) && !string.IsNullOrWhiteSpace(ApiKey)
        ? CompleteModelConfigurationAsync() : Task.CompletedTask;

    /// <summary>失焦、Enter、离散选择及验证按钮共用入口，合并同配置请求。</summary>
    public Task CompleteModelConfigurationAsync(bool force = false)
    {
        EnsureProxyHost();
        return RequestModelCatalog(force, automatic: true);
    }

    private Task RequestModelCatalog(bool force, bool automatic)
    {
        if (_isLoadingSettings || _modelCatalog is null) return Task.CompletedTask;
        ModelEndpointConfig endpoint;
        try
        {
            if (automatic)
            {
                if (string.IsNullOrWhiteSpace(ApiKey) || string.IsNullOrWhiteSpace(AnthropicBaseUrl))
                {
                    ModelRefreshMessage = L.Text("SettingsModelConfigIncomplete");
                    return Task.CompletedTask;
                }
                if (!IsHttpAddress(AnthropicBaseUrl))
                {
                    ModelRefreshMessage = L.Text("SettingsModelBaseUrlInvalid");
                    return Task.CompletedTask;
                }
                if (!string.IsNullOrWhiteSpace(ModelsUrl) && !IsHttpAddress(ModelsUrl))
                {
                    ModelRefreshMessage = L.Text("SettingsModelUrlInvalid");
                    return Task.CompletedTask;
                }
                if (SelectedProxyKind == ProxyKind.Custom && !IsProxyComplete())
                {
                    ModelRefreshMessage = L.Text("SettingsModelProxyIncomplete");
                    return Task.CompletedTask;
                }
            }
            else if (string.IsNullOrWhiteSpace(ApiKey))
            {
                ModelRefreshMessage = L.Text("ApiKeyEmptyMessage");
                return Task.CompletedTask;
            }
            // 模型选择和思考等级不影响目录，不能据此产生重复请求。
            endpoint = BuildEndpoint() with { Model = string.Empty, ThinkingLevel = ThinkingLevel.Off };
        }
        catch (Exception ex)
        {
            LogSettingsException("模型列表获取失败", ex);
            ModelRefreshMessage = automatic ? L.Text("SettingsModelAutoFetchFailed")
                : L.Format("SettingsModelRefreshFailedDetailFormat", ModelCatalogService.Sanitize(DiagnosticConfig(), ex.Message));
            return Task.CompletedTask;
        }

        if (_catalogTask is { IsCompleted: false } && _catalogCancellation is { IsCancellationRequested: false } && endpoint == _catalogEndpoint)
            return _catalogTask;
        if (!force && endpoint == _lastCatalogSuccess && ModelCandidates.Count > 0)
        {
            ModelRefreshMessage = L.Format("SettingsModelRefreshSuccessFormat", $"{ModelCandidates.Count}");
            return Task.CompletedTask;
        }
        // 失败后 Enter 紧接失焦不再立即重发；稍后重新确认编辑或点击验证可重试。
        if (!force && endpoint == _lastCatalogFailure && Stopwatch.GetElapsedTime(_catalogFailureTime) < TimeSpan.FromMilliseconds(500))
        {
            ModelRefreshMessage = L.Text("SettingsModelAutoFetchFailed");
            return Task.CompletedTask;
        }

        _catalogCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _catalogCancellation = cancellation;
        _catalogEndpoint = endpoint;
        IsRefreshingModels = true;
        ModelRefreshMessage = L.Text("SettingsModelRefreshRunning");
        return _catalogTask = LoadModelCatalogAsync(endpoint, cancellation, _catalogVersion, automatic);
    }

    private async Task LoadModelCatalogAsync(ModelEndpointConfig endpoint, CancellationTokenSource cancellation, long version, bool automatic)
    {
        bool IsCurrent() => !cancellation.IsCancellationRequested && version == _catalogVersion
            && ReferenceEquals(_catalogCancellation, cancellation);
        try
        {
            // 只对提交事件短暂合并，不是对每个输入字符做防抖请求。
            if (automatic) await Task.Delay(180, cancellation.Token);
            var models = await Task.Run(() => _modelCatalog!.FetchAsync(endpoint, cancellation.Token));
            if (!IsCurrent()) return;
            var discovered = models.Select(model => model.Id).ToList();
            var available = new HashSet<string>(discovered, StringComparer.OrdinalIgnoreCase);
            var ids = ModelCandidates.Where(available.Contains).Concat(discovered).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            PersistAppSettings(settings => settings.DiscoveredModels = ids);
            ModelCandidates.Clear();
            foreach (var id in ids) ModelCandidates.Add(id);
            RefreshThinkingModels();
            _lastCatalogSuccess = endpoint;
            _lastCatalogFailure = null;
            ModelRefreshMessage = L.Format("SettingsModelRefreshSuccessFormat", $"{ids.Count}");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!IsCurrent()) return;
            LogSettingsException("模型列表获取失败", ex, endpoint);
            _lastCatalogSuccess = null;
            _lastCatalogFailure = endpoint;
            _catalogFailureTime = Stopwatch.GetTimestamp();
            ModelRefreshMessage = automatic ? L.Text("SettingsModelAutoFetchFailed")
                : L.Format("SettingsModelRefreshFailedDetailFormat", ModelCatalogService.Sanitize(endpoint, ex.Message));
            if (!automatic && ex is ModelCatalogExhaustedException && ShowModelDiscoveryFailureAsync is { } showFailure)
                await showFailure(L.Text("SettingsModelDiscoveryExhaustedMessage"));
        }
        finally
        {
            if (ReferenceEquals(_catalogCancellation, cancellation))
            {
                _catalogCancellation = null;
                IsRefreshingModels = false;
            }
            cancellation.Dispose();
        }
    }

    private static bool IsHttpAddress(string? text) => Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && string.IsNullOrEmpty(uri.UserInfo);

    private bool IsProxyComplete()
    {
        if (string.IsNullOrWhiteSpace(ProxyHost) || !int.TryParse(ProxyPortText, out var port) || port is <= 0 or >= 65536) return false;
        var host = ProxyHost.Trim();
        if (!host.Contains("://", StringComparison.Ordinal)) host = "http://" + host;
        return IsHttpAddress(host) && Uri.TryCreate(host, UriKind.Absolute, out var uri)
            && uri.AbsolutePath == "/" && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
    }
}
