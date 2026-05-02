using System.ComponentModel;
using System.Runtime.InteropServices;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Launch.Tokens;

/// <summary>
/// Acquires a primary SYSTEM token from winlogon.exe in the current session.
/// SeDebugPrivilege (enabled at startup in Program.cs) allows OpenProcess on winlogon.
/// </summary>
public class SystemTokenProvider(ILoggingService log) : ISystemTokenProvider
{
    public IntPtr AcquireSystemToken()
    {
        IntPtr hProcess = IntPtr.Zero;
        IntPtr hSystemToken = IntPtr.Zero;
        try
        {
            hProcess = NativeTokenHelper.OpenWinlogonProcess();

            if (!ProcessNative.OpenProcessToken(hProcess,
                    ProcessLaunchNative.TOKEN_DUPLICATE | ProcessLaunchNative.TOKEN_QUERY,
                    out hSystemToken))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcessToken(winlogon) failed");

            if (!ProcessLaunchNative.DuplicateTokenEx(hSystemToken, ProcessLaunchNative.MAXIMUM_ALLOWED,
                    IntPtr.Zero,
                    (int)ProcessLaunchNative.SecurityImpersonationLevel.SecurityImpersonation,
                    (int)ProcessLaunchNative.TokenType.TokenPrimary, out var hPrimary))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "DuplicateTokenEx(SYSTEM primary) failed");

            log.Debug("SystemTokenProvider: acquired SYSTEM primary token");
            return hPrimary;
        }
        finally
        {
            if (hSystemToken != IntPtr.Zero) ProcessNative.CloseHandle(hSystemToken);
            if (hProcess != IntPtr.Zero) ProcessNative.CloseHandle(hProcess);
        }
    }
}
