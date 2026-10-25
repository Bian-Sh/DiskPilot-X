using SpaceMonger.Core.Services.Llm;
using TinyHarness;

namespace SpaceMonger.Core.Services.Assistant;

/// <summary>设置页连接测试与正式运行复用同一个模型客户端工厂。</summary>
public sealed class ModelConnectionTester(ModelClientFactory clients)
{
    public async Task<bool> ValidateAsync(ModelEndpointConfig endpoint, CancellationToken ct)
    {
        var client = clients.Create(endpoint);
        var completed = false;
        await foreach (var item in client.StreamAsync(new ModelRequest([ChatMessage.System("仅回复 OK，用于连接测试。"), ChatMessage.User("你好")], []), ct))
            if (item is ModelCompletedEvent) completed = true;
        return completed;
    }
}
