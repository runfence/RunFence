namespace RunFence.UI;

/// <summary>
/// Encapsulates the notification mechanism that tells the host that settings have changed
/// (pending edit). <see cref="NotifyPendingEdit"/> triggers the host's settings-changed callback,
/// which the panel calls instead of directly raising events on itself.
/// </summary>
public class OptionsPendingEditNotifier(Action notifySettingsChanged)
{
    /// <summary>
    /// Notifies the host that settings have changed (pending edit available).
    /// </summary>
    public void NotifyPendingEdit() => notifySettingsChanged();
}
