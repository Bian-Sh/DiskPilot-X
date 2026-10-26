using System.Net;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Llm;

namespace DiskPilotX.Core.Tests;

public sealed class ModelCatalogEndpointTests
{
    [Fact] public void 自定义列表地址独立于对话地址并传递凭据()
    {
        var settings = new AppSettings { AnthropicBaseUrl="https://relay.example/v1", ModelsUrl="https://catalog.example/custom?limit=20", ApiFormat="openai-chat-completions" };
        var config=ModelEndpointConfig.FromSettings(settings,"测试密钥","model-a");
        Assert.Equal(settings.ModelsUrl,ModelCatalogService.GetModelsUri(config).AbsoluteUri);
        Assert.Equal(settings.AnthropicBaseUrl,config.BaseUrl);
    }
    [Theory]
    [InlineData("anthropic-messages","https://relay.example/v1/models?limit=1000")]
    [InlineData("openai-responses","https://relay.example/v1/models")]
    public void 空地址保留协议默认路径(string format,string expected) { var config=ModelEndpointConfig.FromSettings(new(){AnthropicBaseUrl="https://relay.example",ApiFormat=format},"key","model"); Assert.Equal(expected,ModelCatalogService.GetModelsUri(config).AbsoluteUri); }
    [Theory]
    [InlineData("file:///C:/models.json")]
    [InlineData("/models")]
    [InlineData("ftp://catalog.example/models")]
    public void 拒绝非HTTP完整地址(string url) { var config=ModelEndpointConfig.FromSettings(new(){ModelsUrl=url},"key","model"); Assert.Throws<InvalidOperationException>(()=>ModelCatalogService.GetModelsUri(config)); }
    [Fact] public async Task 自定义地址的实际请求沿用密钥和身份头()
    {
        using var clients=new ModelHttpClientFactory(()=>new Handler(request=> { Assert.Equal("https://catalog.example/list",request.RequestUri!.AbsoluteUri); Assert.Equal("key",request.Headers.Authorization!.Parameter); Assert.True(request.Headers.Contains("originator")); return new(HttpStatusCode.OK){Content=new StringContent("{\"data\":[{\"id\":\"model-a\"}]}")}; }));
        var catalog=new ModelCatalogService(clients); var config=ModelEndpointConfig.FromSettings(new(){ModelsUrl="https://catalog.example/list",ApiFormat="openai-responses",CliIdentity="codex"},"key","model");
        Assert.Equal("model-a",Assert.Single(await catalog.FetchAsync(config,CancellationToken.None)).Id);
    }
    [Fact] public async Task 指纹可逐项重复刷新且失败保留旧版本()
    {
        var calls=0; using var http=new HttpClient(new Handler(_=> { calls++; return new(HttpStatusCode.OK){Content=new StringContent("{\"tag_name\":\"rust-v99.1."+(calls==1?"0":"1")+"\"}")}; }));
        Assert.True(await CliIdentityService.RefreshVersionAsync(CliIdentityKind.Codex,http));
        Assert.True(await CliIdentityService.RefreshVersionAsync(CliIdentityKind.Codex,http));
        Assert.Equal(2,calls); Assert.Equal("99.1.1",CliIdentityService.BuildHeaders(CliIdentityKind.Codex)["version"]);
        using var failed=new HttpClient(new Handler(_=>new(HttpStatusCode.BadGateway)));
        Assert.False(await CliIdentityService.RefreshVersionAsync(CliIdentityKind.Codex,failed)); Assert.Equal("99.1.1",CliIdentityService.GetVersion(CliIdentityKind.Codex));
    }
    [Fact] public async Task Grok无远程版本源时不伪装刷新成功() { using var http=new HttpClient(new Handler(_=>throw new InvalidOperationException("不应发送网络请求"))); Assert.False(await CliIdentityService.RefreshVersionAsync(CliIdentityKind.Grok,http)); }
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> respond):HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>Task.FromResult(respond(request)); }
}
