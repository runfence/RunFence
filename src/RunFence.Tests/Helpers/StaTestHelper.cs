using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using RunFence.Infrastructure;

namespace RunFence.Tests.Helpers;

/// <summary>
/// Provides a shared helper for running WinForms-related test actions on an STA thread.
/// </summary>
public static class StaTestHelper
{
    private static readonly TimeSpan DefaultPumpTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultStaTimeout = TimeSpan.FromSeconds(30);
    private static readonly object StaExecutionGate = new();

    /// <summary>
    /// Runs <paramref name="action"/> on a new STA thread and rethrows any exception on the test thread,
    /// preserving the original exception type and stack trace.
    /// </summary>
    public static void RunOnSta(Action action, TimeSpan? timeout = null)
    {
        lock (StaExecutionGate)
        {
            ExceptionDispatchInfo? captured = null;
            var thread = new Thread(() =>
            {
                var previousContext = SynchronizationContext.Current;
                QueuedStaSynchronizationContext? installedContext = null;

                try
                {
                    using var unhandledExceptionGuard = new UnhandledExceptionGuard();

                    if (previousContext == null)
                    {
                        installedContext = new QueuedStaSynchronizationContext();
                        SynchronizationContext.SetSynchronizationContext(installedContext);
                    }

                    using var nativeDialogGuard = new NativeDialogShowGuard();
                    using var showGuard = new VisibleFormShowGuard();
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        Application.DoEvents();
                        ExecutePendingSynchronizationCallbacks();
                        try
                        {
                            unhandledExceptionGuard.ThrowIfViolationDetected();
                            showGuard.FailIfVisibleFormsExist();
                            showGuard.ThrowIfViolationDetected();
                            nativeDialogGuard.ThrowIfViolationDetected();
                        }
                        catch (Exception guardEx)
                        {
                            captured = ExceptionDispatchInfo.Capture(guardEx);
                            return;
                        }

                        captured = ExceptionDispatchInfo.Capture(ex);
                    }
                    finally
                    {
                        try
                        {
                            Application.DoEvents();
                            ExecutePendingSynchronizationCallbacks();
                            unhandledExceptionGuard.ThrowIfViolationDetected();
                            showGuard.FailIfVisibleFormsExist();
                            showGuard.ThrowIfViolationDetected();
                            nativeDialogGuard.ThrowIfViolationDetected();
                        }
                        catch (Exception ex)
                        {
                            captured = ExceptionDispatchInfo.Capture(ex);
                        }
                        finally
                        {
                            DisposeOpenForms();
                        }
                    }
                }
                catch (Exception ex)
                {
                    captured = ExceptionDispatchInfo.Capture(ex);
                }
                finally
                {
                    if (installedContext != null)
                    {
                        SynchronizationContext.SetSynchronizationContext(previousContext);
                    }
                }
            })
            {
                IsBackground = true
            };

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            if (!thread.Join(timeout ?? DefaultStaTimeout))
                throw new TimeoutException("Timed out waiting for STA test execution to complete.");

            captured?.Throw();
        }
    }

    public static void RunAsyncOnSta(Func<Task> action, TimeSpan? timeout = null)
    {
        RunOnSta(() => RunAsyncWithMessagePump(action, timeout), timeout);
    }

    public static void RunAsyncWithMessagePump(Func<Task> action, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(action);

        RunAsyncWithMessagePump(async () =>
        {
            await action();
            return true;
        }, timeout);
    }

    public static T RunAsyncWithMessagePump<T>(Func<Task<T>> action, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(action);

        var completed = false;
        T result = default!;
        ExceptionDispatchInfo? captured = null;
        var previousContext = SynchronizationContext.Current;
        QueuedStaSynchronizationContext? installedContext = null;

        try
        {
            if (previousContext == null)
            {
                installedContext = new QueuedStaSynchronizationContext();
                SynchronizationContext.SetSynchronizationContext(installedContext);
            }

            async void Start()
            {
                try
                {
                    result = await action();
                }
                catch (Exception ex)
                {
                    captured = ExceptionDispatchInfo.Capture(ex);
                }
                finally
                {
                    completed = true;
                }
            }

            Start();
            PumpUntil(
                () => completed,
                timeout,
                "Timed out waiting for STA async test operation to complete.");
            captured?.Throw();
            return result;
        }
        finally
        {
            if (installedContext != null)
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        }
    }

    private static void ExecutePendingSynchronizationCallbacks()
    {
        if (SynchronizationContext.Current is QueuedStaSynchronizationContext context)
            context.ExecutePending();
    }

    private sealed class QueuedStaSynchronizationContext : SynchronizationContext
    {
        private readonly int ownerThreadId = Environment.CurrentManagedThreadId;
        private readonly Queue<(SendOrPostCallback callback, object? state)> callbacks = new();

        public override void Post(SendOrPostCallback d, object? state)
        {
            ArgumentNullException.ThrowIfNull(d);

            lock (callbacks)
            {
                callbacks.Enqueue((d, state));
            }
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            ArgumentNullException.ThrowIfNull(d);

            if (Environment.CurrentManagedThreadId == ownerThreadId)
            {
                d(state);
                return;
            }

            using var completed = new ManualResetEventSlim();
            ExceptionDispatchInfo? failure = null;
            Post(
                queuedState =>
                {
                    try
                    {
                        d(queuedState);
                    }
                    catch (Exception ex)
                    {
                        failure = ExceptionDispatchInfo.Capture(ex);
                    }
                    finally
                    {
                        completed.Set();
                    }
                },
                state);
            completed.Wait();
            failure?.Throw();
        }

        public override SynchronizationContext CreateCopy() => this;

        public void ExecutePending()
        {
            while (TryDequeue(out var item))
                item.callback(item.state);
        }

        private bool TryDequeue(out (SendOrPostCallback callback, object? state) item)
        {
            lock (callbacks)
            {
                if (callbacks.Count > 0)
                {
                    item = callbacks.Dequeue();
                    return true;
                }
            }

            item = default;
            return false;
        }
    }

    private static void DisposeOpenForms()
    {
        foreach (Form openForm in Application.OpenForms.Cast<Form>().ToArray())
            openForm.Dispose();
    }

    private readonly record struct VisibleFormInfo(string TypeName, string? Text);

    private sealed class VisibleFormShowGuard : IDisposable
    {
        private readonly Func<IReadOnlyList<VisibleFormInfo>> getVisibleForms;
        private readonly Action<IReadOnlyList<VisibleFormInfo>> closeVisibleForms;
        private readonly System.Windows.Forms.Timer timer;

        private Exception? violation;
        private bool disposed;

        public VisibleFormShowGuard()
            : this(GetApplicationVisibleForms, CloseApplicationVisibleForms)
        {
        }

        public VisibleFormShowGuard(
            Func<IReadOnlyList<VisibleFormInfo>> getVisibleForms,
            Action<IReadOnlyList<VisibleFormInfo>> closeVisibleForms)
        {
            this.getVisibleForms = getVisibleForms;
            this.closeVisibleForms = closeVisibleForms;
            timer = new System.Windows.Forms.Timer { Interval = 10 };
            timer.Tick += (_, _) => FailIfVisibleFormsExist();
            timer.Start();
        }

        public void FailIfVisibleFormsExist()
        {
            if (violation != null)
                return;

            var visibleForms = getVisibleForms();

            if (visibleForms.Count == 0)
                return;

            violation = new InvalidOperationException(
                $"Unexpected visible WinForms window in test: {DescribeForm(visibleForms[0])}. " +
                "Use CreateControlTree() for handle creation instead of showing a form.");

            closeVisibleForms(visibleForms);
        }

        public void ThrowIfViolationDetected()
        {
            if (violation != null)
                ExceptionDispatchInfo.Capture(violation).Throw();
        }

        public void Dispose()
        {
            if (disposed)
                return;

            timer.Stop();
            timer.Dispose();
            disposed = true;
        }

        private static IReadOnlyList<VisibleFormInfo> GetApplicationVisibleForms()
            => EnumerateVisibleTopLevelForms()
                .Select(form => new VisibleFormInfo(form.GetType().FullName ?? form.GetType().Name, form.Text))
                .ToArray();

        private static void CloseApplicationVisibleForms(IReadOnlyList<VisibleFormInfo> _)
        {
            foreach (var form in EnumerateVisibleTopLevelForms())
                form.Close();
        }

        private static string DescribeForm(VisibleFormInfo form)
            => string.IsNullOrWhiteSpace(form.Text)
                ? form.TypeName
                : $"{form.TypeName} (Text='{form.Text}')";

        private static IReadOnlyList<Form> EnumerateVisibleTopLevelForms()
        {
            var result = new List<Form>();
            var seenHandles = new HashSet<nint>();
            var threadId = WindowNative.GetCurrentThreadId();
            if (!EnumThreadWindows(threadId, (hwnd, _) =>
            {
                if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd) || !seenHandles.Add(hwnd))
                    return true;

                if (Control.FromHandle(hwnd) is Form form && form.TopLevel && form.Visible)
                    result.Add(form);

                return true;
            }, IntPtr.Zero))
            {
                return Application.OpenForms
                    .Cast<Form>()
                    .Where(form => form.TopLevel && form.Visible)
                    .ToArray();
            }

            return result;
        }

        [DllImport("user32.dll")]
        private static extern bool EnumThreadWindows(uint dwThreadId, EnumThreadDelegate lpfn, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        private delegate bool EnumThreadDelegate(IntPtr hwnd, IntPtr lParam);
    }

    private sealed class NativeDialogShowGuard : IDisposable
    {
        private const uint EventSystemDialogStart = 0x0010u;
        private const int ObjidWindow = 0;
        private const uint WmClose = 0x0010u;

        [ThreadStatic]
        private static NativeDialogShowGuard? current;

        private readonly object sync = new();
        private readonly WindowNative.WinEventDelegate? callback;
        private readonly IntPtr hook;

        private Exception? violation;
        private bool disposed;

        public NativeDialogShowGuard()
        {
            callback = OnDialogStarted;
            hook = WindowNative.SetWinEventHook(
                EventSystemDialogStart,
                EventSystemDialogStart,
                IntPtr.Zero,
                callback,
                (uint)Environment.ProcessId,
                0,
                WindowNative.WinEventOutOfContext);

            if (hook == IntPtr.Zero)
                throw new InvalidOperationException("Failed to install native dialog guard for STA test execution.");

            current = this;
        }

        public void ThrowIfViolationDetected()
        {
            Exception? capturedViolation;
            lock (sync)
            {
                capturedViolation = violation;
            }

            if (capturedViolation != null)
                ExceptionDispatchInfo.Capture(capturedViolation).Throw();
        }

        public void Dispose()
        {
            if (disposed)
                return;

            if (hook != IntPtr.Zero)
                WindowNative.UnhookWinEvent(hook);

            if (ReferenceEquals(current, this))
                current = null;

            disposed = true;
        }

        private void OnDialogStarted(
            IntPtr hookHandle,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint eventThread,
            uint eventTime)
        {
            if (hwnd == IntPtr.Zero || idObject != ObjidWindow || idChild != 0)
                return;

            lock (sync)
            {
                violation ??= CreateViolation(DescribeWindow(hwnd));
            }

            WindowNative.PostMessage(hwnd, WmClose, IntPtr.Zero, IntPtr.Zero);
        }

        private static string DescribeWindow(IntPtr hwnd)
        {
            var className = GetWindowClassName(hwnd);
            var windowText = GetWindowText(hwnd);

            if (string.IsNullOrWhiteSpace(windowText))
                return $"Class='{className}'";

            return $"Class='{className}', Text='{windowText}'";
        }

        private static string GetWindowClassName(IntPtr hwnd)
        {
            var builder = new StringBuilder(256);
            return WindowNative.GetClassName(hwnd, builder, builder.Capacity) > 0
                ? builder.ToString()
                : "<unknown>";
        }

        private static string GetWindowText(IntPtr hwnd)
        {
            var builder = new StringBuilder(256);
            return WindowNative.GetWindowText(hwnd, builder, builder.Capacity) > 0
                ? builder.ToString()
                : string.Empty;
        }

        private void RecordViolation(Exception exception)
        {
            lock (sync)
            {
                violation ??= exception;
            }
        }

        private static InvalidOperationException CreateViolation(string description)
            => new(
                $"Unexpected native dialog in test: {description}. " +
                "Tests must stay headless and must not show MessageBox or TaskDialog.");
    }

    private sealed class UnhandledExceptionGuard : IDisposable
    {
        private readonly object sync = new();

        private Exception? violation;
        private bool disposed;

        public UnhandledExceptionGuard()
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException, threadScope: true);
            Application.ThreadException += OnThreadException;
        }

        public void ThrowIfViolationDetected()
        {
            Exception? capturedViolation;
            lock (sync)
            {
                capturedViolation = violation;
            }

            if (capturedViolation != null)
                ExceptionDispatchInfo.Capture(capturedViolation).Throw();
        }

        public void Dispose()
        {
            if (disposed)
                return;

            Application.ThreadException -= OnThreadException;
            disposed = true;
        }

        private void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            lock (sync)
            {
                violation ??= new InvalidOperationException(
                    $"Unexpected unhandled WinForms exception in test: {e.Exception}",
                    e.Exception);
            }
        }
    }

    /// <summary>
    /// Pumps the WinForms message loop until <paramref name="predicate"/> returns true or the timeout expires.
    /// </summary>
    public static void PumpUntil(
        Func<bool> predicate,
        TimeSpan? timeout = null,
        string? timeoutMessage = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultPumpTimeout);
        while (!predicate())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException(timeoutMessage ?? "Timed out waiting for WinForms state change.");

            ExecutePendingSynchronizationCallbacks();
            Application.DoEvents();
            ExecutePendingSynchronizationCallbacks();
        }
    }

    /// <summary>
    /// Creates the control tree for a WinForms control without showing a visible top-level window.
    /// Use this in tests that need handles and control initialization but must stay headless.
    /// </summary>
    public static void CreateControlTree(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);

        _ = control.Handle;
        foreach (Control child in control.Controls)
            CreateControlTree(child);
    }

    public static void ClickButton(Button button)
    {
        ArgumentNullException.ThrowIfNull(button);

        if (button.CanSelect)
        {
            button.PerformClick();
            Application.DoEvents();
            return;
        }

        var parent = button.Parent;
        if (parent == null)
        {
            button.PerformClick();
            Application.DoEvents();
            return;
        }

        var originalIndex = parent.Controls.GetChildIndex(button);
        parent.Controls.Remove(button);
        try
        {
            button.PerformClick();
        }
        finally
        {
            parent.Controls.Add(button);
            parent.Controls.SetChildIndex(button, originalIndex);
        }

        Application.DoEvents();
    }

}
