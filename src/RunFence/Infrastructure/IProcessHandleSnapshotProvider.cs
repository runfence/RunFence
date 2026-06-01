using Microsoft.Win32.SafeHandles;

namespace RunFence.Infrastructure;

public interface IProcessHandleSnapshotProvider
{
    IReadOnlyList<OwnedJobHandle> GetJobHandleCandidates(SafeProcessHandle ownerProcessHandle);
}
