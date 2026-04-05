using System.Security;
using Moq;
using RunFence.Account;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence;
using RunFence.Security;
using Xunit;

namespace RunFence.Tests;

public class AccountCredentialManagerTests : IDisposable
{
    private const string FakeSid = "S-1-5-21-9999999999-9999999999-9999999999-9001";
    private const string FakeSid2 = "S-1-5-21-9999999999-9999999999-9999999999-9002";

    private readonly AccountCredentialManager _manager;
    private readonly Mock<ILoggingService> _log;
    private readonly Mock<ISidNameCacheService> _sidNameCache;
    private readonly TempDirectory _tempDir;
    private readonly ProtectedBuffer _pinKey;
    private readonly byte[] _argonSalt;

    public AccountCredentialManagerTests()
    {
        _log = new Mock<ILoggingService>();
        _sidNameCache = new Mock<ISidNameCacheService>();
        _tempDir = new TempDirectory("RunFence_CredMgrTest");
        var db = new DatabaseService(_log.Object, allowPlaintextConfig: true,
            configDir: _tempDir.Path, localDataDir: _tempDir.Path);
        var pinKeyBytes = new byte[32];
        new Random(42).NextBytes(pinKeyBytes);
        _argonSalt = new byte[32];
        new Random(99).NextBytes(_argonSalt);
        _pinKey = new ProtectedBuffer(pinKeyBytes, protect: false);

        var encryptionService = new CredentialEncryptionService();
        _manager = new AccountCredentialManager(encryptionService, db, db, _log.Object, _sidNameCache.Object);
    }

    public void Dispose()
    {
        _pinKey.Dispose();
        _tempDir.Dispose();
    }

    [Fact]
    public void ApplyStaleNameUpdates_StaleDetected_UpdatesCacheAndSaves()
    {
        // Arrange
        var database = new AppDatabase
        {
            SidNames =
            {
                [FakeSid] = "old_alice"
            }
        };

        // Full resolved name is stored as-is (not stripped) per CLAUDE.md SidNames convention
        var resolutions = new Dictionary<string, string?>
        {
            [FakeSid] = "DOMAIN\\alice"
        };

        // Act
        var changed = _manager.ApplyStaleNameUpdates(resolutions, database, _pinKey, _argonSalt);

        // Assert — stale name update delegated to cache service with full resolved name
        Assert.True(changed);
        _sidNameCache.Verify(c => c.UpdateName(FakeSid, "DOMAIN\\alice"), Times.Once);
    }

    [Fact]
    public void ApplyStaleNameUpdates_NoChange_ReturnsFalse()
    {
        // Arrange
        var database = new AppDatabase
        {
            SidNames =
            {
                [FakeSid] = "DOMAIN\\alice"
            }
        };

        var resolutions = new Dictionary<string, string?>
        {
            [FakeSid] = "DOMAIN\\alice" // same full name — no change
        };

        // Act
        var changed = _manager.ApplyStaleNameUpdates(resolutions, database, _pinKey, _argonSalt);

        // Assert — no change
        Assert.False(changed);
    }

    // --- StoreCreatedUserCredential ---

    [Fact]
    public void StoreCreatedUserCredential_NewSid_AddsCredentialAndReturnsId()
    {
        // Arrange
        var store = new CredentialStore();
        using var password = new SecureString();
        foreach (var c in "pass")
            password.AppendChar(c);

        // Act
        var id = _manager.StoreCreatedUserCredential(FakeSid, password, store, _pinKey);

        // Assert
        Assert.NotNull(id);
        Assert.Single(store.Credentials);
        Assert.Equal(FakeSid, store.Credentials[0].Sid);
        Assert.Equal(id.Value, store.Credentials[0].Id);
        Assert.NotEmpty(store.Credentials[0].EncryptedPassword);
    }

    [Fact]
    public void StoreCreatedUserCredential_DuplicateSid_ReturnsNullAndDoesNotAdd()
    {
        // Arrange
        var store = new CredentialStore();
        store.Credentials.Add(new CredentialEntry { Sid = FakeSid });
        using var password = new SecureString();

        // Act
        var id = _manager.StoreCreatedUserCredential(FakeSid, password, store, _pinKey);

        // Assert
        Assert.Null(id);
        Assert.Single(store.Credentials); // no duplicate added
    }

    // --- AddNewCredential ---

    [Fact]
    public void AddNewCredential_NewSid_AddsCredentialAndReturnsSuccess()
    {
        // Arrange
        var store = new CredentialStore();
        using var password = new SecureString();
        foreach (var c in "pass")
            password.AppendChar(c);

        // Act
        var (success, id, error) = _manager.AddNewCredential(FakeSid, password, store, _pinKey);

        // Assert
        Assert.True(success);
        Assert.NotNull(id);
        Assert.Null(error);
        Assert.Single(store.Credentials);
        Assert.Equal(FakeSid, store.Credentials[0].Sid);
    }

    [Fact]
    public void AddNewCredential_DuplicateSid_ReturnsErrorAndDoesNotAdd()
    {
        // Arrange
        var store = new CredentialStore();
        store.Credentials.Add(new CredentialEntry { Sid = FakeSid });

        // Act
        var (success, id, error) = _manager.AddNewCredential(FakeSid, null, store, _pinKey);

        // Assert
        Assert.False(success);
        Assert.Null(id);
        Assert.NotNull(error);
        Assert.Contains("already exists", error, StringComparison.OrdinalIgnoreCase);
        Assert.Single(store.Credentials); // no duplicate added
    }

    [Fact]
    public void AddNewCredential_NullPassword_AddsWithEmptyEncryptedPassword()
    {
        // Arrange
        var store = new CredentialStore();

        // Act
        var (success, _, _) = _manager.AddNewCredential(FakeSid, null, store, _pinKey);

        // Assert
        Assert.True(success);
        Assert.Single(store.Credentials);
        Assert.Empty(store.Credentials[0].EncryptedPassword);
    }

    // --- RemoveCredential ---

    [Fact]
    public void RemoveCredential_ExistingId_RemovesEntry()
    {
        // Arrange
        var store = new CredentialStore();
        var credId = Guid.NewGuid();
        store.Credentials.Add(new CredentialEntry { Id = credId, Sid = FakeSid });

        // Act
        _manager.RemoveCredential(credId, store);

        // Assert
        Assert.Empty(store.Credentials);
    }

    [Fact]
    public void RemoveCredential_WrongId_LeavesOtherCredentials()
    {
        // Arrange
        var store = new CredentialStore();
        var credId = Guid.NewGuid();
        store.Credentials.Add(new CredentialEntry { Id = credId, Sid = FakeSid });

        // Act
        _manager.RemoveCredential(Guid.NewGuid(), store);

        // Assert
        Assert.Single(store.Credentials);
    }

    // --- RemoveCredentialsBySid ---

    [Fact]
    public void RemoveCredentialsBySid_MatchingSid_RemovesAllMatching()
    {
        // Arrange
        var store = new CredentialStore();
        store.Credentials.Add(new CredentialEntry { Sid = FakeSid });
        store.Credentials.Add(new CredentialEntry { Sid = FakeSid });
        store.Credentials.Add(new CredentialEntry { Sid = FakeSid2 }); // different SID

        // Act
        _manager.RemoveCredentialsBySid(FakeSid, store);

        // Assert
        Assert.Single(store.Credentials);
        Assert.Equal(FakeSid2, store.Credentials[0].Sid);
    }

    [Fact]
    public void RemoveCredentialsBySid_CaseInsensitive_RemovesEntry()
    {
        // Arrange
        var store = new CredentialStore();
        store.Credentials.Add(new CredentialEntry { Sid = FakeSid.ToUpperInvariant() });

        // Act
        _manager.RemoveCredentialsBySid(FakeSid.ToLowerInvariant(), store);

        // Assert
        Assert.Empty(store.Credentials);
    }

    [Fact]
    public void RemoveCredentialsBySid_NoMatch_LeavesCredentialsUnchanged()
    {
        // Arrange
        var store = new CredentialStore();
        store.Credentials.Add(new CredentialEntry { Sid = FakeSid });

        // Act
        _manager.RemoveCredentialsBySid(FakeSid2, store);

        // Assert
        Assert.Single(store.Credentials);
    }

    [Fact]
    public void ApplyStaleNameUpdates_NullResolution_Skipped()
    {
        // Arrange
        var database = new AppDatabase
        {
            SidNames =
            {
                [FakeSid] = "alice"
            }
        };

        var resolutions = new Dictionary<string, string?>
        {
            [FakeSid] = null // null = resolution failed, skip
        };

        // Act
        var changed = _manager.ApplyStaleNameUpdates(resolutions, database, _pinKey, _argonSalt);

        // Assert — nothing changed
        Assert.False(changed);
        Assert.Equal("alice", database.SidNames[FakeSid]);
    }
}