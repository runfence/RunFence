using Microsoft.Win32.SafeHandles;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.ForegroundMarker;

public sealed class ForegroundProcessJobInspector(
    IProcessQueryHandleProvider processQueryHandleProvider,
    IJobObjectApi jobObjectApi,
    IVerifiedRestrictedJobCache verifiedRestrictedJobCache,
    ILoggingService log)
{
    public ForegroundProcessJobInspectionResult TryIsIsolated(uint pid)
    {
        if (!processQueryHandleProvider.TryOpenProcessForQuery(pid, out var processHandle))
        {
            log.Debug($"ForegroundProcessJobInspector: pid {pid} unknown; OpenProcess for query failed.");
            return ForegroundProcessJobInspectionResult.Unknown;
        }

        using (processHandle)
        {
            var inAnyJob = jobObjectApi.IsProcessInJob(processHandle.DangerousGetHandle(), IntPtr.Zero);
            if (!inAnyJob.HasValue)
            {
                log.Debug($"ForegroundProcessJobInspector: pid {pid} unknown; IsProcessInJob failed.");
                return ForegroundProcessJobInspectionResult.Unknown;
            }

            if (!inAnyJob.Value)
                return ForegroundProcessJobInspectionResult.NotInAnyJob;

            var membership = verifiedRestrictedJobCache.CheckMembership(processHandle);
            var result = membership switch
            {
                VerifiedRestrictedJobMembershipResult.Match => ForegroundProcessJobInspectionResult.Isolated,
                VerifiedRestrictedJobMembershipResult.NoMatch => ForegroundProcessJobInspectionResult.NotInAnyJob,
                _ => ForegroundProcessJobInspectionResult.Unknown,
            };
            log.Debug($"ForegroundProcessJobInspector: pid {pid} job membership {membership}; result {result}.");
            return result;
        }
    }
}
