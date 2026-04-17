using RunFence.Core;
using RunFence.Firewall;
using RunFence.Infrastructure;

namespace RunFence.UI;

/// <summary>
/// Owns the ICMP block checkbox event handler for <see cref="RunFence.UI.Forms.OptionsPanel"/>:
/// updating the setting, applying it on a background thread (marshaled back to UI via
/// <see cref="IUiThreadInvoker"/>), and reverting the control on failure.
/// </summary>
public class OptionsIcmpSection(
    IGlobalIcmpSettingsApplier globalIcmpSettingsApplier,
    IAppStateProvider appStateProvider,
    IUiThreadInvoker uiThreadInvoker,
    ILoggingService log)
{
    private CheckBox _blockIcmpCheckBox = null!;
    private Action _saveSettings = null!;

    /// <summary>
    /// Wires the checkbox control and provides the save action.
    /// Must be called before the checkbox's <c>CheckedChanged</c> event is subscribed.
    /// </summary>
    public void Initialize(CheckBox blockIcmpCheckBox, Action saveSettings)
    {
        _blockIcmpCheckBox = blockIcmpCheckBox;
        _saveSettings = saveSettings;
    }

    /// <summary>
    /// Handles the ICMP checkbox change: updates the setting and applies it on a background thread.
    /// On success, saves settings on the UI thread. On failure, reverts the checkbox and shows an error.
    /// Matches the <see cref="EventHandler"/> signature for wiring to <c>CheckedChanged</c>.
    /// </summary>
    public void OnBlockIcmpChanged(object? sender, EventArgs e)
    {
        var newValue = _blockIcmpCheckBox.Checked;
        appStateProvider.Database.Settings.BlockIcmpWhenInternetBlocked = newValue;
        var snapshot = appStateProvider.Database.CreateSnapshot();
        Task.Run(() =>
        {
            try
            {
                globalIcmpSettingsApplier.ApplyGlobalIcmpSetting(snapshot);
                uiThreadInvoker.BeginInvoke(_saveSettings);
            }
            catch (Exception ex)
            {
                log.Error("Failed to enforce global ICMP block", ex);
                uiThreadInvoker.BeginInvoke(() =>
                {
                    if (_blockIcmpCheckBox.IsDisposed)
                        return;
                    _blockIcmpCheckBox.CheckedChanged -= OnBlockIcmpChanged;
                    _blockIcmpCheckBox.Checked = !newValue;
                    appStateProvider.Database.Settings.BlockIcmpWhenInternetBlocked = !newValue;
                    _blockIcmpCheckBox.CheckedChanged += OnBlockIcmpChanged;
                    MessageBox.Show($"Failed to apply ICMP block setting: {ex.Message}", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                });
            }
        });
    }
}
