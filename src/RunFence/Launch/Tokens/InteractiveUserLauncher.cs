using System.Diagnostics;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Launch.Tokens;

/// <summary>
/// Launches a process under the interactive user's token via CreateProcessWithTokenW.
/// Acquires the token from explorer.exe, duplicates it, and launches.
/// No split token or low integrity manipulation — those are handled by
/// <see cref="SplitTokenLauncher"/> and <see cref="LowIntegrityLauncher"/>.
/// </summary>
public class InteractiveUserLauncher(ILoggingService log) : IInteractiveUserLauncher
{
    public int Launch(ProcessStartInfo psi, Dictionary<string, string>? extraEnvVars = null, bool hideWindow = false)
    {
        IntPtr hToken = IntPtr.Zero;
        IntPtr hDupToken = IntPtr.Zero;
        var envBlock = new NativeEnvironmentBlock();
        try
        {
            hToken = ExplorerTokenHelper.GetExplorerToken(log);
            hDupToken = NativeTokenAcquisition.DuplicateToken(hToken);

            if (ProcessLaunchNative.CreateEnvironmentBlock(out var pEnv, hDupToken, false))
                envBlock = new NativeEnvironmentBlock(pEnv, isOverridden: false);
            else
                log.Warn("InteractiveUserLauncher: CreateEnvironmentBlock failed — process will inherit parent environment");

            envBlock.MergeInPlace(extraEnvVars);

            var pi = ProcessLaunchNative.LaunchWithToken(hDupToken, psi, envBlock.Pointer, hideWindow: hideWindow);
            var pid = (int)pi.dwProcessId;
            NativeMethods.CloseHandle(pi.hProcess);
            NativeMethods.CloseHandle(pi.hThread);
            return pid;
        }
        finally
        {
            envBlock.Dispose();
            if (hDupToken != IntPtr.Zero)
                NativeMethods.CloseHandle(hDupToken);
            if (hToken != IntPtr.Zero)
                NativeMethods.CloseHandle(hToken);
        }
    }
}