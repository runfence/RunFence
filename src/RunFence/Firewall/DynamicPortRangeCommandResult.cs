namespace RunFence.Firewall;

public sealed record DynamicPortRangeCommandResult(
    int ExitCode,
    string StandardOutput,
    bool TimedOut,
    string? FailureMessage);
