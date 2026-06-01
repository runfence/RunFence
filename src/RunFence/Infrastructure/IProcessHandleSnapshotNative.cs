using Microsoft.Win32.SafeHandles;

namespace RunFence.Infrastructure;

public interface IProcessHandleSnapshotNative
{
    int QueryProcessHandleInformation(
        SafeProcessHandle ownerProcessHandle,
        IntPtr buffer,
        int bufferSize,
        out int returnLength);

    bool DuplicateSameAccess(
        SafeProcessHandle ownerProcessHandle,
        IntPtr sourceHandle,
        out IntPtr duplicatedHandle);

    bool DuplicateWithAccess(
        SafeProcessHandle ownerProcessHandle,
        IntPtr sourceHandle,
        uint desiredAccess,
        out IntPtr duplicatedHandle);
}
