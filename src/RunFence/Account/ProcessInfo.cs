using System.Diagnostics;

namespace RunFence.Account;

/// <summary>
/// Snapshot of a running process. <see cref="ProcessHandle"/> holds an open handle acquired at
/// enumeration time to eliminate PID recycling races when the process is later killed or closed.
/// Callers that hold a <see cref="ProcessInfo"/> are responsible for disposing it (or disposing the
/// collection that contains it) when the snapshot is no longer needed.
/// </summary>
public record ProcessInfo(int Pid, string? ExecutablePath, string? CommandLine) : IDisposable
{
    /// <summary>
    /// Open <see cref="Process"/> handle acquired at enumeration time. May be null if the process
    /// exited before the handle could be opened, or if opened in a context that doesn't support it.
    /// </summary>
    public Process? ProcessHandle { get; init; }

    public void Dispose() => ProcessHandle?.Dispose();
}