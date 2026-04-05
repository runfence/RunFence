using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Launch.Tokens;

namespace RunFence.Launch;

/// <summary>
/// Static helpers for acquiring and modifying Windows tokens used by the launchers.
/// Covers logon token acquisition, token duplication, and integrity-level adjustment.
/// </summary>
public static class NativeTokenAcquisition
{
    /// <summary>
    /// Acquires a logon token for the given credentials, or opens the current process token
    /// when <paramref name="password"/> is null (current-account launch).
    /// Handles ERROR_LOGON_TYPE_NOT_GRANTED by temporarily granting SeInteractiveLogonRight
    /// via <see cref="IInteractiveLogonHelper.RunWithLogonRetry{T}"/>.
    /// </summary>
    public static IntPtr AcquireLogonToken(SecureString? password, string? domain,
        string? username, ILoggingService log, IInteractiveLogonHelper logonHelper,
        LaunchTokenSource tokenSource = LaunchTokenSource.Credentials)
    {
        switch (tokenSource)
        {
            case LaunchTokenSource.CurrentProcess:
                if (!NativeMethods.OpenProcessToken(NativeMethods.GetCurrentProcess(),
                        ProcessLaunchNative.TOKEN_DUPLICATE | ProcessLaunchNative.TOKEN_QUERY,
                        out var hProcessToken))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                return hProcessToken;

            case LaunchTokenSource.InteractiveUser:
                try
                {
                    return ExplorerTokenHelper.GetExplorerToken(log);
                }
                catch when (password != null)
                {
                    // Explorer token failed but stored credentials are available — fall back to LogonUser
                    log.Warn("AcquireLogonToken: Explorer token unavailable, falling back to stored credentials.");
                    var fallbackPtr = Marshal.SecureStringToGlobalAllocUnicode(password);
                    try
                    {
                        return logonHelper.RunWithLogonRetry(domain, username,
                            () => LogonUserOrThrow(username!, domain, fallbackPtr));
                    }
                    finally
                    {
                        Marshal.ZeroFreeGlobalAllocUnicode(fallbackPtr);
                    }
                }

            default:
            {
                if (password == null)
                    throw new ArgumentException("Password is required for Credentials token source.", nameof(password));

                var passwordPtr = Marshal.SecureStringToGlobalAllocUnicode(password);
                try
                {
                    return logonHelper.RunWithLogonRetry(domain, username,
                        () => LogonUserOrThrow(username!, domain, passwordPtr));
                }
                finally
                {
                    Marshal.ZeroFreeGlobalAllocUnicode(passwordPtr);
                }
            }
        }
    }

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

    private static void SetIntegrityOnToken(IntPtr hToken, string integritySid, out IntPtr pSid, out IntPtr tmlBuffer)
    {
        pSid = IntPtr.Zero;
        tmlBuffer = IntPtr.Zero;

        if (!NativeMethods.ConvertStringSidToSid(integritySid, out pSid))
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

    private static IntPtr LogonUserOrThrow(string username, string? domain, IntPtr passwordPtr)
    {
        if (TryLogonUser(username, domain, passwordPtr, out var hToken, out var error))
            return hToken;
        throw new Win32Exception(error);
    }

    private static bool TryLogonUser(string username, string? domain, IntPtr passwordPtr,
        out IntPtr hToken, out int error)
    {
        if (ProcessLaunchNative.LogonUser(username, domain, passwordPtr,
                ProcessLaunchNative.LOGON32_LOGON_INTERACTIVE, ProcessLaunchNative.LOGON32_PROVIDER_DEFAULT, out hToken))
        {
            error = 0;
            return true;
        }

        error = Marshal.GetLastWin32Error();
        return false;
    }
}