using Microsoft.Win32.SafeHandles;

namespace RunFence.Infrastructure;

public interface IVerifiedRestrictedJobCache
{
    bool TryAddDuplicate(IntPtr jobHandle);
    VerifiedRestrictedJobMembershipResult CheckMembership(SafeProcessHandle processHandle);
    void SweepEmptyOrInvalidJobs();
}
