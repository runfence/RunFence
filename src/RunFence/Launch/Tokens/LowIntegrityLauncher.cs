using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Launch.Tokens;

/// <summary>
/// Launches a process at low mandatory integrity level using CreateProcessWithTokenW.
/// Requires SE_IMPERSONATE_NAME privilege (present in elevated processes).
/// </summary>
public class LowIntegrityLauncher(ILoggingService log, IInteractiveLogonHelper logonHelper) : ILowIntegrityLauncher
{
    /// <summary>
    /// Launches the process described by <paramref name="psi"/> at low mandatory integrity level.
    /// When <paramref name="password"/> is null, uses the current process token (current account);
    /// otherwise logs on the specified user and uses that token.
    /// </summary>
    public void Launch(ProcessStartInfo psi, SecureString? password,
        string? domain, string? username,
        LaunchTokenSource tokenSource = LaunchTokenSource.Credentials,
        Dictionary<string, string>? extraEnvVars = null, bool hideWindow = false)
    {
        IntPtr hToken = IntPtr.Zero;
        IntPtr hDupToken = IntPtr.Zero;
        IntPtr pLowSid = IntPtr.Zero;
        IntPtr tmlBuffer = IntPtr.Zero;
        var envBlock = new NativeEnvironmentBlock();
        try
        {
            hToken = NativeTokenAcquisition.AcquireLogonToken(password, domain, username, log, logonHelper, tokenSource);
            hDupToken = NativeTokenAcquisition.DuplicateToken(hToken);
            NativeTokenAcquisition.SetLowIntegrityOnToken(hDupToken, out pLowSid, out tmlBuffer);

            if (ProcessLaunchNative.CreateEnvironmentBlock(out var pEnv, hDupToken, false))
                envBlock = new NativeEnvironmentBlock(pEnv, isOverridden: false);
            else
                log.Warn("LowIntegrityLauncher: CreateEnvironmentBlock failed — process will inherit parent environment");

            envBlock.MergeInPlace(extraEnvVars);

            var pi = ProcessLaunchNative.LaunchWithToken(hDupToken, psi, envBlock.Pointer, hideWindow: hideWindow);
            NativeMethods.CloseHandle(pi.hProcess);
            NativeMethods.CloseHandle(pi.hThread);
        }
        finally
        {
            if (tmlBuffer != IntPtr.Zero)
                Marshal.FreeHGlobal(tmlBuffer);
            if (pLowSid != IntPtr.Zero)
                NativeMethods.LocalFree(pLowSid);
            if (hDupToken != IntPtr.Zero)
                NativeMethods.CloseHandle(hDupToken);
            if (hToken != IntPtr.Zero)
                NativeMethods.CloseHandle(hToken);
            envBlock.Dispose();
        }
    }
}