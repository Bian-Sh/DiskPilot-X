using System.Runtime.CompilerServices;
using NSubstitute;
using TinyHarness;
using SpaceMonger.App.ViewModels;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Assistant;
using SpaceMonger.Core.Services.Copilot;
using SpaceMonger.Core.Services.Settings;
namespace SpaceMonger.App.Tests;
public sealed class ChatDiscoveryTests
{
    [Fact] public void DiscoveryReflectsLiveCatalogAndExcludesCreator() { var (vm,router)=Create(); Assert.False(vm.HasMessages); Assert.Single(vm.CleanupRules); router.GetSkillCatalog().Returns(new[] {new AiSkillCatalogItem("qq-cache","QQ 缓存","检查日志")}); vm.RefreshSkills(); Assert.Equal("QQ 缓存",Assert.Single(vm.CleanupRules).Title); }
    [Fact] public async Task PromptStartsConversationInOneAction() { var(vm,_)=Create(); await vm.ExplorePromptCommand.ExecuteAsync("查看我已经创建的清理规则"); Assert.True(vm.HasMessages); Assert.Contains(vm.Messages,m=>m.Text=="查看我已经创建的清理规则"); }
    [Fact] public void TaskCardRemainsAfterFollowingMessages() { var(vm,_)=Create(); var card=new SkillTaskCard("task1","qq-cache","QQ 缓存","待预览"); vm.AddSkillTask(card); vm.Messages.Add(new(){Text="继续"}); Assert.Same(card,vm.Messages.First().SkillTasks.Single()); }
    private static (ChatViewModel,IAiSkillRouter) Create() { var settings=Substitute.For<ISettingsService>(); var saved=new AppSettings(); settings.LoadSettings().Returns(saved); settings.GetApiKey(saved).Returns("test-key"); var router=Substitute.For<IAiSkillRouter>(); router.GetSkillCatalog().Returns(new[]{new AiSkillCatalogItem("skill-creator","创建清理规则","创建"),new AiSkillCatalogItem("unity-project-cleanup","Unity 项目清理","缓存")}); router.Route(Arg.Any<string>(),Arg.Any<FileEntry?>(),Arg.Any<FileEntry?>(),Arg.Any<bool>(),Arg.Any<string?>()).Returns(new AiSkillRoutingResult([])); return(new ChatViewModel(new DiskAssistantService(_=>new Client(),[],new AssistantWorkspace(),settings),settings,router),router); }
    private sealed class Client:IModelClient { public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request,[EnumeratorCancellation]CancellationToken ct) { yield return new TextDeltaEvent("已读取"); await Task.Yield(); } }
}
