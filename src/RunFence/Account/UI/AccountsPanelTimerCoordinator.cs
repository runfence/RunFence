namespace RunFence.Account.UI;

/// <summary>
/// Coordinates the account-check timer for the accounts panel.
/// Owns start/stop lifecycle and visibility forwarding for the change-detection timer.
/// Process timers are managed separately by <see cref="AccountProcessDisplayManager"/>.
/// </summary>
public class AccountsPanelTimerCoordinator(AccountCheckTimerService checkTimerService)
{
    private bool _started;

    public event Action? SidChangeDetected;
    public event Action? RefreshNeeded;

    /// <summary>
    /// Starts the account-check timer. Safe to call multiple times — subsequent calls are no-ops.
    /// </summary>
    public void Start()
    {
        if (_started)
            return;
        _started = true;
        checkTimerService.SidChangeDetected += () => SidChangeDetected?.Invoke();
        checkTimerService.RefreshNeeded += () => RefreshNeeded?.Invoke();
        checkTimerService.Start();
    }

    public void Stop()
        => checkTimerService.Stop();

    public void NotifyVisibilityChanged(bool isVisible)
        => checkTimerService.HandleVisibilityChanged(isVisible);
}