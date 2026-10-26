using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using DiskPilotX.Core.Models;
namespace DiskPilotX.App.ViewModels;
public partial class ChatViewModel
{
    [ObservableProperty] private AskUserDialogViewModel? _pendingAskUser;
    public void CancelPendingInteractions() { pendingDecision?.TrySetResult(false); PendingAskUser?.Cancel(); }
    public async Task<JsonNode> AskUserAsync(JsonNode? arguments,CancellationToken ct)
    {
        var request=UserDecisionRequest.Parse(arguments);
        var decision=new AskUserDialogViewModel(request);
        await OnUiAsync(()=> { PendingAskUser?.Cancel(); PendingInteractionCard=null; PendingAskUser=decision; StatusText="等待你的选择"; });
        try
        {
            var response=await decision.Completion.WaitAsync(ct);
            response["ok"]=response["cancelled"]?.GetValue<bool>()!=true;
            if(response["values"]?["answer"] is JsonNode answer) response["answer"]=answer.DeepClone();
            return response;
        }
        finally
        {
            await OnUiAsync(()=> { decision.Cancel(); if(ReferenceEquals(PendingAskUser,decision)) { PendingAskUser=null; StatusText=IsOperationRunning?"正在处理":"准备就绪"; } });
        }
    }
}
