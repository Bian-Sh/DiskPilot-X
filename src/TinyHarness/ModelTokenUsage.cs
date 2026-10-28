namespace TinyHarness;

/// <summary>接口报告的用量；null 表示未提供，零表示明确报告为零。</summary>
public sealed record ModelTokenUsage(long? InputTokens = null, long? OutputTokens = null,
    long? TotalTokens = null, long? CachedInputTokens = null, long? CacheCreationTokens = null,
    long? ReasoningTokens = null, long? TotalInputTokens = null)
{
    public ModelTokenUsage Add(ModelTokenUsage other) => new(Sum(InputTokens, other.InputTokens),
        Sum(OutputTokens, other.OutputTokens), Sum(TotalTokens, other.TotalTokens),
        Sum(CachedInputTokens, other.CachedInputTokens), Sum(CacheCreationTokens, other.CacheCreationTokens),
        Sum(ReasoningTokens, other.ReasoningTokens),
        TotalInputTokens.HasValue || other.TotalInputTokens.HasValue ? Sum(GetTotalInputTokens(), other.GetTotalInputTokens()) : null);

    /// <summary>旧记录仅有总量与输出时仍可获得准确输入，不能混合累计后漏掉这部分分母。</summary>
    public long? GetTotalInputTokens() => TotalInputTokens
        ?? (TotalTokens is { } total && OutputTokens is { } output && output >= 0 && total >= output ? total - output : null);

    private static long? Sum(long? left, long? right) => left.HasValue || right.HasValue
        ? left.GetValueOrDefault() + right.GetValueOrDefault() : null;
}
