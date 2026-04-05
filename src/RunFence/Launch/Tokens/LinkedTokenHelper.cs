using System.Runtime.InteropServices;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Launch.Container;

namespace RunFence.Launch.Tokens;

/// <summary>
/// Runs actions impersonating the linked (non-elevated) interactive user token,
/// so that operations that need HKCU of the interactive user work correctly
/// in elevated/linked-session scenarios.
/// </summary>
public static class LinkedTokenHelper
{
    /// <summary>
    /// Runs an action impersonating the linked (non-elevated) interactive user token.
    /// The action receives the linked token handle (valid only for the duration of the call).
    /// Falls back to running without impersonation if no linked token is available.
    /// </summary>
    public static void RunUnderLinkedToken(Action<IntPtr> action, ILoggingService log)
    {
        IntPtr hCurrentToken = IntPtr.Zero;
        IntPtr hLinkedToken = IntPtr.Zero;
        try
        {
            if (!NativeMethods.OpenProcessToken(NativeMethods.GetCurrentProcess(),
                    ProcessLaunchNative.TOKEN_QUERY | ProcessLaunchNative.TOKEN_DUPLICATE |
                    ProcessLaunchNative.TOKEN_IMPERSONATE, out hCurrentToken))
            {
                log.Warn("RunUnderLinkedToken: OpenProcessToken failed, running without impersonation");
                action(IntPtr.Zero);
                return;
            }

            hLinkedToken = TryGetLinkedToken(hCurrentToken);
            if (hLinkedToken == IntPtr.Zero)
            {
                log.Warn("RunUnderLinkedToken: No linked token — running without impersonation");
                action(IntPtr.Zero);
                return;
            }

            if (!AppContainerNative.ImpersonateLoggedOnUser(hLinkedToken))
            {
                log.Warn("RunUnderLinkedToken: ImpersonateLoggedOnUser failed, running without impersonation");
                action(hLinkedToken);
                return;
            }

            try
            {
                action(hLinkedToken);
            }
            finally
            {
                AppContainerNative.RevertToSelf();
            }
        }
        finally
        {
            if (hLinkedToken != IntPtr.Zero)
                NativeMethods.CloseHandle(hLinkedToken);
            if (hCurrentToken != IntPtr.Zero)
                NativeMethods.CloseHandle(hCurrentToken);
        }
    }

    public static IntPtr TryGetLinkedToken(IntPtr hToken)
    {
        NativeMethods.GetTokenInformation(hToken, ProcessLaunchNative.TOKEN_LINKED_TOKEN, IntPtr.Zero, 0, out var size);
        if (size == 0)
            return IntPtr.Zero;

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (!NativeMethods.GetTokenInformation(hToken, ProcessLaunchNative.TOKEN_LINKED_TOKEN, buffer, size, out _))
                return IntPtr.Zero;
            return Marshal.ReadIntPtr(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}