using RunFence.TokenTest.Native;

namespace RunFence.TokenTest;

internal class SaferTokenHelper
{
    private readonly NtCreateTokenHelper _ntCreateTokenHelper;

    public SaferTokenHelper(NtCreateTokenHelper ntCreateTokenHelper)
    {
        _ntCreateTokenHelper = ntCreateTokenHelper;
    }

    // Returns the raw SaferComputeTokenFromLevel output without any NtCreateToken rebuild.
    // Used to test whether the Safer token structure itself is sufficient for process launch,
    // or whether the NtCreateToken rebuild (with stdPrivs) in GetSaferToken is the differentiator.
    public IntPtr GetRawSaferToken(IntPtr hCurrentToken)
    {
        if (!SaferNative.SaferCreateLevel(SaferNative.SAFER_SCOPEID_MACHINE,
                SaferNative.SAFER_LEVELID_NORMALUSER, SaferNative.SAFER_LEVEL_OPEN,
                out IntPtr hLevel, IntPtr.Zero))
            throw new InvalidOperationException($"SaferCreateLevel failed: {TokenHelper.GetLastError()}");
        try
        {
            if (!SaferNative.SaferComputeTokenFromLevel(hLevel, hCurrentToken,
                    out IntPtr hSaferToken, 0, IntPtr.Zero))
                throw new InvalidOperationException($"SaferComputeTokenFromLevel failed: {TokenHelper.GetLastError()}");
            return hSaferToken;
        }
        finally { SaferNative.SaferCloseLevel(hLevel); }
    }

    // Same as GetSaferToken but uses an explicit authId (from the original token) instead of the Safer token's.
    // This preserves the real logon session for app activation / MSIX access.
    public IntPtr GetSaferTokenWithAuthId(IntPtr hCurrentToken, TokenNative.LUID authId, bool omitAdmins = true)
    {
        if (!SaferNative.SaferCreateLevel(SaferNative.SAFER_SCOPEID_MACHINE,
                SaferNative.SAFER_LEVELID_NORMALUSER, SaferNative.SAFER_LEVEL_OPEN,
                out IntPtr hLevel, IntPtr.Zero))
            throw new InvalidOperationException($"SaferCreateLevel failed: {TokenHelper.GetLastError()}");

        IntPtr hSaferBase = IntPtr.Zero;
        try
        {
            if (!SaferNative.SaferComputeTokenFromLevel(hLevel, hCurrentToken,
                    out hSaferBase, 0, IntPtr.Zero))
                throw new InvalidOperationException($"SaferComputeTokenFromLevel failed: {TokenHelper.GetLastError()}");

            return _ntCreateTokenHelper.GetCustomToken(hSaferBase,
                omitAdmins: omitAdmins, standardUserPrivileges: true, authIdOverride: authId);
        }
        finally
        {
            SaferNative.SaferCloseLevel(hLevel);
            if (hSaferBase != IntPtr.Zero) ProcessNative.CloseHandle(hSaferBase);
        }
    }

    // Creates a non-admin token using SaferComputeTokenFromLevel as the structural base,
    // then rebuilds it via NtCreateToken to restore the correct standard-user privilege set
    // (SaferComputeTokenFromLevel strips all privileges except SeChangeNotifyPrivilege).
    // omitAdmins: true = remove Admins entirely (default, production use);
    //             false = keep Admins as deny-only (diagnostic: tests if deny-only works with SaferBase LUID).
    public IntPtr GetSaferToken(IntPtr hCurrentToken, bool omitAdmins = true)
    {
        if (!SaferNative.SaferCreateLevel(SaferNative.SAFER_SCOPEID_MACHINE,
                SaferNative.SAFER_LEVELID_NORMALUSER, SaferNative.SAFER_LEVEL_OPEN,
                out IntPtr hLevel, IntPtr.Zero))
            throw new InvalidOperationException($"SaferCreateLevel failed: {TokenHelper.GetLastError()}");

        IntPtr hSaferBase = IntPtr.Zero;
        try
        {
            if (!SaferNative.SaferComputeTokenFromLevel(hLevel, hCurrentToken,
                    out hSaferBase, 0, IntPtr.Zero))
                throw new InvalidOperationException($"SaferComputeTokenFromLevel failed: {TokenHelper.GetLastError()}");

            // Use the SaferToken as source for its logon session LUID and group structure.
            // Rebuild via NtCreateToken with the proper standard-user privilege set.
            // standardUserPrivileges=true: restore the 6 standard-user privileges.
            return _ntCreateTokenHelper.GetCustomToken(hSaferBase,
                omitAdmins: omitAdmins, standardUserPrivileges: true);
        }
        finally
        {
            SaferNative.SaferCloseLevel(hLevel);
            if (hSaferBase != IntPtr.Zero) ProcessNative.CloseHandle(hSaferBase);
        }
    }
}
