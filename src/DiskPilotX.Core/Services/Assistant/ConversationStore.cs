using System.Text.Json;
using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Models;
using TinyHarness;
using Message = DiskPilotX.Core.Models.ChatMessage;
using ModelMessage = TinyHarness.ChatMessage;

namespace DiskPilotX.Core.Services.Assistant;

public sealed record ConversationSummary(string Id, string Title, DateTimeOffset UpdatedAt, int MessageCount)
{
    public bool IsCurrent { get; init; }
    public string UpdatedAtText => UpdatedAt.LocalDateTime.ToString("g");
}

public sealed record SavedChatSegment(ChatSegmentKind Kind, string Text, string? Detail, string? ToolName,
    string? ToolCallId, ChatSegmentStatus Status, bool IsSummary, bool IsFinal = false);

/// <summary>只保存展示数据，不序列化磁盘对象、交互确认或凭据配置。</summary>
public sealed record SavedChatMessage(Guid Id, ChatSender Sender, string Text, string Thinking, DateTime Timestamp,
    string CompletedAtText, bool IsError, IReadOnlyList<SavedChatSegment> Segments,
    DateTime? CompletedAt = null, ChatSegmentStatus? ProcessStatus = null, ModelTokenUsage? Usage = null, string? ModelId = null, bool HasIncompleteCacheUsage = false)
{
    public static SavedChatMessage Capture(Message message) => new(message.Id, message.Sender, message.Text,
        message.Thinking, message.Timestamp, message.CompletedAtText, message.IsError,
        message.Segments.Select(segment => new SavedChatSegment(segment.Kind, segment.Text, segment.Detail,
            segment.ToolName, segment.ToolCallId, segment.Status, segment.IsSummary, segment.IsFinal)).ToArray(),
        message.CompletedAt, message.ProcessStatus, message.Usage, message.ModelId, message.HasIncompleteCacheUsage);

    public Message Restore(ChatSegmentStatus? sessionStatus = null)
    {
        var message = new Message { Id = Id, Sender = Sender, Text = Text, Thinking = Thinking,
            Timestamp = Timestamp, CompletedAtText = CompletedAtText, IsError = IsError, Usage = Usage, ModelId = ModelId, HasIncompleteCacheUsage = HasIncompleteCacheUsage };
        foreach (var segment in Segments)
            message.Segments.Add(new ChatSegment(segment.Kind) { Text = segment.Text, Detail = segment.Detail,
                ToolName = segment.ToolName, ToolCallId = segment.ToolCallId, IsSummary = segment.IsSummary, IsFinal = segment.IsFinal,
                Status = segment.Status is ChatSegmentStatus.Running or ChatSegmentStatus.WaitingForUser
                    ? ChatSegmentStatus.Cancelled : segment.Status });
        var status = ProcessStatus ?? (IsError ? ChatSegmentStatus.Failed
            : sessionStatus ?? (message.Segments.Any(segment => segment.Status == ChatSegmentStatus.Cancelled)
                ? ChatSegmentStatus.Cancelled : ChatSegmentStatus.Completed));
        message.RestoreCompletion(CompletedAt, status is ChatSegmentStatus.Running or ChatSegmentStatus.WaitingForUser
            ? ChatSegmentStatus.Cancelled : status);
        return message;
    }
}

public sealed record ConversationSnapshot(string Id, DateTimeOffset UpdatedAt, string? PrivacySignature,
    AgentSessionState State, IReadOnlyList<ModelMessage> ModelMessages, IReadOnlyList<SavedChatMessage> Messages)
{
    public int Version { get; init; } = 1;
    public string? Title { get; init; }
}

/// <summary>用于授权前核验的最小元数据，不包含对话正文。</summary>
public sealed record ConversationMetadata(string Id, string? PrivacySignature, long SourceLength, DateTime SourceWriteTimeUtc);

/// <summary>每会话一个本地快照；先写同目录临时文件，再原子替换，避免中断破坏上一份记录。</summary>
public sealed class ConversationStore(string rootDirectory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private static readonly JsonSerializerOptions MetadataJsonOptions = new() { RespectRequiredConstructorParameters = true };

    public void Save(ConversationSnapshot snapshot)
    {
        Validate(snapshot, snapshot.Id);
        if (snapshot.Messages.Count == 0) return;
        Directory.CreateDirectory(rootDirectory);
        var target = PathFor(snapshot.Id);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        ConversationMetadata metadata;
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot, JsonOptions));
            // 从本次写入的临时文件取属性，避免绑定到其他保存操作替换后的正文。
            var source = new FileInfo(temporary);
            metadata = new(snapshot.Id, snapshot.PrivacySignature, source.Length, source.LastWriteTimeUtc);
            File.Move(temporary, target, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        SaveMetadata(metadata);
    }

    /// <summary>只读取侧文件与源文件属性；旧快照不迁移，无法核验时拒绝访问。</summary>
    public ConversationMetadata? ReadMetadata(string id)
    {
        var sourcePath = PathFor(id);
        try
        {
            var metadata = JsonSerializer.Deserialize<ConversationMetadata>(
                File.ReadAllText(Path.ChangeExtension(sourcePath, ".metadata")), MetadataJsonOptions);
            if (metadata is null || metadata.Id != id) return null;
            var source = new FileInfo(sourcePath);
            return source.Exists && source.Length == metadata.SourceLength
                && source.LastWriteTimeUtc == metadata.SourceWriteTimeUtc ? metadata : null;
        }
        catch (Exception exception) when (IsStorageError(exception)) { return null; }
    }

    private void SaveMetadata(ConversationMetadata metadata)
    {
        var target = Path.ChangeExtension(PathFor(metadata.Id), ".metadata");
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(metadata, JsonOptions));
            File.Move(temporary, target, overwrite: true);
        }
        catch (Exception exception) when (IsStorageError(exception))
        {
            // 正文已保存，侧文件失败不改变原保存行为；尽可能移除旧元数据。
            try { File.Delete(target); }
            catch (Exception cleanupException) when (IsStorageError(cleanupException)) { }
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception exception) when (IsStorageError(exception)) { }
        }
    }

    public void Delete(string id)
    {
        var path = PathFor(id);
        // 先移除授权元数据；正文删除失败时仍保留会话，不会误报删除成功。
        File.Delete(Path.ChangeExtension(path, ".metadata"));
        File.Delete(path);
    }

    public ConversationSnapshot Load(string id)
    {
        var snapshot = JsonSerializer.Deserialize<ConversationSnapshot>(File.ReadAllText(PathFor(id)), JsonOptions)
            ?? throw new InvalidDataException("历史对话内容为空。");
        Validate(snapshot, id);
        return snapshot;
    }

    public IReadOnlyList<ConversationSummary> List()
    {
        if (!Directory.Exists(rootDirectory)) return [];
        var summaries = new List<ConversationSummary>();
        foreach (var file in Directory.EnumerateFiles(rootDirectory, "*.json"))
        {
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out _)) continue;
            try
            {
                var snapshot = Load(Path.GetFileNameWithoutExtension(file));
                if (snapshot.Messages.Count == 0) continue;
                // 对话可能由应用代发消息（如「分析」按钮）开始，取不到用户消息时回退到任意消息。
                var first = snapshot.Messages.FirstOrDefault(message => message.Sender == ChatSender.User)?.Text
                    ?? snapshot.Messages.FirstOrDefault()?.Text
                    ?? "";
                var title = snapshot.Title ?? string.Join(' ', first.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                if (snapshot.Title is null && title.Length > 40) title = title[..40] + "…";
                summaries.Add(new(snapshot.Id, title, snapshot.UpdatedAt, snapshot.Messages.Count));
            }
            catch (Exception exception) when (IsStorageError(exception)) { /* 单个损坏文件不影响其他会话。 */ }
        }
        return summaries.OrderByDescending(summary => summary.UpdatedAt).ToArray();
    }

    public static bool IsStorageError(Exception exception) => exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException;

    private string PathFor(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("历史对话标识无效。");
        return Path.Combine(rootDirectory, id + ".json");
    }

    private static void Validate(ConversationSnapshot snapshot, string id)
    {
        if (snapshot.Version != 1 || snapshot.Id != id || snapshot.Messages is null || snapshot.ModelMessages is null
            || !Enum.IsDefined(snapshot.State)
            || snapshot.Messages.Any(message => message is null || message.Text is null || message.Thinking is null
                || !Enum.IsDefined(message.Sender) || message.Segments is null
                || message.Segments.Any(segment => segment is null || segment.Text is null
                    || !Enum.IsDefined(segment.Kind) || !Enum.IsDefined(segment.Status)))
            || snapshot.ModelMessages.Any(message => message is null || !Enum.IsDefined(message.Role)
                || message.ToolCalls is null || message.Blocks is null
                || message.ToolCalls.Any(call => call is null)
                || message.Blocks.Any(block => block is null)))
            throw new InvalidDataException("历史对话格式不完整或版本不受支持。");
    }
}
