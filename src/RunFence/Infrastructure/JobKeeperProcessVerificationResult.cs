namespace RunFence.Infrastructure;

public readonly record struct JobKeeperProcessVerificationResult(
    bool Succeeded,
    int ProcessId,
    string? FailureReason)
{
    public static JobKeeperProcessVerificationResult Success(int processId) =>
        new(true, processId, null);

    public static JobKeeperProcessVerificationResult Failure(string reason) =>
        new(false, 0, reason);
}
