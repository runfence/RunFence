using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using RunFence.Infrastructure;

namespace RunFence.Apps.Shortcuts;

public sealed class ShortcutDestinationNativeApi : IShortcutDestinationNativeApi
{
    public SafeFileHandle Open(
        string path,
        uint desiredAccess,
        uint shareMode,
        uint creationDisposition,
        uint flagsAndAttributes)
    {
        var handle = FileSecurityNative.CreateFile(
            path,
            desiredAccess,
            shareMode,
            IntPtr.Zero,
            creationDisposition,
            flagsAndAttributes,
            IntPtr.Zero);
        if (handle == FileSecurityNative.INVALID_HANDLE_VALUE)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        return new SafeFileHandle(handle, ownsHandle: true);
    }

    public FileSecurityNative.BY_HANDLE_FILE_INFORMATION GetFileInformation(SafeFileHandle handle)
    {
        if (!FileSecurityNative.GetFileInformationByHandle(handle.DangerousGetHandle(), out var info))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        return info;
    }

    public void SetDeleteDisposition(SafeFileHandle handle, FileSecurityNative.FILE_DISPOSITION_FLAGS flags)
    {
        var deleteInfoEx = new FileSecurityNative.FILE_DISPOSITION_INFO_EX
        {
            Flags = flags
        };
        if (!FileSecurityNative.SetFileInformationByHandle(
                handle.DangerousGetHandle(),
                FileSecurityNative.FILE_INFO_BY_HANDLE_CLASS.FileDispositionInfoEx,
                ref deleteInfoEx,
                (uint)Marshal.SizeOf<FileSecurityNative.FILE_DISPOSITION_INFO_EX>()))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public void SetBasicInfo(SafeFileHandle handle, FileSecurityNative.FILE_BASIC_INFO basicInfo)
    {
        if (!FileSecurityNative.SetFileInformationByHandle(
                handle.DangerousGetHandle(),
                FileSecurityNative.FILE_INFO_BY_HANDLE_CLASS.FileBasicInfo,
                ref basicInfo,
                (uint)Marshal.SizeOf<FileSecurityNative.FILE_BASIC_INFO>()))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }
}
