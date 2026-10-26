using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DiskPilotX.Core.Services.Scanning;

internal static class NtfsUsnNative
{
    // CreateFile flags
    internal const uint GENERIC_READ = 0x80000000;
    internal const uint FILE_SHARE_READ = 0x00000001;
    internal const uint FILE_SHARE_WRITE = 0x00000002;
    internal const uint FILE_SHARE_DELETE = 0x00000004;
    internal const uint OPEN_EXISTING = 3;
    internal const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

    // FSCTL codes
    internal const uint FSCTL_QUERY_USN_JOURNAL = 0x000900F4;
    internal const uint FSCTL_READ_USN_JOURNAL = 0x000900BB;
    internal const uint FSCTL_ENUM_USN_DATA = 0x000900B3;

    // USN_REASON flags
    internal const uint USN_REASON_DATA_OVERWRITE = 0x00000001;
    internal const uint USN_REASON_DATA_EXTEND = 0x00000002;
    internal const uint USN_REASON_DATA_TRUNCATION = 0x00000004;
    internal const uint USN_REASON_NAMED_DATA_OVERWRITE = 0x00000010;
    internal const uint USN_REASON_NAMED_DATA_EXTEND = 0x00000020;
    internal const uint USN_REASON_NAMED_DATA_TRUNCATION = 0x00000040;
    internal const uint USN_REASON_FILE_CREATE = 0x00000100;
    internal const uint USN_REASON_FILE_DELETE = 0x00000200;
    internal const uint USN_REASON_RENAME_OLD_NAME = 0x00001000;
    internal const uint USN_REASON_RENAME_NEW_NAME = 0x00002000;
    internal const uint USN_REASON_BASIC_INFO_CHANGE = 0x00008000;
    internal const uint USN_REASON_CLOSE = 0x80000000;

    internal const uint USN_REASON_MODIFY_MASK =
        USN_REASON_DATA_OVERWRITE | USN_REASON_DATA_EXTEND | USN_REASON_DATA_TRUNCATION |
        USN_REASON_NAMED_DATA_OVERWRITE | USN_REASON_NAMED_DATA_EXTEND | USN_REASON_NAMED_DATA_TRUNCATION;

    // Win32 error codes
    internal const int ERROR_JOURNAL_ENTRY_DELETED = 1181;
    internal const int ERROR_HANDLE_EOF = 38;

    [StructLayout(LayoutKind.Sequential)]
    internal struct USN_JOURNAL_DATA_V1
    {
        public ulong UsnJournalID;
        public long FirstUsn;
        public long NextUsn;
        public long LowestValidUsn;
        public long MaxUsn;
        public ulong MaximumSize;
        public ulong AllocationDelta;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct READ_USN_JOURNAL_DATA_V0
    {
        public long StartUsn;
        public uint ReasonMask;
        public uint ReturnOnlyOnClose;
        public ulong Timeout;
        public ulong BytesToWaitFor;
        public ulong UsnJournalID;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MFT_ENUM_DATA_V0
    {
        public ulong StartFileReferenceNumber;
        public long LowUsn;
        public long HighUsn;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct BY_HANDLE_FILE_INFORMATION
    {
        public uint dwFileAttributes;
        public uint ftCreationTimeLow;
        public uint ftCreationTimeHigh;
        public uint ftLastAccessTimeLow;
        public uint ftLastAccessTimeHigh;
        public uint ftLastWriteTimeLow;
        public uint ftLastWriteTimeHigh;
        public uint dwVolumeSerialNumber;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint nNumberOfLinks;
        public uint nFileIndexHigh;
        public uint nFileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        nint lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        nint hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        ref READ_USN_JOURNAL_DATA_V0 lpInBuffer,
        int nInBufferSize,
        nint lpOutBuffer,
        int nOutBufferSize,
        out int lpBytesReturned,
        nint lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        nint lpInBuffer,
        int nInBufferSize,
        out USN_JOURNAL_DATA_V1 lpOutBuffer,
        int nOutBufferSize,
        out int lpBytesReturned,
        nint lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        ref MFT_ENUM_DATA_V0 lpInBuffer,
        int nInBufferSize,
        nint lpOutBuffer,
        int nOutBufferSize,
        out int lpBytesReturned,
        nint lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetFileInformationByHandle(
        SafeFileHandle hFile,
        out BY_HANDLE_FILE_INFORMATION lpFileInformation);

    /// <summary>
    /// Opens a volume handle (e.g. \\.\C:) for journal queries.
    /// </summary>
    internal static SafeFileHandle OpenVolume(string volumeRoot)
    {
        // volumeRoot is like "C:\" — we need "\\.\C:"
        var driveLetter = volumeRoot.TrimEnd('\\');
        var volumePath = @"\\.\" + driveLetter;
        return CreateFileW(
            volumePath,
            GENERIC_READ,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            0,
            OPEN_EXISTING,
            0,
            0);
    }

    /// <summary>
    /// Opens a directory handle for GetFileInformationByHandle.
    /// </summary>
    internal static SafeFileHandle OpenDirectory(string path)
    {
        return CreateFileW(
            path,
            0, // no access rights needed — just metadata
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            0,
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS,
            0);
    }

    /// <summary>
    /// Gets the file reference number (FRN) for a directory.
    /// Returns 0 on failure.
    /// </summary>
    internal static long GetFileReferenceNumber(string directoryPath)
    {
        using var handle = OpenDirectory(directoryPath);
        if (handle.IsInvalid)
            return 0;

        if (!GetFileInformationByHandle(handle, out var info))
            return 0;

        return (long)(((ulong)info.nFileIndexHigh << 32) | info.nFileIndexLow);
    }
}
