using System.Runtime.InteropServices;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Launch.Container;

/// <summary>
/// Handles AppContainer profile creation and token virtualization setup.
/// All methods are best-effort and log warnings on failure.
/// </summary>
public class AppContainerProfileSetup(ILoggingService log, IAppContainerEnvironmentSetup environmentSetup)
{
    /// <summary>
    /// Creates the AppContainer profile under the interactive user's HKCU by impersonating
    /// the provided token. Also writes shell folder redirects for SHGetFolderPath.
    /// </summary>
    public void EnsureProfileUnderToken(AppContainerEntry entry, IntPtr hToken)
    {
        try
        {
            if (!AppContainerNative.ImpersonateLoggedOnUser(hToken))
            {
                log.Warn($"AppContainerLauncher: ImpersonateLoggedOnUser failed (error {Marshal.GetLastWin32Error()}), " +
                         "creating profile under elevated user context");
                EnsureProfileDirect(entry);
                return;
            }

            try
            {
                var hr = AppContainerNative.CreateAppContainerProfile(
                    entry.Name, entry.DisplayName,
                    $"RunFence AppContainer: {entry.DisplayName}",
                    IntPtr.Zero, 0, out var sid);

                if (sid != IntPtr.Zero)
                    NativeMethods.LocalFree(sid);

                if (hr == ProcessLaunchNative.HrAlreadyExists)
                    log.Info($"AppContainerLauncher: Profile '{entry.Name}' already exists");
                else if (hr != 0)
                    log.Warn($"AppContainerLauncher: CreateAppContainerProfile returned HRESULT 0x{hr:X8} for '{entry.Name}'");
                else
                    log.Info($"AppContainerLauncher: Profile '{entry.Name}' created under interactive user");

                environmentSetup.WriteShellFolderRedirects(entry.Name);
            }
            finally
            {
                AppContainerNative.RevertToSelf();
            }
        }
        catch (Exception ex)
        {
            log.Warn($"AppContainerLauncher: EnsureProfileUnderToken failed for '{entry.Name}': {ex.Message}");
        }
    }

    /// <summary>Fallback: create profile without impersonation (elevated user's HKCU).</summary>
    private void EnsureProfileDirect(AppContainerEntry entry)
    {
        try
        {
            var hr = AppContainerNative.CreateAppContainerProfile(
                entry.Name, entry.DisplayName,
                $"RunFence AppContainer: {entry.DisplayName}",
                IntPtr.Zero, 0, out var sid);
            if (sid != IntPtr.Zero)
                NativeMethods.LocalFree(sid);

            if (hr != 0 && hr != ProcessLaunchNative.HrAlreadyExists)
                log.Warn($"AppContainerLauncher: CreateAppContainerProfile returned HRESULT 0x{hr:X8} for '{entry.Name}'");

            environmentSetup.WriteShellFolderRedirects(entry.Name);
        }
        catch (Exception ex)
        {
            log.Warn($"AppContainerLauncher: EnsureProfileDirect failed for '{entry.Name}': {ex.Message}");
        }
    }

    /// <summary>
    /// Enables UAC file virtualization on the AppContainer token if the OS permits it, so that
    /// legacy 32-bit apps without a requestedExecutionLevel manifest can redirect writes to VirtualStore.
    /// <para>
    /// Setting <c>TokenVirtualizationAllowed</c> requires <c>SeCreateTokenPrivilege</c>, which is
    /// not present in standard admin tokens. Instead we query the current value and only call
    /// <c>SetTokenInformation(TokenVirtualizationEnabled)</c> when the OS has already allowed it.
    /// For AppContainer tokens the OS sets allowed=0, so virtualization is silently skipped.
    /// </para>
    /// Best-effort — logs warnings on unexpected failure without aborting the launch.
    /// </summary>
    public void TryEnableVirtualization(IntPtr hToken)
    {
        try
        {
            var allowed = QueryTokenUInt(hToken, ProcessLaunchNative.TOKEN_VIRTUALIZATION_ALLOWED);
            if (allowed == null)
            {
                log.Warn($"AppContainerLauncher: GetTokenInformation(TokenVirtualizationAllowed) failed — error {Marshal.GetLastWin32Error()}");
                return;
            }

            if (allowed == 0)
            {
                log.Info("AppContainerLauncher: TokenVirtualizationAllowed=0 on AppContainer token — UAC file virtualization not available");
                return;
            }

            uint enabled = 1;
            if (!ProcessLaunchNative.SetTokenInformation(hToken,
                    ProcessLaunchNative.TOKEN_VIRTUALIZATION_ENABLED, ref enabled, sizeof(uint)))
                log.Warn($"AppContainerLauncher: SetTokenInformation(TokenVirtualizationEnabled) failed — error {Marshal.GetLastWin32Error()}");
            else
                log.Info("AppContainerLauncher: UAC file virtualization enabled on AppContainer token");
        }
        catch (Exception ex)
        {
            log.Warn($"AppContainerLauncher: TryEnableVirtualization failed: {ex.Message}");
        }
    }

    private static uint? QueryTokenUInt(IntPtr hToken, int infoClass)
    {
        var buffer = Marshal.AllocHGlobal(sizeof(uint));
        try
        {
            return NativeMethods.GetTokenInformation(hToken, infoClass, buffer, sizeof(uint), out _)
                ? (uint)Marshal.ReadInt32(buffer)
                : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
