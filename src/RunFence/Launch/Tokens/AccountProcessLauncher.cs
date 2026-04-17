using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Launch.Tokens;

/// <summary>
/// Launches a process using CreateProcessWithTokenW or CreateProcessWithLogonW.
/// Requires SE_IMPERSONATE_NAME privilege (present in elevated processes).
/// All fields on <see cref="AccountLaunchIdentity"/> must be fully resolved before calling:
/// <c>Credentials</c> and <c>PrivilegeLevel</c> must be non-null.
/// </summary>
public class AccountProcessLauncher(ILoggingService log, CreateProcessLauncherHelper createProcessLauncherHelper, IInteractiveLogonHelper logonHelper, LogonTokenProvider logonTokenProvider) : IAccountProcessLauncher
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
        IntPtr hBootstrapToken = IntPtr.Zero;
        var credentials = identity.Credentials!.Value;

        log.Info("LaunchWithCredentials: AcquireBootstrapToken");
        try
        {
            // Bootstrap path: start a simple process under the same credentials,
            // grab its token, then run the same token pipeline on that token.
            // Elevation for UAC-split admins is handled inside LaunchUsingAcquiredToken via
            // ElevatedLinkedTokenProvider (SYSTEM impersonation to get the linked primary token).
            (hBootstrapToken, var pi) = logonHelper.RunWithLogonRetry(credentials.Domain, credentials.Username,
                () => createProcessLauncherHelper.AcquireBootstrapToken(identity));
            try
            {
                return createProcessLauncherHelper.LaunchUsingAcquiredToken(hBootstrapToken, psi, identity);
            }
            finally
            {
                log.Info("LaunchWithCredentials: TerminateProcess temp");
                try
                {
                    ProcessLaunchNative.TerminateProcess(pi.hProcess, 0);
                }
                catch
                {
                }

                if (pi.hThread != IntPtr.Zero)
                    ProcessNative.CloseHandle(pi.hThread);
                if (pi.hProcess != IntPtr.Zero)
                    ProcessNative.CloseHandle(pi.hProcess);
            }
        }
        finally
        {
            if (hBootstrapToken != IntPtr.Zero)
                ProcessNative.CloseHandle(hBootstrapToken);
        }
    }
}
