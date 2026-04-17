using System.ComponentModel;
using System.Runtime.InteropServices;
using RunFence.Infrastructure;

namespace RunFence.Launch.Tokens;

public static class TokenPrivilegeHelper
{
    public const string SeBackupPrivilege = "SeBackupPrivilege";
    public const string SeTakeOwnershipPrivilege = "SeTakeOwnershipPrivilege";
    public const string SeRestorePrivilege = "SeRestorePrivilege";
    public const string SeImpersonatePrivilege = "SeImpersonatePrivilege";
    public const string SeIncreaseQuotaPrivilege = "SeIncreaseQuotaPrivilege";
    public const string SeAssignPrimaryTokenPrivilege = "SeAssignPrimaryTokenPrivilege";
    public const string SeDebugPrivilege = "SeDebugPrivilege";
    public const string SeTcbPrivilege = "SeTcbPrivilege";

    public static void EnablePrivileges(IEnumerable<string> privilegeNames)
    {
        SetPrivileges(privilegeNames, SE_PRIVILEGE_ENABLED);
    }

    public static void DisablePrivileges(IEnumerable<string> privilegeNames)
    {
        SetPrivileges(privilegeNames, 0);
    }

    private static void SetPrivileges(IEnumerable<string> privilegeNames, uint attributes)
    {
        if (!ProcessNative.OpenProcessToken(ProcessNative.GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var tokenHandle))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        // When enabling, track successful enables so we can roll back on partial failure.
        List<string>? enabledSoFar = attributes == SE_PRIVILEGE_ENABLED ? new List<string>() : null;

        try
        {
            foreach (var name in privilegeNames)
            {
                if (!TokenPrivilegeNative.LookupPrivilegeValue(null, name, out var luid))
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                var tp = new TokenPrivilegeNative.TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Luid = luid,
                    Attributes = attributes
                };

                if (!TokenPrivilegeNative.AdjustTokenPrivileges(tokenHandle, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                // AdjustTokenPrivileges can succeed even if the privilege wasn't assigned
                var lastError = Marshal.GetLastWin32Error();
                if (lastError == ERROR_NOT_ALL_ASSIGNED)
                    throw new InvalidOperationException($"Privilege '{name}' not held by the process.");

                enabledSoFar?.Add(name);
            }

            enabledSoFar = null; // all succeeded — no rollback needed
        }
        catch
        {
            // If we enabled some privileges before the failure, roll them back.
            if (enabledSoFar?.Count > 0)
            {
                try
                {
                    SetPrivileges(enabledSoFar, 0);
                }
                catch
                {
                }
            }

            throw;
        }
        finally
        {
            ProcessNative.CloseHandle(tokenHandle);
        }
    }

    /// <summary>
    /// Enables a privilege on <paramref name="hToken"/>. Throws <see cref="Win32Exception"/> if the
    /// privilege is not held by the token (ERROR_NOT_ALL_ASSIGNED).
    /// </summary>
    public static void EnablePrivilegeOnToken(IntPtr hToken, string privilegeName)
    {
        if (!TokenPrivilegeNative.LookupPrivilegeValue(null, privilegeName, out var luid))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        var tp = new TokenPrivilegeNative.TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = SE_PRIVILEGE_ENABLED };
        if (!TokenPrivilegeNative.AdjustTokenPrivileges(hToken, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (Marshal.GetLastWin32Error() == ERROR_NOT_ALL_ASSIGNED)
            throw new Win32Exception(ERROR_NOT_ALL_ASSIGNED, $"Privilege '{privilegeName}' not held by the token.");
    }

    /// <summary>
    /// Disables (but does not remove) privileges on <paramref name="hToken"/>.
    /// Use this to undo privileges that were explicitly enabled on the current process token —
    /// privileges that are naturally present in the token (just disabled by default) are
    /// set back to their disabled state. Silently skips any privilege not present in the token.
    /// </summary>
    public static void DisablePrivilegesOnToken(IntPtr hToken, IEnumerable<string> privilegeNames)
    {
        foreach (var name in privilegeNames)
        {
            if (!TokenPrivilegeNative.LookupPrivilegeValue(null, name, out var luid))
                continue;
            var tp = new TokenPrivilegeNative.TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = 0 };
            TokenPrivilegeNative.AdjustTokenPrivileges(hToken, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
        }
    }

    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;
    private const int ERROR_NOT_ALL_ASSIGNED = 1300;
}
