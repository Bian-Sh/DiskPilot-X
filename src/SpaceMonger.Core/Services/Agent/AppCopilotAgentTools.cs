using System.IO;
using System.Text.Json;
using SpaceMonger.Core.Services.Copilot;
using SpaceMonger.Core.Services.Scanning;

namespace SpaceMonger.Core.Services.Agent;

public abstract class AppCopilotToolBase : IAgentTool
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract JsonElement Schema { get; }
    public virtual ToolRiskLevel RiskLevel => ToolRiskLevel.Low;
    public bool RequiresScanContext => false;

    public abstract Task<JsonElement> ExecuteAsync(AgentContext context, JsonElement arguments, CancellationToken cancellationToken);

    protected static JsonElement Json(object value)
    {
        return JsonSerializer.SerializeToElement(value, AgentJson.Options);
    }

    protected static JsonElement SchemaJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    protected static string? GetString(JsonElement arguments, string name)
    {
        return arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}

public sealed class ResolvePathTool : AppCopilotToolBase
{
    public override string Name => "resolve_path";

    public override string Description => "Resolve a user-supplied Windows path before proposing or executing scan/navigation actions. Expands environment variables such as %USERPROFILE%, normalizes to a full path, and reports whether the target exists and is scannable as a directory.";

    public override JsonElement Schema { get; } = SchemaJson("""
        {"type":"object","properties":{"path":{"type":"string","description":"User-supplied path, including environment variables like %USERPROFILE% or relative paths."}},"required":["path"]}
        """);


    public override Task<JsonElement> ExecuteAsync(AgentContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        var inputPath = GetString(arguments, "path");
        if (!ScanPathResolver.TryResolve(inputPath, out var resolvedPath, out var error))
        {
            return Task.FromResult(Json(new
            {
                ok = false,
                input_path = inputPath,
                resolved_path = (string?)null,
                exists = false,
                is_directory = false,
                can_scan = false,
                error
            }));
        }

        var isDirectory = Directory.Exists(resolvedPath);
        var exists = isDirectory || File.Exists(resolvedPath);
        return Task.FromResult(Json(new
        {
            ok = true,
            input_path = inputPath,
            resolved_path = resolvedPath,
            exists,
            is_directory = isDirectory,
            can_scan = isDirectory,
            error = exists ? null : "Path does not exist."
        }));
    }
}
