using RunFence.Core.Models;
using RunFence.Licensing;
using RunFence.Startup;

namespace RunFence.UI;

/// <summary>
/// Reads settings and license state to produce an <see cref="OptionsPanelState"/> record
/// that <see cref="RunFence.UI.Forms.OptionsPanel"/> applies to its controls.
/// Separates data reading from UI control manipulation.
/// </summary>
public class OptionsPanelDataLoader(IAutoStartService autoStartService, ILicenseService licenseService)
{
    /// <summary>
    /// Reads settings and license state, enforces license restrictions, and returns a state record.
    /// May mutate <paramref name="settings"/> to enforce license restrictions (idle timeout, auto-lock).
    /// Returns whether settings were changed by license enforcement (caller should save).
    /// </summary>
    public (OptionsPanelState State, bool SettingsChangedByLicense) LoadSettings(AppSettings settings)
    {
        bool autoStartEnabled;
        try
        {
            autoStartEnabled = autoStartService.IsAutoStartEnabled();
        }
        catch
        {
            autoStartEnabled = false;
        }

        var isLicensed = licenseService.IsLicensed;
        bool settingsChangedByLicense = false;

        if (!isLicensed)
        {
            if (settings.IdleTimeoutMinutes > 0)
            {
                settings.IdleTimeoutMinutes = 0;
                settingsChangedByLicense = true;
            }

            if (settings.AutoLockOnMinimize)
            {
                settings.AutoLockOnMinimize = false;
                settingsChangedByLicense = true;
            }
        }

        var idleEnabled = settings.IdleTimeoutMinutes > 0;

        return (new OptionsPanelState(
            AutoStartEnabled: autoStartEnabled,
            IdleTimeoutEnabled: idleEnabled,
            IdleTimeoutMinutes: idleEnabled ? settings.IdleTimeoutMinutes : 30,
            AutoLockEnabled: settings.AutoLockOnMinimize,
            AutoLockTimeoutMinutes: Math.Clamp(settings.AutoLockTimeoutMinutes, 0, 999),
            FolderBrowserExePath: settings.FolderBrowserExePath,
            FolderBrowserArguments: settings.FolderBrowserArguments,
            DefaultDesktopSettingsPath: settings.DefaultDesktopSettingsPath,
            UnlockModeIndex: (int)settings.UnlockMode,
            EnableContextMenu: settings.EnableRunAsContextMenu,
            EnableLogging: settings.EnableLogging), settingsChangedByLicense);
    }
}

/// <summary>
/// Pre-computed display state for <see cref="RunFence.UI.Forms.OptionsPanel"/> controls.
/// </summary>
public record OptionsPanelState(
    bool AutoStartEnabled,
    bool IdleTimeoutEnabled,
    int IdleTimeoutMinutes,
    bool AutoLockEnabled,
    int AutoLockTimeoutMinutes,
    string FolderBrowserExePath,
    string FolderBrowserArguments,
    string DefaultDesktopSettingsPath,
    int UnlockModeIndex,
    bool EnableContextMenu,
    bool EnableLogging);