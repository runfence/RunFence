namespace RunFence.Infrastructure;

public readonly record struct JobKeeperJobVerificationResult(bool Succeeded, IntPtr JobHandle, string? FailureReason)
{
    public static JobKeeperJobVerificationResult Success(IntPtr jobHandle) => new(true, jobHandle, null);

    public static JobKeeperJobVerificationResult Failure(string reason) => new(false, IntPtr.Zero, reason);
}
