using SpaceMonger.Core.Services.Assistant;
using TinyHarness;

namespace SpaceMonger.Core.Services.Llm;

/// <summary>按端点配置创建对应协议的模型客户端；首次创建时后台刷新 CLI 指纹版本号。</summary>
public sealed class ModelClientFactory(ModelHttpClientFactory httpClients)
{
    private volatile bool identityRefreshTriggered;

    public IModelClient Create(ModelEndpointConfig config)
    {
        TriggerIdentityRefresh(config.CliIdentity);
        var httpClient = httpClients.Create(config);
        return config.ApiFormat switch
        {
            LlmApiFormat.OpenAiResponses => new OpenAiModelClient(httpClient, config.ApiKey, config.BaseUrl, config.Model, LlmApiFormat.OpenAiResponses, config.ThinkingLevel, config.CliIdentity),
            LlmApiFormat.OpenAiChatCompletions => new OpenAiModelClient(httpClient, config.ApiKey, config.BaseUrl, config.Model, LlmApiFormat.OpenAiChatCompletions, config.ThinkingLevel, config.CliIdentity),
            _ => new AnthropicModelClient(httpClient, config.ApiKey, config.BaseUrl, config.Model, config.ThinkingLevel, config.CliIdentity),
        };
    }

    private void TriggerIdentityRefresh(CliIdentityKind identity)
    {
        if (identity == CliIdentityKind.None || identityRefreshTriggered) return;
        identityRefreshTriggered = true;
        _ = Task.Run(CliIdentityService.RefreshVersionsAsync);
    }
}
