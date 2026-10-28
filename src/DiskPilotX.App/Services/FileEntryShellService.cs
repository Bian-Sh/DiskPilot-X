using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using DiskPilotX.Core.Models;

namespace DiskPilotX.App.Services;

public static class FileEntryShellService
{
    public static void Open(FileEntry entry)
    {
        EnsureAvailable(entry);
        if (entry.IsDirectory)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = entry.Path,
                UseShellExecute = true,
                Verb = "open"
            });
            return;
        }

        if (!File.Exists(entry.Path))
        {
            throw new FileNotFoundException("File does not exist.", entry.Path);
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = entry.Path,
                UseShellExecute = true
            });
        }
        catch
        {
            OpenWith(entry);
        }
    }

    public static void OpenWith(FileEntry entry)
    {
        if (entry.IsDirectory)
        {
            Open(entry);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "rundll32.exe",
            Arguments = $"shell32.dll,OpenAs_RunDLL \"{entry.Path}\"",
            UseShellExecute = true
        });
    }

    public static void ShowInExplorer(FileEntry entry)
    {
        EnsureAvailable(entry);
        var path = Path.GetFullPath(entry.Path);
        Marshal.ThrowExceptionForHR(SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0, out _));
        try
        {
            Marshal.ThrowExceptionForHR(SHOpenFolderAndSelectItems(pidl, 0, IntPtr.Zero, 0));
        }
        finally { Marshal.FreeCoTaskMem(pidl); }
    }

    private static void EnsureAvailable(FileEntry entry)
    {
        if (entry.IsDeleted) throw new InvalidOperationException("该项目已删除，请重新扫描。");
        if (!File.Exists(entry.Path) && !Directory.Exists(entry.Path))
            throw new FileNotFoundException("目标不存在，请重新扫描。", entry.Path);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bindContext, out IntPtr pidl, uint attributes, out uint foundAttributes);

    [DllImport("shell32.dll")]
    private static extern int SHOpenFolderAndSelectItems(IntPtr pidl, uint count, IntPtr items, uint flags);

    public static void ShowProperties(FileEntry entry)
    {
        EnsureAvailable(entry);
        var info = new ShellExecuteInfo
        {
            cbSize = Marshal.SizeOf<ShellExecuteInfo>(),
            lpVerb = "properties",
            lpFile = entry.Path,
            nShow = 5,
            fMask = 0x0000000C
        };

        if (!ShellExecuteEx(ref info))
        {
            throw new InvalidOperationException($"ShellExecuteEx failed with code {Marshal.GetLastWin32Error()}.");
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ShellExecuteEx(ref ShellExecuteInfo lpExecInfo);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public string? lpVerb;
        public string? lpFile;
        public string? lpParameters;
        public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }
}

