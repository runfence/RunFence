using System.Runtime.InteropServices;
using System.Security.Principal;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Launch.Tokens;

/// <summary>
/// Helpers for applying DISABLE_MAX_PRIVILEGE restriction to de-elevate tokens
/// that have active admin group membership.
/// Note: LUA_TOKEN (0x4) is intentionally NOT used — it causes STATUS_DLL_INIT_FAILED (0xC0000142)
/// because seclogon creates a new logon session whose SID has no window station/desktop access.
/// </summary>
public static class TokenRestrictionHelper
{
    private const uint DISABLE_MAX_PRIVILEGE = 0x1;

    /// <summary>
    /// Struct used solely to compute the byte offset of the Groups array within a TOKEN_GROUPS buffer.
    /// TOKEN_GROUPS layout: DWORD GroupCount followed by SID_AND_ATTRIBUTES Groups[].
    /// On x64 there are 4 bytes of padding after GroupCount to align the pointer in Groups.
    /// Marshal.OffsetOf gives the correct offset on all platforms.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_GROUPS_HEADER
    {
        public uint GroupCount;
        public ProcessLaunchNative.SID_AND_ATTRIBUTES FirstGroup;
    }

    /// <summary>
    /// Checks if the token has active admin group membership and applies DISABLE_MAX_PRIVILEGE
    /// to ensure true de-elevation. Returns true if a restricted token was created.
    /// </summary>
    public static bool TryRestrictIfAdmin(IntPtr hDupToken, out IntPtr hRestrictedToken, ILoggingService log)
    {
        hRestrictedToken = IntPtr.Zero;

        if (!TokenHasActiveAdminGroup(hDupToken))
            return false;

        if (!ProcessLaunchNative.CreateRestrictedToken(hDupToken, DISABLE_MAX_PRIVILEGE,
                0, IntPtr.Zero, 0, IntPtr.Zero, 0, IntPtr.Zero,
                out hRestrictedToken))
        {
            log.Warn($"TokenRestrictionHelper: CreateRestrictedToken failed (error {Marshal.GetLastWin32Error()})");
            return false;
        }

        log.Info("TokenRestrictionHelper: Applied DISABLE_MAX_PRIVILEGE (admin token detected)");
        return true;
    }

    private static bool TokenHasActiveAdminGroup(IntPtr hToken)
    {
        const uint SE_GROUP_ENABLED = 0x00000004;
        const int TokenGroups = 2;
        const string adminSidStr = "S-1-5-32-544"; // BUILTIN\Administrators

        NativeMethods.GetTokenInformation(hToken, TokenGroups, IntPtr.Zero, 0, out var needed);
        if (needed == 0)
            return false;

        var buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!NativeMethods.GetTokenInformation(hToken, TokenGroups, buffer, needed, out _))
                return false;

            var groupCount = Marshal.ReadInt32(buffer);
            var groupsOffset = (int)Marshal.OffsetOf<TOKEN_GROUPS_HEADER>(nameof(TOKEN_GROUPS_HEADER.FirstGroup));
            var groupsPtr = IntPtr.Add(buffer, groupsOffset);

            for (int i = 0; i < groupCount; i++)
            {
                var sa = Marshal.PtrToStructure<ProcessLaunchNative.SID_AND_ATTRIBUTES>(
                    IntPtr.Add(groupsPtr, i * Marshal.SizeOf<ProcessLaunchNative.SID_AND_ATTRIBUTES>()));

                if ((sa.Attributes & SE_GROUP_ENABLED) == 0)
                    continue;

                try
                {
                    var sid = new SecurityIdentifier(sa.Sid);
                    if (string.Equals(sid.Value, adminSidStr, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch
                {
                    /* skip unreadable SID */
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return false;
    }
}