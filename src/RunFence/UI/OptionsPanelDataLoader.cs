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
    public async Task<(OptionsPanelState State, bool SettingsChangedByLicense)> LoadSettingsAsync(AppSettings settings)
    {
        bool autoStartEnabled;
        try
        {
            autoStartEnabled = await autoStartService.IsAutoStartEnabled();
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

            if (settings.AutoLockInBackground)
            {
                settings.AutoLockInBackground = false;
                settingsChangedByLicense = true;
            }
        }

        var idleEnabled = settings.IdleTimeoutMinutes > 0;

        return (new OptionsPanelState(
            AutoStartEnabled: autoStartEnabled,
            IdleTimeoutEnabled: idleEnabled,
            IdleTimeoutMinutes: idleEnabled ? Math.Clamp(settings.IdleTimeoutMinutes, 0, 999) : 30,
            AutoLockEnabled: settings.AutoLockInBackground,
            AutoLockTimeoutMinutes: Math.Clamp(settings.AutoLockTimeoutMinutes, 0, 999),
            ShowForegroundPrivilegeMarker: settings.ShowForegroundPrivilegeMarker,
            ShowForegroundPrivilegeMarkerWhenFullscreen: settings.ShowForegroundPrivilegeMarkerWhenFullscreen,
            FolderBrowserExePath: settings.FolderBrowserExePath,
            FolderBrowserArguments: settings.FolderBrowserArguments,
            DefaultDesktopSettingsPath: settings.DefaultDesktopSettingsPath,
            UnlockModeIndex: (int)settings.UnlockMode,
            EnableContextMenu: settings.EnableRunAsContextMenu,
            LogVerbosity: settings.LogVerbosity), settingsChangedByLicense);
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
    bool ShowForegroundPrivilegeMarker,
    bool ShowForegroundPrivilegeMarkerWhenFullscreen,
    string FolderBrowserExePath,
    string FolderBrowserArguments,
    string DefaultDesktopSettingsPath,
    int UnlockModeIndex,
    bool EnableContextMenu,
    LogVerbosity LogVerbosity);
