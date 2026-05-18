using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Launch.Tokens;

/// <summary>
/// Launches a process using CreateProcessWithTokenW or CreateProcessWithLogonW.
/// Requires SE_IMPERSONATE_NAME privilege (present in elevated processes).
/// All fields on <see cref="AccountLaunchIdentity"/> must be fully resolved before calling:
/// <c>Credentials</c> and <c>PrivilegeLevel</c> must be non-null.
/// </summary>
public class AccountProcessLauncher(
    ILoggingService log,
    ICreateProcessLauncherHelper createProcessLauncherHelper,
    IInteractiveLogonHelper logonHelper,
    ILogonTokenProvider logonTokenProvider) : IAccountProcessLauncher
{
    public ProcessInfo Launch(ProcessLaunchTarget target, AccountLaunchIdentity identity)
    {
        var credentials = identity.Credentials!.Value;
        log.Info($"Launching {target.ExePath} {target.Arguments} as {credentials.Username} via {credentials.Domain}");

        return credentials.TokenSource switch
        {
            LaunchTokenSource.Credentials => LaunchWithCredentials(target, identity),
            _ => LaunchWithTokenSource(target, identity)
        };
    }

    private ProcessInfo LaunchWithTokenSource(ProcessLaunchTarget psi, AccountLaunchIdentity identity)
    {
        IntPtr hToken = IntPtr.Zero;
        var credentials = identity.Credentials!.Value;

        log.Info("LaunchWithTokenSource: AcquireLogonToken");
        try
        {
            hToken = logonTokenProvider.AcquireLogonToken(
                credentials.Password,
                credentials.Domain,
                credentials.Username,
                credentials.TokenSource);

            return createProcessLauncherHelper.LaunchUsingAcquiredToken(hToken, psi, identity);
        }
        finally
        {
            if (hToken != IntPtr.Zero)
                ProcessNative.CloseHandle(hToken);
        }
    }

    private ProcessInfo LaunchWithCredentials(ProcessLaunchTarget psi, AccountLaunchIdentity identity)
    {
        IntPtr hProfileKeeperToken = IntPtr.Zero;
        var credentials = identity.Credentials!.Value;

        log.Info("LaunchWithCredentials: AcquireProfileKeeperToken");
        try
        {
            // ProfileKeeper path: start a long-lived same-credentials helper with
            // CreateProcessWithLogonW(LOGON_WITH_PROFILE), duplicate its token, then
            // launch the real target through the existing token pipeline.
            // Elevation for UAC-split admins is still handled inside LaunchUsingAcquiredToken via
            // ElevatedLinkedTokenProvider (SYSTEM impersonation to get the linked primary token).
            hProfileKeeperToken = logonHelper.RunWithLogonRetry(credentials.Domain, credentials.Username,
                () => createProcessLauncherHelper.AcquireProfileKeeperToken(identity));
            return createProcessLauncherHelper.LaunchUsingAcquiredToken(hProfileKeeperToken, psi, identity);
        }
        finally
        {
            if (hProfileKeeperToken != IntPtr.Zero)
                ProcessNative.CloseHandle(hProfileKeeperToken);
        }
    }
}
