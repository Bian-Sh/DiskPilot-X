using System.Text.Json;
using TinyHarness;

namespace DiskPilotX.Core.Services.Llm;

/// <summary>只读取已报告的非负整数；缺失或无效统计不影响正文。</summary>
internal static class ModelUsageParser
{
    public static ModelTokenUsage? Read(JsonElement container, bool anthropic = false, ModelTokenUsage? previous = null)
    {
        if (container.ValueKind != JsonValueKind.Object || !container.TryGetProperty("usage", out var usage)
            || usage.ValueKind != JsonValueKind.Object) return previous;
        var input = Number(usage, anthropic ? "input_tokens" : "prompt_tokens") ?? Number(usage, "input_tokens") ?? previous?.InputTokens;
        var output = Number(usage, anthropic ? "output_tokens" : "completion_tokens") ?? Number(usage, "output_tokens") ?? previous?.OutputTokens;
        var cached = Number(usage, "cache_read_input_tokens") ?? Number(usage, "prompt_cache_hit_tokens")
            ?? Detail(usage, "prompt_tokens_details", "cached_tokens") ?? Detail(usage, "input_tokens_details", "cached_tokens") ?? previous?.CachedInputTokens;
        var created = Number(usage, "cache_creation_input_tokens") ?? previous?.CacheCreationTokens;
        var reasoning = Detail(usage, "completion_tokens_details", "reasoning_tokens") ?? Detail(usage, "output_tokens_details", "reasoning_tokens") ?? previous?.ReasoningTokens;
        // 统一缓存分母：Anthropic 的 input_tokens 不包含缓存读取与写入。
        long? totalInput = input.HasValue ? input.Value + (anthropic ? cached.GetValueOrDefault() + created.GetValueOrDefault() : 0) : null;
        // 总量将缓存读写各计入一次。
        var total = Number(usage, "total_tokens") ?? (input.HasValue && output.HasValue
            ? input.Value + output.Value + (anthropic ? cached.GetValueOrDefault() + created.GetValueOrDefault() : 0) : (long?)null);
        return input.HasValue || output.HasValue || cached.HasValue || created.HasValue || reasoning.HasValue || total.HasValue
            ? new(input, output, total, cached, created, reasoning, totalInput) : previous;
    }

    private static long? Number(JsonElement element, string key) => element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var number) && number >= 0 ? number : null;
    private static long? Detail(JsonElement element, string key, string field) => element.TryGetProperty(key, out var details) ? Number(details, field) : null;
}
