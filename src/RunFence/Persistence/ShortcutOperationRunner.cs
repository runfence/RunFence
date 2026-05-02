using System.Runtime.ExceptionServices;
using RunFence.Core;

namespace RunFence.Persistence;

/// <summary>
/// Production implementation that applies a 1-second bounded timeout to every shortcut COM operation,
/// matching the previous behavior before the timeout protection was removed.
/// </summary>
public class ShortcutOperationRunner(ILoggingService log) : IShortcutOperationRunner
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    public T Run<T>(Func<T> operation, string operationName, T timeoutValue)
    {
        T result = timeoutValue;
        Exception? threadException = null;

        var thread = new Thread(() =>
        {
            try { result = operation(); }
            catch (Exception ex) { threadException = ex; }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!thread.Join(Timeout))
        {
            log.Warn($"Shortcut COM operation '{operationName}' timed out after {Timeout.TotalSeconds:0}s; returning default value.");
            return timeoutValue;
        }

        if (threadException != null)
            ExceptionDispatchInfo.Capture(threadException).Throw();

        return result;
    }

    public void Run(Action operation, string operationName)
    {
        Exception? threadException = null;

        var thread = new Thread(() =>
        {
            try
            {
                operation();
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!thread.Join(Timeout))
        {
            log.Warn($"Shortcut COM operation '{operationName}' timed out after {Timeout.TotalSeconds:0}s.");
            throw new TimeoutException($"Shortcut COM operation '{operationName}' timed out.");
        }

        if (threadException != null)
            ExceptionDispatchInfo.Capture(threadException).Throw();
    }
}
