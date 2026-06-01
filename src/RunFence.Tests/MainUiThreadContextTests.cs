using System.Windows.Forms;
using System.Runtime.ExceptionServices;
using Autofac;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Startup;
using RunFence.UI;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public class MainUiThreadContextTests
{
    [Fact]
    public void FoundationInvoker_InvokesSynchronouslyInCurrentThread()
    {
        using var foundationContainer = ContainerRegistrationBuilder.BuildFoundationContainer();
        var invoker = foundationContainer.Resolve<IUiThreadInvoker>();

        var expectedThreadId = Environment.CurrentManagedThreadId;
        var invokedThreadId = -1;
        invoker.Invoke(() => invokedThreadId = Environment.CurrentManagedThreadId);

        Assert.Equal(expectedThreadId, invokedThreadId);
    }

    [Fact]
    public void SessionScopeInvoker_ThrowsWhenInvokedBeforeBind()
    {
        using var foundationContainer = ContainerRegistrationBuilder.BuildFoundationContainer();
        using var pinKey = TestSecretFactory.Create(32);
        var session = new SessionContext
        {
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore()
        }.WithClonedPinDerivedKey(pinKey);

        using var sessionScope = ContainerRegistrationBuilder.BeginSessionScope(
            foundationContainer,
            session,
            new StartupOptions(false, false));
        var invoker = sessionScope.Resolve<IUiThreadInvoker>();

        var ex = Assert.Throws<InvalidOperationException>(() => invoker.Invoke(() => { }));
        Assert.Contains("not bound", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BoundSessionInvoker_DispatchesThroughMainFormThread()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var form = new Form();

            var context = new MainUiThreadContext();
            context.Bind(form);
            Assert.False(form.IsHandleCreated);
            Assert.True(context.CheckAccess());

            var invokeThreadId = 0;
            var beginInvokeThreadId = 0;
            ExceptionDispatchInfo? workerFailure = null;
            using var workerCompleted = new ManualResetEventSlim(false);
            var worker = new Thread(() =>
            {
                try
                {
                    var invoker = (IUiThreadInvoker)context;
                    Interlocked.Exchange(ref invokeThreadId, invoker.Invoke(() => Environment.CurrentManagedThreadId));
                    invoker.BeginInvoke(() => Interlocked.Exchange(ref beginInvokeThreadId, Environment.CurrentManagedThreadId));
                }
                catch (Exception ex)
                {
                    workerFailure = ExceptionDispatchInfo.Capture(ex);
                }
                finally
                {
                    workerCompleted.Set();
                }
            });

            var uiThreadId = Environment.CurrentManagedThreadId;
            worker.Start();

            StaTestHelper.PumpUntil(
                () => Volatile.Read(ref invokeThreadId) != 0 && Volatile.Read(ref beginInvokeThreadId) != 0 || workerCompleted.IsSet && workerFailure != null,
                TimeSpan.FromSeconds(2),
                "Bound MainUiThreadContext invocation did not complete.");

            Assert.True(worker.Join(TimeSpan.FromSeconds(1)));
            workerFailure?.Throw();
            Assert.Equal(uiThreadId, Volatile.Read(ref invokeThreadId));
            Assert.Equal(uiThreadId, Volatile.Read(ref beginInvokeThreadId));
        });
    }

    [Fact]
    public void Bind_DoesNotConsumeFirstMainFormHandleCreatedEvent()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var form = new Form();
            var context = new MainUiThreadContext();

            context.Bind(form);
            Assert.False(form.IsHandleCreated);

            var handleCreatedCount = 0;
            form.HandleCreated += (_, _) => handleCreatedCount++;

            StaTestHelper.CreateControlTree(form);

            Assert.True(form.IsHandleCreated);
            Assert.Equal(1, handleCreatedCount);
        });
    }

    [Fact]
    public void BoundSessionInvoker_AfterBoundControlDisposal_BecomesUnbound()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var context = new MainUiThreadContext();
            var form = new Form();

            context.Bind(form);
            form.Dispose();

            Assert.False(context.CheckAccess());

            var invokeException = Assert.Throws<InvalidOperationException>(() => context.Invoke(() => 1));
            Assert.Contains("not bound", invokeException.Message, StringComparison.OrdinalIgnoreCase);

            var beginInvokeException = Assert.Throws<InvalidOperationException>(() => context.BeginInvoke(() => { }));
            Assert.Contains("not bound", beginInvokeException.Message, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void Bind_RebindFailure_DoesNotDiscardExistingBinding()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var originalForm = new Form();
            var context = new MainUiThreadContext();
            context.Bind(originalForm);

            using var disposedForm = new Form();
            disposedForm.Dispose();

            var bindException = Assert.Throws<InvalidOperationException>(() => context.Bind(disposedForm));
            Assert.Contains("disposed", bindException.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(context.CheckAccess());
            Assert.Equal(Environment.CurrentManagedThreadId, context.Invoke(() => Environment.CurrentManagedThreadId));
        });
    }

    [Fact]
    public void Bind_RebindsToNewControl_AndOldControlDisposalDoesNotClearNewBinding()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var context = new MainUiThreadContext();
            using var originalForm = new Form();
            using var replacementForm = new Form();

            context.Bind(originalForm);
            context.Bind(replacementForm);
            originalForm.Dispose();

            Assert.True(context.CheckAccess());
            Assert.Equal(Environment.CurrentManagedThreadId, context.Invoke(() => Environment.CurrentManagedThreadId));

            replacementForm.Dispose();
            Assert.False(context.CheckAccess());
        });
    }

    [Fact]
    public void Bind_ExistingForeignHandle_Throws()
    {
        Form? foreignForm = null;
        ExceptionDispatchInfo? foreignThreadFailure = null;
        using var ready = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        using var cleanedUp = new ManualResetEventSlim(false);
        var thread = new Thread(() =>
        {
            try
            {
                foreignForm = new Form();
                StaTestHelper.CreateControlTree(foreignForm);
                ready.Set();
                release.Wait();
                foreignForm.Dispose();
            }
            catch (Exception ex)
            {
                foreignThreadFailure = ExceptionDispatchInfo.Capture(ex);
                ready.Set();
            }
            finally
            {
                cleanedUp.Set();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(ready.Wait(TimeSpan.FromSeconds(5)));
        foreignThreadFailure?.Throw();

        try
        {
            StaTestHelper.RunOnSta(() =>
            {
                var context = new MainUiThreadContext();
                var exception = Assert.Throws<InvalidOperationException>(() => context.Bind(foreignForm!));
                Assert.Contains("owns the WinForms control", exception.Message, StringComparison.OrdinalIgnoreCase);
            });
        }
        finally
        {
            release.Set();
            Assert.True(cleanedUp.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
            foreignThreadFailure?.Throw();
        }
    }

    [Fact]
    public void Bind_ExistingLocalHandle_RemainsUsable()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var form = new Form();
            StaTestHelper.CreateControlTree(form);

            var context = new MainUiThreadContext();
            context.Bind(form);

            Assert.True(form.IsHandleCreated);
            Assert.True(context.CheckAccess());
            Assert.Equal(Environment.CurrentManagedThreadId, context.Invoke(() => Environment.CurrentManagedThreadId));
        });
    }

    [Fact]
    public void Invoke_SameThreadDelegateException_IsNotRewrittenWhenBindingChangesDuringDelegate()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var form = new Form();
            var context = new MainUiThreadContext();
            context.Bind(form);

            var exception = Assert.Throws<InvalidOperationException>(() => context.Invoke(() =>
            {
                form.Dispose();
                throw new InvalidOperationException("delegate failure");
            }));

            Assert.Equal("delegate failure", exception.Message);
            Assert.False(context.CheckAccess());
        });
    }

    [Fact]
    public void Invoke_CrossThreadDelegateException_IsNotRewrittenWhenBindingChangesDuringDelegate()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var form = new Form();
            var context = new MainUiThreadContext();
            context.Bind(form);

            ExceptionDispatchInfo? workerFailure = null;
            using var completed = new ManualResetEventSlim(false);
            var worker = new Thread(() =>
            {
                try
                {
                    var exception = Assert.Throws<InvalidOperationException>(() => context.Invoke(() =>
                    {
                        form.Dispose();
                        throw new InvalidOperationException("delegate failure");
                    }));

                    Assert.Equal("delegate failure", exception.Message);
                }
                catch (Exception ex)
                {
                    workerFailure = ExceptionDispatchInfo.Capture(ex);
                }
                finally
                {
                    completed.Set();
                }
            });
            worker.Start();

            StaTestHelper.PumpUntil(() => completed.IsSet, TimeSpan.FromSeconds(5), "Timed out waiting for cross-thread invoke failure.");

            Assert.True(worker.Join(TimeSpan.FromSeconds(5)));
            workerFailure?.Throw();
            Assert.False(context.CheckAccess());
        });
    }
}
