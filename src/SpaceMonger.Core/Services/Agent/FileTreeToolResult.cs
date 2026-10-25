using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SpaceMonger.Core.Models;

namespace SpaceMonger.Core.Services.Agent;

/// <summary>文件树工具的紧凑投影；路径基准始终随本次结果返回。</summary>
internal static class FileTreeToolResult
{
    public static JsonElement Schema(string json)
    {
        var schema = JsonNode.Parse(json)!;
        schema["properties"]!["details"] = JsonNode.Parse("""{"type":"boolean","description":"按需返回修改时间等详情，默认 false。"}""");
        schema["properties"]!["format"] = JsonNode.Parse("""{"type":"string","enum":["json","tree"],"description":"默认 json；tree 合并公共目录和单子目录链，#序号保留大小排名；目录标签为 JSON 字符串。"}""");
        return JsonSerializer.SerializeToElement(schema, AgentJson.Options);
    }

    public static Dictionary<string, object?> Entry(FileEntry entry, string? basePath, bool details)
    {
        var result = new Dictionary<string, object?>
        {
            ["path"] = basePath is null ? entry.Path : Path.GetRelativePath(basePath, entry.Path).Replace('\\', '/'),
            ["type"] = entry.IsDirectory ? "directory" : "file",
            ["size_bytes"] = entry.Size
        };
        if (entry.IsDirectory) result["child_count"] = entry.Children.Count;
        if (details)
        {
            if (basePath is null) result["name"] = entry.Name;
            if (entry.LastModified != default) result["last_modified"] = entry.LastModified.ToString("O");
            if (basePath is null && !string.IsNullOrEmpty(entry.Extension)) result["extension"] = entry.Extension;
        }
        var flags = new Dictionary<string, bool>();
        if (entry.IsReparsePoint) flags["is_reparse_point"] = true;
        if (entry.IsAccessDenied) flags["is_access_denied"] = true;
        if (entry.IsCloudPlaceholder) flags["is_cloud_placeholder"] = true;
        if (flags.Count > 0) result["flags"] = flags;
        return result;
    }

    public static JsonElement List(string basePath, IReadOnlyList<FileEntry> entries, int limit, JsonElement arguments,
        string collection, string baseKey = "base_path", Dictionary<string, object?>? metadata = null)
    {
        var format = arguments.TryGetProperty("format", out var f) ? f.GetString() : "json";
        if (format is not (null or "json" or "tree"))
            return JsonSerializer.SerializeToElement(new { ok = false, error = new { code = "invalid_arguments", message = "format 仅支持 json 或 tree。" } }, AgentJson.Options);
        var details = arguments.TryGetProperty("details", out var d) && d.ValueKind == JsonValueKind.True;
        var items = entries.Take(limit).Select(entry => Entry(entry, basePath, details)).ToArray();
        var result = metadata ?? new Dictionary<string, object?>();
        result["ok"] = true;
        result[baseKey] = basePath;
        result["path_format"] = "relative";
        result["count"] = items.Length;
        result["truncated"] = entries.Count > limit;
        if (format == "tree") result["tree"] = Tree(items);
        else result[collection] = items;
        return JsonSerializer.SerializeToElement(result, AgentJson.Options);
    }

    private sealed class Branch
    {
        public Dictionary<string, Branch> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, object?>? Item { get; set; }
        public int Rank { get; set; }
    }

    private static string Tree(IReadOnlyList<Dictionary<string, object?>> items)
    {
        var root = new Branch();
        for (var i = 0; i < items.Count; i++)
        {
            var branch = root;
            foreach (var segment in ((string)items[i]["path"]!).Split('/'))
            {
                if (!branch.Children.TryGetValue(segment, out var child))
                    branch.Children[segment] = child = new Branch();
                branch = child;
            }
            branch.Item = items[i];
            branch.Rank = i + 1;
        }
        var text = new StringBuilder();
        Append(root, "", text);
        return text.ToString().TrimEnd();
    }

    private static void Append(Branch parent, string prefix, StringBuilder text)
    {
        var index = 0;
        foreach (var pair in parent.Children)
        {
            var label = pair.Key;
            var branch = pair.Value;
            while (branch.Item is null && branch.Children.Count == 1)
            {
                var next = branch.Children.First();
                label += "/" + next.Key;
                branch = next.Value;
            }
            var last = ++index == parent.Children.Count;
            text.Append(prefix).Append(last ? "└─ " : "├─ ").Append(JsonSerializer.Serialize(label, AgentJson.Options));
            if (branch.Item is not null)
            {
                text.Append(" #").Append(branch.Rank).Append(' ');
                text.Append(JsonSerializer.Serialize(branch.Item.Where(p => p.Key != "path").ToDictionary(), AgentJson.Options));
            }
            else text.Append('/');
            text.Append('\n');
            Append(branch, prefix + (last ? "   " : "│  "), text);
        }
    }
}
