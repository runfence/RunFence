using RunFence.Infrastructure;
using RunFence.Persistence;
using Timer = System.Windows.Forms.Timer;

namespace RunFence.UI.Forms;

/// <summary>
/// Handles saving settings and caller configuration from <see cref="OptionsPanel"/>.
/// Holds the save infrastructure deps so OptionsPanel does not need IConfigRepository directly.
/// </summary>
public class OptionsSettingsHandler(IConfigRepository configRepository, ISessionProvider sessionProvider)
{
    private Timer? _saveDebounceTimer;

    /// <summary>
    /// Saves the current database settings and invokes the callback.
    /// </summary>
    public void SaveSettings(Action onSettingsChanged)
    {
        Save();
        onSettingsChanged();
    }

    /// <summary>
    /// Saves the current database settings (IPC caller list) and invokes the callbacks.
    /// </summary>
    public void SaveCallerChanges(Action refreshCallerList, Action onDataChanged)
    {
        Save();
        refreshCallerList();
        onDataChanged();
    }

    private void Save()
    {
        var session = sessionProvider.GetSession();
        configRepository.SaveConfig(
            session.Database,
            session.PinDerivedKey,
            session.CredentialStore.ArgonSalt);
    }

    /// <summary>
    /// Schedules a debounced save. The actual save runs after a 500 ms idle period.
    /// </summary>
    public void DebounceSave(Action saveSettings)
    {
        if (_saveDebounceTimer == null)
        {
            _saveDebounceTimer = new Timer { Interval = 500 };
            _saveDebounceTimer.Tick += (_, _) =>
            {
                _saveDebounceTimer.Stop();
                saveSettings();
            };
        }

        _saveDebounceTimer.Stop();
        _saveDebounceTimer.Start();
    }

    /// <summary>
    /// Cancels any pending debounced save without flushing.
    /// Used before data deletion to prevent Dispose from re-creating deleted files.
    /// </summary>
    public void CancelPendingSave()
    {
        _saveDebounceTimer?.Stop();
        _saveDebounceTimer?.Dispose();
        _saveDebounceTimer = null;
    }

    /// <summary>
    /// If a debounced save is pending, cancels the timer and flushes it synchronously.
    /// Called from OptionsPanel.Dispose to ensure in-flight changes are persisted.
    /// </summary>
    public void FlushPendingSave(Action saveSettings)
    {
        if (_saveDebounceTimer?.Enabled == true)
        {
            _saveDebounceTimer.Stop();
            saveSettings();
        }

        _saveDebounceTimer?.Dispose();
        _saveDebounceTimer = null;
    }
}
