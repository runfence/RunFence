using System.ComponentModel;
using System.Runtime.InteropServices;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Launch.Tokens;

public class SaferDeElevationHelper(ILoggingService log)
{
    /// <summary>
    /// Creates a de-elevated token from <paramref name="hSourceToken"/> using
    /// SaferComputeTokenFromLevel(NORMALUSER) with S-1-5-114 set to deny-only.
    /// The Administrators SID is set to deny-only by the Safer API.
    /// S-1-5-114 ("Local account and member of Administrators group") is additionally
    /// set to deny-only via CreateRestrictedToken.
    /// Integrity level is NOT set — the caller must apply SetMediumIntegrityOnToken
    /// or SetLowIntegrityOnToken on the returned handle.
    /// The caller owns the returned handle and must close it via CloseHandle.
    /// </summary>
    public IntPtr CreateDeElevatedToken(IntPtr hSourceToken)
    {
        IntPtr hLevel = IntPtr.Zero;
        IntPtr hSaferToken = IntPtr.Zero;
        IntPtr pSid114 = IntPtr.Zero;

        log.Debug("SaferDeElevationHelper: SaferCreateLevel");
        if (!SaferNative.SaferCreateLevel(SaferNative.SAFER_SCOPEID_MACHINE, SaferNative.SAFER_LEVELID_NORMALUSER,
                SaferNative.SAFER_LEVEL_OPEN, out hLevel, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        try
        {
            log.Debug("SaferDeElevationHelper: SaferComputeTokenFromLevel");
            if (!SaferNative.SaferComputeTokenFromLevel(hLevel, hSourceToken, out hSaferToken, 0, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            try
            {
                log.Debug("SaferDeElevationHelper: CreateRestrictedToken(S-1-5-114 → deny-only)");
                if (!ProcessNative.ConvertStringSidToSid("S-1-5-114", out pSid114))
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                var sidToDisable = new ProcessLaunchNative.SID_AND_ATTRIBUTES { Sid = pSid114, Attributes = 0 };
                if (!ProcessLaunchNative.CreateRestrictedToken(hSaferToken, 0,
                        1, [sidToDisable],
                        0, IntPtr.Zero,
                        0, null,
                        out IntPtr hFinalToken))
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                return hFinalToken;
            }
            finally
            {
                ProcessNative.CloseHandle(hSaferToken);
                if (pSid114 != IntPtr.Zero)
                    ProcessNative.LocalFree(pSid114);
            }
        }
        finally
        {
            SaferNative.SaferCloseLevel(hLevel);
        }
    }
}
