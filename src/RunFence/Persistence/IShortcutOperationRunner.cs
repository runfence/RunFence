namespace RunFence.Persistence;

/// <summary>
/// Executes shortcut COM operations with a bounded timeout so they never hang indefinitely.
/// </summary>
public interface IShortcutOperationRunner
{
    /// <summary>
    /// Runs <paramref name="operation"/> with a bounded timeout.
    /// Returns <paramref name="timeoutValue"/> and logs a warning when the timeout expires.
    /// </summary>
    T Run<T>(Func<T> operation, string operationName, T timeoutValue);

    /// <summary>
    /// Runs <paramref name="operation"/> with a bounded timeout.
    /// Logs a warning and throws <see cref="TimeoutException"/> when the timeout expires
    /// so the caller can surface a controlled failure instead of silently reporting success.
    /// </summary>
    void Run(Action operation, string operationName);
}
