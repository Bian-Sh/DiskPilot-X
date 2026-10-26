using System.Text.Json;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.FileTree;
using DiskPilotX.Core.Services.Settings;
using DiskPilotX.Core.Services.Whitelist;

namespace DiskPilotX.Core.Services.Agent;

public abstract class FileTreeAgentToolBase(IFileTreeQueryService queryService, ISettingsService? settingsService = null, IPathWhitelistMatcher? whitelistMatcher = null) : IAgentTool
{
    protected IFileTreeQueryService QueryService { get; } = queryService;
    private readonly ISettingsService? _settingsService = settingsService;
    private readonly IPathWhitelistMatcher _whitelistMatcher = whitelistMatcher ?? new PathWhitelistMatcher();

    public abstract string Name { get; }
    public abstract string Description { get; }
    public ToolRiskLevel RiskLevel => ToolRiskLevel.ReadOnly;
    public abstract JsonElement Schema { get; }

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

    protected static int GetInt(JsonElement arguments, string name, int defaultValue, int min, int max)
    {
        if (arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out var value) && value.TryGetInt32(out var intValue))
        {
            return Math.Clamp(intValue, min, max);
        }

        return defaultValue;
    }

    protected static long? GetLong(JsonElement arguments, string name)
    {
        return arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out var value) && value.TryGetInt64(out var longValue)
            ? longValue
            : null;
    }

    protected static bool GetBool(JsonElement arguments, string name, bool defaultValue = false)
    {
        return arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : defaultValue;
    }

    protected static object EntryJson(FileEntry entry) => FileTreeToolResult.Entry(entry, null, true);

    protected static JsonElement Error(string code, string message)
    {
        return Json(new
        {
            ok = false,
            error = new
            {
                code,
                message
            }
        });
    }

    protected static ScanSession RequireScanSession(AgentContext context)
    {
        return context.Session ?? throw new InvalidOperationException("A completed scan context is required for file tree tools.");
    }

    protected bool IsAiHidden(string? path)
    {
        return _whitelistMatcher.IsExcluded(path, _settingsService?.LoadSettings().AiConversationWhitelist);
    }

    protected static JsonElement HiddenPath()
    {
        return Error("hidden_by_whitelist", "This path is hidden by whitelist settings.");
    }
}

public sealed class FindByNameTool(IFileTreeQueryService queryService, ISettingsService? settingsService = null, IPathWhitelistMatcher? whitelistMatcher = null) : FileTreeAgentToolBase(queryService, settingsService, whitelistMatcher)
{
    public override string Name => "find_by_name";
    public override string Description => "按名称查找已扫描文件或目录，按大小排序；结果路径相对 base_path，后续调用须拼成绝对路径。details 获取详情，format=tree 合并目录。";
    public override JsonElement Schema { get; } = FileTreeToolResult.Schema("""
        {"type":"object","properties":{"name":{"type":"string"},"exact_match":{"type":"boolean"},"max_results":{"type":"integer","minimum":1,"maximum":100}},"required":["name"]}
        """);

    public override Task<JsonElement> ExecuteAsync(AgentContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        var name = GetString(arguments, "name");
        var session = RequireScanSession(context);
        if (string.IsNullOrWhiteSpace(name))
        {
            return Task.FromResult(Error("invalid_arguments", "name is required."));
        }

        var results = QueryService.FindByName(
            session,
            name,
            GetBool(arguments, "exact_match"),
            GetInt(arguments, "max_results", 25, 1, 100) + 1);

        return Task.FromResult(FileTreeToolResult.List(session.RootEntry!.Path, results,
            GetInt(arguments, "max_results", 25, 1, 100), arguments, "results",
            metadata: new() { ["query"] = name }));
    }
}

public sealed class FindByPathTool(IFileTreeQueryService queryService, ISettingsService? settingsService = null, IPathWhitelistMatcher? whitelistMatcher = null) : FileTreeAgentToolBase(queryService, settingsService, whitelistMatcher)
{
    public override string Name => "find_by_path";
    public override string Description => "按完整路径读取已扫描项详情；列表结果先将 path 或 base_path 与相对子项路径拼接。Windows 路径不区分大小写。";
    public override JsonElement Schema { get; } = SchemaJson("""
        {"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}
        """);

    public override Task<JsonElement> ExecuteAsync(AgentContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        var path = GetString(arguments, "path");
        var session = RequireScanSession(context);
        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult(Error("invalid_arguments", "path is required."));
        }

        if (IsAiHidden(path))
        {
            return Task.FromResult(HiddenPath());
        }

        var entry = QueryService.FindByPath(session, path);
        return Task.FromResult(entry is null
            ? Error("not_found", $"Path not found in scanned tree: {path}")
            : Json(new { ok = true, result = EntryJson(entry) }));
    }
}

public sealed class ListChildrenTool(IFileTreeQueryService queryService, ISettingsService? settingsService = null, IPathWhitelistMatcher? whitelistMatcher = null) : FileTreeAgentToolBase(queryService, settingsService, whitelistMatcher)
{
    public override string Name => "list_children";
    public override string Description => "列出已扫描目录的直接子项，按大小排序；子项 path 相对顶层 path，后续调用须拼成绝对路径。details 获取详情，format=tree 输出树形。";
    public override JsonElement Schema { get; } = FileTreeToolResult.Schema("""
        {"type":"object","properties":{"path":{"type":"string"},"max_results":{"type":"integer","minimum":1,"maximum":200}},"required":["path"]}
        """);

    public override Task<JsonElement> ExecuteAsync(AgentContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        var path = GetString(arguments, "path");
        var session = RequireScanSession(context);
        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult(Error("invalid_arguments", "path is required."));
        }

        if (IsAiHidden(path))
        {
            return Task.FromResult(HiddenPath());
        }

        var entry = QueryService.FindByPath(session, path);
        if (entry is null)
        {
            return Task.FromResult(Error("not_found", $"Path not found in scanned tree: {path}"));
        }

        if (!entry.IsDirectory)
        {
            return Task.FromResult(Error("not_directory", $"Path is not a directory: {path}"));
        }

        var limit = GetInt(arguments, "max_results", 50, 1, 200);
        var children = QueryService.ListChildren(session, path, limit + 1);
        return Task.FromResult(FileTreeToolResult.List(entry.Path, children, limit, arguments, "children", "path"));
    }
}

public sealed class SummarizeSubtreeTool(IFileTreeQueryService queryService, ISettingsService? settingsService = null, IPathWhitelistMatcher? whitelistMatcher = null) : FileTreeAgentToolBase(queryService, settingsService, whitelistMatcher)
{
    public override string Name => "summarize_subtree";
    public override string Description => "汇总已扫描子树并列出最大直接子项；子项 path 相对顶层 path，后续调用须拼成绝对路径。details 获取详情，format=tree 输出树形。";
    public override JsonElement Schema { get; } = FileTreeToolResult.Schema("""
        {"type":"object","properties":{"path":{"type":"string"},"top_children":{"type":"integer","minimum":1,"maximum":50}},"required":["path"]}
        """);

    public override Task<JsonElement> ExecuteAsync(AgentContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        var path = GetString(arguments, "path");
        var session = RequireScanSession(context);
        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult(Error("invalid_arguments", "path is required."));
        }

        if (IsAiHidden(path))
        {
            return Task.FromResult(HiddenPath());
        }

        var entry = QueryService.FindByPath(session, path);
        if (entry is null)
        {
            return Task.FromResult(Error("not_found", $"Path not found in scanned tree: {path}"));
        }

        var limit = GetInt(arguments, "top_children", 20, 1, 50);
        var summary = QueryService.SummarizeSubtree(session, path, limit + 1);
        var metadata = new Dictionary<string, object?>
        {
            ["size_bytes"] = summary.SizeBytes,
            ["file_count"] = summary.FileCount,
            ["directory_count"] = summary.DirectoryCount
        };
        if (GetBool(arguments, "details") && summary.LastModified.HasValue)
            metadata["last_modified"] = summary.LastModified.Value.ToString("O");
        return Task.FromResult(FileTreeToolResult.List(summary.Path, summary.LargestChildren, limit, arguments,
            "largest_children", "path", metadata));
    }
}

public sealed class FindLargeFilesTool(IFileTreeQueryService queryService, ISettingsService? settingsService = null, IPathWhitelistMatcher? whitelistMatcher = null) : FileTreeAgentToolBase(queryService, settingsService, whitelistMatcher)
{
    public override string Name => "find_large_files";
    public override string Description => "查找已扫描的大文件，按大小排序；结果路径相对 base_path，后续调用须拼成绝对路径。details 获取详情，format=tree 合并深层目录。";
    public override JsonElement Schema { get; } = FileTreeToolResult.Schema("""
        {"type":"object","properties":{"under_path":{"type":"string"},"max_results":{"type":"integer","minimum":1,"maximum":100},"min_size_bytes":{"type":"integer","minimum":0}}}
        """);

    public override Task<JsonElement> ExecuteAsync(AgentContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        var underPath = GetString(arguments, "under_path");
        var session = RequireScanSession(context);
        if (IsAiHidden(underPath))
        {
            return Task.FromResult(HiddenPath());
        }

        if (!string.IsNullOrWhiteSpace(underPath) && QueryService.FindByPath(session, underPath) is null)
        {
            return Task.FromResult(Error("not_found", $"Path not found in scanned tree: {underPath}"));
        }

        var results = QueryService.FindLargeFiles(
            session,
            underPath,
            GetInt(arguments, "max_results", 25, 1, 100) + 1,
            GetLong(arguments, "min_size_bytes"));

        var basePath = string.IsNullOrWhiteSpace(underPath) ? session.RootEntry!.Path : QueryService.FindByPath(session, underPath)!.Path;
        return Task.FromResult(FileTreeToolResult.List(basePath, results,
            GetInt(arguments, "max_results", 25, 1, 100), arguments, "results"));
    }
}
