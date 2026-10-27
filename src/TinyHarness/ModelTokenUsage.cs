namespace TinyHarness;

/// <summary>接口报告的用量；null 表示未提供，零表示明确报告为零。</summary>
public sealed record ModelTokenUsage(long? InputTokens = null, long? OutputTokens = null,
    long? TotalTokens = null, long? CachedInputTokens = null, long? CacheCreationTokens = null,
    long? ReasoningTokens = null)
{
    public ModelTokenUsage Add(ModelTokenUsage other) => new(Sum(InputTokens, other.InputTokens),
        Sum(OutputTokens, other.OutputTokens), Sum(TotalTokens, other.TotalTokens),
        Sum(CachedInputTokens, other.CachedInputTokens), Sum(CacheCreationTokens, other.CacheCreationTokens),
        Sum(ReasoningTokens, other.ReasoningTokens));

    private static long? Sum(long? left, long? right) => left.HasValue || right.HasValue
        ? left.GetValueOrDefault() + right.GetValueOrDefault() : null;
}
