using System.IO.Pipes;
using System.Security.Principal;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Infrastructure;

public sealed class JobKeeperProcessVerifier(
    IJobKeeperJobVerifier jobVerifier,
    IJobKeeperClientProcessQuery clientProcessQuery,
    IProcessJobManager processJobManager,
    IVerifiedRestrictedJobCache verifiedRestrictedJobCache,
    ILoggingService log,
    string jobKeeperExePath) : IJobKeeperProcessVerifier
{
    public JobKeeperProcessVerificationResult Verify(
        NamedPipeServerStream pipe,
        int expectedPid,
        SecurityIdentifier targetUserSid,
        JobKeeperInstanceIdentity identity)
    {
        try
        {
            if (!clientProcessQuery.TryGetPipeClientProcessId(pipe, out var clientPid))
                return JobKeeperProcessVerificationResult.Failure("pipe client PID was unavailable.");

            var processInfo = clientProcessQuery.QueryProcessInfo(clientPid);
            if (processInfo.ImagePath == null)
            {
                return JobKeeperProcessVerificationResult.Failure(
                    $"QueryFullProcessImageName failed for pipe client PID {clientPid}.");
            }

            if (!string.Equals(processInfo.ImagePath, jobKeeperExePath, StringComparison.OrdinalIgnoreCase))
            {
                return JobKeeperProcessVerificationResult.Failure(
                    $"pipe client image mismatch: expected '{jobKeeperExePath}', actual '{processInfo.ImagePath}'.");
            }

            if (processInfo.OwnerSid == null || !targetUserSid.Equals(processInfo.OwnerSid))
            {
                return JobKeeperProcessVerificationResult.Failure(
                    $"pipe client owner SID mismatch: expected {targetUserSid.Value}, actual {processInfo.OwnerSid?.Value ?? "<unavailable>"}.");
            }

            var il = processInfo.IntegrityLevel ?? NativeTokenHelper.MandatoryLevelMedium;
            var isLow = identity.ExpectedMode == JobKeeperIntegrityMode.LowIntegrity;
            if ((il <= NativeTokenHelper.MandatoryLevelLow) != isLow)
            {
                var expected = isLow ? "low integrity" : "medium-or-higher integrity";
                return JobKeeperProcessVerificationResult.Failure(
                    $"pipe client integrity mismatch: expected {expected}, actual 0x{il:X}.");
            }

            var verification = jobVerifier.Verify((int)clientPid);
            if (!verification.Succeeded)
            {
                return JobKeeperProcessVerificationResult.Failure(
                    $"job verification failed: {verification.FailureReason ?? "unknown reason"}");
            }

            var verifiedJobHandle = verification.JobHandle!;
            try
            {
                processJobManager.RegisterVerifiedRestrictedJob(identity.TargetSid, isLow, verifiedJobHandle.Handle);
                if (!verifiedRestrictedJobCache.TryAddDuplicate(verifiedJobHandle.Handle))
                {
                    log.Warn(
                        $"JobKeeperProcessVerifier: failed to add verified restricted job for SID {identity.TargetSid} to classification cache.");
                }

                verifiedJobHandle.Release();
                return JobKeeperProcessVerificationResult.Success((int)clientPid);
            }
            catch
            {
                verifiedJobHandle.Dispose();
                throw;
            }
        }
        catch (Exception ex)
        {
            return JobKeeperProcessVerificationResult.Failure($"unexpected verifier error: {ex.Message}");
        }
    }
}
