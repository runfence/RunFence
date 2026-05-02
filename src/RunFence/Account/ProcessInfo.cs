namespace RunFence.Account;

/// <summary>
/// Snapshot of a running process.
/// </summary>
public record ProcessInfo(int Pid, string? ExecutablePath, string? CommandLine);