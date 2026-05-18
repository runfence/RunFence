namespace RunFence.Infrastructure;

public sealed record ProcessExecutionResult(
    bool Started,
    int? ExitCode,
    bool TimedOut,
    string StandardOutput,
    string StandardError,
    string? FailureMessage);
