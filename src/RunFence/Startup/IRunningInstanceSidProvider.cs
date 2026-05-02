namespace RunFence.Startup;

public record RunningInstanceInfo(string Sid, int SessionId);

/// <summary>
/// Provides information about another running instance of this application.
/// </summary>
public interface IRunningInstanceSidProvider
{
    /// <summary>
    /// Returns info about a running instance of this application other than the current process,
    /// or null if no such instance is found or the info cannot be determined.
    /// </summary>
    RunningInstanceInfo? GetRunningInstanceInfo();
}
