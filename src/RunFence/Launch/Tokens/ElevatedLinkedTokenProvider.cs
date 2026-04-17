using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Launch.Tokens;

/// <summary>
/// Acquires the elevated linked token for a UAC-filtered interactive logon token by briefly
/// impersonating SYSTEM (winlogon.exe) to gain SeTcbPrivilege, which causes
/// GetTokenInformation(TOKEN_LINKED_TOKEN) to return a usable PRIMARY elevated token instead
/// of a SecurityIdentification impersonation token.
/// </summary>
public class ElevatedLinkedTokenProvider(ILoggingService log)
{
    /// <summary>
    /// Acquires the elevated linked token for <paramref name="hFilteredToken"/>.
    /// The returned handle is owned by the caller and must be closed via CloseHandle.
    /// </summary>
    /// <param name="hFilteredToken">
    /// A UAC-filtered (non-elevated) interactive logon token whose linked token is the
    /// elevated counterpart. Must have TOKEN_QUERY access.
    /// </param>
    /// <returns>A PRIMARY elevated token handle.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when winlogon.exe is not found or the linked token could not be acquired.
    /// </exception>
    /// <exception cref="Win32Exception">
    /// Thrown on P/Invoke failure during SYSTEM token acquisition or impersonation.
    /// </exception>
    public IntPtr AcquireElevatedLinkedToken(IntPtr hFilteredToken)
    {
        IntPtr hProcess = IntPtr.Zero;
        IntPtr hSystemToken = IntPtr.Zero;
        IntPtr hImpToken = IntPtr.Zero;

        log.Debug("ElevatedLinkedTokenProvider: locating winlogon.exe in current session");
        var currentSessionId = Process.GetCurrentProcess().SessionId;
        var winlogonProcesses = Process.GetProcessesByName("winlogon");
        int winlogonId;
        try
        {
            var winlogon = winlogonProcesses.FirstOrDefault(p => p.SessionId == currentSessionId);
            if (winlogon == null)
                throw new InvalidOperationException("winlogon.exe not found in current session");
            winlogonId = winlogon.Id;
        }
        finally
        {
            foreach (var p in winlogonProcesses)
                p.Dispose();
        }

        log.Debug($"ElevatedLinkedTokenProvider: winlogon PID={winlogonId}");

        try
        {
            hProcess = ProcessLaunchNative.OpenProcess(
                ProcessLaunchNative.PROCESS_QUERY_INFORMATION, false, (uint)winlogonId);
            if (hProcess == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcess(winlogon) failed");

            log.Debug("ElevatedLinkedTokenProvider: opening SYSTEM token");
            if (!ProcessNative.OpenProcessToken(hProcess,
                    ProcessLaunchNative.TOKEN_DUPLICATE | ProcessLaunchNative.TOKEN_QUERY | ProcessLaunchNative.TOKEN_IMPERSONATE,
                    out hSystemToken))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcessToken(winlogon) failed");

            log.Debug("ElevatedLinkedTokenProvider: duplicating SYSTEM token as impersonation token");
            if (!ProcessLaunchNative.DuplicateTokenEx(hSystemToken, ProcessLaunchNative.MAXIMUM_ALLOWED, IntPtr.Zero,
                    (int)ProcessLaunchNative.SecurityImpersonationLevel.SecurityImpersonation,
                    (int)ProcessLaunchNative.TokenType.TokenImpersonation, out hImpToken))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "DuplicateTokenEx(SYSTEM impersonation) failed");

            log.Debug("ElevatedLinkedTokenProvider: enabling SeTcbPrivilege on impersonation token");
            TokenPrivilegeHelper.EnablePrivilegeOnToken(hImpToken, TokenPrivilegeHelper.SeTcbPrivilege);

            log.Debug("ElevatedLinkedTokenProvider: impersonating SYSTEM");
            if (!ProcessNative.ImpersonateLoggedOnUser(hImpToken))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "ImpersonateLoggedOnUser(SYSTEM) failed");

            IntPtr hElevated;
            try
            {
                log.Debug("ElevatedLinkedTokenProvider: querying linked token under SYSTEM impersonation");
                hElevated = LinkedTokenHelper.TryGetLinkedToken(hFilteredToken);
                if (hElevated == IntPtr.Zero)
                    throw new InvalidOperationException("Failed to acquire elevated linked token under SYSTEM impersonation");
            }
            finally
            {
                ProcessNative.RevertToSelf();
            }

            log.Debug("ElevatedLinkedTokenProvider: elevated linked token acquired");
            return hElevated;
        }
        finally
        {
            if (hImpToken != IntPtr.Zero)
                ProcessNative.CloseHandle(hImpToken);
            if (hSystemToken != IntPtr.Zero)
                ProcessNative.CloseHandle(hSystemToken);
            if (hProcess != IntPtr.Zero)
                ProcessNative.CloseHandle(hProcess);
        }
    }
}
