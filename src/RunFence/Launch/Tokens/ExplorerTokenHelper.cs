using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Launch.Container;

namespace RunFence.Launch.Tokens;

/// <summary>
/// Acquires the interactive user's token from explorer.exe in the current session.
/// SID-verified: only returns a token from an explorer.exe owned by the interactive user,
/// preventing pickup of admin-owned or RunAs-launched explorer processes.
/// </summary>
public static class ExplorerTokenHelper
{
    private const uint TOKEN_QUERY = ProcessLaunchNative.TOKEN_QUERY;
    private const uint TOKEN_DUPLICATE = ProcessLaunchNative.TOKEN_DUPLICATE;
    private const uint TOKEN_IMPERSONATE = ProcessLaunchNative.TOKEN_IMPERSONATE;
    private const int TOKEN_USER = 1;

    /// <summary>
    /// Returns an open token handle from explorer.exe owned by the interactive user,
    /// or <see cref="IntPtr.Zero"/> if no matching explorer.exe is found.
    /// SID-verified: only returns a token from an explorer.exe whose owner matches
    /// <see cref="SidResolutionHelper.GetInteractiveUserSid"/>.
    /// Caller owns the returned handle and must close it.
    /// </summary>
    public static IntPtr TryGetExplorerToken(ILoggingService log)
    {
        var interactiveSid = SidResolutionHelper.GetInteractiveUserSid();
        if (interactiveSid == null)
            return IntPtr.Zero;

        return TryGetExplorerTokenForSid(interactiveSid, log);
    }

    /// <summary>
    /// Returns an open token handle from explorer.exe owned by the interactive user.
    /// SID-verified. Throws if no matching explorer.exe is found.
    /// Caller owns the returned handle and must close it.
    /// </summary>
    public static IntPtr GetExplorerToken(ILoggingService log)
    {
        var hToken = TryGetExplorerToken(log);
        if (hToken == IntPtr.Zero)
            throw new InvalidOperationException(
                "Interactive user launch failed: no explorer.exe owned by the interactive user is running in the current session.");
        return hToken;
    }

    /// <summary>
    /// Returns an open token handle from any explorer.exe in the current session,
    /// without SID verification. Used by <see cref="AppContainerLauncher"/> which needs
    /// the interactive user's token regardless of whether the interactive user differs
    /// from the current (admin) user.
    /// Caller owns the returned handle and must close it.
    /// </summary>
    public static IntPtr GetSessionExplorerToken(ILoggingService log)
    {
        var currentSessionId = Process.GetCurrentProcess().SessionId;
        var explorers = Process.GetProcessesByName("explorer");
        try
        {
            foreach (var proc in explorers)
            {
                try
                {
                    if (proc.SessionId != currentSessionId)
                        continue;
                    if (NativeMethods.OpenProcessToken(proc.Handle,
                            TOKEN_QUERY | TOKEN_DUPLICATE | TOKEN_IMPERSONATE, out var hToken))
                    {
                        log.Info($"ExplorerTokenHelper: Acquired session token from explorer.exe (PID {proc.Id})");
                        return hToken;
                    }
                }
                catch
                {
                    /* skip inaccessible process */
                }
            }
        }
        finally
        {
            foreach (var p in explorers)
                p.Dispose();
        }

        throw new InvalidOperationException(
            "explorer.exe is not running in the current session. " +
            "The interactive desktop user's token is required.");
    }

    private static IntPtr TryGetExplorerTokenForSid(string targetSid, ILoggingService log)
    {
        var currentSessionId = Process.GetCurrentProcess().SessionId;
        var explorers = Process.GetProcessesByName("explorer");
        try
        {
            foreach (var proc in explorers)
            {
                try
                {
                    if (proc.SessionId != currentSessionId)
                        continue;

                    if (!NativeMethods.OpenProcessToken(proc.Handle,
                            TOKEN_QUERY | TOKEN_DUPLICATE | TOKEN_IMPERSONATE, out var hToken))
                        continue;

                    try
                    {
                        var ownerSid = GetTokenOwnerSid(hToken);
                        if (ownerSid != null && string.Equals(ownerSid, targetSid, StringComparison.OrdinalIgnoreCase))
                        {
                            log.Info($"ExplorerTokenHelper: Acquired SID-verified token from explorer.exe (PID {proc.Id})");
                            return hToken;
                        }
                    }
                    catch
                    {
                        // GetTokenOwnerSid may throw on invalid SID data
                    }

                    NativeMethods.CloseHandle(hToken);
                }
                catch
                {
                    /* skip inaccessible process */
                }
            }
        }
        finally
        {
            foreach (var p in explorers)
                p.Dispose();
        }

        return IntPtr.Zero;
    }

    private static string? GetTokenOwnerSid(IntPtr hToken)
    {
        NativeMethods.GetTokenInformation(hToken, TOKEN_USER, IntPtr.Zero, 0, out var needed);
        if (needed <= 0)
            return null;

        var buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!NativeMethods.GetTokenInformation(hToken, TOKEN_USER, buffer, needed, out _))
                return null;
            var sidPtr = Marshal.ReadIntPtr(buffer);
            return new SecurityIdentifier(sidPtr).Value;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}