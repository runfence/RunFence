using RunFence.Core.Models;
using RunFence.Licensing;
using RunFence.Security;

namespace RunFence.UI;

/// <summary>
/// Handles the auto-lock group of controls (auto-lock checkbox, timeout numeric), idle timeout,
/// and unlock mode for <see cref="Forms.OptionsPanel"/>.
/// These settings share state: the auto-lock checkbox governs whether the timeout control is active,
/// and unlock mode changes may be rejected (e.g. Windows Hello unavailable) — callers must revert the UI.
/// </summary>
public class OptionsPanelCheckboxHandler
{
    private readonly ILicenseService _licenseService;
    private readonly IWindowsHelloService _windowsHello;

    private CheckBox _autoLockCheckBox = null!;
    private NumericUpDown _autoLockTimeoutUpDown = null!;
    private Func<AppSettings> _getSettings = null!;
    private Action _save = null!;

    public OptionsPanelCheckboxHandler(
        ILicenseService licenseService,
        IWindowsHelloService windowsHello)
    {
        _licenseService = licenseService;
        _windowsHello = windowsHello;
    }

    public void Initialize(
        CheckBox autoLockCheckBox,
        NumericUpDown autoLockTimeoutUpDown,
        Func<AppSettings> getSettings,
        Action save)
    {
        _autoLockCheckBox = autoLockCheckBox;
        _autoLockTimeoutUpDown = autoLockTimeoutUpDown;
        _getSettings = getSettings;
        _save = save;
    }

    /// <summary>
    /// Sets the idle timeout. Returns false if the license check fails (caller should revert UI).
    /// Updates <see cref="AppSettings.IdleTimeoutMinutes"/> on success.
    /// </summary>
    public bool SetIdleTimeout(bool enabled, int minutes, AppSettings settings)
    {
        if (enabled && !_licenseService.IsLicensed)
            return false;

        settings.IdleTimeoutMinutes = enabled ? minutes : 0;
        return true;
    }

    /// <summary>
    /// Sets the auto-lock in background feature. Returns false if the license check fails (caller should revert UI).
    /// Updates <see cref="AppSettings.AutoLockInBackground"/> on success.
    /// </summary>
    public bool SetAutoLock(bool enabled, AppSettings settings)
    {
        if (enabled && !_licenseService.IsLicensed)
            return false;

        settings.AutoLockInBackground = enabled;
        return true;
    }

    public void OnAutoLockChanged(bool enabled)
    {
        if (!SetAutoLock(enabled, _getSettings()))
        {
            _autoLockCheckBox.Checked = false;
            MessageBox.Show("Auto-lock requires a license.", "License Required",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _autoLockTimeoutUpDown.Enabled = enabled;
        _save();
    }

    public void OnAutoLockTimeoutChanged(int minutes)
    {
        _getSettings().AutoLockTimeoutMinutes = minutes;
    }

    /// <summary>
    /// Applies the new unlock mode.
    /// Returns <c>false</c> if the mode was rejected (e.g. Windows Hello unavailable) — callers must revert the UI combo to the previous selection.
    /// Returns <c>true</c> if the mode was accepted and saved.
    /// </summary>
    public async Task<bool> OnUnlockModeChanged(UnlockMode newMode)
    {
        if (newMode == UnlockMode.WindowsHello)
        {
            bool available = await _windowsHello.IsAvailableAsync();
            if (!available)
            {
                MessageBox.Show(
                    "Windows Hello is not available or not configured for this account.",
                    "Windows Hello Unavailable",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return false;
            }
        }

        _getSettings().UnlockMode = newMode;
        _save();
        return true;
    }
}
