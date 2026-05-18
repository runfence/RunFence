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
    private readonly Mock<IByteArrayCredentialEncryptionService> _encryptionService = new();
    private readonly Mock<IDatabaseService> _databaseService = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly AppDatabase _database = new();
    private readonly CredentialStore _credentialStore = new();
    private readonly SecureSecret _pinKey = TestSecretFactory.Create(32);
    private readonly List<SessionContext> _sessions = [];

    public RunAsCredentialPersisterTests()
    {
        _appState.Setup(c => c.Database).Returns(_database);
    }

    public void Dispose()
    {
        foreach (var session in _sessions)
            session.Dispose();

        _pinKey.Dispose();
    }

    private SessionContext CreateSession()
    {
        var session = new SessionContext
{
            Database = _database,
            CredentialStore = _credentialStore,
        }.WithOwnedPinDerivedKey(_pinKey);
        _sessions.Add(session);
        return session;
    }

    private RunAsCredentialPersister CreatePersister()
        => new(
            _appState.Object,
            CreateSession(),
            new ByteArrayCredentialEncryptionSpanAdapter(_encryptionService.Object),
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
        _databaseService.Verify(d => d.SaveConfig(_database, It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()), Times.Once);
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
        _databaseService.Verify(d => d.SaveConfig(It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()), Times.Never);
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
        _databaseService.Verify(d => d.SaveConfig(_database, It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()), Times.Once);
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
        _databaseService.Verify(d => d.SaveConfig(It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()), Times.Never);
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
        _encryptionService.Verify(e => e.Encrypt(It.IsAny<ProtectedString>(), It.IsAny<byte[]>()), Times.Never);
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
        _encryptionService.Setup(e => e.Encrypt(It.IsAny<ProtectedString>(), It.IsAny<byte[]>()))
            .Returns(encryptedBytes);
        var credential = new CredentialEntry { Sid = AccountSid };
        using var pw = new ProtectedString();
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
        _encryptionService.Setup(e => e.Encrypt(It.IsAny<ProtectedString>(), It.IsAny<byte[]>()))
            .Returns(newEncryptedBytes);
        var credential = new CredentialEntry { Sid = AccountSid };
        using var pw = new ProtectedString();
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
        _encryptionService.Setup(e => e.Encrypt(It.IsAny<ProtectedString>(), It.IsAny<byte[]>()))
            .Throws(new InvalidOperationException("Encryption failure"));
        var credential = new CredentialEntry { Sid = AccountSid };
        using var pw = new ProtectedString();
        pw.AppendChar('P');
        using var result = MakeResult(credential, rememberPassword: true, adHocPassword: pw);
        var persister = CreatePersister();

        // Act — must not throw
        persister.TrySaveRememberedPassword(result);

        // Assert: warning logged, no credential added
        _log.Verify(l => l.Warn(It.Is<string>(s => s.Contains("remember"))), Times.Once);
        Assert.Empty(_credentialStore.Credentials);
    }

    [Fact]
    public void TrySaveRememberedPassword_EncryptedPassword_RoundTripsWithSnapshotKey()
    {
        byte[] pinKeyBytes = new byte[32];
        for (int i = 0; i < pinKeyBytes.Length; i++)
            pinKeyBytes[i] = (byte)(255 - i);

        var encryptionService = new CredentialEncryptionService(new NativeDpapiProtector());
        using var roundTripPinKey = TestSecretFactory.FromBytes(pinKeyBytes.ToArray());
        using var session = new SessionContext
{
            Database = _database,
            CredentialStore = _credentialStore,
        }.WithOwnedPinDerivedKey(roundTripPinKey);
        var persister = new RunAsCredentialPersister(
            _appState.Object,
            session,
            encryptionService,
            _databaseService.Object,
            _log.Object);

        using var password = ProtectedString.FromChars("RememberMe!".AsSpan());
        using var result = MakeResult(new CredentialEntry { Sid = AccountSid }, rememberPassword: true, adHocPassword: password);

        persister.TrySaveRememberedPassword(result);

        var stored = Assert.Single(_credentialStore.Credentials);
        using var decrypted = encryptionService.Decrypt(stored.EncryptedPassword, pinKeyBytes);
        Assert.True(ProtectedString.ContentEqual(password, decrypted));
        _databaseService.Verify(d => d.SaveCredentialStore(_credentialStore), Times.Once);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static RunAsDialogResult MakeResult(
        CredentialEntry? credential,
        bool rememberPassword = false,
        ProtectedString? adHocPassword = null)
        => new(
            Credential: credential,
            SelectedContainer: null,
            PermissionGrant: null,
            CreateAppEntryOnly: false,
            PrivilegeLevel: PrivilegeLevel.Isolated,
            UpdateOriginalShortcut: false,
            RevertShortcutRequested: false,
            EditExistingApp: null,
            ExistingAppForLaunch: null,
            AdHocPassword: adHocPassword,
            RememberPassword: rememberPassword);
}
