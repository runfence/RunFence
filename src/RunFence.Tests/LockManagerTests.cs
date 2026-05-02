using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Security;
using RunFence.Startup.UI;
using Xunit;

namespace RunFence.Tests;

public class LockManagerTests : IDisposable
{
    private readonly Mock<IPinService> _pinService = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<ISecureDesktopRunner> _secureDesktop = new();
    private readonly Mock<IWindowsHelloService> _windowsHello = new();
    private readonly Mock<IUnlockProcessLauncher> _unlockProcessLauncher = new();

    private readonly AppDatabase _database = new();
    private readonly ProtectedBuffer _pinKey;
    private readonly SessionContext _session;

    public LockManagerTests()
    {
        _pinKey = new ProtectedBuffer(new byte[32], protect: false);
        _session = new SessionContext
        {
            Database = _database,
            CredentialStore = new CredentialStore(),
            PinDerivedKey = _pinKey
        };
        _database.Settings.UnlockMode = UnlockMode.WindowsHello;
        _secureDesktop.Setup(s => s.Run(It.IsAny<Action>())).Callback<Action>(a => a());
    }

    public void Dispose() => _pinKey.Dispose();

    private readonly Mock<IAutoLockTimerService> _autoLockTimer = new();

    private LockManager CreateManager() =>
        new(_session, _pinService.Object, _log.Object,
            secureDesktop: _secureDesktop.Object,
            windowsHello: _windowsHello.Object,
            autoLockTimerService: _autoLockTimer.Object,
            unlockProcessLauncher: _unlockProcessLauncher.Object);

    [Fact]
    public async Task TryShowWindow_HelloVerified_Unlocks()
    {
        _windowsHello.Setup(h => h.VerifyAsync(It.IsAny<string>()))
            .ReturnsAsync(HelloVerificationResult.Verified);

        var manager = CreateManager();
        manager.LockWindow();

        await manager.TryShowWindowAsync();

        Assert.False(manager.IsLocked);
        Assert.NotNull(_session.LastPinVerifiedAt);
    }

    [Fact]
    public async Task TryShowWindow_HelloCanceled_StaysLocked()
    {
        _windowsHello.Setup(h => h.VerifyAsync(It.IsAny<string>()))
            .ReturnsAsync(HelloVerificationResult.Canceled);

        var manager = CreateManager();
        manager.LockWindow();

        await manager.TryShowWindowAsync();

        Assert.True(manager.IsLocked);
    }

    [Fact]
    public async Task TryUnlock_HelloVerified_Unlocks()
    {
        _windowsHello.Setup(h => h.VerifyAsync(It.IsAny<string>()))
            .ReturnsAsync(HelloVerificationResult.Verified);

        var manager = CreateManager();
        manager.LockWindow();

        var result = await manager.TryUnlockAsync(isAdmin: false);

        Assert.True(result);
        Assert.False(manager.IsLocked);
    }

    [Fact]
    public async Task TryUnlock_HelloCanceled_StaysLocked()
    {
        _windowsHello.Setup(h => h.VerifyAsync(It.IsAny<string>()))
            .ReturnsAsync(HelloVerificationResult.Canceled);

        var manager = CreateManager();
        manager.LockWindow();

        var result = await manager.TryUnlockAsync(isAdmin: false);

        Assert.False(result);
        Assert.True(manager.IsLocked);
    }

    [Fact]
    public async Task TryUnlock_HelloMode_AdminFlagDoesNotBypassHello()
    {
        // Unlike Admin/AdminAndPin modes, Hello is required regardless of isAdmin
        _windowsHello.Setup(h => h.VerifyAsync(It.IsAny<string>()))
            .ReturnsAsync(HelloVerificationResult.Verified);

        var manager = CreateManager();
        manager.LockWindow();

        var result = await manager.TryUnlockAsync(isAdmin: true);

        Assert.True(result);
        _windowsHello.Verify(h => h.VerifyAsync(It.IsAny<string>()), Times.Once);
    }

    // Coverage gap: PromptWindowsHelloForUnlock handles NotAvailable/Failed by showing
    // MessageBox "Use PIN?" then calling PromptPinForUnlock. Cannot test because
    // MessageBox.Show is static and not injectable.

    [Fact]
    public async Task Unlock_WithAdminIpc_BypassesHello()
    {
        var manager = CreateManager();
        manager.LockWindow();

        // Unlock() is called via IPC (admin --unlock); Hello should not be prompted
        manager.Unlock();

        Assert.False(manager.IsLocked);
        _windowsHello.Verify(h => h.VerifyAsync(It.IsAny<string>()), Times.Never);
    }

    // ── UnlockMode.Pin ────────────────────────────────────────────────────────

    [Fact]
    public async Task TryUnlock_PinMode_SecureDesktopRunInvoked()
    {
        // In Pin mode, TryUnlockAsync calls PromptPinForUnlock which invokes secureDesktop.Run.
        // The mock is already set up to execute the action (see ctor), but PinDialog.ShowDialog
        // requires a WinForms message loop — so the secureDesktop mock is configured to NOT
        // call the action here to avoid opening a real dialog. The key assertion is that
        // Hello is never invoked and secureDesktop.Run is the mechanism used.
        _database.Settings.UnlockMode = UnlockMode.Pin;
        _secureDesktop.Setup(s => s.Run(It.IsAny<Action>())); // do not invoke action → not verified

        var manager = CreateManager();
        manager.LockWindow();

        var result = await manager.TryUnlockAsync(isAdmin: false);

        // PIN dialog was not completed → still locked
        Assert.False(result);
        Assert.True(manager.IsLocked);
        _windowsHello.Verify(h => h.VerifyAsync(It.IsAny<string>()), Times.Never);
        _secureDesktop.Verify(s => s.Run(It.IsAny<Action>()), Times.Once);
    }

    [Fact]
    public async Task TryShowWindow_PinMode_InvokesSecureDesktop()
    {
        // In Pin mode, TryShowWindowAsync also routes to PromptPinForUnlock via TryUnlockWith.
        _database.Settings.UnlockMode = UnlockMode.Pin;
        _secureDesktop.Setup(s => s.Run(It.IsAny<Action>())); // do not invoke action

        var manager = CreateManager();
        manager.LockWindow();

        await manager.TryShowWindowAsync();

        _secureDesktop.Verify(s => s.Run(It.IsAny<Action>()), Times.Once);
        _windowsHello.Verify(h => h.VerifyAsync(It.IsAny<string>()), Times.Never);
    }

    // ── StartAutoLockTimer ────────────────────────────────────────────────────

    [Fact]
    public void StartAutoLockTimer_FeatureDisabled_DoesNothing()
    {
        // Arrange
        _database.Settings.AutoLockInBackground = false;
        _database.Settings.AutoLockTimeoutMinutes = 5;
        var manager = CreateManager();

        // Act
        manager.StartAutoLockTimer();

        // Assert
        _autoLockTimer.Verify(t => t.Start(It.IsAny<int>(), It.IsAny<Action>()), Times.Never);
        Assert.False(manager.IsLocked);
    }

    [Fact]
    public void StartAutoLockTimer_ImmediateOnZero_True_LocksImmediately()
    {
        // Arrange
        _database.Settings.AutoLockInBackground = true;
        _database.Settings.AutoLockTimeoutMinutes = 0;
        var manager = CreateManager();
        bool postLockCalled = false;

        // Act
        manager.StartAutoLockTimer(immediateOnZero: true, postLockAction: () => postLockCalled = true);

        // Assert
        Assert.True(manager.IsLocked);
        Assert.True(postLockCalled);
        _autoLockTimer.Verify(t => t.Start(It.IsAny<int>(), It.IsAny<Action>()), Times.Never);
    }

    [Fact]
    public void StartAutoLockTimer_ImmediateOnZero_False_UsesOneMinute()
    {
        // Arrange
        _database.Settings.AutoLockInBackground = true;
        _database.Settings.AutoLockTimeoutMinutes = 0;
        var manager = CreateManager();

        // Act
        manager.StartAutoLockTimer(immediateOnZero: false);

        // Assert
        Assert.False(manager.IsLocked);
        _autoLockTimer.Verify(t => t.Start(60, It.IsAny<Action>()), Times.Once);
    }

    [Fact]
    public void StartAutoLockTimer_NonZeroTimeout_StartsTimer()
    {
        // Arrange
        _database.Settings.AutoLockInBackground = true;
        _database.Settings.AutoLockTimeoutMinutes = 3;
        var manager = CreateManager();

        // Act
        manager.StartAutoLockTimer();

        // Assert
        Assert.False(manager.IsLocked);
        _autoLockTimer.Verify(t => t.Start(180, It.IsAny<Action>()), Times.Once);
    }

    [Fact]
    public void StartAutoLockTimer_PostLockAction_InvokedAfterLock()
    {
        // Arrange
        _database.Settings.AutoLockInBackground = true;
        _database.Settings.AutoLockTimeoutMinutes = 1;
        var manager = CreateManager();
        Action? capturedCallback = null;
        _autoLockTimer.Setup(t => t.Start(It.IsAny<int>(), It.IsAny<Action>()))
            .Callback<int, Action>((_, cb) => capturedCallback = cb);
        bool postLockCalled = false;
        bool wasLockedWhenPostLockCalled = false;

        // Act
        manager.StartAutoLockTimer(postLockAction: () =>
        {
            postLockCalled = true;
            wasLockedWhenPostLockCalled = manager.IsLocked;
        });
        capturedCallback!.Invoke();

        // Assert
        Assert.True(postLockCalled);
        Assert.True(wasLockedWhenPostLockCalled);
        Assert.True(manager.IsLocked);
    }

    // ── UnlockMode.AdminAndPin ────────────────────────────────────────────────

    [Fact]
    public async Task TryUnlock_AdminAndPinMode_AdminBypassesHelloButThenRequiresPin()
    {
        // AdminAndPin: isAdmin=true calls Unlock() which itself shows a PIN dialog.
        // The mock does NOT invoke the action → PIN not confirmed → stays locked.
        _database.Settings.UnlockMode = UnlockMode.AdminAndPin;
        _secureDesktop.Setup(s => s.Run(It.IsAny<Action>())); // do not invoke action

        var manager = CreateManager();
        manager.LockWindow();

        var result = await manager.TryUnlockAsync(isAdmin: true);

        // Hello not used; PIN dialog was entered but not confirmed → remains locked
        Assert.False(result);
        _windowsHello.Verify(h => h.VerifyAsync(It.IsAny<string>()), Times.Never);
        _secureDesktop.Verify(s => s.Run(It.IsAny<Action>()), Times.Once);
    }

    [Fact]
    public async Task TryUnlock_AdminAndPinMode_NonAdminReturnsFalseImmediately()
    {
        // AdminAndPin: non-admin (isAdmin=false) immediately returns false without any prompts.
        _database.Settings.UnlockMode = UnlockMode.AdminAndPin;

        var manager = CreateManager();
        manager.LockWindow();

        var result = await manager.TryUnlockAsync(isAdmin: false);

        Assert.False(result);
        Assert.True(manager.IsLocked);
        _windowsHello.Verify(h => h.VerifyAsync(It.IsAny<string>()), Times.Never);
        _secureDesktop.Verify(s => s.Run(It.IsAny<Action>()), Times.Never);
    }

    [Fact]
    public async Task TryUnlock_AdminMode_RunAsExternalUnlock_DoesNotShowWindow()
    {
        _database.Settings.UnlockMode = UnlockMode.Admin;
        var manager = CreateManager();
        manager.LockWindow();
        var showWindowCount = 0;
        var windowlessUnlockCount = 0;
        manager.ShowWindowUnlockedRequested += () => showWindowCount++;
        manager.WindowlessUnlockCompleted += () => windowlessUnlockCount++;

        var unlockTask = manager.TryUnlockForOperationAsync(isAdmin: false);
        Assert.True(manager.IsUnlockPolling);

        Assert.True(manager.CompletePendingOperationUnlock());
        var result = await unlockTask;

        Assert.True(result);
        Assert.False(manager.IsLocked);
        Assert.Equal(0, showWindowCount);
        Assert.Equal(1, windowlessUnlockCount);
        _unlockProcessLauncher.Verify(l => l.LaunchUnlockProcess(true), Times.Once);
    }

    [Fact]
    public async Task TryUnlock_AdminMode_NormalExternalUnlock_LaunchesWithoutWaiting()
    {
        _database.Settings.UnlockMode = UnlockMode.Admin;
        var manager = CreateManager();
        manager.LockWindow();

        var result = await manager.TryUnlockAsync(isAdmin: false);

        Assert.False(result);
        Assert.True(manager.IsLocked);
        Assert.False(manager.IsUnlockPolling);
        _unlockProcessLauncher.Verify(l => l.LaunchUnlockProcess(false), Times.Once);
    }

    [Fact]
    public async Task Unlock_NormalExternalUnlockCompletion_CancelsPendingRunAsUnlockAndShowsWindow()
    {
        _database.Settings.UnlockMode = UnlockMode.Admin;
        var manager = CreateManager();
        manager.LockWindow();
        var showWindowCount = 0;
        manager.ShowWindowUnlockedRequested += () => showWindowCount++;

        var runAsUnlockTask = manager.TryUnlockForOperationAsync(isAdmin: false);
        Assert.True(manager.IsUnlockPolling);
        manager.Unlock();
        var runAsResult = await runAsUnlockTask;

        Assert.False(runAsResult);
        Assert.False(manager.IsLocked);
        Assert.False(manager.IsUnlockPolling);
        Assert.Equal(1, showWindowCount);
        _unlockProcessLauncher.Verify(l => l.LaunchUnlockProcess(true), Times.Once);
    }

    [Fact]
    public async Task TryUnlock_AdminMode_AdminRunAsUnlock_DoesNotShowWindow()
    {
        _database.Settings.UnlockMode = UnlockMode.Admin;
        var manager = CreateManager();
        manager.LockWindow();
        var showWindowCount = 0;
        var windowlessUnlockCount = 0;
        manager.ShowWindowUnlockedRequested += () => showWindowCount++;
        manager.WindowlessUnlockCompleted += () => windowlessUnlockCount++;

        var result = await manager.TryUnlockForOperationAsync(isAdmin: true);

        Assert.True(result);
        Assert.False(manager.IsLocked);
        Assert.Equal(0, showWindowCount);
        Assert.Equal(1, windowlessUnlockCount);
    }

    [Fact]
    public async Task TryUnlock_AdminAndPinMode_AdminRunAsUnlock_RequiresPinAndDoesNotShowWindow()
    {
        _database.Settings.UnlockMode = UnlockMode.AdminAndPin;
        _secureDesktop.Setup(s => s.Run(It.IsAny<Action>()));
        var manager = CreateManager();
        manager.LockWindow();
        var showWindowCount = 0;
        manager.ShowWindowUnlockedRequested += () => showWindowCount++;

        var result = await manager.TryUnlockForOperationAsync(isAdmin: true);

        Assert.False(result);
        Assert.True(manager.IsLocked);
        Assert.Equal(0, showWindowCount);
        _secureDesktop.Verify(s => s.Run(It.IsAny<Action>()), Times.Once);
        _windowsHello.Verify(h => h.VerifyAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task TryUnlock_PinMode_AdminRunAsUnlock_RequiresPinAndDoesNotShowWindow()
    {
        _database.Settings.UnlockMode = UnlockMode.Pin;
        _secureDesktop.Setup(s => s.Run(It.IsAny<Action>()));
        var manager = CreateManager();
        manager.LockWindow();
        var showWindowCount = 0;
        manager.ShowWindowUnlockedRequested += () => showWindowCount++;

        var result = await manager.TryUnlockForOperationAsync(isAdmin: true);

        Assert.False(result);
        Assert.True(manager.IsLocked);
        Assert.Equal(0, showWindowCount);
        _secureDesktop.Verify(s => s.Run(It.IsAny<Action>()), Times.Once);
        _windowsHello.Verify(h => h.VerifyAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task TryUnlock_WindowsHelloRunAsUnlock_DoesNotShowWindow()
    {
        _database.Settings.UnlockMode = UnlockMode.WindowsHello;
        _windowsHello.Setup(h => h.VerifyAsync(It.IsAny<string>()))
            .ReturnsAsync(HelloVerificationResult.Verified);
        var manager = CreateManager();
        manager.LockWindow();
        var showWindowCount = 0;
        manager.ShowWindowUnlockedRequested += () => showWindowCount++;

        var result = await manager.TryUnlockForOperationAsync(isAdmin: false);

        Assert.True(result);
        Assert.False(manager.IsLocked);
        Assert.Equal(0, showWindowCount);
    }

    [Fact]
    public async Task TryUnlock_WindowsHelloMode_ConcurrentCredentialUnlockReturnsFalse()
    {
        _database.Settings.UnlockMode = UnlockMode.WindowsHello;
        var verificationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseVerification = new TaskCompletionSource<HelloVerificationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _windowsHello.Setup(h => h.VerifyAsync(It.IsAny<string>()))
            .Returns(() =>
            {
                verificationStarted.TrySetResult();
                return releaseVerification.Task;
            });
        var manager = CreateManager();
        manager.LockWindow();

        var firstUnlockTask = manager.TryUnlockAsync(isAdmin: false);
        await verificationStarted.Task;
        var secondResult = await manager.TryUnlockAsync(isAdmin: false);
        releaseVerification.SetResult(HelloVerificationResult.Verified);
        var firstResult = await firstUnlockTask;

        Assert.False(secondResult);
        Assert.True(firstResult);
        Assert.False(manager.IsLocked);
        _windowsHello.Verify(h => h.VerifyAsync(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task TryUnlock_AdminMode_SecondRunAsUnlockCancelsPreviousWait()
    {
        _database.Settings.UnlockMode = UnlockMode.Admin;
        var manager = CreateManager();
        manager.LockWindow();

        var firstUnlockTask = manager.TryUnlockForOperationAsync(isAdmin: false);
        Assert.True(manager.IsUnlockPolling);

        var secondUnlockTask = manager.TryUnlockForOperationAsync(isAdmin: false);
        Assert.False(await firstUnlockTask);
        Assert.True(manager.IsUnlockPolling);

        Assert.True(manager.CompletePendingOperationUnlock());
        Assert.True(await secondUnlockTask);
        Assert.False(manager.IsUnlockPolling);
        _unlockProcessLauncher.Verify(l => l.LaunchUnlockProcess(true), Times.Exactly(2));
    }

    [Fact]
    public async Task TryShowWindow_AdminMode_CancelsPendingRunAsUnlock()
    {
        _database.Settings.UnlockMode = UnlockMode.Admin;
        var manager = CreateManager();
        manager.LockWindow();

        var runAsUnlockTask = manager.TryUnlockForOperationAsync(isAdmin: false);
        Assert.True(manager.IsUnlockPolling);

        await manager.TryShowWindowAsync();
        var runAsResult = await runAsUnlockTask;

        Assert.False(runAsResult);
        Assert.True(manager.IsLocked);
        Assert.False(manager.IsUnlockPolling);
        _unlockProcessLauncher.Verify(l => l.LaunchUnlockProcess(true), Times.Once);
        _unlockProcessLauncher.Verify(l => l.LaunchUnlockProcess(false), Times.Once);
    }

}
