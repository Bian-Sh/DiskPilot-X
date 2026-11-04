using System.Runtime.CompilerServices;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Assistant;
using DiskPilotX.Core.Services.Settings;
using DiskPilotX.Core.Services.Llm;
using NSubstitute;
using TinyHarness;
using FluentAssertions;
namespace DiskPilotX.Core.Tests;
public class RepeatedToolFailureTests
{
    [Fact] public void 三次相同失败终止且对象键重排不能绕过()
    {
        var guard=new RepeatedToolFailureGuard();
        guard.Observe("scan","{\"a\":1,\"b\":2}",false,"{\"code\":\"not_scanned\"}").Should().BeFalse();
        guard.Observe("scan","{\"b\":2,\"a\":1}",false,"{\"code\":\"not_scanned\"}").Should().BeFalse();
        guard.Observe("scan","{\"a\":1,\"b\":2}",false,"{\"code\":\"not_scanned\"}").Should().BeTrue();
    }
    [Fact] public void 重复相同成功查询不是新进展且新守卫代表新轮次()
    {
        var guard=new RepeatedToolFailureGuard();
        guard.Observe("context","{}",true,"same");
        guard.Observe("scan","{}",false,"fail");guard.Observe("context","{}",true,"same");
        guard.Observe("scan","{}",false,"fail");guard.Observe("context","{}",true,"same");
        guard.Observe("scan","{}",false,"fail").Should().BeTrue();
        new RepeatedToolFailureGuard().Observe("scan","{}",false,"fail").Should().BeFalse();
    }
    [Fact] public async Task 真实循环第三次相同失败后不发起第四次模型请求()
    {
        var client=new RepeatingClient();var settings=Substitute.For<ISettingsService>();settings.LoadSettings().Returns(new AppSettings());
        var service=new DiskAssistantService(_=>client,[],new AssistantWorkspace(),settings);
        var result=await service.RunAsync("重复失败夹具",new(new ModelEndpointConfig {ApiKey="fixture",Model="fixture"}),null,CancellationToken.None);
        result.State.Should().Be(AgentSessionState.Failed);client.Calls.Should().Be(3);
        result.FailureDetail.Should().Contain("三次");
    }
    private sealed class RepeatingClient : IModelClient
    {
        public int Calls;
        public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request,[EnumeratorCancellation]CancellationToken ct)
        {
            await Task.Yield();Calls++;
            yield return new ModelToolCallEvent(new ToolCall("repeat-"+Calls,"scan_path","{\"path\":\"\"}"));
            yield return new ModelCompletedEvent("tool_use");
        }
    }
}
