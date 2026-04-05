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
        var session = sessionProvider.GetSession();
        using var scope = session.PinDerivedKey.Unprotect();
        configRepository.SaveConfig(session.Database, scope.Data, session.CredentialStore.ArgonSalt);
        onSettingsChanged();
    }

    /// <summary>
    /// Saves the current database settings (IPC caller list) and invokes the callbacks.
    /// </summary>
    public void SaveCallerChanges(Action refreshCallerList, Action onDataChanged)
    {
        var session = sessionProvider.GetSession();
        using var scope = session.PinDerivedKey.Unprotect();
        configRepository.SaveConfig(session.Database, scope.Data, session.CredentialStore.ArgonSalt);
        refreshCallerList();
        onDataChanged();
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