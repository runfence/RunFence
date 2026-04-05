using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Launch.Tokens;

/// <summary>
/// Launches a process with a de-elevated token using CreateRestrictedToken(DISABLE_MAX_PRIVILEGE)
/// followed by CreateProcessWithTokenW. All privileges are stripped and medium integrity is set,
/// significantly reducing process capabilities. Note: LUA_TOKEN (which makes Administrators SIDs
/// deny-only) is NOT used because it causes 0xc0000142 (STATUS_DLL_INIT_FAILED) — seclogon creates
/// a new logon session whose SID has no window station/desktop access, and there is no workaround
/// without SYSTEM-level privileges.
/// When a linked (filtered) UAC token is available, it is used as the source — providing full
/// de-elevation with admin SIDs deny-only.
/// </summary>
public class SplitTokenLauncher(ILoggingService log, IInteractiveLogonHelper logonHelper) : ISplitTokenLauncher
{
    /// <summary>
    /// Launches the process described by <paramref name="psi"/> with a de-elevated token.
    /// Returns the process ID on success, or -1 if the token is not elevated (caller should
    /// fall through to normal launch).
    /// When <paramref name="password"/> is null (current account), uses the linked non-elevated UAC
    /// token as the source (falling back to the current process token if no linked token exists);
    /// otherwise logs on the specified user and uses that token.
    /// If <paramref name="applyLowIl"/> is true, sets Low integrity instead of Medium.
    /// </summary>
    public int Launch(ProcessStartInfo psi, SecureString? password,
        string? domain, string? username, bool applyLowIl,
        LaunchTokenSource tokenSource = LaunchTokenSource.Credentials,
        Dictionary<string, string>? extraEnvVars = null, bool hideWindow = false)
    {
        IntPtr hToken = IntPtr.Zero;
        IntPtr hLinkedToken = IntPtr.Zero;
        IntPtr hDupToken = IntPtr.Zero;
        IntPtr hRestrictedToken = IntPtr.Zero;
        IntPtr pIntegritySid = IntPtr.Zero;
        IntPtr tmlBuffer = IntPtr.Zero;
        var envBlock = new NativeEnvironmentBlock();
        try
        {
            hToken = NativeTokenAcquisition.AcquireLogonToken(password, domain, username, log, logonHelper, tokenSource);

            // Skip de-elevation for non-elevated tokens (non-admin accounts, or accounts
            // whose admin status was revoked since the split token setting was saved).
            if (!IsTokenElevated(hToken))
            {
                log.Info("SplitTokenLauncher: Token is not elevated — skipping de-elevation");
                return -1;
            }

            // For the current account, try using the linked non-elevated UAC token as the source.
            // The linked token provides full de-elevation (admin SIDs deny-only + medium integrity).
            // When no linked token exists (built-in Administrator, RunAs from different account),
            // fall back to DISABLE_MAX_PRIVILEGE + medium integrity (partial de-elevation).
            var sourceToken = hToken;
            var hasLinkedToken = false;
            if (tokenSource == LaunchTokenSource.CurrentProcess)
            {
                hLinkedToken = LinkedTokenHelper.TryGetLinkedToken(hToken);
                if (hLinkedToken != IntPtr.Zero)
                {
                    sourceToken = hLinkedToken;
                    hasLinkedToken = true;
                    log.Info("SplitTokenLauncher: Using linked (filtered) token for de-elevation");
                }
                else
                {
                    log.Info("SplitTokenLauncher: No linked token — using DISABLE_MAX_PRIVILEGE + medium integrity");
                }
            }

            hDupToken = NativeTokenAcquisition.DuplicateToken(sourceToken);

            var effectiveToken = hDupToken;
            if (TokenRestrictionHelper.TryRestrictIfAdmin(hDupToken, out hRestrictedToken, log))
            {
                effectiveToken = hRestrictedToken;
            }
            else if (!hasLinkedToken)
            {
                // Token is elevated but TryRestrictIfAdmin returned false (admin group missing or
                // CreateRestrictedToken failed — Win32 error already logged by TryRestrictIfAdmin).
                throw new InvalidOperationException(
                    "SplitTokenLauncher: Token is elevated but could not be restricted — cannot de-elevate safely.");
            }
            // else: linked token is already fully de-elevated — no restriction needed.

            if (applyLowIl)
                NativeTokenAcquisition.SetLowIntegrityOnToken(effectiveToken, out pIntegritySid, out tmlBuffer);
            else if (!hasLinkedToken)
                NativeTokenAcquisition.SetMediumIntegrityOnToken(effectiveToken, out pIntegritySid, out tmlBuffer);

            if (ProcessLaunchNative.CreateEnvironmentBlock(out var pEnv, effectiveToken, false))
                envBlock = new NativeEnvironmentBlock(pEnv, isOverridden: false);
            else
                log.Warn("SplitTokenLauncher: CreateEnvironmentBlock failed — process will inherit parent environment");

            envBlock.MergeInPlace(extraEnvVars);

            var pi = ProcessLaunchNative.LaunchWithToken(effectiveToken, psi, envBlock.Pointer, hideWindow: hideWindow);
            var pid = (int)pi.dwProcessId;
            NativeMethods.CloseHandle(pi.hProcess);
            NativeMethods.CloseHandle(pi.hThread);
            return pid;
        }
        finally
        {
            if (tmlBuffer != IntPtr.Zero)
                Marshal.FreeHGlobal(tmlBuffer);
            if (pIntegritySid != IntPtr.Zero)
                NativeMethods.LocalFree(pIntegritySid);
            if (hRestrictedToken != IntPtr.Zero)
                NativeMethods.CloseHandle(hRestrictedToken);
            if (hDupToken != IntPtr.Zero)
                NativeMethods.CloseHandle(hDupToken);
            if (hLinkedToken != IntPtr.Zero)
                NativeMethods.CloseHandle(hLinkedToken);
            if (hToken != IntPtr.Zero)
                NativeMethods.CloseHandle(hToken);
            envBlock.Dispose();
        }
    }

    private const int TokenElevation = 20;

    private static bool IsTokenElevated(IntPtr hToken)
    {
        var buffer = Marshal.AllocHGlobal(4);
        try
        {
            if (NativeMethods.GetTokenInformation(hToken, TokenElevation, buffer, 4, out _))
                return Marshal.ReadInt32(buffer) != 0;
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}