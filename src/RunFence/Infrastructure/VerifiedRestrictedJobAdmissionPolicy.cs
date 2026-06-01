using RunFence.Core;

namespace RunFence.Infrastructure;

public sealed class VerifiedRestrictedJobAdmissionPolicy(
    IJobObjectApi jobObjectApi,
    ILoggingService log)
{
    public bool TryValidateForAdmission(
        IntPtr jobHandle,
        out string? failureReason,
        out bool shouldLogFailure)
    {
        if (!TryValidateCachedClassification(jobHandle, out failureReason, out shouldLogFailure))
            return false;

        var basicLimitFlags = jobObjectApi.QueryBasicLimitFlags(jobHandle);
        if (!basicLimitFlags.HasValue)
        {
            failureReason = "basic limit flags unavailable";
            shouldLogFailure = true;
            return false;
        }

        if ((basicLimitFlags.Value & ProcessJobManager.JobObjectLimitKillOnJobClose) == 0)
            return true;

        failureReason = "kill-on-close job lifecycle must not be cached";
        shouldLogFailure = true;
        log.Warn(
            "VerifiedRestrictedJobAdmissionPolicy: verified keeper job unexpectedly carried JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE.");

        return false;
    }

    public bool TryValidateCachedClassification(IntPtr jobHandle, out string? failureReason) =>
        TryValidateCachedClassification(jobHandle, out failureReason, out _);

    private bool TryValidateCachedClassification(
        IntPtr jobHandle,
        out string? failureReason,
        out bool shouldLogFailure)
    {
        var uiRestrictions = jobObjectApi.QueryUiRestrictions(jobHandle);
        if (!uiRestrictions.HasValue)
        {
            failureReason = "UI restrictions unavailable";
            shouldLogFailure = true;
            return false;
        }

        if ((uiRestrictions.Value & ProcessJobManager.JobObjectUiLimitHandles) == 0)
        {
            failureReason = null;
            shouldLogFailure = false;
            return false;
        }

        failureReason = RestrictedJobSecurityValidator.GetSecurityFailure(
            jobObjectApi.GetSecuritySnapshot(jobHandle));
        shouldLogFailure = failureReason != null;
        return failureReason == null;
    }
}
