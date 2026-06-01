using Moq;
using RunFence.Account;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public class SessionPersistenceHelperTests : IDisposable
{
    private const string FakeSid = "S-1-5-21-9999999999-9999999999-9999999999-9001";
    private const string FakeSid2 = "S-1-5-21-9999999999-9999999999-9999999999-9002";

    private readonly SessionPersistenceHelper _persistenceHelper;
    private readonly Mock<ISidNameCacheService> _sidNameCache;
    private readonly Mock<IMainConfigPersistence> _configRepository;
    private readonly SecureSecret _pinKey;
    private readonly byte[] _argonSalt;

    public SessionPersistenceHelperTests()
    {
        var log = new Mock<ILoggingService>();
        _sidNameCache = new Mock<ISidNameCacheService>();
        var credentialRepository = new Mock<IConfigReencryptionPersistence>();
        _configRepository = new Mock<IMainConfigPersistence>();
        var pinKeyBytes = new byte[32];
        new Random(42).NextBytes(pinKeyBytes);
        _argonSalt = new byte[32];
        new Random(99).NextBytes(_argonSalt);
        _pinKey = new SecureSecret(
            pinKeyBytes.Length,
            span => pinKeyBytes.AsSpan().CopyTo(span),
            NativeProtectedMemoryApi.Instance,
            null);

        _persistenceHelper = new SessionPersistenceHelper(
            credentialRepository.Object,
            _configRepository.Object,
            _sidNameCache.Object,
            () => new InlineUiThreadInvoker(action => action()),
            log.Object);
    }

    public void Dispose()
    {
        _pinKey.Dispose();
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
        var changed = _persistenceHelper.ApplyStaleNameUpdates(resolutions, database, _pinKey, _argonSalt);

        // Assert — stale name update delegated to cache service with full resolved name
        Assert.True(changed);
        _sidNameCache.Verify(c => c.UpdateName(FakeSid, "DOMAIN\\alice"), Times.Once);
        // R2_TL1: SaveConfig must be called when ApplyStaleNameUpdates returns true
        _configRepository.Verify(r => r.SaveConfig(database, It.IsAny<ISecureSecretSnapshotSource>(), _argonSalt), Times.Once);
    }

    [Fact]
    public void ApplyStaleNameUpdates_SidAbsentFromSidNamesButResolverReturnsName_UpdatesCacheAndSaves()
    {
        // Arrange — R2_TL2: SID not in SidNames, resolver returns non-null name
        var database = new AppDatabase();
        // FakeSid2 intentionally absent from SidNames

        var resolutions = new Dictionary<string, string?>
        {
            [FakeSid2] = "DOMAIN\\bob"
        };

        // Act
        var changed = _persistenceHelper.ApplyStaleNameUpdates(resolutions, database, _pinKey, _argonSalt);

        // Assert — absent SID treated as stale (existing == null != "DOMAIN\\bob"), so it is updated
        Assert.True(changed);
        _sidNameCache.Verify(c => c.UpdateName(FakeSid2, "DOMAIN\\bob"), Times.Once);
        _configRepository.Verify(r => r.SaveConfig(database, It.IsAny<ISecureSecretSnapshotSource>(), _argonSalt), Times.Once);
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
        var changed = _persistenceHelper.ApplyStaleNameUpdates(resolutions, database, _pinKey, _argonSalt);

        // Assert — no change
        Assert.False(changed);
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
        var changed = _persistenceHelper.ApplyStaleNameUpdates(resolutions, database, _pinKey, _argonSalt);

        // Assert — nothing changed
        Assert.False(changed);
        Assert.Equal("alice", database.SidNames[FakeSid]);
    }

    [Fact]
    public async Task SaveConfig_WorkerThread_MarshalsEncryptedSaveToUiThread()
    {
        using var uiInvoker = new DedicatedThreadUiInvoker();
        var configRepository = new Mock<IMainConfigPersistence>();
        var helper = new SessionPersistenceHelper(
            Mock.Of<IConfigReencryptionPersistence>(),
            configRepository.Object,
            Mock.Of<ISidNameCacheService>(),
            () => uiInvoker,
            Mock.Of<ILoggingService>());
        var database = new AppDatabase();
        var saveThreadId = 0;
        configRepository
            .Setup(repository => repository.SaveConfig(database, It.IsAny<ISecureSecretSnapshotSource>(), _argonSalt))
            .Callback(() => saveThreadId = Environment.CurrentManagedThreadId);

        await Task.Run(() => helper.SaveConfig(database, _pinKey, _argonSalt));

        Assert.Equal(uiInvoker.ThreadId, saveThreadId);
    }

    [Fact]
    public async Task SaveCredentialStoreAndConfig_WorkerThread_MarshalsEncryptedSaveToUiThread()
    {
        using var uiInvoker = new DedicatedThreadUiInvoker();
        var credentialRepository = new Mock<IConfigReencryptionPersistence>();
        var helper = new SessionPersistenceHelper(
            credentialRepository.Object,
            Mock.Of<IMainConfigPersistence>(),
            Mock.Of<ISidNameCacheService>(),
            () => uiInvoker,
            Mock.Of<ILoggingService>());
        var store = new CredentialStore();
        var database = new AppDatabase();
        var saveThreadId = 0;
        credentialRepository
            .Setup(repository => repository.SaveCredentialStoreAndConfig(store, database, It.IsAny<ISecureSecretSnapshotSource>()))
            .Callback(() => saveThreadId = Environment.CurrentManagedThreadId);

        await Task.Run(() => helper.SaveCredentialStoreAndConfig(store, database, _pinKey));

        Assert.Equal(uiInvoker.ThreadId, saveThreadId);
    }
}
