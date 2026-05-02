using System.Security.Principal;
using RunFence.Core.Models;

namespace RunFence.Infrastructure;

public sealed class JobKeeperJobVerifier(IJobObjectApi jobObjectApi) : IJobKeeperJobVerifier
{
    private const int GenericAllAccessMask = 0x10000000;
    private const int MappedJobFullControlMask = 0x001F003F;

    public JobKeeperJobVerificationResult Verify(JobKeeperInstanceIdentity identity, int keeperPid)
    {
        var jobHandle = jobObjectApi.OpenJobObject(ProcessJobManager.JobObjectReconnectAccess, false, identity.JobName);
        if (jobHandle == IntPtr.Zero)
            return JobKeeperJobVerificationResult.Failure($"named job '{identity.JobName}' could not be opened.");

        try
        {
            var pids = jobObjectApi.QueryProcessIds(jobHandle);
            if (pids?.Contains(keeperPid) != true)
                return CloseAndFail(jobHandle, $"keeper PID {keeperPid} is not assigned to job '{identity.JobName}'.");

            var uiRestrictions = jobObjectApi.QueryUiRestrictions(jobHandle);
            if (!uiRestrictions.HasValue
                || (uiRestrictions.Value & ProcessJobManager.UiRestrictionFlags) != ProcessJobManager.UiRestrictionFlags)
            {
                return CloseAndFail(
                    jobHandle,
                    $"job UI restrictions mismatch: expected mask 0x{ProcessJobManager.UiRestrictionFlags:X}, actual {(uiRestrictions.HasValue ? $"0x{uiRestrictions.Value:X}" : "<unavailable>")}.");
            }

            var securityFailure = GetJobSecurityFailure(jobObjectApi.GetSecuritySnapshot(jobHandle));
            if (securityFailure != null)
                return CloseAndFail(jobHandle, securityFailure);

            return JobKeeperJobVerificationResult.Success(jobHandle);
        }
        catch (Exception ex)
        {
            jobObjectApi.CloseHandle(jobHandle);
            return JobKeeperJobVerificationResult.Failure($"job verifier error: {ex.Message}");
        }
    }

    private JobKeeperJobVerificationResult CloseAndFail(IntPtr jobHandle, string reason)
    {
        jobObjectApi.CloseHandle(jobHandle);
        return JobKeeperJobVerificationResult.Failure(reason);
    }

    private static string? GetJobSecurityFailure(JobObjectSecuritySnapshot? security)
    {
        if (security?.Owner == null)
            return "job security descriptor or owner could not be read.";

        var administratorsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        if (!administratorsSid.Equals(security.Owner))
            return $"job owner mismatch: expected {administratorsSid.Value}, actual {security.Owner.Value}.";

        var allowEntries = security.AccessEntries.Where(entry => entry.IsAllow).ToList();
        if (allowEntries.Count != security.AccessEntries.Count || allowEntries.Count != 2)
        {
            return "job DACL mismatch: expected exactly two allow ACEs and no deny ACEs "
                   + $"but found {allowEntries.Count} allow ACE(s) across {security.AccessEntries.Count} ACE(s).";
        }

        if (!HasFullControlEntry(allowEntries, administratorsSid))
            return $"job DACL missing Administrators full-control ACE ({administratorsSid.Value}).";

        if (!HasFullControlEntry(allowEntries, systemSid))
            return $"job DACL missing LocalSystem full-control ACE ({systemSid.Value}).";

        return null;
    }

    private static bool HasFullControlEntry(IEnumerable<JobObjectAccessEntry> entries, SecurityIdentifier sid) =>
        entries.Any(entry => sid.Equals(entry.Identity)
                             && (entry.AccessMask == GenericAllAccessMask
                                 || entry.AccessMask == MappedJobFullControlMask));
}
