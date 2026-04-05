namespace RunFence.Launch;

/// <summary>
/// Specifies how the process token is acquired for launching a process.
/// </summary>
public enum LaunchTokenSource
{
    /// <summary>Password is provided — use LogonUser to acquire a token.</summary>
    Credentials,

    /// <summary>Current admin account — use the current process token.</summary>
    CurrentProcess,

    /// <summary>Interactive desktop user — acquire token from explorer.exe.</summary>
    InteractiveUser
}