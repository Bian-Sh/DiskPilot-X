using System.Text.Json;
using System.Text.RegularExpressions;
using DiskPilotX.Core.Services.Skills;

namespace DiskPilotX.Core.Services.Analysis;

public sealed record AnalysisScanContext(Guid ScanId, string ScopePath, string Source, string? ConversationId,
    string? MessageId, string Goal, IReadOnlyList<string> Preserve, string PrivacySignature, string? RuleId = null, string? RuleVersion = null);
public sealed record AnalysisPreference(Guid Id, string ScopePath, string Text, bool Enabled, DateTimeOffset UpdatedAt);

/// <summary>最小本地记录：任务来源、确认偏好和自愿启用的行为线索；不读取历史或操作用户文件。</summary>
public sealed class AnalysisContextStore(string directory, TimeProvider? clock = null)
{
    private readonly string file = Path.Combine(Path.GetFullPath(directory), "analysis-context.json");
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private readonly object gate = new();
    private static readonly Regex SafeId = new("^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant);
    public string? Warning { get; private set; }
    private sealed record Usage(string Topic, string Action, DateTimeOffset At);
    private sealed record Prompt(string Topic, string Action, DateTimeOffset At, bool Never);
    private sealed class Data
    {
        public bool UsageRecordingEnabled { get; set; }
        public List<AnalysisScanContext> Scans { get; set; } = [];
        public List<AnalysisPreference> Preferences { get; set; } = [];
        public List<Usage> Uses { get; set; } = [];
        public List<Prompt> Prompts { get; set; } = [];
    }
    private Data Load()
    {
        Warning = null;
        if (!File.Exists(file)) return new();
        try
        {
            CleanupSkillSafety.EnsureNoReparsePoints(file);
            if (new FileInfo(file).Length > 2 * 1024 * 1024 || (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("记录过大或不是普通文件。");
            var data = JsonSerializer.Deserialize<Data>(File.ReadAllText(file)) ?? throw new JsonException();
            if (data.Scans is null || data.Preferences is null || data.Uses is null || data.Prompts is null || data.Scans.Any(x => x is null) || data.Preferences.Any(x => x is null) || data.Uses.Any(x => x is null) || data.Prompts.Any(x => x is null)) throw new JsonException();
            return data;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        { Warning = "本地分析记录不可读取，已暂停应用相关上下文；原文件未改写。"; return new(); }
    }
    private void Change(Action<Data> change)
    {
        lock (gate)
        {
            var data = Load();
            if (Warning is not null) throw new InvalidDataException(Warning);
            change(data);
            var dir = Path.GetDirectoryName(file)!;
            CleanupSkillSafety.EnsureNoReparsePoints(dir);
            Directory.CreateDirectory(dir);
            if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) throw new UnauthorizedAccessException("记录目录不能是重解析点。");
            data.Uses.RemoveAll(x => x.At < clock.GetUtcNow().AddDays(-30));
            var text = JsonSerializer.Serialize(data, UserDataJson.Options);
            if (text.Length > 1_000_000) throw new InvalidDataException("记录容量已达到限制，请清除不再需要的记录。");
            var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temp, text); File.Move(temp, file, true); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
    private static string Scope(string scope)
    {
        if (!Path.IsPathFullyQualified(scope)) throw new ArgumentException("需要明确的本地绝对范围。");
        return Path.GetFullPath(scope);
    }
    private static void Topic(string topic, string action)
    {
        if (!SafeId.IsMatch(topic) || action is not ("query" or "analysis" or "cleanup")) throw new ArgumentException("行为记录只接受公开规范化主题和已知动作，不接受路径或原文。");
    }
    public void SaveScan(AnalysisScanContext context)
    {
        _ = Scope(context.ScopePath);
        if (context.ScanId == Guid.Empty || context.Source is not ("manual" or "chat" or "rule")) throw new ArgumentException("来源记录无效。");
        if (context.Goal.Length > 1000 || context.Preserve.Count > 100 || context.Preserve.Any(x => x.Length > 500)) throw new ArgumentException("来源摘要超出限制。");
        Change(data => { data.Scans.RemoveAll(x => x.ScanId == context.ScanId); data.Scans.Add(context); if (data.Scans.Count > 200) data.Scans.RemoveAt(0); });
    }
    public AnalysisScanContext? ReadScan(Guid scanId, string privacySignature, Func<string, bool>? canReadConversation = null)
    {
        lock (gate)
        {
            var result = Load().Scans.LastOrDefault(x => x.ScanId == scanId && x.PrivacySignature == privacySignature);
            if (result?.ConversationId is { } id)
            {
                try { if (canReadConversation?.Invoke(id) != true) return null; }
                catch { return null; }
            }
            return result;
        }
    }
    public AnalysisPreference SavePreference(string scopePath, string text)
    {
        scopePath = Scope(scopePath);
        if (string.IsNullOrWhiteSpace(text) || text.Length > 2000) throw new ArgumentException("偏好需要简短、明确的内容。");
        var result = new AnalysisPreference(Guid.NewGuid(), scopePath, text.Trim(), true, clock.GetUtcNow());
        Change(data => { data.Preferences.RemoveAll(x => x.ScopePath.Equals(scopePath, StringComparison.OrdinalIgnoreCase) && x.Text == result.Text); data.Preferences.Add(result); });
        return result;
    }
    public IReadOnlyList<AnalysisPreference> ListPreferences() { lock (gate) return Load().Preferences.ToArray(); }
    public IReadOnlyList<AnalysisPreference> GetPreferences(string scopePath) => ListPreferences().Where(x => x.Enabled && AnalysisTaskContext.IsWithin(scopePath, x.ScopePath)).ToArray();
    public void DeletePreference(Guid id) => Change(data => data.Preferences.RemoveAll(x => x.Id == id));
    public void SetPreferenceEnabled(Guid id, bool enabled) => Change(data => { for (var i = 0; i < data.Preferences.Count; i++) if (data.Preferences[i].Id == id) data.Preferences[i] = data.Preferences[i] with { Enabled = enabled, UpdatedAt = clock.GetUtcNow() }; });
    public void UpdatePreference(Guid id, string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 2000) throw new ArgumentException("偏好内容无效。");
        Change(data => { for (var i = 0; i < data.Preferences.Count; i++) if (data.Preferences[i].Id == id) data.Preferences[i] = data.Preferences[i] with { Text = text.Trim(), UpdatedAt = clock.GetUtcNow() }; });
    }
    public bool UsageRecordingEnabled { get { lock (gate) return Load().UsageRecordingEnabled; } }
    public void SetUsageRecording(bool enabled) => Change(data => data.UsageRecordingEnabled = enabled);
    public void ClearUsage() => Change(data => { data.Uses.Clear(); data.Prompts.RemoveAll(x => !x.Never); });
    public void RecordUsage(string topic, string action, bool success)
    {
        Topic(topic, action);
        if (!success || !UsageRecordingEnabled) return;
        Change(data => { if (data.UsageRecordingEnabled) { data.Uses.Add(new(topic, action, clock.GetUtcNow())); if (data.Uses.Count > 1000) data.Uses.RemoveAt(0); } });
    }
    public bool ShouldSuggestShortcut(string topic, string action)
    {
        Topic(topic, action);
        lock (gate)
        {
            var d = Load(); var cutoff = clock.GetUtcNow().AddDays(-30);
            return d.UsageRecordingEnabled && !d.Prompts.Any(x => x.Topic == topic && (x.Never || x.At > cutoff)) && d.Uses.Count(x => x.Topic == topic && x.Action == action && x.At >= cutoff) >= 3;
        }
    }
    public void MarkShortcutOffered(string topic, string action) { Topic(topic, action); Change(d => d.Prompts.Add(new(topic, action, clock.GetUtcNow(), false))); }
    public void DismissShortcut(string topic, string action, bool never) { Topic(topic, action); Change(d => d.Prompts.Add(new(topic, action, clock.GetUtcNow(), never))); }
    public void ResetShortcutDismissal(string topic, string action) { Topic(topic, action); Change(d => d.Prompts.RemoveAll(x => x.Topic == topic)); }
}
