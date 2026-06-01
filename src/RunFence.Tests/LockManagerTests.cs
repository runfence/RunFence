using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Startup.UI;
using Xunit;

namespace RunFence.Tests;

public class LockManagerTests : IDisposable
{
    private readonly AppDatabase _database = new();
    private readonly SecureSecret _pinKey;
    private readonly SessionContext _session;
    private readonly Mock<IUnlockProcessLauncher> _unlockProcessLauncher = new();
    private readonly Mock<IAutoLockTimerService> _autoLockTimer = new();
    private readonly Mock<ICredentialUnlockService> _credentialUnlockService = new();
    private readonly Mock<IUiThreadInvoker> _uiThreadInvoker = new();

    public LockManagerTests()
    {
        _pinKey = TestSecretFactory.Create(32);
        _session = new SessionContext
        {
            Database = _database,
            CredentialStore = new CredentialStore()
        }.WithClonedPinDerivedKey(_pinKey);
        _credentialUnlockService.Setup(c => c.VerifyPin()).Returns(CredentialUnlockResult.Canceled);
        _credentialUnlockService.Setup(c => c.VerifyAsync(It.IsAny<CredentialUnlockMode>())).ReturnsAsync(CredentialUnlockResult.Canceled);
        _uiThreadInvoker.Setup(i => i.Invoke(It.IsAny<Func<bool>>())).Returns<Func<bool>>(f => f());
    }

    public void Dispose() => _pinKey.Dispose();

    private LockManager CreateManager(TimeSpan? timeout = null, IUiThreadInvoker? uiThreadInvoker = null) =>
        new(
            _session,
            Mock.Of<ILoggingService>(),
            _autoLockTimer.Object,
            _unlockProcessLauncher.Object,
            new LockStateService(_session),
            _credentialUnlockService.Object,
            uiThreadInvoker ?? _uiThreadInvoker.Object,
            timeout ?? TimeSpan.FromMinutes(5));

    [Fact]
    public void StartAutoLockTimer_WithConfiguredTimeout_UsesConfiguredTimeout()
    {
        _database.Settings.AutoLockInBackground = true;
        _database.Settings.AutoLockTimeoutMinutes = 5;

        CreateManager().StartAutoLockTimer(immediateOnZero: false);

        _autoLockTimer.Verify(t => t.Start(300, It.IsAny<Action>()), Times.Once);
    }

    [Fact]
    public void StartAutoLockTimer_WithZeroTimeoutAndNoDelayUsesDefaultFallback()
    {
        _database.Settings.AutoLockInBackground = true;
        _database.Settings.AutoLockTimeoutMinutes = 0;

        CreateManager().StartAutoLockTimer(immediateOnZero: false);

        _autoLockTimer.Verify(t => t.Start(60, It.IsAny<Action>()), Times.Once);
    }

    [Fact]
    public void StartAutoLockTimer_WithTimeoutOverride_UsesOverrideSeconds()
    {
        _database.Settings.AutoLockInBackground = true;
        _database.Settings.AutoLockTimeoutMinutes = 5;

        CreateManager().StartAutoLockTimer(immediateOnZero: false, timeoutOverrideSeconds: 10);

        _autoLockTimer.Verify(t => t.Start(10, It.IsAny<Action>()), Times.Once);
    }

    [Fact]
    public async Task TryUnlock_AdminMode_NonAdmin_RequestsExternalUnlock()
    {
        _database.Settings.UnlockMode = UnlockMode.Admin;
        var manager = CreateManager();
        manager.LockWindow();

        var result = await manager.TryUnlockAsync(false);

        Assert.False(result);
        Assert.True(manager.IsLocked);
        _unlockProcessLauncher.Verify(l => l.LaunchUnlockProcess(false), Times.Once);
    }

    [Fact]
    public async Task TryUnlock_AdminAndPin_NonAdmin_RequestsExternalUnlock()
    {
        _database.Settings.UnlockMode = UnlockMode.AdminAndPin;
        var manager = CreateManager();
        manager.LockWindow();

        var result = await manager.TryUnlockAsync(false);

        Assert.False(result);
        Assert.True(manager.IsLocked);
        _unlockProcessLauncher.Verify(l => l.LaunchUnlockProcess(false), Times.Once);
        _credentialUnlockService.Verify(c => c.VerifyAsync(It.IsAny<CredentialUnlockMode>()), Times.Never);
    }

    [Fact]
    public async Task TryUnlock_AdminAndPin_Admin_UnlocksWithPin()
    {
        _database.Settings.UnlockMode = UnlockMode.AdminAndPin;
        _credentialUnlockService.Setup(c => c.VerifyAsync(CredentialUnlockMode.Pin)).ReturnsAsync(CredentialUnlockResult.Succeeded);
        var manager = CreateManager();
        manager.LockWindow();

        var result = await manager.TryUnlockAsync(true);

        Assert.True(result);
        Assert.False(manager.IsLocked);
        _credentialUnlockService.Verify(c => c.VerifyAsync(CredentialUnlockMode.Pin), Times.Once);
    }

    [Fact]
    public async Task TryUnlock_PinMode_UnlocksViaPinVerification()
    {
        _database.Settings.UnlockMode = UnlockMode.Pin;
        _credentialUnlockService.Setup(c => c.VerifyAsync(CredentialUnlockMode.Pin)).ReturnsAsync(CredentialUnlockResult.Succeeded);
        var manager = CreateManager();
        manager.LockWindow();

        var result = await manager.TryUnlockAsync(false);

        Assert.True(result);
        Assert.False(manager.IsLocked);
        _credentialUnlockService.Verify(c => c.VerifyAsync(CredentialUnlockMode.Pin), Times.Once);
    }

    [Theory]
    [InlineData(CredentialUnlockResult.Canceled)]
    [InlineData(CredentialUnlockResult.Failed)]
    [InlineData((CredentialUnlockResult)77)]
    public async Task TryUnlock_Hello_MapsResultByFailureKind(CredentialUnlockResult credentialResult)
    {
        _database.Settings.UnlockMode = UnlockMode.WindowsHello;
        _credentialUnlockService.Setup(c => c.VerifyAsync(CredentialUnlockMode.WindowsHelloThenPin)).ReturnsAsync(credentialResult);
        var manager = CreateManager();
        manager.LockWindow();

        var result = await manager.TryUnlockAsync(false);

        Assert.False(result);
        Assert.True(manager.IsLocked);
        _credentialUnlockService.Verify(c => c.VerifyAsync(CredentialUnlockMode.WindowsHelloThenPin), Times.Once);
    }

    [Theory]
    [InlineData(CredentialUnlockResult.Canceled, OperationUnlockResult.Declined)]
    [InlineData(CredentialUnlockResult.Failed, OperationUnlockResult.Failed)]
    [InlineData((CredentialUnlockResult)77, OperationUnlockResult.Unavailable)]
    public async Task TryUnlockForOperation_Hello_MapsResultByFailureKind(
        CredentialUnlockResult credentialResult,
        OperationUnlockResult expectedResult)
    {
        _database.Settings.UnlockMode = UnlockMode.WindowsHello;
        _credentialUnlockService.Setup(c => c.VerifyAsync(CredentialUnlockMode.WindowsHelloThenPin))
            .ReturnsAsync(credentialResult);
        var manager = CreateManager();
        manager.LockWindow();

        var result = await manager.TryUnlockForOperationWithResultAsync(false);

        Assert.Equal(expectedResult, result);
        Assert.True(manager.IsLocked);
        _credentialUnlockService.Verify(c => c.VerifyAsync(CredentialUnlockMode.WindowsHelloThenPin), Times.Once);
    }

    [Fact]
    public async Task TryUnlock_AdminMode_Admin_UnlocksDirectly()
    {
        _database.Settings.UnlockMode = UnlockMode.Admin;
        var manager = CreateManager();
        manager.LockWindow();

        var result = await manager.TryUnlockAsync(true);

        Assert.True(result);
        Assert.False(manager.IsLocked);
    }

    [Fact]
    public async Task TryUnlockForOperation_AdminMode_NonAdmin_WaitsForCompletion()
    {
        _database.Settings.UnlockMode = UnlockMode.Admin;
        var manager = CreateManager();
        manager.LockWindow();

        var task = manager.TryUnlockForOperationAsync(false);
        Assert.True(manager.IsUnlockPolling);
        Assert.True(manager.CompletePendingOperationUnlock());

        Assert.True(await task);
        Assert.False(manager.IsLocked);
        _unlockProcessLauncher.Verify(l => l.LaunchUnlockProcess(true), Times.Once);
    }

    [Fact]
    public async Task TryUnlockForOperation_LaunchThrows_ClearsPendingOperationUnlock()
    {
        _database.Settings.UnlockMode = UnlockMode.Admin;
        _unlockProcessLauncher.Setup(l => l.LaunchUnlockProcess(true))
            .Throws(new InvalidOperationException("boom"));
        var manager = CreateManager();
        manager.LockWindow();

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.TryUnlockForOperationAsync(false));

        Assert.True(manager.IsLocked);
        Assert.False(manager.IsUnlockPolling);
        Assert.False(manager.CompletePendingOperationUnlock());
    }

    [Fact]
    public async Task TryUnlockForOperation_Timeout_ReturnsUnavailableAndClearsPendingState()
    {
        _database.Settings.UnlockMode = UnlockMode.Admin;
        var manager = CreateManager(TimeSpan.FromMilliseconds(20));
        manager.LockWindow();

        var result = await manager.TryUnlockForOperationWithResultAsync(false);

        Assert.Equal(OperationUnlockResult.Unavailable, result);
        Assert.True(manager.IsLocked);
        Assert.False(manager.IsUnlockPolling);
        Assert.False(manager.CompletePendingOperationUnlock());
    }

    [Fact]
    public void CompletePendingOperationUnlock_WhenNoOperationPending_ReturnsFalse()
    {
        var manager = CreateManager();

        Assert.False(manager.CompletePendingOperationUnlock());
    }

    [Fact]
    public async Task TryUnlock_HelloVerified_MarshalsCompletionToUiThreadInvoker()
    {
        _database.Settings.UnlockMode = UnlockMode.WindowsHello;
        var verification = new TaskCompletionSource<CredentialUnlockResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _credentialUnlockService.Setup(c => c.VerifyAsync(CredentialUnlockMode.WindowsHelloThenPin)).Returns(verification.Task);

        var invoked = new ManualResetEventSlim(false);
        var uiThreadInvoker = new Mock<IUiThreadInvoker>();
        uiThreadInvoker.Setup(i => i.Invoke(It.IsAny<Func<bool>>())).Returns<Func<bool>>(f =>
        {
            invoked.Set();
            return f();
        });

        var manager = CreateManager(uiThreadInvoker: uiThreadInvoker.Object);
        manager.LockWindow();
        var unlockTask = manager.TryUnlockAsync(false);
        verification.SetResult(CredentialUnlockResult.Succeeded);

        Assert.True(await unlockTask);
        Assert.True(invoked.Wait(TimeSpan.FromSeconds(1)));
        Assert.False(manager.IsLocked);
    }
}
