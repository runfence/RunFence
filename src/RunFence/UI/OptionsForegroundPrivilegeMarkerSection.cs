using RunFence.Core.Models;
using RunFence.Apps.UI;
using RunFence.ForegroundMarker;

namespace RunFence.UI;

/// <summary>
/// Owns the foreground marker checkbox event flow and persistence-before-runtime-apply warning handling.
/// </summary>
public sealed class OptionsForegroundPrivilegeMarkerSection(
    IForegroundPrivilegeMarkerService foregroundPrivilegeMarkerService,
    IMessageBoxService messageBoxService)
{
    private CheckBox _foregroundPrivilegeMarkerCheckBox = null!;
    private CheckBox _foregroundPrivilegeMarkerFullscreenCheckBox = null!;
    private Func<AppSettings> _getSettings = null!;
    private Action _saveSettings = null!;

    public void Initialize(
        CheckBox foregroundPrivilegeMarkerCheckBox,
        CheckBox foregroundPrivilegeMarkerFullscreenCheckBox,
        Func<AppSettings> getSettings,
        Action saveSettings)
    {
        _foregroundPrivilegeMarkerCheckBox = foregroundPrivilegeMarkerCheckBox;
        _foregroundPrivilegeMarkerFullscreenCheckBox = foregroundPrivilegeMarkerFullscreenCheckBox;
        _getSettings = getSettings;
        _saveSettings = saveSettings;
        _foregroundPrivilegeMarkerCheckBox.CheckedChanged += OnForegroundPrivilegeMarkerChanged;
        _foregroundPrivilegeMarkerFullscreenCheckBox.CheckedChanged += OnForegroundPrivilegeMarkerFullscreenChanged;
    }

    public void ApplyLoadedState(bool enabled, bool enabledWhenFullscreen)
    {
        _foregroundPrivilegeMarkerCheckBox.CheckedChanged -= OnForegroundPrivilegeMarkerChanged;
        _foregroundPrivilegeMarkerFullscreenCheckBox.CheckedChanged -= OnForegroundPrivilegeMarkerFullscreenChanged;
        _foregroundPrivilegeMarkerCheckBox.Checked = enabled;
        _foregroundPrivilegeMarkerFullscreenCheckBox.Checked = enabledWhenFullscreen;
        _foregroundPrivilegeMarkerFullscreenCheckBox.Enabled = enabled;
        _foregroundPrivilegeMarkerCheckBox.CheckedChanged += OnForegroundPrivilegeMarkerChanged;
        _foregroundPrivilegeMarkerFullscreenCheckBox.CheckedChanged += OnForegroundPrivilegeMarkerFullscreenChanged;
    }

    private void OnForegroundPrivilegeMarkerChanged(object? sender, EventArgs e)
    {
        var enabled = _foregroundPrivilegeMarkerCheckBox.Checked;
        var settings = _getSettings();
        settings.ShowForegroundPrivilegeMarker = enabled;
        _foregroundPrivilegeMarkerFullscreenCheckBox.Enabled = enabled;
        _saveSettings();

        try
        {
            foregroundPrivilegeMarkerService.SetMarkerWindowEnabled(enabled);
            return;
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }

        messageBoxService.Show(
            "Your preference was saved, but the foreground marker is unavailable for the rest of this RunFence session. Restart RunFence to apply the saved setting.",
            "Foreground Marker Unavailable",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    private void OnForegroundPrivilegeMarkerFullscreenChanged(object? sender, EventArgs e)
    {
        var enabledWhenFullscreen = _foregroundPrivilegeMarkerFullscreenCheckBox.Checked;
        var settings = _getSettings();
        settings.ShowForegroundPrivilegeMarkerWhenFullscreen = enabledWhenFullscreen;
        _saveSettings();

        try
        {
            foregroundPrivilegeMarkerService.SetMarkerWindowEnabledWhenFullscreen(enabledWhenFullscreen);
            return;
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }

        messageBoxService.Show(
            "Your preference was saved, but the foreground marker is unavailable for the rest of this RunFence session. Restart RunFence to apply the saved setting.",
            "Foreground Marker Unavailable",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }
}
