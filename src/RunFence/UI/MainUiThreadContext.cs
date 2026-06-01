using System.Threading;
using System.Windows.Forms;
using RunFence.Infrastructure;

namespace RunFence.UI;

public sealed class MainUiThreadContext : IUiThreadInvoker
{
    private readonly object _syncRoot = new();
    private Control? _uiRoot;
    private Control? _dispatcher;
    private uint _uiThreadId;

    public void Bind(Control uiRoot)
    {
        ArgumentNullException.ThrowIfNull(uiRoot);

        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("Main UI context must be bound on an STA UI thread.");
        if (uiRoot.IsDisposed || uiRoot.Disposing)
            throw new InvalidOperationException("Main UI context could not bind because the WinForms control is already disposed.");

        lock (_syncRoot)
        {
            var dispatcher = new Control();
            try
            {
                _ = dispatcher.Handle;
            }
            catch (ObjectDisposedException ex)
            {
                dispatcher.Dispose();
                throw new InvalidOperationException("Main UI context could not bind because the dispatcher control was disposed before an invokable handle could be created.", ex);
            }

            var currentThreadId = MainUiThreadContextNative.GetCurrentThreadId();
            if (uiRoot.IsHandleCreated &&
                (uint)MainUiThreadContextNative.GetWindowThreadProcessId(uiRoot.Handle, out _) != currentThreadId)
            {
                dispatcher.Dispose();
                throw new InvalidOperationException("Main UI context must be bound on the UI thread that owns the WinForms control.");
            }

            if ((uint)MainUiThreadContextNative.GetWindowThreadProcessId(dispatcher.Handle, out _) != currentThreadId)
            {
                dispatcher.Dispose();
                throw new InvalidOperationException("Main UI context must be bound on the UI thread that owns the WinForms control.");
            }

            ReleaseBinding_NoLock();
            _uiRoot = uiRoot;
            _dispatcher = dispatcher;
            _uiThreadId = currentThreadId;
            uiRoot.Disposed += OnUiRootDisposed;
        }
    }

    public bool CheckAccess()
    {
        lock (_syncRoot)
        {
            return _dispatcher != null && _uiThreadId == MainUiThreadContextNative.GetCurrentThreadId();
        }
    }

    public void Invoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        Invoke(() =>
        {
            action();
            return 0;
        });
    }

    public T Invoke<T>(Func<T> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        Control dispatcher;
        var requiresInvoke = false;
        lock (_syncRoot)
        {
            if (_dispatcher == null)
                throw CreateNotBoundException();

            dispatcher = _dispatcher;
            requiresInvoke = _uiThreadId != MainUiThreadContextNative.GetCurrentThreadId();
        }

        if (!requiresInvoke)
            return func();

        var delegateStarted = 0;
        try
        {
            return dispatcher.Invoke(() =>
            {
                Interlocked.Exchange(ref delegateStarted, 1);
                return func();
            });
        }
        catch (ObjectDisposedException ex) when (delegateStarted == 0 && WasBindingReleased(dispatcher))
        {
            throw CreateNotBoundException(ex);
        }
        catch (InvalidOperationException ex) when (delegateStarted == 0 && WasBindingReleased(dispatcher))
        {
            throw CreateNotBoundException(ex);
        }
    }

    public void BeginInvoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        Control dispatcher;
        lock (_syncRoot)
        {
            if (_dispatcher == null)
                throw CreateNotBoundException();

            dispatcher = _dispatcher;
        }

        try
        {
            dispatcher.BeginInvoke(action);
        }
        catch (ObjectDisposedException ex) when (WasBindingReleased(dispatcher))
        {
            throw CreateNotBoundException(ex);
        }
        catch (InvalidOperationException ex) when (WasBindingReleased(dispatcher))
        {
            throw CreateNotBoundException(ex);
        }
    }

    private void OnUiRootDisposed(object? sender, EventArgs e)
    {
        lock (_syncRoot)
        {
            ReleaseBinding_NoLock();
        }
    }

    private void ReleaseBinding_NoLock()
    {
        if (_uiRoot != null)
            _uiRoot.Disposed -= OnUiRootDisposed;

        _uiRoot = null;
        _uiThreadId = 0;

        if (_dispatcher == null)
            return;

        _dispatcher.Dispose();
        _dispatcher = null;
    }

    private static InvalidOperationException CreateNotBoundException()
        => new("Main UI context is not bound to a control. Call Bind(Control) before invoking UI-thread code.");

    private static InvalidOperationException CreateNotBoundException(Exception innerException)
        => new("Main UI context is not bound to a control. Call Bind(Control) before invoking UI-thread code.", innerException);

    private bool WasBindingReleased(Control dispatcher)
    {
        lock (_syncRoot)
        {
            return !ReferenceEquals(_dispatcher, dispatcher);
        }
    }
}
