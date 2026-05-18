namespace RunFence.Launch.Tokens;

/// <summary>
/// Launches a process using an acquired token, handling privilege level configuration
/// and token manipulation (de-elevation, integrity level adjustment).
/// </summary>
public interface ICreateProcessLauncherHelper
{
    ProcessInfo LaunchUsingAcquiredToken(IntPtr hToken, ProcessLaunchTarget psi, AccountLaunchIdentity identity);
    IntPtr AcquireProfileKeeperToken(AccountLaunchIdentity identity);
}
