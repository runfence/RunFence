using System.ComponentModel;
using System.Runtime.InteropServices;
using RunFence.Infrastructure;

namespace RunFence.Launch;

/// <summary>
/// Static thin P/Invoke wrappers for modifying Windows tokens used by the launchers.
/// Covers token duplication and integrity-level adjustment.
/// Logon token acquisition is handled by <see cref="Tokens.LogonTokenProvider"/>.
/// </summary>
public static class NativeTokenAcquisition
{
    /// <summary>Duplicates <paramref name="hToken"/> as a primary token (full access).</summary>
    public static IntPtr DuplicateToken(IntPtr hToken)
    {
        if (!ProcessLaunchNative.DuplicateTokenEx(hToken, ProcessLaunchNative.MAXIMUM_ALLOWED, IntPtr.Zero,
                (int)ProcessLaunchNative.SecurityImpersonationLevel.SecurityImpersonation,
                (int)ProcessLaunchNative.TokenType.TokenPrimary, out var hDupToken))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return hDupToken;
    }

    /// <summary>
    /// Applies the Low Integrity mandatory label to <paramref name="hToken"/>.
    /// Caller is responsible for freeing <paramref name="pLowSid"/> via LocalFree
    /// and <paramref name="tmlBuffer"/> via Marshal.FreeHGlobal.
    /// </summary>
    public static void SetLowIntegrityOnToken(IntPtr hToken, out IntPtr pLowSid, out IntPtr tmlBuffer)
        => SetIntegrityOnToken(hToken, ProcessLaunchNative.LowIntegritySid, out pLowSid, out tmlBuffer);

    /// <summary>
    /// Applies the Medium Integrity mandatory label to <paramref name="hToken"/>.
    /// Used for de-elevation: elevated tokens default to High integrity; lowering to Medium
    /// matches the level UAC assigns to standard user processes.
    /// </summary>
    public static void SetMediumIntegrityOnToken(IntPtr hToken, out IntPtr pSid, out IntPtr tmlBuffer)
        => SetIntegrityOnToken(hToken, ProcessLaunchNative.MediumIntegritySid, out pSid, out tmlBuffer);

    /// <summary>
    /// Replaces the DefaultDacl on <paramref name="hToken"/> with a minimal DACL granting
    /// GENERIC_ALL only to SYSTEM, <paramref name="accountSid"/>, and any
    /// <paramref name="additionalSids"/>. This propagates to every process and kernel object
    /// created by the launched process tree.
    /// </summary>
    public static void SetRestrictiveDefaultDacl(IntPtr hToken, string accountSid, params string[] additionalSids)
    {
        var sddl = $"D:(A;;GA;;;SY)(A;;GA;;;{accountSid})";
        foreach (var sid in additionalSids)
            sddl += $"(A;;GA;;;{sid})";
        if (!ProcessLaunchNative.ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl, 1, out var pSd, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            if (!FileSecurityNative.GetSecurityDescriptorDacl(pSd, out _, out var pDacl, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var buffer = Marshal.AllocHGlobal(IntPtr.Size);
            try
            {
                Marshal.WriteIntPtr(buffer, pDacl);
                if (!ProcessLaunchNative.SetTokenInformation(hToken, ProcessLaunchNative.TOKEN_DEFAULT_DACL, buffer, (uint)IntPtr.Size))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            ProcessNative.LocalFree(pSd);
        }
    }

    private static void SetIntegrityOnToken(IntPtr hToken, string integritySid, out IntPtr pSid, out IntPtr tmlBuffer)
    {
        pSid = IntPtr.Zero;
        tmlBuffer = IntPtr.Zero;

        if (!ProcessNative.ConvertStringSidToSid(integritySid, out pSid))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        var sidLen = ProcessLaunchNative.GetLengthSid(pSid);
        var tmlSize = Marshal.SizeOf<ProcessLaunchNative.TOKEN_MANDATORY_LABEL>();
        var totalSize = tmlSize + sidLen;

        tmlBuffer = Marshal.AllocHGlobal(totalSize);

        var sidInBuffer = IntPtr.Add(tmlBuffer, tmlSize);
        var sidBytes = new byte[sidLen];
        Marshal.Copy(pSid, sidBytes, 0, sidLen);
        Marshal.Copy(sidBytes, 0, sidInBuffer, sidLen);

        var tml = new ProcessLaunchNative.TOKEN_MANDATORY_LABEL
        {
            Label = new ProcessLaunchNative.SID_AND_ATTRIBUTES { Sid = sidInBuffer, Attributes = ProcessLaunchNative.SE_GROUP_INTEGRITY }
        };
        Marshal.StructureToPtr(tml, tmlBuffer, false);

        if (!ProcessLaunchNative.SetTokenInformation(hToken, ProcessLaunchNative.TOKEN_INTEGRITY_LEVEL, tmlBuffer, (uint)totalSize))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

}