using Moq;
using RunFence.Account;
using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Launch.Container;
using RunFence.Launch.Tokens;
using RunFence.Security;
using LaunchProcessInfo = RunFence.Launch.Tokens.ProcessInfo;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Tests for <see cref="ProcessLauncher.Accept(AccountLaunchIdentity, ProcessLaunchTarget)"/> credential resolution behavior.
/// </summary>
public class ProcessLauncherTests : IDisposable
{
    private const string TestSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";

    private readonly Mock<IAccountProcessLauncher> _accountLauncher = new();
    private readonly Mock<ISessionProvider> _sessionProvider = new();
    private readonly Mock<IProfileRepairHelper> _profileRepair = new();
    private readonly Mock<IFolderHandlerService> _folderHandler = new();
    private readonly Mock<IAssociationAutoSetService> _associationAutoSetService = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<ISidResolver> _sidResolver = new();
    private readonly Mock<ICredentialEncryptionService> _encryptionService = new();
    private readonly Mock<IAppContainerProcessLauncher> _containerLauncher = new();

    private readonly AppDatabase _database = new();
    private readonly CredentialStore _credentialStore = new();
    private readonly byte[] _pinDerivedKey = new byte[32];
    private readonly ProtectedBuffer _protectedPinKey;

    private readonly ProcessLauncher _processLauncher;

    private static LaunchProcessInfo MakeProcessInfo()
        => new LaunchProcessInfo(new ProcessLaunchNative.PROCESS_INFORMATION());

    public ProcessLauncherTests()
    {
        _protectedPinKey = new ProtectedBuffer(_pinDerivedKey, protect: false);

        _sessionProvider.Setup(s => s.GetSession()).Returns(new SessionContext
        {
            Database = _database,
            CredentialStore = _credentialStore,
            PinDerivedKey = _protectedPinKey
        });

        var credentialDecryption = new CredentialDecryptionService(
            _encryptionService.Object, _sidResolver.Object);
        var credentialsLookup = new LaunchCredentialsLookup(
            _sessionProvider.Object,
            credentialDecryption);

        _profileRepair
            .Setup(p => p.ExecuteWithProfileRepair(It.IsAny<Func<LaunchProcessInfo?>>(), It.IsAny<string?>()))
            .Returns<Func<LaunchProcessInfo?>, string?>((action, _) => action());

        _accountLauncher
            .Setup(a => a.Launch(It.IsAny<ProcessLaunchTarget>(), It.IsAny<AccountLaunchIdentity>()))
            .Returns(MakeProcessInfo);

        _processLauncher = new ProcessLauncher(
            _accountLauncher.Object,
            credentialsLookup,
            _profileRepair.Object,
            _folderHandler.Object,
            _associationAutoSetService.Object,
            _containerLauncher.Object,
            _log.Object);
    }

    public void Dispose() => _protectedPinKey.Dispose();

    private void SetupStoredPassword(string sid, ProtectedString password)
    {
        var encryptedBytes = new byte[] { 1, 2, 3 };
        _credentialStore.Credentials.Add(new CredentialEntry
        {
            Sid = sid,
            EncryptedPassword = encryptedBytes
        });
        _encryptionService.Setup(e => e.Decrypt(encryptedBytes, _pinDerivedKey)).Returns(password);
        _sidResolver.Setup(s => s.TryResolveName(sid)).Returns("DOMAIN\\testuser");
    }

    [Fact]
    public void Accept_NullCredentials_ResolvesViaLookup()
    {
        // Arrange — no Credentials on identity; lookup must resolve them from the store
        var password = new ProtectedString();
        password.AppendChar('p');
        SetupStoredPassword(TestSid, password);

        var identity = new AccountLaunchIdentity(TestSid)
        {
            PrivilegeLevel = PrivilegeLevel.Basic
        };
        var target = new ProcessLaunchTarget(@"C:\apps\app.exe");

        // Act
        _processLauncher.Accept(identity, target);

        // Assert — account launcher called with resolved credentials
        _accountLauncher.Verify(a => a.Launch(
            target,
            It.Is<AccountLaunchIdentity>(id =>
                id.Credentials != null &&
                id.Credentials.Value.TokenSource == LaunchTokenSource.Credentials)),
            Times.Once);
    }

    [Fact]
    public void Accept_CallerProvidedCredentials_SkipsLookup()
    {
        // Arrange — caller passes Credentials directly; lookup should NOT be called
        using var callerPassword = new ProtectedString();
        callerPassword.AppendChar('x');

        var callerCreds = new LaunchCredentials(callerPassword, "DOMAIN", "user");
        var identity = new AccountLaunchIdentity(TestSid)
        {
            Credentials = callerCreds,
            PrivilegeLevel = PrivilegeLevel.Basic
        };
        var target = new ProcessLaunchTarget(@"C:\apps\app.exe");

        // Act
        _processLauncher.Accept(identity, target);

        // Assert — lookup never called (no EncryptedPassword set up, would throw if called)
        _accountLauncher.Verify(a => a.Launch(
            target,
            It.Is<AccountLaunchIdentity>(id =>
                id.Credentials != null &&
                id.Credentials.Value.Password == callerPassword)),
            Times.Once);
        _encryptionService.Verify(e => e.Decrypt(It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public void Accept_LookedUpPassword_DisposedAfterLaunch()
    {
        // Arrange — password resolved by lookup; must be disposed in finally block
        var password = new ProtectedString();
        password.AppendChar('s');
        SetupStoredPassword(TestSid, password);

        var identity = new AccountLaunchIdentity(TestSid)
        {
            PrivilegeLevel = PrivilegeLevel.Basic
        };
        var target = new ProcessLaunchTarget(@"C:\apps\app.exe");

        // Act
        _processLauncher.Accept(identity, target);

        // Assert — password is disposed after launch (accessing it throws ObjectDisposedException)
        Assert.Throws<ObjectDisposedException>(() => password.AppendChar('x'));
    }

    [Fact]
    public void Accept_CallerPassword_NotDisposed()
    {
        // Arrange — caller provides Credentials; the caller owns the password lifetime;
        // ProcessLauncher must NOT dispose it
        var callerPassword = new ProtectedString();
        callerPassword.AppendChar('y');

        var callerCreds = new LaunchCredentials(callerPassword, "DOMAIN", "user");
        var identity = new AccountLaunchIdentity(TestSid)
        {
            Credentials = callerCreds,
            PrivilegeLevel = PrivilegeLevel.Basic
        };
        var target = new ProcessLaunchTarget(@"C:\apps\app.exe");

        // Act
        _processLauncher.Accept(identity, target);

        // Assert — password still accessible (not disposed by ProcessLauncher)
        callerPassword.AppendChar('z'); // should NOT throw
        callerPassword.Dispose();
    }

    // ── AppContainer dispatch (TC-3) ────────────────────────────────────────

    [Fact]
    public void Launch_AppContainerIdentity_DelegatesToContainerLauncher()
    {
        // Arrange
        var container = new AppContainerEntry { Name = "rfn_test", Sid = "S-1-15-2-1-2-3-4" };
        var identity = new AppContainerLaunchIdentity(container);
        var target = new ProcessLaunchTarget(@"C:\apps\browser.exe");
        var expectedInfo = MakeProcessInfo();
        _containerLauncher.Setup(c => c.LaunchFile(target, identity)).Returns(expectedInfo);

        // Act
        var result = _processLauncher.Launch(identity, target);

        // Assert — delegated to container launcher
        Assert.Equal(expectedInfo, result);
        _containerLauncher.Verify(c => c.LaunchFile(target, identity), Times.Once);
        _accountLauncher.Verify(a => a.Launch(It.IsAny<ProcessLaunchTarget>(),
            It.IsAny<AccountLaunchIdentity>()), Times.Never);
    }

    [Fact]
    public void Launch_AppContainerIdentity_DoesNotRegisterFolderHandlerOrAutoSet()
    {
        // Arrange
        var container = new AppContainerEntry { Name = "rfn_browser", Sid = "S-1-15-2-2-2-3" };
        var identity = new AppContainerLaunchIdentity(container);
        _containerLauncher.Setup(c => c.LaunchFile(It.IsAny<ProcessLaunchTarget>(),
            It.IsAny<AppContainerLaunchIdentity>())).Returns(MakeProcessInfo());

        // Act
        _processLauncher.Launch(identity, new ProcessLaunchTarget(@"C:\apps\b.exe"));

        // Assert — folder handler and association auto-set not called for AppContainers
        _folderHandler.Verify(f => f.Register(It.IsAny<string>()), Times.Never);
        _associationAutoSetService.Verify(a => a.AutoSetForUser(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void Accept_AccountIdentity_InteractiveUserTokenSource_DoesNotDisposeCallerPassword()
    {
        // Arrange — InteractiveUser via IsInteractiveUser flag, with a stored password as fallback.
        // ProcessLauncher must not dispose caller-owned credentials.
        var callerPassword = new ProtectedString();
        callerPassword.AppendChar('i');
        var callerCreds = new LaunchCredentials(callerPassword, null, null, LaunchTokenSource.InteractiveUser);
        var identity = new AccountLaunchIdentity(TestSid)
        {
            Credentials = callerCreds,
            PrivilegeLevel = PrivilegeLevel.Basic
        };
        var target = new ProcessLaunchTarget(@"C:\apps\app.exe");

        // Act
        _processLauncher.Accept(identity, target);

        // Assert — password survives (caller owns it)
        callerPassword.AppendChar('j'); // should NOT throw
        callerPassword.Dispose();
    }

    [Fact]
    public void Accept_AccountIdentity_LaunchSuccess_RegistersFolderHandlerAndAutoSet()
    {
        // Arrange
        var identity = new AccountLaunchIdentity(TestSid)
        {
            Credentials = LaunchCredentials.CurrentAccount,
            PrivilegeLevel = PrivilegeLevel.Basic
        };

        // Act
        _processLauncher.Accept(identity, new ProcessLaunchTarget(@"C:\apps\app.exe"));

        // Assert
        _folderHandler.Verify(f => f.Register(TestSid), Times.Once);
        _associationAutoSetService.Verify(a => a.AutoSetForUser(TestSid), Times.Once);
    }
}
