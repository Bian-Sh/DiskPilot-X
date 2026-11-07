using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using DiskPilotX.Core.Enums;

namespace DiskPilotX.Core.Services.Cleanup;

/// <summary>应用批准后只进入 Windows 执行阶段；使用现代系统进度窗口，不再次确认删除。</summary>
internal static class SystemDeleteProgress
{
    internal static uint GetOperationFlags(DeletionMode mode)
    {
        // 保留进度，不静默；禁止扩大到关联目录，也不递归跟随重解析点。
        const uint flags = 0x0010 | 0x2000 | 0x8000; // FOF_NOCONFIRMATION / NO_CONNECTED_ELEMENTS / NORECURSEREPARSE
        return mode switch
        {
            DeletionMode.PermanentDelete => flags,
            DeletionMode.MoveToRecycleBin => flags | 0x0040 | 0x00080000, // FOF_ALLOWUNDO / FOFX_RECYCLEONDELETE
            _ => throw new InvalidOperationException("不支持的删除方式。")
        };
    }

    internal static void Delete(string path, DeletionMode mode)
    {
        // IFileOperation 必须在 STA 使用；调用者仍在既有后台逐项指纹复核链中。
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { DeleteCore(Path.GetFullPath(path), mode); }
            catch (Exception error) { failure = error; }
        }) { IsBackground = true, Name = "DiskPilot-X-系统删除进度" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void DeleteCore(string path, DeletionMode mode)
    {
        IFileOperation? operation = null;
        IShellItem? item = null;
        try
        {
            operation = (IFileOperation)new FileOperation();
            Marshal.ThrowExceptionForHR(operation.SetOperationFlags(GetOperationFlags(mode)));
            var iid = typeof(IShellItem).GUID;
            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out item));
            Marshal.ThrowExceptionForHR(operation.DeleteItem(item, IntPtr.Zero));
            var result = operation.PerformOperations();
            Marshal.ThrowExceptionForHR(operation.GetAnyOperationsAborted(out var aborted));
            if (aborted || result == unchecked((int)0x800704C7) || result == unchecked((int)0x80004004))
                throw new OperationCanceledException("用户已在系统删除进度窗口取消操作。");
            Marshal.ThrowExceptionForHR(result);
            if (File.Exists(path) || Directory.Exists(path))
                throw new IOException("系统删除未完成，目标仍然存在：" + path);
        }
        finally
        {
            if (item is not null) Marshal.FinalReleaseComObject(item);
            if (operation is not null) Marshal.FinalReleaseComObject(operation);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr binding, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

    [ComImport, Guid("3AD05575-8857-4850-9277-11B85BDB8E09")]
    private class FileOperation { }

    // 只作为系统 Shell 项目的 COM 契约标识使用，不自行遍历 Shell 名称空间。
    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem { }

    // COM 方法顺序必须与 Windows SDK 一致；未使用的入口仍保留其 vtable 位置。
    [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        [PreserveSig] int Advise(IntPtr sink, out uint cookie);
        [PreserveSig] int Unadvise(uint cookie);
        [PreserveSig] int SetOperationFlags(uint flags);
        [PreserveSig] int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        [PreserveSig] int SetProgressDialog(IntPtr dialog);
        [PreserveSig] int SetProperties(IntPtr properties);
        [PreserveSig] int SetOwnerWindow(IntPtr owner);
        [PreserveSig] int ApplyPropertiesToItem(IShellItem item);
        [PreserveSig] int ApplyPropertiesToItems(IntPtr items);
        [PreserveSig] int RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr sink);
        [PreserveSig] int RenameItems(IntPtr items, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int MoveItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? name, IntPtr sink);
        [PreserveSig] int MoveItems(IntPtr items, IShellItem destination);
        [PreserveSig] int CopyItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? name, IntPtr sink);
        [PreserveSig] int CopyItems(IntPtr items, IShellItem destination);
        [PreserveSig] int DeleteItem(IShellItem item, IntPtr sink);
        [PreserveSig] int DeleteItems(IntPtr items);
        [PreserveSig] int NewItem(IShellItem destination, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name,
            [MarshalAs(UnmanagedType.LPWStr)] string? template, IntPtr sink);
        [PreserveSig] int PerformOperations();
        [PreserveSig] int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }
}