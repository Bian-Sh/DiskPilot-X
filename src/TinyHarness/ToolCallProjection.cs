using System.Text.Json;
using System.Text.Json.Nodes;

namespace TinyHarness;

/// <summary>
/// 将原始工具调用投影为可进入历史、事件、UI 与批准流程的公开协议形态。
/// 该组件不负责凭据检测；凭据保护统一由 Credential Protection Pipeline 完成。
/// </summary>
public static class ToolCallProjection
{
    private const string HiddenArgumentsJson = "{\"summary\":\"参数已隐藏\"}";
    internal const string UnknownToolName = "unknown.tool";

    public static ToolCall Project(ToolCall rawCall, ToolDefinition? tool)
    {
        ArgumentNullException.ThrowIfNull(rawCall);
        if (tool is null)
        {
            return Hide(rawCall, UnknownToolName);
        }

        if (tool.SafeArgumentsProjector is null)
        {
            return Hide(rawCall, tool.Name);
        }

        try
        {
            var rawArguments = string.IsNullOrWhiteSpace(rawCall.ArgumentsJson)
                ? null
                : JsonNode.Parse(rawCall.ArgumentsJson);
            var projection = tool.SafeArgumentsProjector(rawArguments);
            return rawCall with
            {
                Name = tool.Name,
                ArgumentsJson = projection?.ToJsonString() ?? "{}",
                ArgumentsAreSafe = true,
            };
        }
        catch (Exception)
        {
            return Hide(rawCall, tool.Name);
        }
    }

    /// <summary>模型历史保留安全参数；UI 的隐藏摘要不能伪装成 schema 参数。</summary>
    public static string ToModelArgumentsJson(ToolCall call, JsonNode? parametersSchema = null)
    {
        var safe = EnsureSafe(call);
        if (safe.ArgumentsJson == HiddenArgumentsJson) return "{}";
        if (parametersSchema is null) return safe.ArgumentsJson;
        try
        {
            if (JsonNode.Parse(safe.ArgumentsJson) is not JsonObject arguments
                || parametersSchema is not JsonObject schema || schema["properties"] is not JsonObject properties) return "{}";
            // 交互摘要等公开投影不包含真实必需参数，不能当作历史调用输入。
            if (schema["required"] is JsonArray required
                && required.Any(item => item is not JsonValue value || !value.TryGetValue<string>(out var name) || !arguments.ContainsKey(name)))
                return "{}";
            return new JsonObject(arguments.Where(item => properties.ContainsKey(item.Key))
                .Select(item => KeyValuePair.Create(item.Key, item.Value?.DeepClone()))).ToJsonString();
        }
        catch (JsonException) { return "{}"; }
    }

    public static ToolCall EnsureSafe(ToolCall call) => call.ArgumentsAreSafe ? call : Hide(call);

    public static ChatMessage EnsureSafe(ChatMessage message)
    {
        var calls = message.ToolCalls.Select(EnsureSafe).ToArray();
        var blocks = message.Blocks.Select(block => block.ToolCall is null
            ? block
            : block with { ToolCall = EnsureSafe(block.ToolCall) }).ToArray();
        return message with { ToolCalls = calls, Blocks = blocks };
    }

    private static ToolCall Hide(ToolCall call, string? safeName = null) => call with
    {
        Name = safeName ?? call.Name,
        ArgumentsJson = HiddenArgumentsJson,
        ArgumentsAreSafe = true,
    };
}
