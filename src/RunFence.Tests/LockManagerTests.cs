using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence;
using RunFence.Security;
using RunFence.Startup;
using RunFence.Startup.UI;
using Xunit;

namespace RunFence.Tests;

public class LockManagerTests : IDisposable
{
    private readonly Mock<IPinService> _pinService = new();
    private readonly Mock<IDatabaseService> _databaseService = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<ISecureDesktopRunner> _secureDesktop = new();
    private readonly Mock<IAppInitializationHelper> _appInit = new();
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
        new(_session, _pinService.Object, _databaseService.Object, _log.Object,
            secureDesktop: _secureDesktop.Object, appInit: _appInit.Object,
            windowsHello: _windowsHello.Object);

    [Fact]
    public void TryShowWindow_HelloVerified_Unlocks()
    {
        _windowsHello.Setup(h => h.VerifySync(It.IsAny<string>()))
            .Returns(HelloVerificationResult.Verified);

        var manager = CreateManager();
        manager.LockWindow();

        manager.TryShowWindow();

        Assert.False(manager.IsLocked);
        Assert.NotNull(_session.LastPinVerifiedAt);
    }

    [Fact]
    public void TryShowWindow_HelloCanceled_StaysLocked()
    {
        _windowsHello.Setup(h => h.VerifySync(It.IsAny<string>()))
            .Returns(HelloVerificationResult.Canceled);

        var manager = CreateManager();
        manager.LockWindow();

        manager.TryShowWindow();

        Assert.True(manager.IsLocked);
    }

    [Fact]
    public void TryUnlock_HelloVerified_Unlocks()
    {
        _windowsHello.Setup(h => h.VerifySync(It.IsAny<string>()))
            .Returns(HelloVerificationResult.Verified);

        var manager = CreateManager();
        manager.LockWindow();

        var result = manager.TryUnlock(isAdmin: false);

        Assert.True(result);
        Assert.False(manager.IsLocked);
    }

    [Fact]
    public void TryUnlock_HelloCanceled_StaysLocked()
    {
        _windowsHello.Setup(h => h.VerifySync(It.IsAny<string>()))
            .Returns(HelloVerificationResult.Canceled);

        var manager = CreateManager();
        manager.LockWindow();

        var result = manager.TryUnlock(isAdmin: false);

        Assert.False(result);
        Assert.True(manager.IsLocked);
    }

    [Fact]
    public void TryUnlock_HelloMode_AdminFlagDoesNotBypassHello()
    {
        // Unlike Admin/AdminAndPin modes, Hello is required regardless of isAdmin
        _windowsHello.Setup(h => h.VerifySync(It.IsAny<string>()))
            .Returns(HelloVerificationResult.Verified);

        var manager = CreateManager();
        manager.LockWindow();

        var result = manager.TryUnlock(isAdmin: true);

        Assert.True(result);
        _windowsHello.Verify(h => h.VerifySync(It.IsAny<string>()), Times.Once);
    }

    // Coverage gap: PromptWindowsHelloForUnlock handles NotAvailable/Failed by showing
    // MessageBox "Use PIN?" then calling PromptPinForUnlock. Cannot test because
    // MessageBox.Show is static and not injectable.

    [Fact]
    public void Unlock_WithAdminIpc_BypassesHello()
    {
        var manager = CreateManager();
        manager.LockWindow();

        // Unlock() is called via IPC (admin --unlock); Hello should not be prompted
        manager.Unlock();

        Assert.False(manager.IsLocked);
        _windowsHello.Verify(h => h.VerifySync(It.IsAny<string>()), Times.Never);
    }
}