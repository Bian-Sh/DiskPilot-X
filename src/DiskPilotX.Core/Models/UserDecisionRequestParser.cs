using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiskPilotX.Core.Models;

/// <summary>移植 Loom-X askuser 的严格解析、内容边界与输入 Schema。</summary>
internal static class UserDecisionRequestParser
{
    private static readonly HashSet<string> RequestProperties = new(StringComparer.Ordinal)
    {
        "title", "question", "reason", "impact_summary", "allow_cancel", "fields",
    };
    private static readonly HashSet<string> FieldProperties = new(StringComparer.Ordinal)
    {
        "id", "label", "type", "is_required", "options", "default_option_id", "default_option_ids",
        "min_selections", "max_selections", "default_number", "min_number", "max_number", "step",
        "default_text", "is_multiline", "allow_custom_input", "custom_input_placeholder", "max_length", "purpose",
    };
    private static readonly HashSet<string> OptionProperties = new(StringComparer.Ordinal)
    {
        "id", "label", "description",
    };

    public static UserDecisionRequest Parse(JsonNode? arguments)
    {
        var root = arguments as JsonObject
            ?? throw new UserDecisionValidationException(
                [new UserDecisionValidationError(UserDecisionValidationError.RequestFieldId, "工具参数必须是对象。")]);
        ValidateProperties(root, RequestProperties);
        // 只有完全缺少 fields 才视为旧协议；显式空数组或错误类型必须报错。
        if (!root.ContainsKey("fields"))
        {
            var legacy = new UserDecisionRequest(
                RequireString(root, "title"), RequireString(root, "question"),
                [new UserDecisionField("answer", "回答", UserDecisionFieldType.Text, isRequired: true, isMultiline: true)],
                OptionalString(root, "reason"), OptionalString(root, "impact_summary"),
                OptionalValue(root, "allow_cancel", true));
            ValidateContentBoundary(legacy);
            ThrowIfInvalid(legacy);
            return legacy;
        }
        var fieldsNode = root["fields"] as JsonArray
            ?? throw new UserDecisionValidationException(
                [new UserDecisionValidationError(UserDecisionValidationError.RequestFieldId, "用户决策字段无效。")]);

        var fields = fieldsNode.Select(ParseField).ToArray();
        var request = new UserDecisionRequest(
            RequireString(root, "title"),
            RequireString(root, "question"),
            fields,
            OptionalString(root, "reason"),
            OptionalString(root, "impact_summary"),
            OptionalValue(root, "allow_cancel", true));
        ValidateContentBoundary(request);
        ThrowIfInvalid(request);
        return request;
    }

    private static UserDecisionField ParseField(JsonNode? node)
    {
        var field = node as JsonObject
            ?? throw new UserDecisionValidationException(
                [new UserDecisionValidationError(UserDecisionValidationError.RequestFieldId, "用户决策字段无效。")]);
        ValidateProperties(field, FieldProperties);
        var type = ParseFieldType(RequireString(field, "type"));
        var options = ReadOptions(field, "options");
        var purpose = OptionalString(field, "purpose") ?? "general";
        if (purpose is not ("general" or "credential_name")) throw InvalidRequest("用户决策字段用途无效。");

        return new UserDecisionField(
            RequireString(field, "id"),
            RequireString(field, "label"),
            type,
            OptionalValue(field, "is_required", false),
            options,
            OptionalString(field, "default_option_id"),
            ReadStringArray(field, "default_option_ids"),
            OptionalNullableValue<int>(field, "min_selections"),
            OptionalNullableValue<int>(field, "max_selections"),
            OptionalNullableValue<decimal>(field, "default_number"),
            OptionalNullableValue<decimal>(field, "min_number"),
            OptionalNullableValue<decimal>(field, "max_number"),
            OptionalNullableValue<decimal>(field, "step"),
            OptionalString(field, "default_text"),
            OptionalValue(field, "is_multiline", false),
            OptionalValue(field, "allow_custom_input", type is UserDecisionFieldType.SingleSelect or UserDecisionFieldType.MultiSelect),
            OptionalString(field, "custom_input_placeholder"),
            OptionalNullableValue<int>(field, "max_length"), purpose == "credential_name");
    }

    private static UserDecisionOption ParseOption(JsonNode? node)
    {
        var option = node as JsonObject
            ?? throw new UserDecisionValidationException(
                [new UserDecisionValidationError(UserDecisionValidationError.RequestFieldId, "用户决策选项无效。")]);
        ValidateProperties(option, OptionProperties);
        return new UserDecisionOption(
            RequireString(option, "id"),
            RequireString(option, "label"),
            OptionalString(option, "description"));
    }

    private static UserDecisionFieldType ParseFieldType(string value) => value switch
    {
        "single_select" => UserDecisionFieldType.SingleSelect,
        "multi_select" => UserDecisionFieldType.MultiSelect,
        "number" => UserDecisionFieldType.Number,
        "text" => UserDecisionFieldType.Text,
        _ => throw new UserDecisionValidationException(
            [new UserDecisionValidationError(UserDecisionValidationError.RequestFieldId, "用户决策字段类型无效。")]),
    };

    private static void ValidateProperties(JsonObject source, IReadOnlySet<string> allowedProperties)
    {
        if (source.Any(property => !allowedProperties.Contains(property.Key)))
        {
            throw InvalidRequest("用户决策请求包含未知属性。");
        }
    }

    private static void ValidateContentBoundary(UserDecisionRequest request)
    {
        if (EnumerateDisplayContent(request).Any(IsProhibitedContent))
        {
            throw InvalidRequest("用户决策请求包含禁止的正文或配置内容。");
        }
    }

    private static IEnumerable<string?> EnumerateDisplayContent(UserDecisionRequest request)
    {
        yield return request.Title;
        yield return request.Question;
        yield return request.Description;
        yield return request.ImpactSummary;

        foreach (var field in request.Fields)
        {
            yield return field.Label;
            yield return field.DefaultText;
            foreach (var option in field.Options)
            {
                yield return option.Label;
                yield return option.Description;
            }
        }
    }

    private static bool IsProhibitedContent(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var trimmed = content.Trim();
        if (LooksLikeJsonDocument(trimmed))
        {
            return true;
        }

        var lines = content
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var assignmentCount = lines.Count(LooksLikeTomlAssignment);
        if (assignmentCount >= 2 || (assignmentCount >= 1 && lines.Any(LooksLikeTomlTableHeader)))
        {
            return true;
        }

        return lines.Any(LooksLikeHttpHeader);
    }

    private static bool LooksLikeJsonDocument(string content)
    {
        if (content.Length < 2
            || (content[0] != '{' || content[^1] != '}')
            && (content[0] != '[' || content[^1] != ']'))
        {
            return false;
        }

        try
        {
            return JsonNode.Parse(content) is JsonObject or JsonArray;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool LooksLikeTomlTableHeader(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length >= 3 && trimmed.StartsWith('[') && trimmed.EndsWith(']');
    }

    private static bool LooksLikeTomlAssignment(string line)
    {
        var trimmed = line.Trim();
        var equalsIndex = trimmed.IndexOf('=');
        if (equalsIndex <= 0 || equalsIndex == trimmed.Length - 1)
        {
            return false;
        }

        var key = trimmed[..equalsIndex].Trim();
        if (key.Length >= 2
            && (key[0] == '"' && key[^1] == '"' || key[0] == '\'' && key[^1] == '\''))
        {
            return true;
        }

        return key.All(character => char.IsLetterOrDigit(character)
            || character is '_' or '-' or '.' or ' ');
    }

    private static bool LooksLikeHttpHeader(string line)
    {
        var trimmed = line.Trim();
        var colonIndex = trimmed.IndexOf(':');
        if (colonIndex <= 0 || colonIndex == trimmed.Length - 1)
        {
            return false;
        }

        // 目录和资料网址是本应用的正常输入，不是 HTTP 请求头。
        if (colonIndex == 1 && char.IsAsciiLetter(trimmed[0]) && trimmed[colonIndex+1] is '\\' or '/') return false;
        if (Uri.TryCreate(trimmed,UriKind.Absolute,out var uri) && uri.Scheme is "http" or "https") return false;
        var name = trimmed[..colonIndex];
        return name.All(character => character is >= 'A' and <= 'Z'
            or >= 'a' and <= 'z'
            or >= '0' and <= '9'
            or '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.'
            or '^' or '_' or '`' or '|' or '~');
    }

    private static void ThrowIfInvalid(UserDecisionRequest request)
    {
        var errors = UserDecisionValidator.ValidateRequest(request);
        if (errors.Count > 0) throw new UserDecisionValidationException(errors);
    }

    private static UserDecisionValidationException InvalidRequest(string message) =>
        new([new UserDecisionValidationError(UserDecisionValidationError.RequestFieldId, message)]);

    private static string RequireString(JsonObject source, string propertyName)
    {
        if (source[propertyName] is JsonValue value && value.TryGetValue<string>(out var result))
        {
            return result;
        }

        throw InvalidRequest("用户决策请求缺少必填属性或属性类型无效。");
    }

    private static string? OptionalString(JsonObject source, string propertyName)
    {
        if (!source.TryGetPropertyValue(propertyName, out var node))
        {
            return null;
        }

        if (node is null)
        {
            throw InvalidRequest("用户决策可选属性类型无效。");
        }

        return node is JsonValue value && value.TryGetValue<string>(out var result)
            ? result
            : throw InvalidRequest("用户决策可选属性类型无效。");
    }

    private static T OptionalValue<T>(JsonObject source, string propertyName, T defaultValue)
    {
        if (!source.TryGetPropertyValue(propertyName, out var node))
        {
            return defaultValue;
        }

        if (node is null)
        {
            throw InvalidRequest("用户决策可选属性类型无效。");
        }

        return ReadValue<T>(node);
    }

    private static T? OptionalNullableValue<T>(JsonObject source, string propertyName)
        where T : struct
    {
        if (!source.TryGetPropertyValue(propertyName, out var node))
        {
            return null;
        }

        if (node is null)
        {
            throw InvalidRequest("用户决策可选属性类型无效。");
        }

        return ReadValue<T>(node);
    }

    private static T ReadValue<T>(JsonNode node)
    {
        if (node is not JsonValue)
        {
            throw InvalidRequest("用户决策可选属性类型无效。");
        }

        try
        {
            return node.Deserialize<T>()!;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            throw InvalidRequest("用户决策可选属性类型无效。");
        }
    }

    private static IReadOnlyList<UserDecisionOption> ReadOptions(JsonObject source, string propertyName)
    {
        if (!source.TryGetPropertyValue(propertyName, out var node))
        {
            return [];
        }

        if (node is not JsonArray items)
        {
            throw InvalidRequest("用户决策选项类型无效。");
        }

        return items.Select(ParseOption).ToArray();
    }

    private static IReadOnlyList<string> ReadStringArray(JsonObject source, string propertyName)
    {
        if (!source.TryGetPropertyValue(propertyName, out var node))
        {
            return [];
        }

        if (node is not JsonArray items)
        {
            throw InvalidRequest("用户决策默认选项类型无效。");
        }

        return items.Select(item => item is JsonValue value && value.TryGetValue<string>(out var result)
                ? result
                : throw InvalidRequest("用户决策默认选项无效。"))
            .ToArray();
    }

    public static JsonNode CreateSchema() => JsonNode.Parse("""
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["title", "question", "fields"],
          "properties": {
            "title": { "type": "string", "maxLength": 120 },
            "question": { "type": "string", "maxLength": 2000 },
            "reason": { "type": "string", "maxLength": 2000 },
            "impact_summary": { "type": "string", "maxLength": 1000 },
            "allow_cancel": { "type": "boolean", "default": true },
            "fields": {
              "type": "array",
              "description": "字段列表；每个字段独立分页。单选或多选字段默认在选项下方同一页附带自由输入框（allow_custom_input 默认 true），不要新增 text 字段。",
              "minItems": 1,
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["id", "label", "type"],
                "properties": {
                  "id": { "type": "string", "maxLength": 64 },
                  "label": { "type": "string", "maxLength": 200 },
                  "type": { "type": "string", "enum": ["single_select", "multi_select", "number", "text"], "description": "single_select 和 multi_select 必须提供至少一个 options。" },
                  "purpose": { "type": "string", "enum": ["general", "credential_name"], "default": "general", "description": "只有收集、选择或自定义凭据名称时设为 credential_name；仅限单行 text 或 single_select 字段，最长200字。允许名称术语，仍禁止真实密钥、认证赋值及凭据引用。" },
                  "is_required": { "type": "boolean", "default": false },
                  "options": {
                    "type": "array",
                    "items": {
                      "type": "object",
                      "additionalProperties": false,
                      "required": ["id", "label"],
                      "properties": {
                        "id": { "type": "string", "maxLength": 64 },
                        "label": { "type": "string", "maxLength": 200 },
                        "description": { "type": "string", "maxLength": 500 }
                      }
                    }
                  },
                  "default_option_id": { "type": "string" },
                  "default_option_ids": { "type": "array", "items": { "type": "string" } },
                  "min_selections": { "type": "integer", "minimum": 0 },
                  "max_selections": { "type": "integer", "minimum": 0 },
                  "default_number": { "type": "number" },
                  "min_number": { "type": "number" },
                  "max_number": { "type": "number" },
                  "step": { "type": "number", "exclusiveMinimum": 0 },
                  "default_text": { "type": "string" },
                  "is_multiline": { "type": "boolean", "default": false },
                  "allow_custom_input": { "type": "boolean", "default": true, "description": "仅用于 single_select 或 multi_select；默认 true：选项下方同页显示自由输入框，避免选项扭曲用户意图。适用于其他选项、都不符合或我有其他想法等回答；不要新增 text 字段。仅当确实不接纳自由回答时才显式设为 false。" },
                  "custom_input_placeholder": { "type": "string", "maxLength": 200, "description": "选择题同页自由输入框的 Watermark；仅在 allow_custom_input=true 时使用。" },
                  "max_length": { "type": "integer", "minimum": 1, "maximum": 4000, "description": "text 字段或已启用 allow_custom_input 的选择字段之自由输入最大字符数；用户说输入框80字时应设置为80。" }
                },
                "allOf": [
                  {
                    "if": { "properties": { "type": { "const": "single_select" } }, "required": ["type"] },
                    "then": { "required": ["options"], "properties": { "options": { "minItems": 1 } } }
                  },
                  {
                    "if": { "properties": { "type": { "const": "multi_select" } }, "required": ["type"] },
                    "then": { "required": ["options"], "properties": { "options": { "minItems": 1 } } }
                  }
                ]
              }
            }
          }
        }
        """)!;

}
