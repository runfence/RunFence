namespace RunFence.Infrastructure;

public readonly record struct JobKeeperJobVerificationResult(
    bool Succeeded,
    OwnedJobHandle? JobHandle,
    string? FailureReason)
{
    public static JobKeeperJobVerificationResult Success(OwnedJobHandle jobHandle) => new(true, jobHandle, null);

    public static JobKeeperJobVerificationResult Failure(string reason) => new(false, null, reason);
}
