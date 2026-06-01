using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using RunFence.Infrastructure;

namespace RunFence.Tests;

public sealed class DedicatedThreadUiInvoker : IUiThreadInvoker, IDisposable
{
    private readonly BlockingCollection<Action> _queue = [];
    private readonly Thread _thread;
    private bool _disposed;

    public DedicatedThreadUiInvoker()
    {
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            ThreadId = Environment.CurrentManagedThreadId;
            ready.Set();
            foreach (var action in _queue.GetConsumingEnumerable())
                action();
        });
        _thread.IsBackground = true;
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait();
    }

    public int ThreadId { get; private set; }

    public T Invoke<T>(Func<T> func)
    {
        if (Environment.CurrentManagedThreadId == ThreadId)
            return func();

        T result = default!;
        Exception? error = null;
        using var done = new ManualResetEventSlim();
        _queue.Add(() =>
        {
            try
            {
                result = func();
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                done.Set();
            }
        });

        done.Wait();
        if (error != null)
            ExceptionDispatchInfo.Throw(error);

        return result;
    }

    public void BeginInvoke(Action action) => _queue.Add(action);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _queue.CompleteAdding();
        if (_thread.IsAlive)
            _thread.Join();
        _queue.Dispose();
    }
}
