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

    private LockManager CreateManager() =>
        new(_session, _pinService.Object, _log.Object,
            secureDesktop: _secureDesktop.Object,
            windowsHello: _windowsHello.Object,
            autoLockTimerService: new Mock<IAutoLockTimerService>().Object);

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
}