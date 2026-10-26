using Microsoft.Extensions.Logging;
using System.Diagnostics;
using DiskPilotX.Core.Models;

namespace DiskPilotX.Core.Services.Scanning;

public partial class IncrementalFileScanner
{
    private void CaptureWatermark(ScanSession session, string fullPath, string volumeRoot)
    {
        var watermark = UsnJournalReader.QueryJournal(volumeRoot);
        if (watermark == null)
        {
            _logger.LogWarning("QueryJournal returned null for {VolumeRoot}; incremental rescan unavailable", volumeRoot);
            return;
        }

        _logger.LogInformation("USN journal captured: journalId={JournalId}, nextUsn={NextUsn}", watermark.JournalId, watermark.NextUsn);

        var frnIndex = BuildFrnIndex(session.RootEntry!);

        _logger.LogInformation("FRN index built: {DirectoryCount} directories indexed", frnIndex.Count);

        _volumeStates[volumeRoot] = new VolumeScanState
        {
            ScannedPath = fullPath,
            CachedSession = session,
            Watermark = watermark,
            FrnIndex = frnIndex
        };
    }

    /// <summary>
    /// Walks the FileEntry tree and gets FRNs for all directory entries.
    /// </summary>
    private static Dictionary<long, FileEntry> BuildFrnIndex(FileEntry root)
    {
        var index = new Dictionary<long, FileEntry>();
        var stack = new Stack<FileEntry>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var entry = stack.Pop();
            if (entry.IsDirectory)
            {
                var frn = NtfsUsnNative.GetFileReferenceNumber(entry.Path);
                if (frn != 0)
                {
                    entry.FileReferenceNumber = frn;
                    index[frn] = entry;
                }

                foreach (var child in entry.Children)
                {
                    if (child.IsDirectory)
                        stack.Push(child);
                }
            }
        }

        return index;
    }

}
