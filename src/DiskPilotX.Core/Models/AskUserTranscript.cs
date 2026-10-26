using System.Text.Json;

namespace DiskPilotX.Core.Models;

/// <summary>ask_user 工具段的一条问答展示：问题 + 选项标题或用户输入。</summary>
public sealed record AskUserTranscriptEntry(string Question, string Answer);

/// <summary>把 ask_user 的参数与结果 JSON 投影为友好问答；解析失败返回 null，由界面显示简短状态。</summary>
internal static class AskUserTranscript
{
    private const string DetailPrefix = "参数：";
    private const string PendingAnswer = "…";
    private const string CancelledAnswer = "已取消";
    private const string SkippedAnswer = "已跳过";

    public static IReadOnlyList<AskUserTranscriptEntry>? TryCreate(string? detail, string? resultText, ChatSegmentStatus status)
    {
        if (string.IsNullOrWhiteSpace(detail)) return null;
        var argumentsText = detail;
        if (argumentsText.StartsWith(DetailPrefix, StringComparison.Ordinal))
        {
            var newline = argumentsText.IndexOf('\n');
            if (newline < 0 || newline + 1 >= argumentsText.Length) return null;
            argumentsText = argumentsText[(newline + 1)..];
        }
        JsonDocument? result = null;
        try
        {
            // 失败状态可能在参数后追加错误摘要，只读取首个完整 JSON 对象。
            var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(argumentsText));
            using var arguments = JsonDocument.ParseValue(ref reader);
            if (!string.IsNullOrWhiteSpace(resultText)) result = JsonDocument.Parse(resultText);
            var root = arguments.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (result is not null && result.RootElement.ValueKind != JsonValueKind.Object) return null;

            var cancelled = result is not null
                && result.RootElement.TryGetProperty("cancelled", out var cancelledValue)
                && cancelledValue.ValueKind == JsonValueKind.True;
            var hasValues = result is not null
                && result.RootElement.TryGetProperty("values", out _)
                && result.RootElement.GetProperty("values").ValueKind == JsonValueKind.Object;
            var hasCustomInputs = result is not null
                && result.RootElement.TryGetProperty("custom_inputs", out _)
                && result.RootElement.GetProperty("custom_inputs").ValueKind == JsonValueKind.Object;

            var fields = ReadFields(root);
            if (fields is null || fields.Count == 0) return null;

            var entries = new List<AskUserTranscriptEntry>(fields.Count);
            foreach (var field in fields)
            {
                entries.Add(new AskUserTranscriptEntry(field.Label, ResolveAnswer(field, result, cancelled, hasValues, hasCustomInputs, status)));
            }
            return entries;
        }
        catch (JsonException) { return null; }
        finally { result?.Dispose(); }
    }

    private static List<(string Id, string Label, Dictionary<string, string> OptionLabels)>? ReadFields(JsonElement root)
    {
        if (!root.TryGetProperty("fields", out var fieldsNode) || fieldsNode.ValueKind != JsonValueKind.Array)
        {
            // 旧协议：单个文本回答字段，问题取整体 question。
            if (!root.TryGetProperty("question", out var question) || question.ValueKind != JsonValueKind.String) return null;
            return [("answer", question.GetString() ?? string.Empty, new Dictionary<string, string>())];
        }
        var fields = new List<(string, string, Dictionary<string, string>)>();
        foreach (var fieldNode in fieldsNode.EnumerateArray())
        {
            if (fieldNode.ValueKind != JsonValueKind.Object
                || !fieldNode.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String
                || !fieldNode.TryGetProperty("label", out var label) || label.ValueKind != JsonValueKind.String)
                return null;
            var optionLabels = new Dictionary<string, string>(StringComparer.Ordinal);
            if (fieldNode.TryGetProperty("options", out var options) && options.ValueKind == JsonValueKind.Array)
            {
                foreach (var option in options.EnumerateArray())
                {
                    if (option.ValueKind == JsonValueKind.Object
                        && option.TryGetProperty("id", out var optionId) && optionId.ValueKind == JsonValueKind.String
                        && option.TryGetProperty("label", out var optionLabel) && optionLabel.ValueKind == JsonValueKind.String)
                        optionLabels[optionId.GetString()!] = optionLabel.GetString()!;
                }
            }
            fields.Add((id.GetString()!, label.GetString()!, optionLabels));
        }
        return fields;
    }

    private static string ResolveAnswer(
        (string Id, string Label, Dictionary<string, string> OptionLabels) field,
        JsonDocument? result, bool cancelled, bool hasValues, bool hasCustomInputs, ChatSegmentStatus status)
    {
        if (cancelled || status == ChatSegmentStatus.Cancelled) return CancelledAnswer;
        if (status == ChatSegmentStatus.Failed) return "未能完成提问";
        if (result is null) return status == ChatSegmentStatus.Completed ? "未收到回答" : PendingAnswer;
        if (!hasValues) return "未收到回答";

        var answers = new List<string>();
        if (result.RootElement.GetProperty("values").TryGetProperty(field.Id, out var value))
        {
            string Label(JsonElement item)
            {
                if (item.ValueKind != JsonValueKind.String) return item.GetRawText();
                var text = item.GetString()!;
                return field.OptionLabels.TryGetValue(text, out var label) ? label : text;
            }
            if (value.ValueKind == JsonValueKind.Array)
                answers.AddRange(value.EnumerateArray().Select(Label));
            else if (value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                answers.Add(Label(value));
        }
        if (hasCustomInputs
            && result.RootElement.GetProperty("custom_inputs").TryGetProperty(field.Id, out var custom)
            && custom.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(custom.GetString()))
            answers.Add(custom.GetString()!);
        var answer = string.Join("、", answers.Where(text => !string.IsNullOrWhiteSpace(text)));
        return answer.Length == 0 ? SkippedAnswer : answer;
    }
}
