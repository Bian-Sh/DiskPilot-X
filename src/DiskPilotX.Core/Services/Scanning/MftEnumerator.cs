using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

namespace DiskPilotX.Core.Services.Scanning;

internal record MftRecord(
    long FileReferenceNumber,
    long ParentFileReferenceNumber,
    string FileName,
    uint FileAttributes,
    bool IsDirectory);

internal static class MftEnumerator
{
    // USN_RECORD_V2 field offsets (same as UsnJournalReader)
    private const int RecordLengthOffset = 0;
    private const int MajorVersionOffset = 4;
    private const int FileReferenceNumberOffset = 8;
    private const int ParentFrnOffset = 16;
    private const int FileAttributesOffset = 52;
    private const int FileNameLengthOffset = 56;
    private const int FileNameOffsetOffset = 58;

    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    private const int BufferSize = 1024 * 1024; // 1MB 鈥?large sequential reads
    private const int ProgressInterval = 50_000;

    // NTFS FRN layout: lower 48 bits = MFT segment number, upper 16 bits = sequence number.
    private const long SegmentNumberMask = 0x0000_FFFF_FFFF_FFFF;

    /// <summary>
    /// Enumerates all MFT records on the volume via FSCTL_ENUM_USN_DATA.
    /// Returns null on any failure (non-NTFS, permission denied, etc.).
    /// </summary>
    internal static Dictionary<long, MftRecord>? EnumerateVolume(
        string volumeRoot,
        IProgress<ScanProgress> progress,
        CancellationToken ct,
        ILogger? logger = null)
    {
        return EnumerateVolumeCore(volumeRoot, progress, ct, directoriesOnly: false, logger);
    }

    internal static Dictionary<long, MftRecord>? EnumerateDirectories(
        string volumeRoot,
        IProgress<ScanProgress> progress,
        CancellationToken ct,
        ILogger? logger = null)
    {
        return EnumerateVolumeCore(volumeRoot, progress, ct, directoriesOnly: true, logger);
    }

    private static Dictionary<long, MftRecord>? EnumerateVolumeCore(
        string volumeRoot,
        IProgress<ScanProgress> progress,
        CancellationToken ct,
        bool directoriesOnly,
        ILogger? logger)
    {
        try
        {
            using var volumeHandle = NtfsUsnNative.OpenVolume(volumeRoot);
            if (volumeHandle.IsInvalid)
                return null;

            // Query journal to get MaxUsn
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

            var enumData = new NtfsUsnNative.MFT_ENUM_DATA_V0
            {
                StartFileReferenceNumber = 0,
                LowUsn = 0,
                HighUsn = long.MaxValue
            };

            var buffer = Marshal.AllocHGlobal(BufferSize);
            try
            {
                var records = new Dictionary<long, MftRecord>();
                var count = 0;
                var scannedCount = 0;
                var fileCount = 0;
                var folderCount = 0;

                while (!ct.IsCancellationRequested)
                {
                    var success = NtfsUsnNative.DeviceIoControl(
                        volumeHandle,
                        NtfsUsnNative.FSCTL_ENUM_USN_DATA,
                        ref enumData,
                        Marshal.SizeOf<NtfsUsnNative.MFT_ENUM_DATA_V0>(),
                        buffer,
                        BufferSize,
                        out var bytesReturned,
                        0);

                    if (!success)
                    {
                        var error = Marshal.GetLastWin32Error();
                        if (error == NtfsUsnNative.ERROR_HANDLE_EOF)
                            break; // Done 鈥?enumerated all MFT records
                        logger?.LogWarning("FSCTL_ENUM_USN_DATA failed: win32 error {Error}, records so far: {Count}", error, count);
                        return null; // Unexpected error
                    }

                    if (bytesReturned <= 8)
                        break;

                    // First 8 bytes: next StartFileReferenceNumber
                    var nextFrn = (ulong)Marshal.ReadInt64(buffer);
                    var offset = 8;

                    while (offset < bytesReturned)
                    {
                        var recordLength = Marshal.ReadInt32(buffer + offset);
                        if (recordLength == 0)
                            break;

                        scannedCount++;
                        var record = ParseRecord(buffer + offset, directoriesOnly);
                        if (record != null)
                        {
                            records[record.FileReferenceNumber] = record;
                            count++;
                            if (record.IsDirectory) folderCount++;
                            else fileCount++;
                        }

                        if (scannedCount % ProgressInterval == 0)
                        {
                            progress.Report(new ScanProgress(
                                "Reading file system",
                                fileCount, folderCount));
                        }

                        offset += recordLength;
                    }

                    enumData.StartFileReferenceNumber = nextFrn;
                }

                if (ct.IsCancellationRequested)
                    return null;

                logger?.LogInformation("MFT enumerated {RecordCount} records", records.Count);
                return records;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "MFT EnumerateVolume failed");
            return null;
        }
    }

    private static MftRecord? ParseRecord(nint recordPtr, bool directoriesOnly)
    {
        var majorVersion = Marshal.ReadInt16(recordPtr + MajorVersionOffset);
        if (majorVersion != 2)
            return null;

        var frn = Marshal.ReadInt64(recordPtr + FileReferenceNumberOffset);
        var parentFrn = Marshal.ReadInt64(recordPtr + ParentFrnOffset);
        var fileAttributes = (uint)Marshal.ReadInt32(recordPtr + FileAttributesOffset);
        var isDirectory = (fileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
        if (directoriesOnly && !isDirectory)
            return null;

        var fileNameLength = Marshal.ReadInt16(recordPtr + FileNameLengthOffset);
        var fileNameOffset = Marshal.ReadInt16(recordPtr + FileNameOffsetOffset);

        // Skip NTFS metadata files (segment 0-4 and 6-23), except segment 5 (root directory).
        // The full FRN includes a sequence number in the upper 16 bits 鈥?mask it off.
        var segmentNumber = frn & SegmentNumberMask;
        if (segmentNumber < 24 && segmentNumber != 5)
            return null;

        var fileName = Marshal.PtrToStringUni(recordPtr + fileNameOffset, fileNameLength / 2)
            ?? string.Empty;

        // Skip alternate data streams
        if (fileName.Contains(':'))
            return null;

        return new MftRecord(frn, parentFrn, fileName, fileAttributes, isDirectory);
    }
}
