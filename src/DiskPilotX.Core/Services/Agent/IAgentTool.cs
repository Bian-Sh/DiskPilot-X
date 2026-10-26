using System.Text.Json;

namespace DiskPilotX.Core.Services.Agent;

public interface IAgentTool
{
    string Name { get; }
    string Description { get; }
    ToolRiskLevel RiskLevel { get; }
    JsonElement Schema { get; }
    bool RequiresScanContext => true;

    Task<JsonElement> ExecuteAsync(AgentContext context, JsonElement arguments, CancellationToken cancellationToken);
}
