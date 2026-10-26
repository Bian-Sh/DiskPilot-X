using System.Runtime.InteropServices;
using DiskPilotX.Core.Models;

namespace DiskPilotX.Core.Services.Scanning;

internal enum UsnChangeKind
{
    Create,
    Delete,
    Modify,
    Rename
}

internal record UsnChange(
    long FileReferenceNumber,
    long ParentFileReferenceNumber,
    string FileName,
    UsnChangeKind Kind,
    bool IsDirectory,
    uint FileAttributes,
    long OldParentFileReferenceNumber = 0,
    string? OldFileName = null);

internal static class UsnJournalReader
{
    // USN_RECORD_V2 fixed-size header fields
    private const int RecordLengthOffset = 0;     // DWORD
    private const int MajorVersionOffset = 4;      // WORD
    private const int FileReferenceNumberOffset = 8;  // DWORDLONG (8 bytes)
    private const int ParentFrnOffset = 16;        // DWORDLONG (8 bytes)
    private const int ReasonOffset = 40;           // DWORD
    private const int FileAttributesOffset = 52;   // DWORD
    private const int FileNameLengthOffset = 56;   // WORD
    private const int FileNameOffsetOffset = 58;   // WORD
    // FileName starts at offset 60 in V2

    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;

    /// <summary>
    /// Queries the USN journal on the volume containing the given root path.
    /// Returns null if the volume is not NTFS or the journal is not active.
    /// </summary>
    internal static UsnWatermark? QueryJournal(string volumeRoot)
    {
        using var volumeHandle = NtfsUsnNative.OpenVolume(volumeRoot);
        if (volumeHandle.IsInvalid)
            return null;

        var ok = NtfsUsnNative.DeviceIoControl(
            volumeHandle,
            NtfsUsnNative.FSCTL_QUERY_USN_JOURNAL,
            0,
            0,
            out NtfsUsnNative.USN_JOURNAL_DATA_V1 journalData,
            Marshal.SizeOf<NtfsUsnNative.USN_JOURNAL_DATA_V1>(),
            out _,
            0);

        if (!ok)
            return null;

        return new UsnWatermark(journalData.UsnJournalID, journalData.NextUsn, volumeRoot);
    }

    /// <summary>
    /// Reads all USN changes since the given watermark.
    /// Returns null if the journal was purged or the journal ID changed (caller should do full rescan).
    /// </summary>
    internal static List<UsnChange>? ReadChanges(UsnWatermark watermark, CancellationToken ct)
    {
        using var volumeHandle = NtfsUsnNative.OpenVolume(watermark.VolumeRoot);
        if (volumeHandle.IsInvalid)
            return null;

        var reasonMask =
            NtfsUsnNative.USN_REASON_FILE_CREATE |
            NtfsUsnNative.USN_REASON_FILE_DELETE |
            NtfsUsnNative.USN_REASON_MODIFY_MASK |
            NtfsUsnNative.USN_REASON_RENAME_OLD_NAME |
            NtfsUsnNative.USN_REASON_RENAME_NEW_NAME |
            NtfsUsnNative.USN_REASON_BASIC_INFO_CHANGE |
            NtfsUsnNative.USN_REASON_CLOSE;

        var readData = new NtfsUsnNative.READ_USN_JOURNAL_DATA_V0
        {
            StartUsn = watermark.NextUsn,
            ReasonMask = reasonMask,
            ReturnOnlyOnClose = 0,
            Timeout = 0,
            BytesToWaitFor = 0,
            UsnJournalID = watermark.JournalId
        };

        const int bufferSize = 65536;
        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            var rawRecords = new List<RawUsnRecord>();

            while (!ct.IsCancellationRequested)
            {
                var ok = NtfsUsnNative.DeviceIoControl(
                    volumeHandle,
                    NtfsUsnNative.FSCTL_READ_USN_JOURNAL,
                    ref readData,
                    Marshal.SizeOf<NtfsUsnNative.READ_USN_JOURNAL_DATA_V0>(),
                    buffer,
                    bufferSize,
                    out var bytesReturned,
                    0);

                if (!ok)
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == NtfsUsnNative.ERROR_JOURNAL_ENTRY_DELETED)
                        return null; // Journal purged — full rescan needed
                    return null;     // Any other error — full rescan
                }

                // First 8 bytes of output is the next USN to continue from
                if (bytesReturned <= 8)
                    break; // No more records

                var nextUsn = Marshal.ReadInt64(buffer);
                var offset = 8;

                while (offset < bytesReturned)
                {
                    var recordLength = Marshal.ReadInt32(buffer + offset);
                    if (recordLength == 0)
                        break;

                    ParseRecord(buffer + offset, rawRecords);
                    offset += recordLength;
                }

                // If nextUsn hasn't moved, we've read everything
                if (nextUsn == readData.StartUsn)
                    break;

                readData.StartUsn = nextUsn;
            }

            return Deduplicate(rawRecords);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void ParseRecord(nint recordPtr, List<RawUsnRecord> records)
    {
        var majorVersion = Marshal.ReadInt16(recordPtr + MajorVersionOffset);
        if (majorVersion != 2)
            return; // Only handle V2 records

        var frn = Marshal.ReadInt64(recordPtr + FileReferenceNumberOffset);
        var parentFrn = Marshal.ReadInt64(recordPtr + ParentFrnOffset);
        var reason = (uint)Marshal.ReadInt32(recordPtr + ReasonOffset);
        var fileAttributes = (uint)Marshal.ReadInt32(recordPtr + FileAttributesOffset);
        var fileNameLength = Marshal.ReadInt16(recordPtr + FileNameLengthOffset);
        var fileNameOffset = Marshal.ReadInt16(recordPtr + FileNameOffsetOffset);

        var fileName = Marshal.PtrToStringUni(recordPtr + fileNameOffset, fileNameLength / 2) ?? string.Empty;
        var isDirectory = (fileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;

        records.Add(new RawUsnRecord(frn, parentFrn, fileName, reason, isDirectory, fileAttributes));
    }

    /// <summary>
    /// Deduplicates raw USN records by FileReferenceNumber.
    /// Multiple records per file collapse to the net effect.
    /// </summary>
    private static List<UsnChange> Deduplicate(List<RawUsnRecord> rawRecords)
    {
        // Group by FRN — take the last record's name/parent but OR all reasons together.
        // Preserve the first parent FRN and name so renames can locate the entry by old location.
        var grouped = new Dictionary<long, (long FirstParentFrn, string FirstFileName, long LastParentFrn, string LastFileName, uint CombinedReason, bool IsDirectory, uint FileAttributes)>();

        foreach (var rec in rawRecords)
        {
            if (grouped.TryGetValue(rec.Frn, out var existing))
            {
                grouped[rec.Frn] = (existing.FirstParentFrn, existing.FirstFileName, rec.ParentFrn, rec.FileName, existing.CombinedReason | rec.Reason, rec.IsDirectory, rec.FileAttributes);
            }
            else
            {
                grouped[rec.Frn] = (rec.ParentFrn, rec.FileName, rec.ParentFrn, rec.FileName, rec.Reason, rec.IsDirectory, rec.FileAttributes);
            }
        }

        var changes = new List<UsnChange>(grouped.Count);

        foreach (var (frn, entry) in grouped)
        {
            var kind = ClassifyChange(entry.CombinedReason);
            var change = new UsnChange(frn, entry.LastParentFrn, entry.LastFileName, kind, entry.IsDirectory, entry.FileAttributes,
                OldParentFileReferenceNumber: entry.FirstParentFrn,
                OldFileName: entry.FirstFileName);
            changes.Add(change);
        }

        return changes;
    }

    private static UsnChangeKind ClassifyChange(uint combinedReason)
    {
        // If both created and deleted, net effect is nothing — treat as delete
        // (the file was created then deleted within the window)
        var created = (combinedReason & NtfsUsnNative.USN_REASON_FILE_CREATE) != 0;
        var deleted = (combinedReason & NtfsUsnNative.USN_REASON_FILE_DELETE) != 0;
        var renamed = (combinedReason & NtfsUsnNative.USN_REASON_RENAME_NEW_NAME) != 0;

        if (deleted)
            return UsnChangeKind.Delete;
        if (created)
            return UsnChangeKind.Create;
        if (renamed)
            return UsnChangeKind.Rename;
        return UsnChangeKind.Modify;
    }

    private record RawUsnRecord(long Frn, long ParentFrn, string FileName, uint Reason, bool IsDirectory, uint FileAttributes);
}
