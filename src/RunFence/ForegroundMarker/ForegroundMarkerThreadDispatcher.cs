using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RunFence.ForegroundMarker;

public sealed class ForegroundMarkerThreadDispatcher : IForegroundMarkerThreadDispatcher
{
    private const uint InvokeMessage = ForegroundMarkerThreadDispatcherNative.WmApp + 1;
    private const uint StopMessage = ForegroundMarkerThreadDispatcherNative.WmApp + 2;

    private readonly object syncRoot = new();
    private readonly ConcurrentQueue<QueuedAction> actions = new();
    private Thread? thread;
    private int threadId;
    private uint nativeThreadId;
    private bool disposed;

    public bool IsCurrentThread => Environment.CurrentManagedThreadId == Volatile.Read(ref threadId);

    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        lock (syncRoot)
        {
            if (thread != null)
                return;

            var ready = new BlockingCollection<Exception?>(1);
            var startupReported = false;
            thread = new Thread(() =>
            {
                var previousContext = SynchronizationContext.Current;
                try
                {
                    Volatile.Write(ref threadId, Environment.CurrentManagedThreadId);
                    Volatile.Write(ref nativeThreadId, ForegroundMarkerThreadDispatcherNative.GetCurrentThreadId());
                    _ = ForegroundMarkerThreadDispatcherNative.PeekMessage(
                        out _,
                        IntPtr.Zero,
                        0,
                        0,
                        ForegroundMarkerThreadDispatcherNative.PmNoRemove);
                    SynchronizationContext.SetSynchronizationContext(new ForegroundMarkerSynchronizationContext(this));
                    ready.Add(null);
                    startupReported = true;
                    RunMessageLoop();
                }
            catch (Exception ex)
            {
                if (!startupReported)
                    ready.TryAdd(ex);
            }
                finally
                {
                    SynchronizationContext.SetSynchronizationContext(previousContext);
                    Volatile.Write(ref nativeThreadId, 0u);
                    Volatile.Write(ref threadId, 0);
                    lock (syncRoot)
                    {
                        if (ReferenceEquals(thread, Thread.CurrentThread))
                            thread = null;
                    }
                }
            })
            {
                IsBackground = true,
                Name = "ForegroundPrivilegeMarker"
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            var startupError = ready.Take();
            ready.Dispose();
            if (startupError != null)
            {
                thread = null;
                ExceptionDispatchInfo.Capture(startupError).Throw();
            }
        }
    }

    public void Stop()
    {
        Thread? threadToJoin;
        uint threadToStop;

        lock (syncRoot)
        {
            threadToJoin = thread;
            threadToStop = Volatile.Read(ref nativeThreadId);
            thread = null;
        }

        if (threadToJoin == null)
            return;

        if (IsCurrentThread)
        {
            ForegroundMarkerThreadDispatcherNative.PostQuitMessage(0);
            return;
        }

        if (threadToStop != 0)
            _ = ForegroundMarkerThreadDispatcherNative.PostThreadMessage(threadToStop, StopMessage, IntPtr.Zero, IntPtr.Zero);

        threadToJoin.Join();
    }

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (IsCurrentThread)
        {
            action();
            return;
        }

        EnqueueAction(QueuedAction.Post(action));
    }

    public void Invoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (IsCurrentThread)
        {
            action();
            return;
        }

        using var completion = new ManualResetEventSlim();
        var queuedAction = QueuedAction.Invoke(action, completion);
        EnqueueAction(queuedAction);
        completion.Wait();
        if (queuedAction.Exception != null)
            ExceptionDispatchInfo.Capture(queuedAction.Exception).Throw();
    }

    public void Dispose()
    {
        if (disposed)
            return;

        Stop();
        disposed = true;
    }

    private void EnqueueAction(QueuedAction action)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        var targetThreadId = GetRunningNativeThreadId();
        actions.Enqueue(action);
        if (!ForegroundMarkerThreadDispatcherNative.PostThreadMessage(targetThreadId, InvokeMessage, IntPtr.Zero, IntPtr.Zero))
        {
            action.Cancel();
            throw new InvalidOperationException(
                $"Failed to post work to the foreground marker dispatcher thread. Win32 error: {Marshal.GetLastWin32Error()}.");
        }
    }

    private uint GetRunningNativeThreadId()
    {
        lock (syncRoot)
        {
            var targetThreadId = Volatile.Read(ref nativeThreadId);
            if (thread == null || targetThreadId == 0)
                throw new InvalidOperationException("Foreground marker dispatcher thread is not running.");

            return targetThreadId;
        }
    }

    private void RunMessageLoop()
    {
        while (true)
        {
            var result = ForegroundMarkerThreadDispatcherNative.GetMessage(out var message, IntPtr.Zero, 0, 0);
            if (result == -1)
            {
                Application.OnThreadException(
                    new InvalidOperationException(
                        $"Foreground marker dispatcher message pump failed. Win32 error: {Marshal.GetLastWin32Error()}."));
                break;
            }

            if (result == 0)
                break;

            if (message.HWnd == IntPtr.Zero && message.Message == InvokeMessage)
            {
                DrainAction();
                continue;
            }

            if (message.HWnd == IntPtr.Zero && message.Message == StopMessage)
            {
                ForegroundMarkerThreadDispatcherNative.PostQuitMessage(0);
                continue;
            }

            _ = ForegroundMarkerThreadDispatcherNative.TranslateMessage(ref message);
            _ = ForegroundMarkerThreadDispatcherNative.DispatchMessage(ref message);
        }

        DrainActions();
    }

    private void DrainAction()
    {
        while (actions.TryDequeue(out var action))
        {
            if (action.Invoke())
                return;
        }
    }

    private void DrainActions()
    {
        while (actions.TryDequeue(out var action))
            action.Invoke();
    }

    private sealed class QueuedAction(Action action, ManualResetEventSlim? completion, bool captureException)
    {
        public Exception? Exception { get; private set; }
        private volatile bool canceled;

        public static QueuedAction Post(Action action) => new(action, null, captureException: false);

        public static QueuedAction Invoke(Action action, ManualResetEventSlim completion) =>
            new(action, completion, captureException: true);

        public bool Invoke()
        {
            if (canceled)
            {
                Complete();
                return false;
            }

            try
            {
                action();
            }
            catch (Exception ex)
            {
                if (!captureException)
                {
                    Application.OnThreadException(ex);
                    return true;
                }

                Exception = ex;
            }
            finally
            {
                Complete();
            }

            return true;
        }

        public void Cancel()
        {
            canceled = true;
            Complete();
        }

        public void Complete() => completion?.Set();
    }

    private sealed class ForegroundMarkerSynchronizationContext(ForegroundMarkerThreadDispatcher dispatcher)
        : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => dispatcher.EnqueueAction(QueuedAction.Post(() => d(state)));

        public override void Send(SendOrPostCallback d, object? state) => dispatcher.Invoke(() => d(state));

        public override SynchronizationContext CreateCopy() => new ForegroundMarkerSynchronizationContext(dispatcher);
    }
}
