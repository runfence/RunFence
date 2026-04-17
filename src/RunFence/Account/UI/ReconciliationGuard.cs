namespace RunFence.Account.UI;

/// <summary>
/// Shared flag used to prevent concurrent grant reconciliation between
/// <see cref="AccountCheckTimerService"/> (timer-triggered) and
/// <see cref="AccountGridRefreshHandler"/> (manual refresh-triggered).
/// Both services run on the UI thread, so no synchronization is required.
/// </summary>
public class ReconciliationGuard
{
    public bool IsInProgress { get; set; }
}
