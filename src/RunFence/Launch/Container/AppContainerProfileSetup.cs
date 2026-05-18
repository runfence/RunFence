using System.Runtime.InteropServices;
using System.Security.Principal;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Launch.Container;

/// <summary>
/// Handles AppContainer profile creation and token virtualization setup.
/// </summary>
public class AppContainerProfileSetup(
    ILoggingService log,
    IAppContainerEnvironmentSetup environmentSetup)
    : IAppContainerProfileSetup
{
    /// <summary>
    /// Creates the AppContainer profile under the interactive user's HKCU by impersonating
    /// the provided token. Also writes shell folder redirects for SHGetFolderPath.
    /// </summary>
    public AppContainerProfileSetupResult EnsureProfileUnderToken(AppContainerEntry entry, IntPtr hToken)
    {
        try
        {
            var interactiveUserSid = ResolveInteractiveUserSid();
            if (string.IsNullOrWhiteSpace(interactiveUserSid))
            {
                return AppContainerProfileSetupResult.Failure(
                    AppContainerProfileSetupStatus.ProfileFailed,
                    "Interactive user SID is unavailable.");
            }

            var tokenSid = ResolveTokenSid(hToken);
            if (!string.Equals(tokenSid, interactiveUserSid, StringComparison.OrdinalIgnoreCase))
            {
                return AppContainerProfileSetupResult.Failure(
                    AppContainerProfileSetupStatus.ProfileFailed,
                    $"Explorer token SID does not match the interactive user SID. Expected '{interactiveUserSid}', got '{tokenSid ?? "<null>"}'.");
            }

            if (!ProcessNative.ImpersonateLoggedOnUser(hToken))
            {
                return AppContainerProfileSetupResult.Failure(
                    AppContainerProfileSetupStatus.ProfileFailed,
                    $"ImpersonateLoggedOnUser failed with error {Marshal.GetLastWin32Error()}.");
            }

            try
            {
                var impersonatedSid = WindowsIdentity.GetCurrent().User?.Value;
                if (!string.Equals(impersonatedSid, interactiveUserSid, StringComparison.OrdinalIgnoreCase))
                {
                    return AppContainerProfileSetupResult.Failure(
                        AppContainerProfileSetupStatus.ProfileFailed,
                        $"Impersonation switched to '{impersonatedSid ?? "<null>"}' instead of '{interactiveUserSid}'.");
                }

                var hr = AppContainerNative.CreateAppContainerProfile(
                    entry.Name, entry.DisplayName,
                    $"RunFence AppContainer: {entry.DisplayName}",
                    IntPtr.Zero, 0, out var sid);

                if (sid != IntPtr.Zero)
                    ProcessNative.LocalFree(sid);

                if (hr != 0 && hr != ProcessLaunchNative.HrAlreadyExists)
                {
                    return AppContainerProfileSetupResult.Failure(
                        AppContainerProfileSetupStatus.ProfileFailed,
                        $"CreateAppContainerProfile failed with HRESULT 0x{hr:X8} for '{entry.Name}'.");
                }

                var redirectResult = environmentSetup.WriteShellFolderRedirects(entry.Name, interactiveUserSid);
                if (redirectResult.Status != AppContainerProfileSetupStatus.Succeeded)
                {
                    return AppContainerProfileSetupResult.Failure(
                        redirectResult.Status,
                        redirectResult.ErrorMessage ?? "AppContainer shell folder redirect setup failed.",
                        profileCreatedOrAlreadyExists: true,
                        shellFolderRedirectsWritten: redirectResult.ShellFolderRedirectsWritten,
                        environmentRewritten: redirectResult.EnvironmentRewritten,
                        warnings: redirectResult.Warnings);
                }

                return AppContainerProfileSetupResult.Success(
                    profileCreatedOrAlreadyExists: true,
                    shellFolderRedirectsWritten: true);
            }
            finally
            {
                ProcessNative.RevertToSelf();
            }
        }
        catch (Exception ex)
        {
            return AppContainerProfileSetupResult.Failure(
                AppContainerProfileSetupStatus.ProfileFailed,
                $"EnsureProfileUnderToken failed for '{entry.Name}': {ex.Message}");
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
    /// Best-effort - logs warnings on unexpected failure without aborting the launch.
    /// </summary>
    public void TryEnableVirtualization(IntPtr hToken)
    {
        try
        {
            var allowed = QueryTokenUInt(hToken, ProcessLaunchNative.TOKEN_VIRTUALIZATION_ALLOWED);
            if (allowed == null)
            {
                log.Warn($"AppContainerLauncher: GetTokenInformation(TokenVirtualizationAllowed) failed - error {Marshal.GetLastWin32Error()}");
                return;
            }

            if (allowed == 0)
            {
                log.Info("AppContainerLauncher: TokenVirtualizationAllowed=0 on AppContainer token - UAC file virtualization not available");
                return;
            }

            uint enabled = 1;
            if (!ProcessLaunchNative.SetTokenInformation(hToken,
                    ProcessLaunchNative.TOKEN_VIRTUALIZATION_ENABLED, ref enabled, sizeof(uint)))
                log.Warn($"AppContainerLauncher: SetTokenInformation(TokenVirtualizationEnabled) failed - error {Marshal.GetLastWin32Error()}");
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
            return ProcessNative.GetTokenInformation(hToken, infoClass, buffer, sizeof(uint), out _)
                ? (uint)Marshal.ReadInt32(buffer)
                : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string? ResolveInteractiveUserSid()
        => SidResolutionHelper.GetInteractiveUserSid() ?? NativeTokenHelper.TryGetInteractiveUserSid()?.Value;

    private static string? ResolveTokenSid(IntPtr hToken)
    {
        const int tokenUser = 1;
        ProcessNative.GetTokenInformation(hToken, tokenUser, IntPtr.Zero, 0, out var needed);
        if (needed <= 0)
            return null;

        var buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!ProcessNative.GetTokenInformation(hToken, tokenUser, buffer, needed, out _))
                return null;

            return new SecurityIdentifier(Marshal.ReadIntPtr(buffer)).Value;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
