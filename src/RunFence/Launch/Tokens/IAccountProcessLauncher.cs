namespace RunFence.Launch.Tokens;

public interface IAccountProcessLauncher
{
    /// <summary>
    /// Launches the process described by <paramref name="identity"/> at <paramref name="target"/>.
    /// All fields on <paramref name="identity"/> must be fully resolved before calling:
    /// <c>Credentials</c> and <c>PrivilegeLevel</c> must be non-null.
    /// </summary>
    ProcessInfo Launch(ProcessLaunchTarget target, AccountLaunchIdentity identity);
}
