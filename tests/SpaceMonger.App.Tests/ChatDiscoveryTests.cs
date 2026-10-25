using System.Runtime.CompilerServices;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using SpaceMonger.Core.Services.Llm;
using NSubstitute;
using TinyHarness;
using SpaceMonger.App.ViewModels;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Assistant;
using SpaceMonger.Core.Services.Copilot;
using SpaceMonger.Core.Services.Settings;
namespace SpaceMonger.App.Tests;
[Collection("CLI 指纹状态")]
public sealed class ChatDiscoveryTests
{
    [Fact] public void DiscoveryReflectsLiveCatalogAndExcludesCreator() { var (vm,router)=Create(); Assert.False(vm.HasMessages); Assert.Single(vm.CleanupRules); router.GetSkillCatalog().Returns(new[] {new AiSkillCatalogItem("qq-cache","QQ 缓存","检查日志")}); vm.RefreshSkills(); Assert.Equal("QQ 缓存",Assert.Single(vm.CleanupRules).Title); }
    [Fact] public async Task PromptStartsConversationInOneAction() { var(vm,_)=Create(); await vm.ExplorePromptCommand.ExecuteAsync("查看我已经创建的清理规则"); Assert.True(vm.HasMessages); Assert.Contains(vm.Messages,m=>m.Text=="查看我已经创建的清理规则"); }
    [Fact] public void TaskCardRemainsAfterFollowingMessages() { var(vm,_)=Create(); var card=new SkillTaskCard("task1","qq-cache","QQ 缓存","待预览"); vm.AddSkillTask(card); vm.Messages.Add(new(){Text="继续"}); Assert.Same(card,vm.Messages.First().SkillTasks.Single()); }
    [Fact] public void 空模型缓存不展示推断模型名() { var (vm,_) = Create(new() { AnthropicBaseUrl="https://api.deepseek.com", ChatModelName="旧模型" }); Assert.Empty(vm.ModelCandidates); Assert.Null(vm.SelectedChatModel); Assert.DoesNotContain("deepseek", vm.ModelDisplayName); Assert.DoesNotContain("旧模型", vm.ModelDisplayName); }
    [Fact] public void 只展示候选列表内的已选模型() { var (vm,_) = Create(new() { ChatModelName="model-a", DiscoveredModels=["model-a","model-b"] }); Assert.Equal("model-a",vm.SelectedChatModel); Assert.Equal("model-a",vm.ModelDisplayName); vm.SelectedChatModel="model-b"; Assert.Equal("model-b",vm.ModelDisplayName); }
    [Fact] public void 已选模型从缓存消失后清除展示() { var saved=new AppSettings { ChatModelName="model-a",DiscoveredModels=["model-a"] }; var (vm,_)=Create(saved); saved.DiscoveredModels.Clear(); vm.RefreshApiKeyStatus(); Assert.Null(vm.SelectedChatModel); Assert.NotEqual("model-a",vm.ModelDisplayName); }
    [Fact] public void 仅启用身份时显示CLI身份() { var (none,_)=Create(); Assert.Null(none.CliIdentityDisplayName); var (codex,_)=Create(new() {CliIdentity="codex"}); Assert.Contains("Codex",codex.CliIdentityDisplayName); Assert.Contains("codex_cli_rs",codex.CliIdentityToolTip); }
    [Fact] public async Task 后台指纹变化同步更新chat标签和请求头()
    {
        var (vm,_)=Create(new(){CliIdentity="codex"});
        var next=$"{Version.Parse(CliIdentityService.GetVersion(CliIdentityKind.Codex)).Major+1}.2.3";
        using var http=new HttpClient(new IdentityHandler(next));
        Assert.True(await CliIdentityService.RefreshVersionAsync(CliIdentityKind.Codex,http));
        Assert.Contains(next,vm.CliIdentityDisplayName);
        Assert.Contains(CliIdentityService.BuildHeaders(CliIdentityKind.Codex)["User-Agent"],vm.CliIdentityToolTip);
    }
    private sealed class IdentityHandler(string version):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
            =>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{tag_name=$"rust-v{version}"}))});
    }
    private static (ChatViewModel,IAiSkillRouter) Create(AppSettings? initial = null) { var settings=Substitute.For<ISettingsService>(); var saved=initial ?? new AppSettings(); settings.LoadSettings().Returns(saved); settings.GetApiKey(saved).Returns("test-key"); var router=Substitute.For<IAiSkillRouter>(); router.GetSkillCatalog().Returns(new[]{new AiSkillCatalogItem("skill-creator","创建清理规则","创建"),new AiSkillCatalogItem("unity-project-cleanup","Unity 项目清理","缓存")}); router.Route(Arg.Any<string>(),Arg.Any<FileEntry?>(),Arg.Any<FileEntry?>(),Arg.Any<bool>(),Arg.Any<string?>()).Returns(new AiSkillRoutingResult([])); return(new ChatViewModel(new DiskAssistantService(_=>new Client(),[],new AssistantWorkspace(),settings),settings,router),router); }
    private sealed class Client:IModelClient { public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request,[EnumeratorCancellation]CancellationToken ct) { yield return new TextDeltaEvent("已读取"); await Task.Yield(); } }
}
