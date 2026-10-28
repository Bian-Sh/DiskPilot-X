using System.IO;
using System.Text;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Analysis;

namespace DiskPilotX.App.Services;

/// <summary>
/// 一次多范围无头分析的批次。多个扫描根目录在同一批次里并存，互不覆盖：
/// 用户一次勾了 2 个游戏时，两个目录各自扫描、各自校验，最后合并成一份清单发布到「推荐清理」。
/// 主工作区的 Treemap / 文件树不会被替换——无头扫描只产出数据，不动视图。
/// </summary>
public sealed class AssistantAnalysisBatch
{
    public Guid Id { get; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
    public string? Note { get; init; }
    public List<AssistantAnalysisScope> Scopes { get; } = [];
    public List<string> Errors { get; } = [];

    /// <summary>各范围根挂在一个批次根下，供清理前按路径定位条目（FindByPath 是逐条比对，不依赖公共前缀）。</summary>
    public ScanSession ToSession()
    {
        var root = new FileEntry
        {
            Name = BuildLabel(),
            Path = CommonRoot(),
            IsDirectory = true,
            Size = Scopes.Sum(scope => scope.Root.Size),
            SubtreeFileCount = Scopes.Sum(scope => scope.Root.SubtreeFileCount),
            SubtreeFolderCount = Scopes.Sum(scope => scope.Root.SubtreeFolderCount),
            LastModified = Scopes.Max(scope => scope.Root.LastModified)
        };
        foreach (var scope in Scopes)
        {
            scope.Root.Parent = root;
            root.Children.Add(scope.Root);
        }
        return new ScanSession
        {
            Id = Id,
            TargetPath = root.Path,
            RootEntry = root,
            TotalFiles = root.SubtreeFileCount,
            TotalFolders = root.SubtreeFolderCount,
            TotalSize = root.Size,
            StartTime = CreatedAt.LocalDateTime,
            EndTime = DateTime.Now
        };
    }

    private string BuildLabel() => Scopes.Count == 1
        ? Scopes[0].Label
        : (string.IsNullOrWhiteSpace(Note) ? $"{Scopes.Count} 个位置" : Note!);

    /// <summary>所有范围的最长公共目录；跨盘时退回首个范围根，仅用于展示。</summary>
    private string CommonRoot()
    {
        if (Scopes.Count == 0) return string.Empty;
        var first = Path.GetFullPath(Scopes[0].Path);
        if (Scopes.Count == 1) return first;
        var candidate = first;
        foreach (var scope in Scopes.Skip(1))
        {
            while (!IsPrefixOf(candidate, Path.GetFullPath(scope.Path)) && candidate.Length > 0)
            {
                var parent = Directory.GetParent(candidate);
                if (parent is null) { candidate = string.Empty; break; }
                candidate = parent.FullName;
            }
            if (candidate.Length == 0) break;
        }
        return candidate.Length == 0 ? first : candidate;
    }

    private static bool IsPrefixOf(string prefix, string path)
    {
        var normalizedPrefix = prefix.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(normalizedPrefix, path, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(normalizedPrefix + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>批次里的一个扫描范围：一个根目录 + 它自己的扫描会话。</summary>
public sealed class AssistantAnalysisScope
{
    public required string Path { get; init; }
    public string Label { get; init; } = "";
    public required ScanSession Session { get; init; }
    public FileEntry Root => Session.RootEntry!;
}

/// <summary>批次证据的拼装：每个范围一段，带上编号与路径，模型可逐段给出候选。</summary>
public static class AssistantAnalysisEvidence
{
    private const int MaxEvidencePerScope = 12_000;

    public static string Build(RecommendationEngine engine, AssistantAnalysisBatch batch) =>
        Build(engine, batch, MaxEvidencePerScope, int.MaxValue);

    /// <summary>
    /// 带上限的版本。证据要塞回用户消息时（宿主自己跑完多选批次，再把结果交给模型）
    /// 必须收敛长度，否则 6 个范围能把上下文撑爆。
    /// </summary>
    public static string Build(RecommendationEngine engine, AssistantAnalysisBatch batch, int maxPerScope, int maxTotal) =>
        BuildCore(engine, batch, maxPerScope, maxTotal);

    private static string BuildCore(RecommendationEngine engine, AssistantAnalysisBatch batch, int maxPerScope, int maxTotal)
    {
        var builder = new StringBuilder();
        builder.Append("本次共 ").Append(batch.Scopes.Count).Append(" 个分析范围，逐范围给出清理候选，最后一次性发布全部候选。\n\n");
        var budget = maxTotal;
        for (var index = 0; index < batch.Scopes.Count; index++)
        {
            var scope = batch.Scopes[index];
            builder.Append("### 范围 ").Append(index + 1).Append('/').Append(batch.Scopes.Count)
                   .Append("：").Append(scope.Label).Append('\n')
                   .Append("路径：").Append(scope.Path).Append('\n')
                   .Append("规模：").Append(scope.Session.TotalFiles).Append(" 个文件 / ")
                   .Append(scope.Session.TotalFolders).Append(" 个文件夹 / ")
                   .Append(scope.Session.TotalSize).Append(" 字节\n\n");
            budget -= builder.Length;
            var evidence = engine.BuildEvidence(scope.Session, scope.Root);
            // 剩余预算不足时至少保留一段可读证据，不够的部分明确标注已截断。
            var allowance = Math.Clamp(Math.Min(maxPerScope, budget), 500, maxPerScope);
            builder.AppendLine(string.IsNullOrWhiteSpace(evidence)
                ? "（该范围内没有可读到的分析证据）"
                : Truncate(evidence, allowance)).Append('\n');
            budget -= allowance;
            if (budget <= 0 && index < batch.Scopes.Count - 1)
            {
                builder.AppendLine("…（后续范围证据已达长度上限，仍可针对它们给出候选）");
                break;
            }
        }
        if (batch.Errors.Count > 0)
        {
            builder.Append("以下范围未能扫描：\n");
            foreach (var error in batch.Errors) builder.Append("- ").AppendLine(error);
        }
        return builder.ToString();
    }

    private static string Truncate(string text) => Truncate(text, MaxEvidencePerScope);

    private static string Truncate(string text, int limit) =>
        text.Length <= limit ? text : text[..limit] + "\n…（证据已截断，仍可针对该范围给出候选）";
}
