namespace RunFence.Infrastructure;

public sealed record ProcessExecutionRequest(
    string FileName,
    string Arguments,
    TimeSpan Timeout,
    bool KillEntireProcessTreeOnTimeout,
    bool RedirectStandardOutput,
    bool RedirectStandardError,
    CancellationToken CancellationToken);
