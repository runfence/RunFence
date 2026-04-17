using RunFence.Core;

namespace RunFence.Infrastructure;

/// <summary>
/// Dispatches an action onto the UI thread. Used by background services that need
/// to marshal work (e.g., COM calls, UI updates) back to the UI thread.
/// </summary>
public interface IUiThreadInvoker
{
    /// <summary>
    /// Executes <paramref name="func"/> on the UI thread and returns the result.
    /// Always blocking — guarantees completion before the caller continues.
    /// </summary>
    T Invoke<T>(Func<T> func);

    /// <summary>
    /// Executes <paramref name="action"/> on the UI thread. Always blocking.
    /// Default implementation forwards to <see cref="Invoke{T}"/>.
    /// </summary>
    void Invoke(Action action)
    {
        Invoke(() => { action(); return default(VoidStruct); });
    }

    void BeginInvoke(Action action);

    /// <summary>
    /// Runs <paramref name="action"/> on the UI thread. Executes directly if already on the
    /// UI thread; probes responsiveness from a background thread and uses blocking
    /// <see cref="Invoke{T}"/> if the UI is pumping, or <see cref="BeginInvoke"/> if it is blocked.
    /// </summary>
    void RunOnUiThread(Action action);
}