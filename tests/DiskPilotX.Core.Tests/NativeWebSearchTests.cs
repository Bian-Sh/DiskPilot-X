using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using DiskPilotX.Core.Services.Llm;

namespace DiskPilotX.Core.Tests;

public sealed class NativeWebSearchTests
{
    [Fact] public async Task Responses使用配置模型原生工具并保留来源()
    {
        using var clients=new ModelHttpClientFactory(()=>new Handler(async request=>
        {
            Assert.Equal("https://relay.example/v1/responses",request.RequestUri!.AbsoluteUri);
            Assert.Equal("test-key",request.Headers.Authorization!.Parameter);
            Assert.Contains("codex_cli_rs",request.Headers.UserAgent.ToString());
            var body=JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
            Assert.Equal("selected-model",body["model"]!.GetValue<string>());
            Assert.Equal("web_search",body["tools"]![0]!["type"]!.GetValue<string>());
            Assert.Equal("web_search_call.action.sources",body["include"]![0]!.GetValue<string>());
            return Json("""{"status":"completed","output":[{"type":"web_search_call","status":"completed","action":{"sources":[{"url":"https://docs.example/rule","title":"规则说明"}]}},{"type":"message","content":[{"type":"output_text","text":"已查证规则"}]}]}""");
        }));
        var result=await new NativeWebSearchService(clients).SearchAsync(Config(LlmApiFormat.OpenAiResponses),"查询规则",default);
        Assert.True(result["ok"]!.GetValue<bool>());Assert.Equal("https://docs.example/rule",result["sources"]![0]!["url"]!.GetValue<string>());Assert.Equal("已查证规则",result["answer"]!.GetValue<string>());
    }
    [Fact] public async Task Anthropic暂停后原样续接并返回真实来源()
    {
        var calls=0;
        using var clients=new ModelHttpClientFactory(()=>new Handler(async request=>
        {
            var body=JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
            Assert.Equal("https://relay.example/v1/messages",request.RequestUri!.AbsoluteUri);
            Assert.Equal("test-key",Assert.Single(request.Headers.GetValues("x-api-key")));
            Assert.Single(request.Headers.GetValues("anthropic-version"));
            Assert.Equal("web_search_20250305",body["tools"]![0]!["type"]!.GetValue<string>());
            if(++calls==1)return Json("""{"stop_reason":"pause_turn","content":[{"type":"server_tool_use","id":"search-1","name":"web_search","input":{"query":"缓存规则"}}]}""");
            Assert.Equal("assistant",body["messages"]![1]!["role"]!.GetValue<string>());
            Assert.Equal("search-1",body["messages"]![1]!["content"]![0]!["id"]!.GetValue<string>());
            return Json("""{"stop_reason":"end_turn","content":[{"type":"web_search_tool_result","content":[{"type":"web_search_result","url":"https://docs.example/cache","title":"缓存参考","encrypted_content":"opaque"}]},{"type":"text","text":"缓存说明"}]}""");
        }));
        var config=Config(LlmApiFormat.AnthropicMessages) with {CliIdentity=CliIdentityKind.ClaudeCode};
        var result=await new NativeWebSearchService(clients).SearchAsync(config,"缓存规则",default);
        Assert.Equal(2,calls);Assert.True(result["ok"]!.GetValue<bool>());Assert.Equal("https://docs.example/cache",result["sources"]![0]!["url"]!.GetValue<string>());
    }
    [Theory]
    [InlineData(LlmApiFormat.OpenAiResponses,"tools web_search is not supported by this model")]
    [InlineData(LlmApiFormat.OpenAiResponses,"The selected model does not support web search")]
    [InlineData(LlmApiFormat.AnthropicMessages,"web_search_20250305 tool is not enabled")]
    [InlineData(LlmApiFormat.OpenAiChatCompletions,"unknown parameter web_search_options")]
    public async Task 明确拒绝搜索能力时返回可见提示(LlmApiFormat format,string message)
    {
        using var clients=new ModelHttpClientFactory(()=>new Handler(_=>Task.FromResult(Json(new JsonObject{["error"]=new JsonObject{["message"]=message}}.ToJsonString(),HttpStatusCode.BadRequest))));
        var result=await new NativeWebSearchService(clients).SearchAsync(Config(format),"资料",default);
        Assert.False(result["ok"]!.GetValue<bool>());Assert.True(result["unsupported"]!.GetValue<bool>());Assert.Contains("当前配置的 AI 模型不支持 Web 搜索",result["user_notice"]!.GetValue<string>());Assert.Contains("链接",result["user_notice"]!.GetValue<string>());
    }
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task 鉴权限流和网络错误不误报模型不支持(HttpStatusCode status)
    {
        using var clients=new ModelHttpClientFactory(()=>new Handler(_=>Task.FromResult(Json("""{"error":{"message":"bad credential test-key"}}""",status))));
        var result=await new NativeWebSearchService(clients).SearchAsync(Config(LlmApiFormat.OpenAiResponses),"资料",default);
        Assert.False(result["ok"]!.GetValue<bool>());Assert.False(result["unsupported"]!.GetValue<bool>());Assert.DoesNotContain("test-key",result.ToJsonString());
    }
    [Fact] public async Task 普通回答没有联网证据时不冒充搜索成功()
    {
        using var clients=new ModelHttpClientFactory(()=>new Handler(_=>Task.FromResult(Json("""{"choices":[{"message":{"content":"我凭已有知识回答"}}]}"""))));
        var result=await new NativeWebSearchService(clients).SearchAsync(Config(LlmApiFormat.OpenAiChatCompletions),"资料",default);
        Assert.False(result["ok"]!.GetValue<bool>());Assert.False(result["unsupported"]!.GetValue<bool>());Assert.Equal("unverified",result["status"]!.GetValue<string>());
    }
    [Fact] public async Task ChatCompletions仅凭真实引用认定搜索可用()
    {
        using var clients=new ModelHttpClientFactory(()=>new Handler(async request=>
        {
            var body=JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;Assert.NotNull(body["web_search_options"]);
            return Json("""{"choices":[{"message":{"content":"查得规则","annotations":[{"type":"url_citation","url_citation":{"url":"https://docs.example/search","title":"原始资料"}}]}}]}""");
        }));
        var result=await new NativeWebSearchService(clients).SearchAsync(Config(LlmApiFormat.OpenAiChatCompletions),"资料",default);Assert.True(result["ok"]!.GetValue<bool>());Assert.Single(result["sources"]!.AsArray());
    }
    [Fact] public async Task 来源只保留无凭据HTTP链接并去重()
    {
        using var clients=new ModelHttpClientFactory(()=>new Handler(_=>Task.FromResult(Json("""{"output":[{"type":"web_search_call","status":"completed","action":{"sources":[{"url":"javascript:alert(1)"},{"url":"https://user:secret@docs.example"},{"url":"https://docs.example/ok","title":"资料"},{"url":"https://docs.example/ok"}]}}]}"""))));
        var result=await new NativeWebSearchService(clients).SearchAsync(Config(LlmApiFormat.OpenAiResponses),"资料",default);Assert.Equal("https://docs.example/ok",Assert.Single(result["sources"]!.AsArray())!["url"]!.GetValue<string>());
    }
    [Fact] public async Task 用户取消不被降级成能力错误()
    {
        using var clients=new ModelHttpClientFactory(()=>new Handler(_=>throw new InvalidOperationException("不应发出请求")));using var cts=new CancellationTokenSource();cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>new NativeWebSearchService(clients).SearchAsync(Config(LlmApiFormat.OpenAiResponses),"资料",cts.Token));
    }
    [Fact] public async Task 搜索服务端错误不冒充能力不支持()
    {
        using var clients=new ModelHttpClientFactory(()=>new Handler(_=>Task.FromResult(Json("""{"stop_reason":"end_turn","content":[{"type":"web_search_tool_result","content":{"type":"web_search_tool_result_error","error_code":"too_many_requests"}}]}"""))));
        var result=await new NativeWebSearchService(clients).SearchAsync(Config(LlmApiFormat.AnthropicMessages),"资料",default);Assert.False(result["ok"]!.GetValue<bool>());Assert.False(result["unsupported"]!.GetValue<bool>());
    }
    [Fact] public async Task 查询域名被拒绝不误报模型不支持()
    {
        using var clients=new ModelHttpClientFactory(()=>new Handler(_=>Task.FromResult(Json("""{"error":{"message":"web_search: requested domain is not allowed"}}""",HttpStatusCode.BadRequest))));
        var result=await new NativeWebSearchService(clients).SearchAsync(Config(LlmApiFormat.OpenAiResponses),"资料",default);Assert.False(result["unsupported"]!.GetValue<bool>());
    }
    [Fact] public async Task Anthropic引用位置元数据也可保留来源()
    {
        using var clients=new ModelHttpClientFactory(()=>new Handler(_=>Task.FromResult(Json("""{"content":[{"type":"text","text":"资料内容","citations":[{"type":"web_search_result_location","url":"https://docs.example/citation","title":"公开引用"}]}]}"""))));
        var result=await new NativeWebSearchService(clients).SearchAsync(Config(LlmApiFormat.AnthropicMessages),"资料",default);Assert.True(result["ok"]!.GetValue<bool>());Assert.Equal("https://docs.example/citation",result["sources"]![0]!["url"]!.GetValue<string>());
    }
    [Fact] public async Task 暂停续接有上限并如实返回未完成()
    {
        var calls=0;using var clients=new ModelHttpClientFactory(()=>new Handler(_=>{calls++;return Task.FromResult(Json("""{"stop_reason":"pause_turn","content":[{"type":"text","text":"继续检索"}]}"""));}));
        var result=await new NativeWebSearchService(clients).SearchAsync(Config(LlmApiFormat.AnthropicMessages),"资料",default);Assert.Equal(3,calls);Assert.False(result["ok"]!.GetValue<bool>());Assert.False(result["unsupported"]!.GetValue<bool>());
    }
    [Fact] public async Task 超大响应受到限制且不泄漏上游内容()
    {
        using var clients=new ModelHttpClientFactory(()=>new Handler(_=>Task.FromResult(Json(new string('x',2*1024*1024+1)))));
        var result=await new NativeWebSearchService(clients).SearchAsync(Config(LlmApiFormat.OpenAiResponses),"资料",default);Assert.False(result["ok"]!.GetValue<bool>());Assert.False(result["unsupported"]!.GetValue<bool>());Assert.True(result.ToJsonString().Length<1000);
    }
    [Fact] public async Task 暂停前获得的来源在最终回答中仍然保留()
    {
        var calls=0;using var clients=new ModelHttpClientFactory(()=>new Handler(_=>Task.FromResult(Json(++calls==1
            ? """{"stop_reason":"pause_turn","content":[{"type":"web_search_tool_result","content":[{"type":"web_search_result","url":"https://docs.example/first","title":"前一阶段资料"}]}]}"""
            : """{"stop_reason":"end_turn","content":[{"type":"text","text":"根据前一步检索资料说明规则"}]}"""))));
        var result=await new NativeWebSearchService(clients).SearchAsync(Config(LlmApiFormat.AnthropicMessages),"资料",default);
        Assert.True(result["ok"]!.GetValue<bool>());Assert.Equal("https://docs.example/first",result["sources"]![0]!["url"]!.GetValue<string>());
    }
    private static ModelEndpointConfig Config(LlmApiFormat format)=>new(){ApiKey="test-key",BaseUrl="https://relay.example/v1",Model="selected-model",ApiFormat=format,CliIdentity=CliIdentityKind.Codex};
    private static HttpResponseMessage Json(string json,HttpStatusCode status=HttpStatusCode.OK)=>new(status){Content=new StringContent(json,Encoding.UTF8,"application/json")};
    private sealed class Handler(Func<HttpRequestMessage,Task<HttpResponseMessage>> respond):HttpMessageHandler {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>respond(request);}
}
