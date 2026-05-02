using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Infrastructure;

public sealed class JobKeeperProcessVerifier(
    IJobKeeperJobVerifier jobVerifier,
    IProcessJobManager processJobManager,
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
            ProcessNative.GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var clientPid);

            using var handle = ProcessNative.OpenProcess(ProcessNative.ProcessQueryLimitedInformation, false, clientPid);
            if (handle.IsInvalid)
                return JobKeeperProcessVerificationResult.Failure($"OpenProcess failed for pipe client PID {clientPid}.");

            var sb = new StringBuilder(1024);
            uint size = (uint)sb.Capacity;
            if (!ProcessNative.QueryFullProcessImageName(handle, 0, sb, ref size))
                return JobKeeperProcessVerificationResult.Failure($"QueryFullProcessImageName failed for pipe client PID {clientPid}.");

            if (!string.Equals(sb.ToString(), jobKeeperExePath, StringComparison.OrdinalIgnoreCase))
            {
                return JobKeeperProcessVerificationResult.Failure(
                    $"pipe client image mismatch: expected '{jobKeeperExePath}', actual '{sb}'.");
            }

            if (expectedPid > 0 && clientPid != (uint)expectedPid)
            {
                return JobKeeperProcessVerificationResult.Failure(
                    $"pipe client PID mismatch: expected {expectedPid}, actual {clientPid}.");
            }

            var ownerSid = NativeTokenHelper.TryGetProcessOwnerSid(clientPid);
            if (ownerSid == null || !targetUserSid.Equals(ownerSid))
            {
                return JobKeeperProcessVerificationResult.Failure(
                    $"pipe client owner SID mismatch: expected {targetUserSid.Value}, actual {ownerSid?.Value ?? "<unavailable>"}.");
            }

            var il = NativeTokenHelper.TryGetProcessIntegrityLevel(clientPid) ?? NativeTokenHelper.MandatoryLevelMedium;
            var isLow = identity.ExpectedMode == JobKeeperIntegrityMode.LowIntegrity;
            if ((il <= NativeTokenHelper.MandatoryLevelLow) != isLow)
            {
                var expected = isLow ? "low integrity" : "medium-or-higher integrity";
                return JobKeeperProcessVerificationResult.Failure(
                    $"pipe client integrity mismatch: expected {expected}, actual 0x{il:X}.");
            }

            var verification = jobVerifier.Verify(identity, (int)clientPid);
            if (!verification.Succeeded)
            {
                return JobKeeperProcessVerificationResult.Failure(
                    $"job verification failed: {verification.FailureReason ?? "unknown reason"}");
            }

            processJobManager.RegisterVerifiedRestrictedJob(identity.TargetSid, isLow, verification.JobHandle);
            return JobKeeperProcessVerificationResult.Success((int)clientPid);
        }
        catch (Exception ex)
        {
            return JobKeeperProcessVerificationResult.Failure($"unexpected verifier error: {ex.Message}");
        }
    }
}
