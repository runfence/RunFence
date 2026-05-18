using Moq;
using RunFence.Acl.Permissions;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Ipc;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Ipc;
using RunFence.Launch;
using RunFence.Licensing;
using RunFence.Persistence;
using RunFence.RunAs;
using RunFence.Security;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Tests for <see cref="RunAsFlowHandler.HandleRunAs"/> and <see cref="RunAsFlowHandler.TriggerFromUI"/>:
/// UNC path rejection, DoS protection blocking, concurrent guard, authorization, and state guards.
///
/// Guard tests exercise logic that runs synchronously before work is posted to the UI thread.
/// <c>dialogPresenter</c> and <c>resultProcessor</c> are constructed with stub/null
/// dependencies since they are never invoked by the guards under test.
/// </summary>
public class RunAsFlowHandlerTests
{
    private const string ValidLocalPath = @"C:\tools\MyApp.exe";
    private const string CallerSid = "S-1-5-21-0-0-0-1001";

    private readonly Mock<IAppStateProvider> _appState = new();
    private readonly Mock<IAppLockControl> _appLock = new();
    private readonly Mock<IUiThreadInvoker> _uiInvoker = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<IIpcCallerAuthorizer> _authorizer = new();
    private readonly Mock<IIdleMonitorService> _idleMonitor = new();
    private readonly RunAsDosProtection _dosProtection;

    private long _stubTimestamp;

    public RunAsFlowHandlerTests()
    {
        _stubTimestamp = 0;
        var stopwatch = new StubStopwatch(() => _stubTimestamp, (s, e) => e - s);
        _dosProtection = new RunAsDosProtection(stopwatch);

        _appState.Setup(a => a.IsShuttingDown).Returns(false);
        _appState.Setup(a => a.IsModalOpen).Returns(false);
        _appState.Setup(a => a.IsOperationInProgress).Returns(false);
        _appState.Setup(a => a.Database).Returns(new AppDatabase());
        _appLock.Setup(l => l.IsUnlockPolling).Returns(false);

        _authorizer.Setup(a => a.IsCallerAuthorizedGlobal(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<AppDatabase>(), It.IsAny<bool>()))
            .Returns(true);

        // BeginInvoke is a no-op: the async UI-thread part is never executed in these tests.
        _uiInvoker.Setup(u => u.BeginInvoke(It.IsAny<Action>()));
    }

    private sealed class StubStopwatch(Func<long> getTimestamp, Func<long, long, double> getElapsed)
        : IStopwatchProvider
    {
        public long GetTimestamp() => getTimestamp();
        public double GetElapsedSeconds(long s, long e) => getElapsed(s, e);
    }

    /// <summary>
    /// Creates a minimal <see cref="RunAsDialogPresenter"/> with all-null internals.
    /// Safe because <c>HandleRunAsOnUIThreadAsync</c> is never reached in these tests:
    /// for <c>HandleRunAs</c> tests — BeginInvoke is mocked as no-op;
    /// for <c>TriggerFromUI</c> tests — a guard fires before the async call.
    /// </summary>
    private static RunAsDialogPresenter MakeDialogPresenter() =>
        new(null!, null!, null!, null!, null!, null!, null!, null!, null!);

    /// <summary>
    /// Creates a minimal <see cref="RunAsResultProcessor"/> with all-null internals.
    /// Safe because <c>HandleRunAsOnUIThreadAsync</c> is never reached in these tests:
    /// for <c>HandleRunAs</c> tests — BeginInvoke is mocked as no-op;
    /// for <c>TriggerFromUI</c> tests — a guard fires before the async call.
    /// </summary>
    private static RunAsResultProcessor MakeResultProcessor() =>
        new(null!, null!, null!, new RunAsDosProtection(new Mock<IStopwatchProvider>().Object), null!, null!);

    private RunAsFlowHandler CreateHandler(RunAsDialogPresenter? dialogPresenter = null) => new(
        _appState.Object,
        _appLock.Object,
        _uiInvoker.Object,
        _log.Object,
        dialogPresenter ?? MakeDialogPresenter(),
        MakeResultProcessor(),
        _dosProtection,
        _authorizer.Object,
        _idleMonitor.Object,
        new ShortcutTargetResolver(new Mock<IShortcutComHelper>().Object));

    private static IpcMessage MakeMessage(string appId) =>
        new() { Command = "RunAs", AppId = appId };

    private static IpcCallerContext MakeContext() =>
        new(CallerIdentity: @"DOMAIN\User", CallerSid: CallerSid, IsAdmin: false, IdentityFromImpersonation: true);

    // ── UNC path rejection ───────────────────────────────────────────────────

    [Fact]
    public void HandleRunAs_UncPath_ReturnsFalseWithError()
    {
        // Arrange: UNC path — must be rejected regardless of auth state
        var handler = CreateHandler();

        // Act
        var response = handler.HandleRunAs(MakeMessage(@"\\server\share\app.exe"), MakeContext());

        // Assert
        Assert.False(response.Success);
        Assert.NotNull(response.ErrorMessage);
        Assert.Contains("UNC", response.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(@"C:\tools\app.exe")]
    [InlineData(@"C:\Program Files\App\app.exe")]
    public void HandleRunAs_LocalPath_ProceedsToAuthCheck(string localPath)
    {
        // Arrange: local paths must pass the UNC check and reach the authorizer
        var handler = CreateHandler();

        // Act
        handler.HandleRunAs(MakeMessage(localPath), MakeContext());

        // Assert: authorizer was reached (UNC guard did not short-circuit)
        _authorizer.Verify(a => a.IsCallerAuthorizedGlobal(
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<AppDatabase>(), It.IsAny<bool>()),
            Times.Once);
    }

    // ── DoS protection ───────────────────────────────────────────────────────

    [Fact]
    public void HandleRunAs_DosProtectionBlocked_ReturnsFalseWithError()
    {
        // Arrange: trigger 4 declines to activate DoS block.
        _stubTimestamp = 100;
        _dosProtection.RecordDecline();
        _dosProtection.RecordDecline();
        _dosProtection.RecordDecline();
        _dosProtection.RecordDecline();

        var handler = CreateHandler();

        // Act
        var response = handler.HandleRunAs(MakeMessage(ValidLocalPath), MakeContext());

        // Assert
        Assert.False(response.Success);
        Assert.NotNull(response.ErrorMessage);
        Assert.Contains("Too many", response.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HandleRunAs_DosProtectionBlocked_ReleasesRunAsGuard()
    {
        // Arrange: DoS blocked — _runAsInProgress must be released so the next (non-blocked) request proceeds.
        _stubTimestamp = 100;
        _dosProtection.RecordDecline();
        _dosProtection.RecordDecline();
        _dosProtection.RecordDecline();
        _dosProtection.RecordDecline();

        var handler = CreateHandler();

        // First call: blocked by DoS
        var blockedResponse = handler.HandleRunAs(MakeMessage(ValidLocalPath), MakeContext());
        Assert.False(blockedResponse.Success);

        // Reset DoS so the second call can proceed
        _dosProtection.Reset();

        // Second call: must not be rejected by the concurrent guard (flag must have been released)
        var secondResponse = handler.HandleRunAs(MakeMessage(ValidLocalPath), MakeContext());

        Assert.True(secondResponse.Success);
    }

    [Fact]
    public void HandleRunAs_DosNotBlocked_Succeeds()
    {
        // Arrange: no declines recorded
        _dosProtection.Reset();
        var handler = CreateHandler();

        // Act
        var response = handler.HandleRunAs(MakeMessage(ValidLocalPath), MakeContext());

        // Assert: success (UI part deferred via BeginInvoke)
        Assert.True(response.Success);
    }

    // ── Concurrent guard ─────────────────────────────────────────────────────

    [Fact]
    public async Task HandleRunAs_ConcurrentCall_SecondReturnsFalse()
    {
        // Arrange: block BeginInvoke so first call holds _runAsInProgress=1
        var firstCallStarted = new ManualResetEventSlim(false);
        var allowFirstToComplete = new ManualResetEventSlim(false);

        _uiInvoker.Setup(u => u.BeginInvoke(It.IsAny<Action>()))
            .Callback<Action>(_ =>
            {
                firstCallStarted.Set();
                Assert.True(allowFirstToComplete.Wait(TimeSpan.FromSeconds(2)),
                    "First RunAs call should remain blocked until signaled.");
                // Deliberately do NOT invoke the action: the async UI-thread work (HandleRunAsOnUIThreadAsync)
                // would call MessageBox.Show for a non-existent path, which blocks in a test environment.
                // The concurrent guard (_runAsInProgress) is set BEFORE BeginInvoke is called and is
                // sufficient for testing second-call rejection without executing the async UI work.
            });

        var handler = CreateHandler();

        // First call on a background thread – holds _runAsInProgress during BeginInvoke
        var firstTask = Task.Factory.StartNew(
            () => handler.HandleRunAs(MakeMessage(ValidLocalPath), MakeContext()),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        Assert.True(firstCallStarted.Wait(TimeSpan.FromSeconds(2)),
            "First call should reach BeginInvoke before issuing concurrent request.");

        // Second call on this thread: concurrent guard must reject it
        var secondResponse = handler.HandleRunAs(MakeMessage(ValidLocalPath), MakeContext());

        // Release the first call
        allowFirstToComplete.Set();
        await firstTask;

        // Assert: second call rejected with concurrent guard error
        Assert.False(secondResponse.Success);
        Assert.NotNull(secondResponse.ErrorMessage);
        Assert.Contains("in progress", secondResponse.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HandleRunAs_BeginInvokeThrows_ReleasesRunAsGuardAndReturnsFalse()
    {
        // Arrange: BeginInvoke throws (control object disposed) — _runAsInProgress must be released.
        _uiInvoker.Setup(u => u.BeginInvoke(It.IsAny<Action>()))
            .Throws(new InvalidOperationException("Control disposed"));

        var handler = CreateHandler();

        // Act: first call → BeginInvoke throws → released + error
        var first = handler.HandleRunAs(MakeMessage(ValidLocalPath), MakeContext());

        // Second call: guard should be free (flag was released after BeginInvoke threw)
        var second = handler.HandleRunAs(MakeMessage(ValidLocalPath), MakeContext());

        // Assert
        Assert.False(first.Success);
        // Second call also hits BeginInvoke (which throws again), but guard is free — not "in progress"
        Assert.False(second.Success);
        Assert.DoesNotContain("in progress", second.ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);
    }

    // ── Authorization ────────────────────────────────────────────────────────

    [Fact]
    public void HandleRunAs_CallerNotAuthorized_ReturnsFalse()
    {
        // Arrange
        _authorizer.Setup(a => a.IsCallerAuthorizedGlobal(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<AppDatabase>(), It.IsAny<bool>()))
            .Returns(false);

        var handler = CreateHandler();

        // Act
        var response = handler.HandleRunAs(MakeMessage(ValidLocalPath), MakeContext());

        // Assert
        Assert.False(response.Success);
        Assert.NotNull(response.ErrorMessage);
    }

    // ── State guards ─────────────────────────────────────────────────────────

    [Fact]
    public void HandleRunAs_IsShuttingDown_ReturnsFalse()
    {
        // Arrange
        _appState.Setup(a => a.IsShuttingDown).Returns(true);
        var handler = CreateHandler();

        // Act
        var response = handler.HandleRunAs(MakeMessage(ValidLocalPath), MakeContext());

        // Assert
        Assert.False(response.Success);
        Assert.NotNull(response.ErrorMessage);
        Assert.Contains("Shutting down", response.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HandleRunAs_IsModalOpen_ReturnsFalse()
    {
        // Arrange
        _appState.Setup(a => a.IsModalOpen).Returns(true);
        var handler = CreateHandler();

        // Act
        var response = handler.HandleRunAs(MakeMessage(ValidLocalPath), MakeContext());

        // Assert
        Assert.False(response.Success);
        Assert.NotNull(response.ErrorMessage);
        Assert.Contains("Busy", response.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ── TriggerFromUI guards ──────────────────────────────────────────────────
    //
    // TriggerFromUI does not post to BeginInvoke — it fires HandleRunAsOnUIThreadAsync
    // directly as a fire-and-forget Task. When a guard fires, the method returns
    // immediately without starting the async task, so idleMonitor.ResetIdleTimer()
    // (which is always called in the task's finally block) is never called.
    // Verifying Times.Never on ResetIdleTimer is the observable proxy for "no dialog shown".

    [Fact]
    public void HandleRunAs_WhenUnlockPolling_QueuesRequest()
    {
        _appLock.Setup(l => l.IsUnlockPolling).Returns(true);
        var handler = CreateHandler();

        var response = handler.HandleRunAs(MakeMessage(ValidLocalPath), MakeContext());

        Assert.True(response.Success);
        _uiInvoker.Verify(u => u.BeginInvoke(It.IsAny<Action>()), Times.Once);
    }

    [Fact]
    public void TriggerFromUI_WhenShuttingDown_DoesNotShowDialog()
    {
        // Arrange
        _appState.Setup(a => a.IsShuttingDown).Returns(true);
        var handler = CreateHandler();

        // Act
        handler.TriggerFromUI(ValidLocalPath);

        // Assert: async task never started → ResetIdleTimer never called
        _idleMonitor.Verify(m => m.ResetIdleTimer(), Times.Never);
    }

    [Fact]
    public async Task TriggerFromUIAsync_WhenUnlockPolling_StartsRunAsFlow()
    {
        using var pinKey = TestSecretFactory.Create(32);
        using var session = new SessionContext
{
            Database = _appState.Object.Database,
            CredentialStore = new CredentialStore(),
        }.WithOwnedPinDerivedKey(pinKey);
        _appLock.Setup(l => l.IsUnlockPolling).Returns(true);
        _appLock.Setup(l => l.TryUnlockForOperationAsync(true)).ReturnsAsync(true);
        var handler = CreateHandler(CreateNoOpDialogPresenter(session));

        await handler.TriggerFromUIAsync(ValidLocalPath);

        _idleMonitor.Verify(m => m.ResetIdleTimer(), Times.Once);
    }

    private RunAsDialogPresenter CreateNoOpDialogPresenter(SessionContext session)
    {
        var modalCoordinator = new Mock<IModalCoordinator>();
        var startupUnlockGrant = new Mock<IStartupUnlockGrant>();
        return new RunAsDialogPresenter(
            modalCoordinator.Object,
            new RunAsPermissionChecker(new Mock<IAclPermissionService>().Object),
            new RunAsCredentialPersister(
                _appState.Object,
                session,
                new ByteArrayCredentialEncryptionSpanAdapter(new Mock<IByteArrayCredentialEncryptionService>().Object),
                new Mock<IDatabaseService>().Object,
                _log.Object),
            _appState.Object,
            _appLock.Object,
            startupUnlockGrant.Object,
            session,
            new RunAsPostDialogRouter(
                new Mock<IRunAsUserAccountCreator>().Object,
                new Mock<IRunAsContainerCreator>().Object,
                new Mock<IEvaluationLimitHelper>().Object,
                _dosProtection),
            () => null!);
    }

    [Fact]
    public void TriggerFromUI_WhenModalOpen_DoesNotShowDialog()
    {
        // Arrange
        _appState.Setup(a => a.IsModalOpen).Returns(true);
        var handler = CreateHandler();

        // Act
        handler.TriggerFromUI(ValidLocalPath);

        // Assert
        _idleMonitor.Verify(m => m.ResetIdleTimer(), Times.Never);
    }

    [Fact]
    public void TriggerFromUI_WhenOperationInProgress_DoesNotShowDialog()
    {
        // Arrange
        _appState.Setup(a => a.IsOperationInProgress).Returns(true);
        var handler = CreateHandler();

        // Act
        handler.TriggerFromUI(ValidLocalPath);

        // Assert
        _idleMonitor.Verify(m => m.ResetIdleTimer(), Times.Never);
    }

    [Fact]
    public async Task TriggerFromUI_WhenConcurrentGuardBlocked_DoesNotShowDialog()
    {
        // Arrange: use HandleRunAs to hold _runAsInProgress = 1 via BeginInvoke blocking.
        // TriggerFromUI shares the same _runAsInProgress field.
        var firstCallStarted = new ManualResetEventSlim(false);
        var allowFirstToComplete = new ManualResetEventSlim(false);

        _uiInvoker.Setup(u => u.BeginInvoke(It.IsAny<Action>()))
            .Callback<Action>(_ =>
            {
                firstCallStarted.Set();
                Assert.True(allowFirstToComplete.Wait(TimeSpan.FromSeconds(2)),
                    "First RunAs call should remain blocked until signaled.");
            });

        var handler = CreateHandler();

        // First call via HandleRunAs: sets _runAsInProgress = 1, blocks inside BeginInvoke
        var firstTask = Task.Factory.StartNew(
            () => handler.HandleRunAs(MakeMessage(ValidLocalPath), MakeContext()),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        Assert.True(firstCallStarted.Wait(TimeSpan.FromSeconds(2)),
            "First call should reach BeginInvoke before concurrent TriggerFromUI request.");

        // Second call via TriggerFromUI: concurrent guard must fire → returns immediately
        handler.TriggerFromUI(ValidLocalPath);

        // Release the first HandleRunAs call
        allowFirstToComplete.Set();
        await firstTask;

        // Assert: TriggerFromUI was blocked by the concurrent guard → ResetIdleTimer never called
        _idleMonitor.Verify(m => m.ResetIdleTimer(), Times.Never);
    }

    [Fact]
    public void TriggerFromUI_WhenDosBlocked_DoesNotShowDialog()
    {
        // Arrange: trigger 4 declines to activate DoS block
        _stubTimestamp = 100;
        _dosProtection.RecordDecline();
        _dosProtection.RecordDecline();
        _dosProtection.RecordDecline();
        _dosProtection.RecordDecline();

        var handler = CreateHandler();

        // Act
        handler.TriggerFromUI(ValidLocalPath);

        // Assert: DoS guard fired → _runAsInProgress released → async task never started
        _idleMonitor.Verify(m => m.ResetIdleTimer(), Times.Never);
    }
}
