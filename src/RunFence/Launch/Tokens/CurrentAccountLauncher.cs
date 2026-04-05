using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Launch.Tokens;

/// <summary>
/// Launches a process under the current account with RunFence's explicitly enabled privileges
/// disabled on the child token. RunFence enables SeBackupPrivilege, SeRestorePrivilege, and
/// SeTakeOwnershipPrivilege for its own ACL operations; these are disabled (not removed) on
/// the duplicate token so the launched process starts with them in the default disabled state.
/// Uses CreateProcessWithTokenW with a duplicate of the current process token.
/// </summary>
public class CurrentAccountLauncher(ILoggingService log) : ICurrentAccountLauncher
{
    private static readonly string[] PrivilegesToDisable =
    [
        TokenPrivilegeHelper.SeBackupPrivilege,
        TokenPrivilegeHelper.SeRestorePrivilege,
        TokenPrivilegeHelper.SeTakeOwnershipPrivilege,
    ];

    public int Launch(ProcessStartInfo psi, Dictionary<string, string>? extraEnvVars = null, bool hideWindow = false)
    {
        IntPtr hToken = IntPtr.Zero;
        IntPtr hDupToken = IntPtr.Zero;
        var envBlock = new NativeEnvironmentBlock();
        try
        {
            if (!NativeMethods.OpenProcessToken(NativeMethods.GetCurrentProcess(),
                    ProcessLaunchNative.TOKEN_DUPLICATE | ProcessLaunchNative.TOKEN_QUERY,
                    out hToken))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            hDupToken = NativeTokenAcquisition.DuplicateToken(hToken);
            TokenPrivilegeHelper.DisablePrivilegesOnToken(hDupToken, PrivilegesToDisable);

            if (ProcessLaunchNative.CreateEnvironmentBlock(out var pEnv, hDupToken, false))
                envBlock = new NativeEnvironmentBlock(pEnv, isOverridden: false);
            else
                log.Warn("CurrentAccountLauncher: CreateEnvironmentBlock failed — process will inherit parent environment");

            envBlock.MergeInPlace(extraEnvVars);

            var pi = ProcessLaunchNative.LaunchWithToken(hDupToken, psi, envBlock.Pointer, hideWindow: hideWindow);
            var pid = (int)pi.dwProcessId;
            NativeMethods.CloseHandle(pi.hProcess);
            NativeMethods.CloseHandle(pi.hThread);
            return pid;
        }
        finally
        {
            envBlock.Dispose();
            if (hDupToken != IntPtr.Zero)
                NativeMethods.CloseHandle(hDupToken);
            if (hToken != IntPtr.Zero)
                NativeMethods.CloseHandle(hToken);
        }
    }
}