namespace RunFence.Infrastructure;

/// <summary>
/// Dispatches an action onto the UI thread. Used by background services that need
/// to marshal work (e.g., COM calls, UI updates) back to the UI thread.
/// </summary>
public interface IUiThreadInvoker
{
    void Invoke(Action action);
    void BeginInvoke(Action action);

    /// <summary>
    /// Runs <paramref name="action"/> on the UI thread. Executes directly if already on the
    /// UI thread; probes responsiveness from a background thread and uses blocking
    /// <see cref="Invoke"/> if the UI is pumping, or <see cref="BeginInvoke"/> if it is blocked.
    /// </summary>
    void RunOnUiThread(Action action);
}