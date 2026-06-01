namespace RunFence.Infrastructure;

public sealed class JobKeeperJobVerifier(
    IJobObjectApi jobObjectApi,
    IProcessHandleSnapshotProvider processHandleSnapshotProvider,
    VerifiedRestrictedJobAdmissionPolicy admissionPolicy) : IJobKeeperJobVerifier
{
    public JobKeeperJobVerificationResult Verify(int keeperPid)
    {
        try
        {
            using var keeperProcessHandle = ProcessNative.OpenProcess(
                ProcessNative.ProcessDuplicateHandle | ProcessNative.PROCESS_QUERY_INFORMATION,
                false,
                (uint)keeperPid);
            if (keeperProcessHandle.IsInvalid)
            {
                return JobKeeperJobVerificationResult.Failure(
                    $"OpenProcess failed for keeper PID {keeperPid} with duplicate/query-information rights.");
            }

            var candidates = processHandleSnapshotProvider.GetJobHandleCandidates(keeperProcessHandle);
            OwnedJobHandle? selectedCandidate = null;
            try
            {
                foreach (var candidate in candidates)
                {
                    var pids = jobObjectApi.QueryProcessIds(candidate.Handle);
                    if (pids?.Contains(keeperPid) != true)
                        continue;

                    var uiRestrictions = jobObjectApi.QueryUiRestrictions(candidate.Handle);
                    if (!uiRestrictions.HasValue
                        || (uiRestrictions.Value & ProcessJobManager.UiRestrictionFlags) != ProcessJobManager.UiRestrictionFlags)
                    {
                        continue;
                    }

                    if (!admissionPolicy.TryValidateForAdmission(
                            candidate.Handle,
                            out _,
                            out _))
                    {
                        continue;
                    }

                    selectedCandidate = candidate;
                    return JobKeeperJobVerificationResult.Success(candidate);
                }

                return JobKeeperJobVerificationResult.Failure(
                    $"keeper PID {keeperPid} did not carry a verified restricted job handle.");
            }
            finally
            {
                foreach (var candidate in candidates)
                {
                    if (!ReferenceEquals(candidate, selectedCandidate))
                        candidate.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            return JobKeeperJobVerificationResult.Failure($"job verifier error: {ex.Message}");
        }
    }
}
