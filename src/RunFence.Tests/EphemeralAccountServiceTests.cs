#region

using Moq;
using RunFence.Acl;
using RunFence.Account;
using RunFence.Account.Lifecycle;
using RunFence.Account.OrphanedProfiles;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using Xunit;

#endregion

namespace RunFence.Tests;

public class EphemeralAccountServiceTests
{
    private record ServiceScope(
        EphemeralAccountService Service,
        Mock<IConfigReencryptionPersistence> CredRepo,
        Mock<ILocalUserProvider> LocalUserProvider,
        Mock<ILoggingService> Log,
        Mock<ITrayBalloonService> TrayBalloon,
        Mock<IAccountDeletionService> AccountDeletion,
        Mock<IGrantAccountCleanupService> PathGrantService,
        Mock<ITrackingJobStateStore> TrackingJobStateStore,
        SecureSecret PinKey) : IDisposable
    {
        public void Dispose()
        {
            PinKey.Dispose();
            Service.Dispose();
        }
    }

    private static ServiceScope BuildService(AppDatabase database, CredentialStore store,
        IAccountValidationService? accountValidation = null,
        Mock<IAccountDeletionService>? accountDeletion = null,
        Mock<IGrantAccountCleanupService>? pathGrantService = null)
    {
        var credRepo = new Mock<IConfigReencryptionPersistence>();
        var configRepo = new Mock<IMainConfigPersistence>();
        var windowsService = new Mock<IWindowsAccountService>();
        var localUserProvider = new Mock<ILocalUserProvider>();
        var orphanedProfiles = new Mock<IOrphanedProfileService>();
        var aclCleanup = new Mock<IOrphanedAclCleanupService>();
        var log = new Mock<ILoggingService>();
        var trayBalloon = new Mock<ITrayBalloonService>();

        var sidNameCache = new Mock<ISidNameCacheService>();
        var sidResolver = new Mock<ISidResolver>();
        var persistenceHelper = new SessionPersistenceHelper(
            credRepo.Object,
            configRepo.Object,
            sidNameCache.Object,
            () => new InlineUiThreadInvoker(a => a()),
            log.Object);
        var loginRestriction = new Mock<IAccountLoginRestrictionService>();
        var lsaRestriction = new Mock<IAccountLsaRestrictionService>();
        var accountValidationForLifecycle = new Mock<IAccountValidationService>();

        bool customDeletion = accountDeletion != null;
        accountDeletion ??= new Mock<IAccountDeletionService>();
        if (!customDeletion)
        {
            // Default: DeleteAccount succeeds (no exception) and removes the AccountEntry + credential
            accountDeletion.Setup(d => d.DeleteAccountAsync(
                    It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<CredentialStore>(),
                    It.IsAny<bool>()))
                .Callback<string, string, CredentialStore, bool>((sid, _, cs, _) =>
                {
                    // Simulate CleanupSidFromAppData removing the AccountEntry
                    var entry = database.GetAccount(sid);
                    if (entry != null)
                        database.Accounts.Remove(entry);
                    cs.Credentials.RemoveAll(c =>
                        string.Equals(c.Sid, sid, StringComparison.OrdinalIgnoreCase));
                })
                .ReturnsAsync(AccountDeletionCleanupResult.Success());
        }

        var pinKey = TestSecretFactory.Create(32);
        var session = new SessionContext
        {
            Database = database,
            CredentialStore = store
        }.WithClonedPinDerivedKey(pinKey);

        IAccountValidationService resolvedValidation;
        if (accountValidation != null)
        {
            resolvedValidation = accountValidation;
        }
        else
        {
            var defaultValidation = new Mock<IAccountValidationService>();
            defaultValidation.Setup(v => v.GetProcessesRunningAsSid(It.IsAny<string>())).Returns([]);
            resolvedValidation = defaultValidation.Object;
        }

        pathGrantService ??= new Mock<IGrantAccountCleanupService>();
        var trackingJobStateStore = CreateTrackingJobStateStore(database);
        var service = new EphemeralAccountService(
            accountDeletion.Object, persistenceHelper, localUserProvider.Object, log.Object, resolvedValidation,
            new LambdaSessionProvider(() => session), new InlineUiThreadInvoker(a => a()), trayBalloon.Object,
            sidResolver.Object,
            pathGrantService.Object,
            trackingJobStateStore.Object);
        service.Start();

        return new ServiceScope(service, credRepo, localUserProvider, log, trayBalloon, accountDeletion, pathGrantService,
            trackingJobStateStore, pinKey);
    }

    private static Mock<ITrackingJobStateStore> CreateTrackingJobStateStore(AppDatabase database)
    {
        var store = new Mock<ITrackingJobStateStore>();
        store.Setup(s => s.RemoveTrackingJobSid(It.IsAny<string>(), It.IsAny<bool>()))
            .Callback<string, bool>((sid, _) =>
            {
                if (database.TrackingJobSids == null)
                    return;

                database.TrackingJobSids.RemoveAll(existing =>
                    string.Equals(existing, sid, StringComparison.OrdinalIgnoreCase));
                if (database.TrackingJobSids.Count == 0)
                    database.TrackingJobSids = null;
            });
        return store;
    }

    [Fact]
    public async Task ProcessExpiredAccounts_ExpiredEntry_DeletesAndRaisesAccountsChangedAndSaves()
    {
        // Arrange
        var database = new AppDatabase();
        var store = new CredentialStore();
        const string sid = "S-1-5-21-0-0-0-9901";
        const string username = "testeph_inst01";
        database.SidNames[sid] = username;
        store.Credentials.Add(new CredentialEntry { Sid = sid, EncryptedPassword = [1] });
        database.GetOrCreateAccount(sid).DeleteAfterUtc = DateTime.UtcNow.AddHours(-1);

        using var scope = BuildService(database, store);
        bool accountsChangedRaised = false;
        scope.Service.AccountsChanged += () => accountsChangedRaised = true;

        // Act
        await scope.Service.ProcessExpiredAccountsAsync();

        // Assert
        Assert.Null(database.GetAccount(sid));
        Assert.Empty(store.Credentials);
        Assert.True(accountsChangedRaised);
        scope.AccountDeletion.Verify(d => d.DeleteAccountAsync(sid, username, store, true), Times.Once);
        scope.LocalUserProvider.Verify(s => s.InvalidateCache(), Times.Once);
        scope.CredRepo.Verify(r => r.SaveCredentialStoreAndConfig(
            store, database, It.IsAny<ISecureSecretSnapshotSource>()), Times.Once);
        // Normal deletion path: grants cleared by DeleteAccount/CleanupSidFromAppData, not by pathGrantService
        scope.PathGrantService.Verify(p => p.RemoveAll(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ProcessExpiredAccounts_ExpiredEntry_WithDeleteWarnings_LogsAndShowsWarnings()
    {
        // Arrange
        var database = new AppDatabase();
        var store = new CredentialStore();
        const string sid = "S-1-5-21-0-0-0-9907";
        const string username = "testeph_warning";
        database.SidNames[sid] = username;
        store.Credentials.Add(new CredentialEntry { Sid = sid, EncryptedPassword = [1] });
        database.GetOrCreateAccount(sid).DeleteAfterUtc = DateTime.UtcNow.AddHours(-1);
        var warning = "Delete account grant cleanup completed with warning.";

        var accountDeletion = new Mock<IAccountDeletionService>();
        accountDeletion.Setup(d => d.DeleteAccountAsync(sid, username, It.IsAny<CredentialStore>(), It.IsAny<bool>()))
            .Callback<string, string, CredentialStore, bool>((s, _, cs, _) =>
            {
                var entry = database.GetAccount(s);
                if (entry != null)
                    database.Accounts.Remove(entry);
                cs.Credentials.RemoveAll(c => string.Equals(c.Sid, s, StringComparison.OrdinalIgnoreCase));
            })
            .ReturnsAsync(new AccountDeletionCleanupResult([warning]));

        using var scope = BuildService(database, store, accountDeletion: accountDeletion);

        // Act
        await scope.Service.ProcessExpiredAccountsAsync();

        // Assert: warnings are logged and surfaced through tray
        scope.Log.Verify(l => l.Warn(It.Is<string>(w => w == warning)), Times.Once);
        scope.TrayBalloon.Verify(t => t.ShowWarning(warning), Times.Once);
        // Simulate cleanup still completed
        Assert.Null(database.GetAccount(sid));
    }

    [Fact]
    public async Task ProcessExpiredAccounts_OrphanedEntry_RemovesWithoutDeletionAndRaisesEvent()
    {
        // Arrange: SID not resolvable, no credential → orphaned
        var database = new AppDatabase();
        var store = new CredentialStore();
        const string orphanSid = "S-1-5-21-0-0-0-9902";
        database.GetOrCreateAccount(orphanSid).DeleteAfterUtc = DateTime.UtcNow.AddHours(24);
        database.TrackingJobSids = [orphanSid];

        using var scope = BuildService(database, store);
        bool accountsChangedRaised = false;
        scope.Service.AccountsChanged += () => accountsChangedRaised = true;

        // Act
        await scope.Service.ProcessExpiredAccountsAsync();

        // Assert: entry removed (AccountEntry becomes empty → RemoveAccountIfEmpty removes it), no OS-level deletion, event raised, save called
        Assert.Null(database.GetAccount(orphanSid));
        Assert.True(accountsChangedRaised);
        scope.AccountDeletion.Verify(d => d.DeleteAccountAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CredentialStore>(), It.IsAny<bool>()), Times.Never);
        scope.PathGrantService.Verify(p => p.UntrackAll(orphanSid), Times.Once);
        scope.TrackingJobStateStore.Verify(store => store.RemoveTrackingJobSid(orphanSid, false), Times.Once);
        Assert.Null(database.TrackingJobSids);
        scope.CredRepo.Verify(r => r.SaveCredentialStoreAndConfig(
            It.IsAny<CredentialStore>(), It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>()), Times.Once);
    }

    [Fact]
    public async Task ProcessExpiredAccounts_ExpiredEntryUsernameUnresolvable_PermanentizesAndClearsGrants()
    {
        // Arrange: entry has a credential (not orphaned) but SID cannot be resolved to a username
        var database = new AppDatabase();
        var store = new CredentialStore();
        const string sid = "S-1-5-21-0-0-0-9905";
        store.Credentials.Add(new CredentialEntry { Sid = sid, EncryptedPassword = [1] });
        var entry = database.GetOrCreateAccount(sid);
        entry.DeleteAfterUtc = DateTime.UtcNow.AddHours(-1);
        database.TrackingJobSids = [sid];
        // SidNames and sidResolver both return null → username unresolvable

        using var scope = BuildService(database, store);
        bool accountsChangedRaised = false;
        scope.Service.AccountsChanged += () => accountsChangedRaised = true;

        // Act
        await scope.Service.ProcessExpiredAccountsAsync();

        // Assert: entry permanentized (DeleteAfterUtc cleared → entry becomes empty → removed), grants reverted, no OS-level deletion
        Assert.Null(database.GetAccount(sid));
        Assert.True(accountsChangedRaised);
        scope.PathGrantService.Verify(p => p.UntrackAll(sid), Times.Once);
        scope.TrackingJobStateStore.Verify(store => store.RemoveTrackingJobSid(sid, false), Times.Once);
        Assert.Null(database.TrackingJobSids);
        scope.AccountDeletion.Verify(d => d.DeleteAccountAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CredentialStore>(), It.IsAny<bool>()), Times.Never);
        scope.CredRepo.Verify(r => r.SaveCredentialStoreAndConfig(
            store, database, It.IsAny<ISecureSecretSnapshotSource>()), Times.Once);
    }

    [Fact]
    public async Task ProcessExpiredAccounts_ExpiredEntryUsernameUnresolvable_UntrackAllWarning_LoggedAndShown()
    {
        // Arrange
        var database = new AppDatabase();
        var store = new CredentialStore();
        const string sid = "S-1-5-21-0-0-0-9905";
        store.Credentials.Add(new CredentialEntry { Sid = sid, EncryptedPassword = [1] });
        var warning = new GrantApplyWarning(
            GrantApplyFailureStep.UntrackAllSave,
            @"C:\Untracked",
            null,
            new InvalidOperationException("temporary warning"));
        var pathGrantService = new Mock<IGrantAccountCleanupService>();
        pathGrantService.Setup(p => p.UntrackAll(sid))
            .Returns(new GrantApplyResult(
                DatabaseModified: true,
                Warnings: [warning]));

        database.GetOrCreateAccount(sid).DeleteAfterUtc = DateTime.UtcNow.AddHours(-1);

        var accountDeletion = new Mock<IAccountDeletionService>();
        using var scope = BuildService(database, store, pathGrantService: pathGrantService, accountDeletion: accountDeletion);

        // Act
        await scope.Service.ProcessExpiredAccountsAsync();

        // Assert: warning is logged and surfaced as tray notification
        var formattedWarning = GrantApplyFailureFormatter.Format(warning);
        scope.Log.Verify(l => l.Warn(formattedWarning), Times.Once);
        scope.TrayBalloon.Verify(t => t.ShowWarning(formattedWarning), Times.Once);
        Assert.Null(database.GetAccount(sid));
    }

    [Fact]
    public async Task ProcessExpiredAccounts_NothingToProcess_NoEventNoSave()
    {
        // Arrange: entry with future expiry, has a credential → neither orphaned nor expired
        var database = new AppDatabase();
        var store = new CredentialStore();
        const string sid = "S-1-5-21-0-0-0-9903";
        store.Credentials.Add(new CredentialEntry { Sid = sid, EncryptedPassword = [1] });
        database.GetOrCreateAccount(sid).DeleteAfterUtc = DateTime.UtcNow.AddHours(24); // not expired

        using var scope = BuildService(database, store);
        bool accountsChangedRaised = false;
        scope.Service.AccountsChanged += () => accountsChangedRaised = true;

        // Act
        await scope.Service.ProcessExpiredAccountsAsync();

        // Assert: no changes, no event, no save
        Assert.False(accountsChangedRaised);
        Assert.Equal(1, database.Accounts.Count(a => a.DeleteAfterUtc.HasValue));
        scope.CredRepo.Verify(r => r.SaveCredentialStoreAndConfig(
            It.IsAny<CredentialStore>(), It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>()), Times.Never);
    }

    [Fact]
    public async Task ProcessExpiredAccounts_ExpiredEntryWithRunningProcesses_PostponedBy24h()
    {
        // Arrange
        var database = new AppDatabase();
        var store = new CredentialStore();
        const string sid = "S-1-5-21-0-0-0-9904";
        const string username = "testeph_running";
        database.SidNames[sid] = username;
        store.Credentials.Add(new CredentialEntry { Sid = sid, EncryptedPassword = [1] });
        var originalExpiry = DateTime.UtcNow.AddHours(-1);
        database.GetOrCreateAccount(sid).DeleteAfterUtc = originalExpiry;

        var mockValidation = new Mock<IAccountValidationService>();
        mockValidation.Setup(v => v.GetProcessesRunningAsSid(sid)).Returns(["testprocess.exe"]);

        using var scope = BuildService(database, store, mockValidation.Object);
        bool accountsChangedRaised = false;
        scope.Service.AccountsChanged += () => accountsChangedRaised = true;

        // Act
        await scope.Service.ProcessExpiredAccountsAsync();

        // Assert: entry preserved and expiry extended by 24h; no deletion performed
        var entry = database.GetAccount(sid)!;
        Assert.True(entry.DeleteAfterUtc > originalExpiry.AddHours(23));
        scope.AccountDeletion.Verify(d => d.DeleteAccountAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CredentialStore>(), It.IsAny<bool>()), Times.Never);
        // AccountsChanged is still raised because DeleteAfterUtc was updated (changed=true)
        Assert.True(accountsChangedRaised);
        scope.CredRepo.Verify(r => r.SaveCredentialStoreAndConfig(
            store, database, It.IsAny<ISecureSecretSnapshotSource>()), Times.Once);
    }

    [Fact]
    public async Task ProcessExpiredAccounts_DeleteAccountThrows_EntryKeptAndContinues()
    {
        // Arrange: expired entry whose DeleteAccount throws (DeleteUser failed internally)
        var database = new AppDatabase();
        var store = new CredentialStore();
        const string sid = "S-1-5-21-0-0-0-9906";
        const string username = "testeph_fail";
        database.SidNames[sid] = username;
        store.Credentials.Add(new CredentialEntry { Sid = sid, EncryptedPassword = [1] });
        database.GetOrCreateAccount(sid).DeleteAfterUtc = DateTime.UtcNow.AddHours(-1);

        var accountDeletion = new Mock<IAccountDeletionService>();
        accountDeletion.Setup(d => d.DeleteAccountAsync(
                sid, username, It.IsAny<CredentialStore>(), It.IsAny<bool>()))
            .ThrowsAsync(new InvalidOperationException("Access denied"));

        using var scope = BuildService(database, store, accountDeletion: accountDeletion);

        // Act
        await scope.Service.ProcessExpiredAccountsAsync();

        // Assert: deletion failed → entry NOT removed, no change committed (no save/event)
        Assert.NotNull(database.GetAccount(sid));
        Assert.True(database.GetAccount(sid)!.DeleteAfterUtc.HasValue);
        Assert.Single(store.Credentials);
        scope.CredRepo.Verify(r => r.SaveCredentialStoreAndConfig(
            It.IsAny<CredentialStore>(), It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>()), Times.Never);
    }

    [Fact]
    public async Task ProcessExpiredAccounts_MultipleEntries_OneFailsOneSucceeds_PartialSuccess()
    {
        // Arrange — R2_TL10: two expired entries; one deletion throws, the other succeeds.
        // Verify: the successful entry is removed, the failed entry is kept, and save is called once.
        var database = new AppDatabase();
        var store = new CredentialStore();

        const string goodSid = "S-1-5-21-0-0-0-9910";
        const string goodUsername = "testeph_good";
        database.SidNames[goodSid] = goodUsername;
        store.Credentials.Add(new CredentialEntry { Sid = goodSid, EncryptedPassword = [1] });
        database.GetOrCreateAccount(goodSid).DeleteAfterUtc = DateTime.UtcNow.AddHours(-1);

        const string failSid = "S-1-5-21-0-0-0-9911";
        const string failUsername = "testeph_fail2";
        database.SidNames[failSid] = failUsername;
        store.Credentials.Add(new CredentialEntry { Sid = failSid, EncryptedPassword = [2] });
        database.GetOrCreateAccount(failSid).DeleteAfterUtc = DateTime.UtcNow.AddHours(-1);

        var accountDeletion = new Mock<IAccountDeletionService>();

        // goodSid succeeds: simulate CleanupSidFromAppData removing the AccountEntry + credential
        accountDeletion.Setup(d => d.DeleteAccountAsync(goodSid, goodUsername, It.IsAny<CredentialStore>(), It.IsAny<bool>()))
            .Callback<string, string, CredentialStore, bool>((s, _, cs, _) =>
            {
                var entry = database.GetAccount(s);
                if (entry != null)
                    database.Accounts.Remove(entry);
                cs.Credentials.RemoveAll(c => string.Equals(c.Sid, s, StringComparison.OrdinalIgnoreCase));
            })
            .ReturnsAsync(AccountDeletionCleanupResult.Success());

        // failSid fails: throws, entry must be kept
        accountDeletion.Setup(d => d.DeleteAccountAsync(failSid, failUsername, It.IsAny<CredentialStore>(), It.IsAny<bool>()))
            .ThrowsAsync(new InvalidOperationException("Access denied"));

        using var scope = BuildService(database, store, accountDeletion: accountDeletion);
        bool accountsChangedRaised = false;
        scope.Service.AccountsChanged += () => accountsChangedRaised = true;

        // Act
        await scope.Service.ProcessExpiredAccountsAsync();

        // Assert: successful entry removed, failed entry kept
        Assert.Null(database.GetAccount(goodSid));
        Assert.NotNull(database.GetAccount(failSid));
        Assert.True(database.GetAccount(failSid)!.DeleteAfterUtc.HasValue);

        // The one successful deletion caused a save and event
        Assert.True(accountsChangedRaised);
        scope.CredRepo.Verify(r => r.SaveCredentialStoreAndConfig(
            store, database, It.IsAny<ISecureSecretSnapshotSource>()), Times.Once);
    }
}
