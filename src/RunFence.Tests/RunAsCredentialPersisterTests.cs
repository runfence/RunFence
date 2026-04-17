using System.Security;
using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.RunAs;
using RunFence.RunAs.UI;
using RunFence.Security;
using Xunit;

namespace RunFence.Tests;

public class RunAsCredentialPersisterTests : IDisposable
{
    private const string AccountSid = "S-1-5-21-1000-1000-1000-1001";
    private const string ContainerName = "rfn_testcontainer";

    private readonly Mock<IAppStateProvider> _appState = new();
    private readonly Mock<ICredentialEncryptionService> _encryptionService = new();
    private readonly Mock<IDatabaseService> _databaseService = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly AppDatabase _database = new();
    private readonly CredentialStore _credentialStore = new();
    private readonly ProtectedBuffer _pinKey = new(new byte[32], protect: false);

    public RunAsCredentialPersisterTests()
    {
        _appState.Setup(c => c.Database).Returns(_database);
    }

    public void Dispose() => _pinKey.Dispose();

    private SessionContext CreateSession() => new()
    {
        Database = _database,
        CredentialStore = _credentialStore,
        PinDerivedKey = _pinKey
    };

    private RunAsCredentialPersister CreatePersister()
        => new(_appState.Object, CreateSession(), _encryptionService.Object,
            _databaseService.Object, _log.Object);

    // ── Constructor — initial state from database ─────────────────────────

    [Fact]
    public void Constructor_InitializesFromDatabaseSettings()
    {
        // Arrange
        _database.Settings.LastUsedRunAsAccountSid = AccountSid;
        _database.Settings.LastUsedRunAsContainerName = ContainerName;

        // Act
        var persister = CreatePersister();

        // Assert: persister reflects stored settings
        Assert.Equal(AccountSid, persister.LastUsedRunAsAccountSid);
        Assert.Equal(ContainerName, persister.LastUsedRunAsContainerName);
    }

    // ── SetLastUsedAccount ────────────────────────────────────────────────

    [Fact]
    public void SetLastUsedAccount_UpdatesAccountSidAndClearsContainer()
    {
        // Arrange
        _database.Settings.LastUsedRunAsContainerName = ContainerName;
        var persister = CreatePersister();

        // Act
        persister.SetLastUsedAccount(AccountSid);

        // Assert
        Assert.Equal(AccountSid, persister.LastUsedRunAsAccountSid);
        Assert.Null(persister.LastUsedRunAsContainerName);
    }

    [Fact]
    public void SetLastUsedAccount_PersistsToDatabase()
    {
        // Arrange
        var persister = CreatePersister();

        // Act
        persister.SetLastUsedAccount(AccountSid);

        // Assert: settings updated and config saved
        Assert.Equal(AccountSid, _database.Settings.LastUsedRunAsAccountSid);
        Assert.Null(_database.Settings.LastUsedRunAsContainerName);
        _databaseService.Verify(d => d.SaveConfig(_database, It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Once);
    }

    [Fact]
    public void SetLastUsedAccount_SameValueAsAlreadyStored_DoesNotSave()
    {
        // Arrange: database already has this SID, container is null
        _database.Settings.LastUsedRunAsAccountSid = AccountSid;
        _database.Settings.LastUsedRunAsContainerName = null;
        var persister = CreatePersister();

        // Act
        persister.SetLastUsedAccount(AccountSid);

        // Assert: no redundant save since nothing changed
        _databaseService.Verify(d => d.SaveConfig(It.IsAny<AppDatabase>(), It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Never);
    }

    // ── SetLastUsedContainer ──────────────────────────────────────────────

    [Fact]
    public void SetLastUsedContainer_UpdatesContainerAndClearsAccountSid()
    {
        // Arrange
        _database.Settings.LastUsedRunAsAccountSid = AccountSid;
        var persister = CreatePersister();

        // Act
        persister.SetLastUsedContainer(ContainerName);

        // Assert
        Assert.Equal(ContainerName, persister.LastUsedRunAsContainerName);
        Assert.Null(persister.LastUsedRunAsAccountSid);
    }

    [Fact]
    public void SetLastUsedContainer_PersistsToDatabase()
    {
        // Arrange
        var persister = CreatePersister();

        // Act
        persister.SetLastUsedContainer(ContainerName);

        // Assert
        Assert.Equal(ContainerName, _database.Settings.LastUsedRunAsContainerName);
        Assert.Null(_database.Settings.LastUsedRunAsAccountSid);
        _databaseService.Verify(d => d.SaveConfig(_database, It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Once);
    }

    [Fact]
    public void SetLastUsedContainer_SameValueAsAlreadyStored_DoesNotSave()
    {
        // Arrange: database already has this container, account SID is null
        _database.Settings.LastUsedRunAsContainerName = ContainerName;
        _database.Settings.LastUsedRunAsAccountSid = null;
        var persister = CreatePersister();

        // Act
        persister.SetLastUsedContainer(ContainerName);

        // Assert: no redundant save since nothing changed
        _databaseService.Verify(d => d.SaveConfig(It.IsAny<AppDatabase>(), It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Never);
    }

    // ── TrySaveRememberedPassword ─────────────────────────────────────────

    [Fact]
    public void TrySaveRememberedPassword_RememberFalse_DoesNothing()
    {
        // Arrange
        var credential = new CredentialEntry { Sid = AccountSid };
        using var result = MakeResult(credential, rememberPassword: false);
        var persister = CreatePersister();

        // Act
        persister.TrySaveRememberedPassword(result);

        // Assert: no credential added, no save
        Assert.Empty(_credentialStore.Credentials);
        _encryptionService.Verify(e => e.Encrypt(It.IsAny<SecureString>(), It.IsAny<byte[]>()), Times.Never);
        _databaseService.Verify(d => d.SaveCredentialStore(It.IsAny<CredentialStore>()), Times.Never);
    }

    [Fact]
    public void TrySaveRememberedPassword_NullAdHocPassword_DoesNothing()
    {
        // Arrange: RememberPassword=true but no password provided
        var credential = new CredentialEntry { Sid = AccountSid };
        using var result = MakeResult(credential, rememberPassword: true, adHocPassword: null);
        var persister = CreatePersister();

        // Act
        persister.TrySaveRememberedPassword(result);

        // Assert
        Assert.Empty(_credentialStore.Credentials);
        _databaseService.Verify(d => d.SaveCredentialStore(It.IsAny<CredentialStore>()), Times.Never);
    }

    [Fact]
    public void TrySaveRememberedPassword_NullCredential_DoesNothing()
    {
        // Arrange: RememberPassword=true but no credential selected
        using var result = MakeResult(credential: null, rememberPassword: true);
        var persister = CreatePersister();

        // Act
        persister.TrySaveRememberedPassword(result);

        // Assert
        Assert.Empty(_credentialStore.Credentials);
        _databaseService.Verify(d => d.SaveCredentialStore(It.IsAny<CredentialStore>()), Times.Never);
    }

    [Fact]
    public void TrySaveRememberedPassword_ValidResult_EncryptsAndStoresCredential()
    {
        // Arrange
        var encryptedBytes = new byte[] { 1, 2, 3 };
        _encryptionService.Setup(e => e.Encrypt(It.IsAny<SecureString>(), It.IsAny<byte[]>()))
            .Returns(encryptedBytes);
        var credential = new CredentialEntry { Sid = AccountSid };
        using var pw = new SecureString();
        pw.AppendChar('P');
        using var result = MakeResult(credential, rememberPassword: true, adHocPassword: pw);
        var persister = CreatePersister();

        // Act
        persister.TrySaveRememberedPassword(result);

        // Assert: credential added with encrypted password and correct SID
        Assert.Single(_credentialStore.Credentials);
        Assert.Equal(AccountSid, _credentialStore.Credentials[0].Sid, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(encryptedBytes, _credentialStore.Credentials[0].EncryptedPassword);
        _databaseService.Verify(d => d.SaveCredentialStore(_credentialStore), Times.Once);
    }

    [Fact]
    public void TrySaveRememberedPassword_DuplicateSid_UpdatesExistingInsteadOfAdding()
    {
        // Arrange: an existing credential for the same SID is already in the store
        var existingCred = new CredentialEntry { Id = Guid.NewGuid(), Sid = AccountSid, EncryptedPassword = new byte[] { 9, 9, 9 } };
        _credentialStore.Credentials.Add(existingCred);
        var newEncryptedBytes = new byte[] { 1, 2, 3 };
        _encryptionService.Setup(e => e.Encrypt(It.IsAny<SecureString>(), It.IsAny<byte[]>()))
            .Returns(newEncryptedBytes);
        var credential = new CredentialEntry { Sid = AccountSid };
        using var pw = new SecureString();
        pw.AppendChar('P');
        using var result = MakeResult(credential, rememberPassword: true, adHocPassword: pw);
        var persister = CreatePersister();

        // Act
        persister.TrySaveRememberedPassword(result);

        // Assert: no duplicate added, existing entry updated with new encrypted password
        Assert.Single(_credentialStore.Credentials);
        Assert.Same(existingCred, _credentialStore.Credentials[0]);
        Assert.Equal(newEncryptedBytes, _credentialStore.Credentials[0].EncryptedPassword);
        _databaseService.Verify(d => d.SaveCredentialStore(_credentialStore), Times.Once);
    }

    [Fact]
    public void TrySaveRememberedPassword_EncryptionThrows_LogsWarningAndDoesNotThrow()
    {
        // Arrange
        _encryptionService.Setup(e => e.Encrypt(It.IsAny<SecureString>(), It.IsAny<byte[]>()))
            .Throws(new InvalidOperationException("Encryption failure"));
        var credential = new CredentialEntry { Sid = AccountSid };
        using var pw = new SecureString();
        pw.AppendChar('P');
        using var result = MakeResult(credential, rememberPassword: true, adHocPassword: pw);
        var persister = CreatePersister();

        // Act — must not throw
        persister.TrySaveRememberedPassword(result);

        // Assert: warning logged, no credential added
        _log.Verify(l => l.Warn(It.Is<string>(s => s.Contains("remember"))), Times.Once);
        Assert.Empty(_credentialStore.Credentials);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static RunAsDialogResult MakeResult(
        CredentialEntry? credential,
        bool rememberPassword = false,
        SecureString? adHocPassword = null)
        => new(
            Credential: credential,
            SelectedContainer: null,
            PermissionGrant: null,
            CreateAppEntryOnly: false,
            PrivilegeLevel: PrivilegeLevel.Basic,
            UpdateOriginalShortcut: false,
            RevertShortcutRequested: false,
            EditExistingApp: null,
            ExistingAppForLaunch: null,
            AdHocPassword: adHocPassword,
            RememberPassword: rememberPassword);
}