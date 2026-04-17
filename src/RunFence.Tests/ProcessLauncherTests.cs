using System.Security;
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
    private readonly ILaunchCredentialsLookup _credentialsLookup;

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
        _credentialsLookup = new LaunchCredentialsLookup(
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
            _credentialsLookup,
            _profileRepair.Object,
            _folderHandler.Object,
            _associationAutoSetService.Object,
            _containerLauncher.Object,
            _log.Object);
    }

    public void Dispose() => _protectedPinKey.Dispose();

    private void SetupStoredPassword(string sid, SecureString password)
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
        var password = new SecureString();
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
        using var callerPassword = new SecureString();
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
        var password = new SecureString();
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
        var callerPassword = new SecureString();
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
}
