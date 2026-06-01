using Moq;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.RunAs;
using RunFence.Security;
using Xunit;

namespace RunFence.Tests;

public sealed class RunAsCreatedAccountPersistenceCoordinatorTests : IDisposable
{
    private readonly SecureSecret _pinKey = TestSecretFactory.Create(32);
    private readonly List<SessionContext> _sessions = [];

    public void Dispose()
    {
        foreach (var session in _sessions)
            session.Dispose();

        _pinKey.Dispose();
    }

    [Fact]
    public async Task PersistOrRollbackAsync_CleanupStateSaveFailed_NotifiesAndReturnsStatus()
    {
        var database = new AppDatabase();
        var credentialStore = new CredentialStore();
        var session = CreateSession(database, credentialStore);
        var dataChangeNotifier = new Mock<IDataChangeNotifier>();
        var lifecycleManager = new Mock<IAccountLifecycleManager>();
        var databaseService = new Mock<IDatabaseService>();

        var coordinator = CreateCoordinator(
            session,
            dataChangeNotifier: dataChangeNotifier,
            lifecycleManager: lifecycleManager,
            databaseService: databaseService);

        var result = await coordinator.PersistOrRollbackAsync(
            new RunAsCreatedAccountPersistenceRequest
            {
                CreatedAccountStatus = CreateAccountStatus.CleanupStateSaveFailed,
                CreatedAccountErrorMessage = "save failed",
                ScheduleEphemeralCleanupOnRollbackFailure = true
            },
            credentialStore,
            database);

        Assert.Equal(RunAsCreatedAccountPersistenceStatus.CleanupStateSaveFailed, result.Status);
        Assert.Null(result.Credential);
        Assert.True(result.DataChangeNotified);
        Assert.Equal("save failed", result.ErrorMessage);
        Assert.Null(result.RollbackErrorMessage);
        dataChangeNotifier.Verify(n => n.NotifyDataChanged(), Times.Once);
        Assert.Empty(credentialStore.Credentials);
        lifecycleManager.Verify(s => s.DeleteSamAccount(It.IsAny<string>()), Times.Never);
        databaseService.Verify(s => s.SaveCredentialStore(It.IsAny<CredentialStore>()), Times.Never);
    }

    [Fact]
    public async Task PersistOrRollbackAsync_Success_PersistsCredentialWithoutNotification()
    {
        const string sid = "S-1-5-21-1-2-3-1001";
        var database = new AppDatabase();
        var credentialStore = new CredentialStore();
        var session = CreateSession(database, credentialStore);
        using var password = ProtectedString.FromChars("P@ssw0rd".AsSpan());

        var dataChangeNotifier = new Mock<IDataChangeNotifier>();
        var sidNameCache = new Mock<ISidNameCacheService>();
        var localUserProvider = new Mock<ILocalUserProvider>();
        var databaseService = new Mock<IDatabaseService>();

        var coordinator = CreateCoordinator(
            session,
            databaseService: databaseService,
            sidNameCache: sidNameCache,
            localUserProvider: localUserProvider,
            dataChangeNotifier: dataChangeNotifier);

        var result = await coordinator.PersistOrRollbackAsync(
            new RunAsCreatedAccountPersistenceRequest
            {
                CreatedSid = sid,
                Username = "newuser",
                CreatedPassword = password,
                CreatedRollbackState = new CreatedAccountRollbackState
                {
                    Sid = sid,
                    Username = "newuser",
                    HadPreviousAccount = false,
                    HadPreviousSidName = false,
                    HadPreviousFirewallSettings = false
                },
                CreatedAccountStatus = CreateAccountStatus.Succeeded,
                ScheduleEphemeralCleanupOnRollbackFailure = true
            },
            credentialStore,
            database);

        Assert.Equal(RunAsCreatedAccountPersistenceStatus.Succeeded, result.Status);
        var credential = Assert.IsType<CredentialEntry>(result.Credential);
        Assert.Equal(sid, credential.Sid);
        Assert.False(result.DataChangeNotified);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.RollbackErrorMessage);
        Assert.Single(credentialStore.Credentials);
        databaseService.Verify(s => s.SaveCredentialStore(credentialStore), Times.Once);
        sidNameCache.Verify(s => s.ResolveAndCache(sid, "newuser"), Times.Once);
        localUserProvider.Verify(s => s.InvalidateCache(), Times.Once);
        dataChangeNotifier.Verify(n => n.NotifyDataChanged(), Times.Never);
    }

    [Fact]
    public async Task PersistOrRollbackAsync_CredentialSaveFailure_RollsBackCreatedAccount()
    {
        const string sid = "S-1-5-21-1-2-3-1001";
        var database = new AppDatabase();
        database.SidNames[sid] = "newuser";
        database.Accounts.Add(new AccountEntry { Sid = sid, PrivilegeLevel = PrivilegeLevel.HighestAllowed });
        var credentialStore = new CredentialStore();
        var session = CreateSession(database, credentialStore);

        using var password = ProtectedString.FromChars("P@ssw0rd".AsSpan());
        var rollbackState = new CreatedAccountRollbackState
        {
            Sid = sid,
            Username = "newuser",
            HadPreviousAccount = false,
            HadPreviousSidName = false,
            HadPreviousFirewallSettings = false
        };

        var databaseService = new Mock<IDatabaseService>();
        databaseService.Setup(s => s.SaveCredentialStore(credentialStore))
            .Throws(new InvalidOperationException("save failed"));

        var lifecycleManager = new Mock<IAccountLifecycleManager>();
        lifecycleManager.Setup(s => s.DeleteSamAccount(sid))
            .Returns(new AccountDeletionResult(true, sid, null));
        lifecycleManager.Setup(s => s.DeleteProfileAsync(sid))
            .ReturnsAsync((string?)null);

        var credentialManager = new Mock<IAccountCredentialManager>();
        credentialManager.Setup(s => s.RemoveCredential(It.IsAny<Guid>(), credentialStore))
            .Callback<Guid, CredentialStore>((id, store) => store.Credentials.RemoveAll(c => c.Id == id));

        var coordinator = CreateCoordinator(
            session,
            databaseService: databaseService,
            lifecycleManager: lifecycleManager,
            credentialManager: credentialManager);

        var result = await coordinator.PersistOrRollbackAsync(
            new RunAsCreatedAccountPersistenceRequest
            {
                CreatedSid = sid,
                Username = "newuser",
                CreatedPassword = password,
                CreatedRollbackState = rollbackState,
                CreatedAccountStatus = CreateAccountStatus.Succeeded,
                ScheduleEphemeralCleanupOnRollbackFailure = true
            },
            credentialStore,
            database);

        Assert.Equal(RunAsCreatedAccountPersistenceStatus.CredentialSaveRolledBack, result.Status);
        Assert.Null(result.Credential);
        Assert.False(result.DataChangeNotified);
        Assert.Equal("save failed", result.ErrorMessage);
        Assert.Null(result.RollbackErrorMessage);
        Assert.Empty(credentialStore.Credentials);
        Assert.Empty(database.Accounts);
        Assert.DoesNotContain(sid, database.SidNames.Keys);
        lifecycleManager.Verify(s => s.DeleteSamAccount(sid), Times.Once);
        lifecycleManager.Verify(s => s.DeleteProfileAsync(sid), Times.Once);
    }

    [Fact]
    public async Task PersistOrRollbackAsync_CredentialSaveFailureAndRollbackFailure_SchedulesCleanup()
    {
        const string sid = "S-1-5-21-1-2-3-1001";
        var database = new AppDatabase();
        var existingAccount = database.GetOrCreateAccount(sid);
        var credentialStore = new CredentialStore();
        var session = CreateSession(database, credentialStore);

        using var password = ProtectedString.FromChars("P@ssw0rd".AsSpan());
        var rollbackState = new CreatedAccountRollbackState
        {
            Sid = sid,
            Username = "newuser",
            HadPreviousAccount = false,
            HadPreviousSidName = false,
            HadPreviousFirewallSettings = false
        };

        var databaseService = new Mock<IDatabaseService>();
        databaseService.Setup(s => s.SaveCredentialStore(credentialStore))
            .Throws(new InvalidOperationException("save failed"));

        var lifecycleManager = new Mock<IAccountLifecycleManager>();
        lifecycleManager.Setup(s => s.DeleteSamAccount(sid))
            .Returns(new AccountDeletionResult(false, sid, "delete failed"));

        var dataChangeNotifier = new Mock<IDataChangeNotifier>();
        var coordinator = CreateCoordinator(
            session,
            databaseService: databaseService,
            lifecycleManager: lifecycleManager,
            dataChangeNotifier: dataChangeNotifier);

        var result = await coordinator.PersistOrRollbackAsync(
            new RunAsCreatedAccountPersistenceRequest
            {
                CreatedSid = sid,
                Username = "newuser",
                CreatedPassword = password,
                CreatedRollbackState = rollbackState,
                CreatedAccountStatus = CreateAccountStatus.Succeeded,
                ScheduleEphemeralCleanupOnRollbackFailure = true
            },
            credentialStore,
            database);

        Assert.Equal(RunAsCreatedAccountPersistenceStatus.CredentialSaveRollbackFailed, result.Status);
        Assert.Null(result.Credential);
        Assert.True(result.DataChangeNotified);
        Assert.Equal("save failed", result.ErrorMessage);
        Assert.Equal("delete failed", result.RollbackErrorMessage);
        Assert.NotNull(existingAccount.DeleteAfterUtc);
        Assert.Single(credentialStore.Credentials);
        dataChangeNotifier.Verify(n => n.NotifyDataChanged(), Times.Once);
    }

    [Fact]
    public async Task PersistOrRollbackAsync_PrePersistenceFailure_RollsBackCreatedAccount()
    {
        const string sid = "S-1-5-21-1-2-3-1001";
        var database = new AppDatabase();
        database.SidNames[sid] = "newuser";
        database.Accounts.Add(new AccountEntry { Sid = sid, PrivilegeLevel = PrivilegeLevel.Isolated });
        var credentialStore = new CredentialStore();
        var session = CreateSession(database, credentialStore);

        using var password = ProtectedString.FromChars("P@ssw0rd".AsSpan());
        var rollbackState = new CreatedAccountRollbackState
        {
            Sid = sid,
            Username = "newuser",
            HadPreviousAccount = false,
            HadPreviousSidName = false,
            HadPreviousFirewallSettings = false
        };

        var encryptionService = new Mock<IByteArrayCredentialEncryptionService>();
        encryptionService.Setup(s => s.Encrypt(It.IsAny<ProtectedString>(), It.IsAny<byte[]>()))
            .Throws(new InvalidOperationException("encrypt failed"));

        var lifecycleManager = new Mock<IAccountLifecycleManager>();
        lifecycleManager.Setup(s => s.DeleteSamAccount(sid))
            .Returns(new AccountDeletionResult(true, sid, null));
        lifecycleManager.Setup(s => s.DeleteProfileAsync(sid))
            .ReturnsAsync((string?)null);

        var coordinator = CreateCoordinator(
            session,
            encryptionService: encryptionService,
            lifecycleManager: lifecycleManager);

        var result = await coordinator.PersistOrRollbackAsync(
            new RunAsCreatedAccountPersistenceRequest
            {
                CreatedSid = sid,
                Username = "newuser",
                CreatedPassword = password,
                CreatedRollbackState = rollbackState,
                CreatedAccountStatus = CreateAccountStatus.Succeeded,
                ScheduleEphemeralCleanupOnRollbackFailure = true
            },
            credentialStore,
            database);

        Assert.Equal(RunAsCreatedAccountPersistenceStatus.PrePersistenceRolledBack, result.Status);
        Assert.Null(result.Credential);
        Assert.False(result.DataChangeNotified);
        Assert.Equal("encrypt failed", result.ErrorMessage);
        Assert.Null(result.RollbackErrorMessage);
        Assert.Empty(credentialStore.Credentials);
        Assert.Empty(database.Accounts);
        Assert.DoesNotContain(sid, database.SidNames.Keys);
    }

    [Fact]
    public async Task PersistOrRollbackAsync_PrePersistenceFailureAndRollbackFailure_NotifiesCleanupAndReturnsFailedStatus()
    {
        const string sid = "S-1-5-21-1-2-3-1001";
        var database = new AppDatabase();
        var credentialStore = new CredentialStore();
        var session = CreateSession(database, credentialStore);

        using var password = ProtectedString.FromChars("P@ssw0rd".AsSpan());
        var rollbackState = new CreatedAccountRollbackState
        {
            Sid = sid,
            Username = "newuser",
            HadPreviousAccount = false,
            HadPreviousSidName = false,
            HadPreviousFirewallSettings = false
        };

        var encryptionService = new Mock<IByteArrayCredentialEncryptionService>();
        encryptionService.Setup(s => s.Encrypt(It.IsAny<ProtectedString>(), It.IsAny<byte[]>()))
            .Throws(new InvalidOperationException("encrypt failed"));

        var lifecycleManager = new Mock<IAccountLifecycleManager>();
        lifecycleManager.Setup(s => s.DeleteSamAccount(sid))
            .Returns(new AccountDeletionResult(false, sid, "delete failed"));

        var dataChangeNotifier = new Mock<IDataChangeNotifier>();
        var coordinator = CreateCoordinator(
            session,
            encryptionService: encryptionService,
            lifecycleManager: lifecycleManager,
            dataChangeNotifier: dataChangeNotifier);

        var result = await coordinator.PersistOrRollbackAsync(
            new RunAsCreatedAccountPersistenceRequest
            {
                CreatedSid = sid,
                Username = "newuser",
                CreatedPassword = password,
                CreatedRollbackState = rollbackState,
                CreatedAccountStatus = CreateAccountStatus.Succeeded,
                ScheduleEphemeralCleanupOnRollbackFailure = true
            },
            credentialStore,
            database);

        Assert.Equal(RunAsCreatedAccountPersistenceStatus.PrePersistenceRollbackFailed, result.Status);
        Assert.Null(result.Credential);
        Assert.True(result.DataChangeNotified);
        Assert.Equal("encrypt failed", result.ErrorMessage);
        Assert.Equal("delete failed", result.RollbackErrorMessage);
        dataChangeNotifier.Verify(n => n.NotifyDataChanged(), Times.Once);
        lifecycleManager.Verify(s => s.DeleteSamAccount(sid), Times.Once);
        lifecycleManager.Verify(s => s.DeleteProfileAsync(It.IsAny<string>()), Times.Never);
        Assert.NotNull(database.GetAccount(sid)?.DeleteAfterUtc);
    }

    [Fact]
    public async Task PersistOrRollbackAsync_MissingRollbackState_SchedulesCleanupAndRethrows()
    {
        const string sid = "S-1-5-21-1-2-3-1001";
        var database = new AppDatabase();
        var credentialStore = new CredentialStore();
        var session = CreateSession(database, credentialStore);
        using var password = ProtectedString.FromChars("P@ssw0rd".AsSpan());

        var dataChangeNotifier = new Mock<IDataChangeNotifier>();
        var log = new Mock<ILoggingService>();
        var coordinator = CreateCoordinator(
            session,
            dataChangeNotifier: dataChangeNotifier,
            log: log);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.PersistOrRollbackAsync(
                new RunAsCreatedAccountPersistenceRequest
                {
                    CreatedSid = sid,
                    Username = "newuser",
                    CreatedPassword = password,
                    CreatedRollbackState = null,
                    CreatedAccountStatus = CreateAccountStatus.Succeeded,
                    ScheduleEphemeralCleanupOnRollbackFailure = true
                },
                credentialStore,
                database));

        Assert.Equal("Missing rollback state for created RunAs account.", ex.Message);
        Assert.NotNull(database.GetAccount(sid)?.DeleteAfterUtc);
        Assert.Empty(credentialStore.Credentials);
        dataChangeNotifier.Verify(n => n.NotifyDataChanged(), Times.Once);
    }

    private SessionContext CreateSession(AppDatabase database, CredentialStore credentialStore)
    {
        credentialStore.ArgonSalt = [1, 2, 3];
        var session = new SessionContext
{
            Database = database,
            CredentialStore = credentialStore,
        }.WithClonedPinDerivedKey(_pinKey);
        _sessions.Add(session);
        return session;
    }

    private RunAsCreatedAccountPersistenceCoordinator CreateCoordinator(
        SessionContext session,
        Mock<IByteArrayCredentialEncryptionService>? encryptionService = null,
        Mock<IDatabaseService>? databaseService = null,
        Mock<IAccountLifecycleManager>? lifecycleManager = null,
        Mock<IAccountCredentialManager>? credentialManager = null,
        Mock<IAssociationAutoSetService>? associationService = null,
        Mock<ILocalUserProvider>? localUserProvider = null,
        Mock<ISidNameCacheService>? sidNameCache = null,
        Mock<IDataChangeNotifier>? dataChangeNotifier = null,
        Mock<ILoggingService>? log = null)
    {
        if (encryptionService == null)
        {
            encryptionService = new Mock<IByteArrayCredentialEncryptionService>();
            encryptionService.Setup(s => s.Encrypt(It.IsAny<ProtectedString>(), It.IsAny<byte[]>()))
                .Returns([1, 2, 3]);
        }

        var credentialCreator = new RunAsCredentialCreator(
            session,
            new ByteArrayCredentialEncryptionSpanAdapter(encryptionService.Object),
            (databaseService ?? new Mock<IDatabaseService>()).Object,
            (localUserProvider ?? new Mock<ILocalUserProvider>()).Object,
            (sidNameCache ?? new Mock<ISidNameCacheService>()).Object);

        var rollbackExecutor = new CreatedAccountRollbackExecutor(
            (lifecycleManager ?? new Mock<IAccountLifecycleManager>()).Object,
            (credentialManager ?? new Mock<IAccountCredentialManager>()).Object,
            (associationService ?? new Mock<IAssociationAutoSetService>()).Object,
            (localUserProvider ?? new Mock<ILocalUserProvider>()).Object,
            (log ?? new Mock<ILoggingService>()).Object);

        return new RunAsCreatedAccountPersistenceCoordinator(
            credentialCreator,
            rollbackExecutor,
            (dataChangeNotifier ?? new Mock<IDataChangeNotifier>()).Object,
            (log ?? new Mock<ILoggingService>()).Object);
    }
}
