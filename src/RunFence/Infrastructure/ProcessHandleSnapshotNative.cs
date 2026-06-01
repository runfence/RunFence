using Microsoft.Win32.SafeHandles;

namespace RunFence.Infrastructure;

public sealed class ProcessHandleSnapshotNative : IProcessHandleSnapshotNative
{
    public int QueryProcessHandleInformation(
        SafeProcessHandle ownerProcessHandle,
        IntPtr buffer,
        int bufferSize,
        out int returnLength)
    {
        ArgumentNullException.ThrowIfNull(ownerProcessHandle);
        return ProcessHandleNative.NtQueryInformationProcess(
            ownerProcessHandle.DangerousGetHandle(),
            ProcessHandleNative.ProcessHandleInformation,
            buffer,
            bufferSize,
            out returnLength);
    }

    public bool DuplicateSameAccess(
        SafeProcessHandle ownerProcessHandle,
        IntPtr sourceHandle,
        out IntPtr duplicatedHandle)
    {
        ArgumentNullException.ThrowIfNull(ownerProcessHandle);
        return JobNative.DuplicateHandle(
            ownerProcessHandle.DangerousGetHandle(),
            sourceHandle,
            ProcessNative.GetCurrentProcess(),
            out duplicatedHandle,
            0,
            false,
            SystemHandleNative.DuplicateSameAccess);
    }

    public bool DuplicateWithAccess(
        SafeProcessHandle ownerProcessHandle,
        IntPtr sourceHandle,
        uint desiredAccess,
        out IntPtr duplicatedHandle)
    {
        ArgumentNullException.ThrowIfNull(ownerProcessHandle);
        return JobNative.DuplicateHandle(
            ownerProcessHandle.DangerousGetHandle(),
            sourceHandle,
            ProcessNative.GetCurrentProcess(),
            out duplicatedHandle,
            desiredAccess,
            false,
            0);
    }
}
