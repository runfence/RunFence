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
    /// Applies the requested ICMP-block setting value without depending on WinForms event state.
    /// </summary>
    public void SetBlockIcmp(bool value)
    {
        appStateProvider.Database.Settings.BlockIcmpWhenInternetBlocked = value;
        var snapshot = appStateProvider.Database.CreateSnapshot();
        Task.Run(() =>
        {
            try
            {
                globalIcmpSettingsApplier.ApplyGlobalIcmpSetting(snapshot);
            }
            catch (Exception ex)
            {
                log.Error("Failed to enforce global ICMP block", ex);
                uiThreadInvoker.BeginInvoke(() =>
                {
                    if (_blockIcmpCheckBox.IsDisposed)
                        return;
                    _saveSettings();
                    MessageBox.Show($"ICMP block setting was saved, but enforcement will retry: {ex.Message}", "RunFence",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                });
                return;
            }

            uiThreadInvoker.BeginInvoke(_saveSettings);
        });
    }
}
