namespace SpaceMonger.Core.Services.Skills;

internal static class CleanupSkillPreviewer
{
    internal static string ValidateScope(string scopePath)
    {
        if (!Path.IsPathFullyQualified(scopePath) || scopePath.StartsWith(@"\\") || scopePath.IndexOf(':', 2) >= 0)
            throw new ArgumentException("预览需要明确的本地绝对目录，不接受网络路径或数据流。");
        var scope = Path.GetFullPath(scopePath);
        CleanupSkillSafety.EnsureNoReparsePoints(scope);
        if (!Directory.Exists(scope)) throw new DirectoryNotFoundException("选定范围不存在。");
        return scope;
    }

    internal static CleanupSkillPreview Preview(CleanupSkillPackage package, string scopePath, CancellationToken token)
    {
        var scope = ValidateScope(scopePath);
        var candidates = new List<CleanupSkillCandidate>();
        var warnings = new List<string>();
        var pending = new Stack<string>(); pending.Push(scope);
        var visited = 0;
        while (pending.TryPop(out var directory))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                CleanupSkillSafety.EnsureNoReparsePoints(directory);
                foreach (var path in Directory.EnumerateFileSystemEntries(directory))
                {
                    token.ThrowIfCancellationRequested();
                    if (++visited > 200_000) throw new InvalidDataException("预览超出 200000 项限制，请缩小范围。");
                    try
                    {
                        CleanupSkillSafety.EnsureWithin(scope, path);
                        var attributes = File.GetAttributes(path);
                        if ((attributes & FileAttributes.ReparsePoint) != 0) { warnings.Add("已跳过重解析点：" + path); continue; }
                        if ((attributes & FileAttributes.Directory) != 0) { pending.Push(path); continue; }
                        var relative = Path.GetRelativePath(scope, path).Replace('\\', '/');
                        var matches = package.Rules.Select(rule => (Rule: rule, Root: MatchRoot(relative, rule.Path))).Where(m => m.Root is not null).ToArray();
                        if (matches.Length == 0) continue;
                        if (matches.Any(m => IsExcluded(relative, m.Root!, m.Rule))) continue;
                        var rule = matches[0].Rule;
                        CleanupSkillSafety.EnsureNoReparsePoints(path);
                        candidates.Add(new(rule.Id, path, new FileInfo(path).Length, rule.Description, rule.Risk, rule.Sources));
                        if (candidates.Count > 20_000) throw new InvalidDataException("候选超过 20000 项，请缩小范围。");
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    { warnings.Add("无法读取，已跳过：" + path + "；" + ex.Message); }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { warnings.Add("无法读取目录：" + directory + "；" + ex.Message); }
        }
        return new(package.Id, package.Digest, scope, candidates, warnings.Distinct().Take(200).ToArray());
    }

    private static string? MatchRoot(string relative, string pattern)
    {
        if (CleanupSkillSafety.Matches(relative, pattern)) return relative;
        for (var slash = relative.LastIndexOf('/'); slash > 0; slash = relative.LastIndexOf('/', slash - 1))
            if (CleanupSkillSafety.Matches(relative[..slash], pattern)) return relative[..slash];
        return null;
    }

    private static bool IsExcluded(string relative, string matchedRoot, CleanupSkillRule rule)
    {
        var within = relative.Length > matchedRoot.Length ? relative[(matchedRoot.Length + 1)..] : relative;
        var exclusions = rule.Excludes.Concat(rule.Preserve).ToArray();
        return CleanupSkillSafety.IsExcluded(relative, exclusions) || CleanupSkillSafety.IsExcluded(within, exclusions);
    }

    internal static CleanupSkillCandidate VerifySourceCandidate(CleanupSkillCandidate candidate, string scope, CleanupSkillPackage package)
    {
        if (candidate is null) throw new InvalidDataException("源码返回空候选。");
        if (!Path.IsPathFullyQualified(candidate.Path)) throw new InvalidDataException("源码候选必须为范围内绝对路径。");
        var path = Path.GetFullPath(candidate.Path);
        CleanupSkillSafety.EnsureWithin(scope, path); CleanupSkillSafety.EnsureNoReparsePoints(path);
        // 输出只接受实际文件，目录需由检测器展开；忽略源码给出的估算大小。
        if (!File.Exists(path)) throw new InvalidDataException("源码候选不是现存文件。");
        var rule = package.Rules.FirstOrDefault(r => r.Id == candidate.RuleId);
        if (rule is null && package.Rules.Count > 0) throw new InvalidDataException("源码候选引用未知规则 ID。");
        if (string.IsNullOrWhiteSpace(candidate.RuleId) || string.IsNullOrWhiteSpace(candidate.Description) || string.IsNullOrWhiteSpace(candidate.Risk) || candidate.Sources is null || candidate.Sources.Count == 0)
            throw new InvalidDataException("源码候选缺少规则、风险或来源。");
        var relative = Path.GetRelativePath(scope, path).Replace('\\', '/');
        if (rule is not null && MatchRoot(relative, rule.Path) is null) throw new InvalidDataException("源码候选路径不符合其对应规则。");
        foreach (var item in package.Rules)
        {
            var matchedRoot = MatchRoot(relative, item.Path);
            if (matchedRoot is not null && IsExcluded(relative, matchedRoot, item)) throw new InvalidDataException("源码候选命中保留项。");
        }
        return candidate with { Path = path, SizeBytes = new FileInfo(path).Length };
    }
}
