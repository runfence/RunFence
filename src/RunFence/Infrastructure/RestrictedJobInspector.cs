using RunFence.Core;

namespace RunFence.Infrastructure;

public sealed class RestrictedJobInspector(
    IProcessQueryHandleProvider processQueryHandleProvider,
    IVerifiedRestrictedJobCache verifiedRestrictedJobCache,
    IJobObjectApi jobObjectApi,
    ILoggingService log) : IRestrictedJobInspector
{
    public bool IsProcessInHandleLimitedJob(int pid)
    {
        if (pid <= 0)
            return false;

        try
        {
            if (!processQueryHandleProvider.TryOpenProcessForQuery((uint)pid, out var processHandle))
                return false;

            using (processHandle)
            {
                if (jobObjectApi.IsProcessInJob(processHandle.DangerousGetHandle(), IntPtr.Zero) != true)
                    return false;

                return verifiedRestrictedJobCache.CheckMembership(processHandle)
                    == VerifiedRestrictedJobMembershipResult.Match;
            }
        }
        catch (Exception ex)
        {
            log.Warn($"RestrictedJobInspector: failed to inspect pid {pid}: {ex.Message}");
            return false;
        }
    }
}
