using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using NSubstitute;
using SpaceMonger.App.ViewModels;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Assistant;
using SpaceMonger.Core.Services.Copilot;
using SpaceMonger.Core.Services.Llm;
using SpaceMonger.Core.Services.Settings;
using TinyHarness;

namespace SpaceMonger.App.Tests;

public sealed class WebSearchNoticeTests
{
    [Fact] public async Task 模型不解释能力错误时界面仍明确告知用户()
    {
        var (vm,model)=await Run(LlmApiFormat.OpenAiResponses,HttpStatusCode.BadRequest,"""{"error":{"message":"web_search is not supported by this model"}}""");
        Assert.Contains("当前配置的 AI 模型不支持 Web 搜索",vm.ErrorMessage);Assert.Contains("参考链接",vm.ErrorMessage);
        Assert.False(vm.IsOperationRunning);Assert.Contains("unsupported",model.ToolReply);Assert.Equal(2,model.Requests);
    }
    [Fact] public async Task 鉴权错误不会告知模型不支持搜索()
    {
        var (vm,_)=await Run(LlmApiFormat.OpenAiResponses,HttpStatusCode.Unauthorized,"""{"error":{"message":"invalid API key test-key"}}""");
        Assert.Contains("HTTP 401",vm.ErrorMessage);Assert.DoesNotContain("不支持",vm.ErrorMessage);Assert.DoesNotContain("test-key",vm.ErrorMessage);
    }
    [Fact] public async Task 无联网证据明确提示无法确认而不是假装已搜索()
    {
        var (vm,_)=await Run(LlmApiFormat.OpenAiChatCompletions,HttpStatusCode.OK,"""{"choices":[{"message":{"content":"凭已有知识回答"}}]}""");
        Assert.Contains("无法确认",vm.ErrorMessage);Assert.DoesNotContain("不支持",vm.ErrorMessage);
    }
    [Fact] public async Task 成功搜索的真实链接进入模型工具上下文()
    {
        var (vm,model)=await Run(LlmApiFormat.OpenAiResponses,HttpStatusCode.OK,"""{"output":[{"type":"web_search_call","status":"completed","action":{"sources":[{"url":"https://docs.example/rule","title":"参考规则"}]}},{"type":"message","content":[{"type":"output_text","text":"已查证"}]}]}""");
        Assert.Null(vm.ErrorMessage);Assert.Contains("https://docs.example/rule",model.ToolReply);Assert.Contains("请继续提供资料",vm.Messages.Last().Text);
    }
    private static async Task<(ChatViewModel,Client)> Run(LlmApiFormat format,HttpStatusCode status,string response)
    {
        using var clients=new ModelHttpClientFactory(()=>new Handler(status,response));
        var saved=new AppSettings {ChatModelName="selected-model",DiscoveredModels=["selected-model"],ApiFormat=LlmContracts.FormatApiFormat(format),AnthropicBaseUrl="https://relay.example/v1"};
        var settings=Substitute.For<ISettingsService>();settings.LoadSettings().Returns(saved);settings.GetApiKey(saved).Returns("test-key");
        var router=Substitute.For<IAiSkillRouter>();router.GetSkillCatalog().Returns(Array.Empty<AiSkillCatalogItem>());router.Route(Arg.Any<string>(),Arg.Any<FileEntry?>(),Arg.Any<FileEntry?>(),Arg.Any<bool>(),Arg.Any<string?>()).Returns(new AiSkillRoutingResult([]));
        var client=new Client();var service=new DiskAssistantService(_=>client,[],new AssistantWorkspace(),settings,webSearch:new NativeWebSearchService(clients));
        var vm=new ChatViewModel(service,settings,router){InputText="请参考 https://docs.example/rule 新增扫描规则"};
        await vm.SubmitOrStopCommand.ExecuteAsync(null);return(vm,client);
    }
    private sealed class Client:IModelClient
    {
        public int Requests;public string? ToolReply;
        public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request,[EnumeratorCancellation]CancellationToken cancellationToken)
        {
            Assert.Contains(request.Tools,tool=>tool.Name=="web_search");Requests++;
            if(Requests==1){yield return new ModelToolCallEvent(new ToolCall("web-1","web_search","{\"query\":\"https://docs.example/rule 缓存规则\"}"));yield return new ModelCompletedEvent("tool_use");}
            else{ToolReply=request.Messages.Last(message=>message.Role==ChatRole.Tool).Content;yield return new TextDeltaEvent("请继续提供资料。");yield return new ModelCompletedEvent("stop");}
            await Task.Yield();
        }
    }
    private sealed class Handler(HttpStatusCode status,string response):HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var body=JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!;Assert.Equal("selected-model",body["model"]!.GetValue<string>());
            return new(status){Content=new StringContent(response,Encoding.UTF8,"application/json")};
        }
    }
}
