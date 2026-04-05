using RunFence.Core.Models;
using RunFence.Licensing;
using RunFence.Startup;

namespace RunFence.UI;

/// <summary>
/// Handles auto-start and idle/auto-lock feature toggling for <see cref="RunFence.UI.Forms.OptionsPanel"/>.
/// Applies changes to <see cref="AppSettings"/> and returns whether the operation succeeded.
/// </summary>
public class OptionsAutoFeatureHandler(IAutoStartService autoStartService, ILicenseService licenseService)
{
    /// <summary>
    /// Enables or disables the Windows auto-start entry.
    /// Updates <see cref="AppSettings.AutoStartOnLogin"/> on success.
    /// Throws if the underlying auto-start service fails.
    /// </summary>
    public void SetAutoStart(bool enabled, AppSettings settings)
    {
        if (enabled)
            autoStartService.EnableAutoStart();
        else
            autoStartService.DisableAutoStart();

        settings.AutoStartOnLogin = enabled;
    }

    /// <summary>
    /// Sets the idle timeout. Returns false if the license check fails (caller should revert UI).
    /// Updates <see cref="AppSettings.IdleTimeoutMinutes"/> on success.
    /// </summary>
    public bool SetIdleTimeout(bool enabled, int minutes, AppSettings settings)
    {
        if (enabled && !licenseService.IsLicensed)
            return false;

        settings.IdleTimeoutMinutes = enabled ? minutes : 0;
        return true;
    }

    /// <summary>
    /// Sets the auto-lock on minimize feature. Returns false if the license check fails (caller should revert UI).
    /// Updates <see cref="AppSettings.AutoLockOnMinimize"/> on success.
    /// </summary>
    public bool SetAutoLock(bool enabled, AppSettings settings)
    {
        if (enabled && !licenseService.IsLicensed)
            return false;

        settings.AutoLockOnMinimize = enabled;
        return true;
    }
}