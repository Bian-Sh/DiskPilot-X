using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using DiskPilotX.App.Diagnostics;
using DiskPilotX.Core.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TinyHarness;
using Message=DiskPilotX.Core.Models.ChatMessage;
namespace DiskPilotX.App;
public partial class MainWindow
{
    private JsonObject? _retryProbe;
    internal object? AcceptanceRetryStatus(bool start)
    {
        if(!AcceptanceAutomationServer.IsEnabled()||string.IsNullOrWhiteSpace(Core.AppCompatibility.GetEnvironmentVariable("DISKPILOT_X_ACCEPTANCE_SETTINGS")))throw new InvalidOperationException("需要独立验收环境。");
        if(!start)return _retryProbe;
        if(_chatViewModel is null||_chatViewModel.IsOperationRunning)throw new InvalidOperationException("聊天工作区忙碌。");
        _retryProbe=new JsonObject {["running"]=true};_=Run();return _retryProbe;
        async Task Run()
        {
            var message=new Message {Sender=ChatSender.Assistant,Timestamp=DateTime.Now};_chatViewModel.Messages.Add(message);_chatViewModel.IsOperationRunning=true;
            try
            {
                var loop=new AgentLoop(new RetryFixtureClient(),new ToolRegistry(),NullLogger<AgentLoop>.Instance);
                await foreach(var activity in loop.RunAsync(new AgentSession(),"本地429状态夹具，不请求网络"))
                {
                    message.ApplyActivity(activity);
                    if(activity.Kind==AgentEventKind.ModelRetryStatus&&_retryProbe is not null)_retryProbe["status"]=activity.Detail;
                }
                _retryProbe=new JsonObject {["running"]=false,["ok"]=message.Segments.Any(segment=>segment.Text.Contains("等待")),["text"]=message.Text,["statuses"]=System.Text.Json.JsonSerializer.SerializeToNode(message.Segments.Where(segment=>segment.IsSummary).Select(segment=>segment.Text))};
            }
            catch(Exception ex){_retryProbe=new JsonObject {["running"]=false,["ok"]=false,["error"]=ex.Message};}
            finally {_chatViewModel.IsOperationRunning=false;App.Services!.GetRequiredService<Core.Services.Assistant.DiskAssistantService>().SaveConversation(_chatViewModel.Messages);}
        }
    }
    private sealed class RetryFixtureClient:IModelClient
    {
        private int count;
        public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request,[EnumeratorCancellation]CancellationToken ct)
        {
            await Task.Yield();if(++count==1)throw new ModelClientException("合成429",ModelErrorKind.RateLimited,429){RetryAfter=TimeSpan.FromSeconds(10)};
            yield return new TextDeltaEvent("本地重试夹具恢复完成，未请求网络。");yield return new ModelCompletedEvent("stop");
        }
    }
}
