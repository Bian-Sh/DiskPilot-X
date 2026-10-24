using TinyHarness;
namespace SpaceMonger.Core.Services.Assistant;

/// <summary>设置页连接测试与正式运行复用同一个原生模型适配器。</summary>
public sealed class ModelConnectionTester(IHttpClientFactory clients)
{
    public async Task<bool> ValidateAsync(string apiKey,string? baseUrl,string model,CancellationToken ct)
    {
        var client=new AnthropicModelClient(clients.CreateClient("Anthropic"),apiKey,baseUrl,model);
        var completed=false;
        await foreach(var item in client.StreamAsync(new ModelRequest([ChatMessage.System("仅回复 OK，用于连接测试。"),ChatMessage.User("你好")],[]),ct))
            if(item is ModelCompletedEvent)completed=true;
        return completed;
    }
}
