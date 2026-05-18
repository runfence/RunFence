using System.Runtime.InteropServices;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Launch.Tokens;

/// <summary>
/// Acquires a Windows logon token for a given credential set or token source.
/// Handles ERROR_LOGON_TYPE_NOT_GRANTED by temporarily granting SeInteractiveLogonRight
/// via <see cref="IInteractiveLogonHelper.RunWithLogonRetry{T}"/>.
/// </summary>
public class LogonTokenProvider(
    ILoggingService log,
    IInteractiveLogonHelper logonHelper,
    IExplorerTokenProvider explorerTokenProvider,
    ISystemTokenProvider systemTokenProvider) : ILogonTokenProvider
{
    /// <summary>
    /// Acquires a logon token for the given credentials, or opens the current process token
    /// when <paramref name="password"/> is null (current-account launch).
    /// </summary>
    public IntPtr AcquireLogonToken(
        ProtectedString? password,
        string? domain,
        string? username,
        LaunchTokenSource tokenSource = LaunchTokenSource.Credentials)
    {
        switch (tokenSource)
        {
            case LaunchTokenSource.CurrentProcess:
                if (!ProcessNative.OpenProcessToken(
                        ProcessNative.GetCurrentProcess(),
                        ProcessLaunchNative.TOKEN_DUPLICATE | ProcessLaunchNative.TOKEN_QUERY,
                        out var hProcessToken))
                {
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                }

                return hProcessToken;

            case LaunchTokenSource.SystemAccount:
                return systemTokenProvider.AcquireSystemToken();

            case LaunchTokenSource.InteractiveUser:
                try
                {
                    return explorerTokenProvider.GetExplorerToken();
                }
                catch when (password != null)
                {
                    log.Warn("AcquireLogonToken: Explorer token unavailable, falling back to stored credentials.");
                    IntPtr fallbackToken = IntPtr.Zero;
                    password.UseUnicodeSnapshot(snapshot =>
                    {
                        IntPtr passwordPtr = snapshot.DangerousGetIntPtr();
                        fallbackToken = logonHelper.RunWithLogonRetry(domain, username,
                            () => LogonUserOrThrow(username!, domain, passwordPtr));
                    });
                    return fallbackToken;
                }

            default:
            {
                if (password == null)
                    throw new ArgumentException("Password is required for Credentials token source.", nameof(password));

                IntPtr token = IntPtr.Zero;
                password.UseUnicodeSnapshot(snapshot =>
                {
                    IntPtr passwordPtr = snapshot.DangerousGetIntPtr();
                    token = logonHelper.RunWithLogonRetry(domain, username,
                        () => LogonUserOrThrow(username!, domain, passwordPtr));
                });
                return token;
            }
        }
    }

    private static IntPtr LogonUserOrThrow(string username, string? domain, IntPtr passwordPtr)
    {
        if (TryLogonUser(username, domain, passwordPtr, out var hToken, out var error))
            return hToken;

        throw new System.ComponentModel.Win32Exception(error);
    }

    private static bool TryLogonUser(string username, string? domain, IntPtr passwordPtr, out IntPtr hToken, out int error)
    {
        if (ProcessLaunchNative.LogonUser(
                username,
                domain,
                passwordPtr,
                ProcessLaunchNative.LOGON32_LOGON_INTERACTIVE,
                ProcessLaunchNative.LOGON32_PROVIDER_DEFAULT,
                out hToken))
        {
            error = 0;
            return true;
        }

        error = Marshal.GetLastWin32Error();
        return false;
    }
}
