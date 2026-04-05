namespace RunFence.Infrastructure;

/// <summary>
/// Terminates all processes owned by the specified SID, excluding the current process.
/// Supports both regular account SIDs and AppContainer SIDs (S-1-15-2-*).
/// </summary>
public interface IProcessTerminationService
{
    /// <summary>
    /// Terminates all processes owned by the specified SID.
    /// Returns the count of successfully killed and failed-to-kill processes.
    /// </summary>
    (int Killed, int Failed) KillProcesses(string sid);
}