namespace RunFence.Account;

public sealed record AccountDeleteValidationResult(
    string? ErrorMessage,
    IReadOnlyList<ProcessInfo> RunningProcesses)
{
    public static AccountDeleteValidationResult Success { get; } =
        new(null, Array.Empty<ProcessInfo>());

    public bool HasRunningProcesses => RunningProcesses.Count > 0;
}
