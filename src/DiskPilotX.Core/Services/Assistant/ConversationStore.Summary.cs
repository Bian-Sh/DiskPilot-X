using System.Text.Json;
using DiskPilotX.Core.Enums;
namespace DiskPilotX.Core.Services.Assistant;

public sealed partial class ConversationStore
{
    // 展示摘要独立于授权元数据；不可作为隐私授权依据。
    private sealed record ListMetadata(ConversationSummary Summary,long SourceLength,DateTime SourceWriteTimeUtc);
    private static ConversationSummary BuildSummary(ConversationSnapshot snapshot)
    {
        var first=snapshot.Messages.FirstOrDefault(m=>m.Sender==ChatSender.User)?.Text??snapshot.Messages.FirstOrDefault()?.Text??"";
        var title=snapshot.Title??string.Join(' ',first.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries));
        if(snapshot.Title is null && title.Length>40)title=title[..40]+"…";
        return new(snapshot.Id,title,snapshot.UpdatedAt,snapshot.Messages.Count);
    }
    private void SaveSummary(ConversationSnapshot snapshot)
    {
        var target=Path.ChangeExtension(PathFor(snapshot.Id),".summary");
        var temporary=target+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            var source=new FileInfo(PathFor(snapshot.Id));
            File.WriteAllText(temporary,JsonSerializer.Serialize(new ListMetadata(BuildSummary(snapshot),source.Length,source.LastWriteTimeUtc),JsonOptions));
            File.Move(temporary,target,true);
        }
        catch(Exception ex) when(IsStorageError(ex)) { }
        finally { try { File.Delete(temporary); } catch(Exception ex) when(IsStorageError(ex)) { } }
    }
    private ConversationSummary? ReadSummary(string file)
    {
        var id=Path.GetFileNameWithoutExtension(file);
        try
        {
            var metadata=JsonSerializer.Deserialize<ListMetadata>(File.ReadAllText(Path.ChangeExtension(file,".summary")),MetadataJsonOptions);
            var info=new FileInfo(file);
            if(metadata?.Summary is { } summary && summary.Id==id && metadata.SourceLength==info.Length
                && metadata.SourceWriteTimeUtc==info.LastWriteTimeUtc && summary.MessageCount>0 && !string.IsNullOrWhiteSpace(summary.Title))
                return summary;
        }
        catch(Exception ex) when(IsStorageError(ex)) { }
        var snapshot=Load(id);
        SaveSummary(snapshot);
        return BuildSummary(snapshot);
    }
}
