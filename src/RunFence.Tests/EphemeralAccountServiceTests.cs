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
using RunFence.Security;
using Xunit;

#endregion

namespace RunFence.Tests;

public class EphemeralAccountServiceTests
{
    private record ServiceScope(
        EphemeralAccountService Service,
        Mock<ICredentialRepository> CredRepo,
        Mock<ILocalUserProvider> LocalUserProvider,
        Mock<IAccountDeletionService> AccountDeletion,
        Mock<IPathGrantService> PathGrantService,
        ProtectedBuffer PinKey) : IDisposable
    {
        public void Dispose()
        {
            PinKey.Dispose();
            Service.Dispose();
        }
    }

    private static ServiceScope BuildService(AppDatabase database, CredentialStore store,
        IAccountValidationService? accountValidation = null,
        Mock<IAccountDeletionService>? accountDeletion = null)
    {
        var credRepo = new Mock<ICredentialRepository>();
        var configRepo = new Mock<IConfigRepository>();
        var windowsService = new Mock<IWindowsAccountService>();
        var localUserProvider = new Mock<ILocalUserProvider>();
        var orphanedProfiles = new Mock<IOrphanedProfileService>();
        var aclCleanup = new Mock<IOrphanedAclCleanupService>();
        var log = new Mock<ILoggingService>();

        var sidNameCache = new Mock<ISidNameCacheService>();
        var sidResolver = new Mock<ISidResolver>();
        var persistenceHelper = new SessionPersistenceHelper(
            credRepo.Object, configRepo.Object, sidNameCache.Object, log.Object);
        var loginRestriction = new Mock<IAccountLoginRestrictionService>();
        var lsaRestriction = new Mock<IAccountLsaRestrictionService>();
        var accountValidationForLifecycle = new Mock<IAccountValidationService>();
        var lifecycleManager = new AccountLifecycleManager(
            windowsService.Object, loginRestriction.Object, lsaRestriction.Object, orphanedProfiles.Object,
            aclCleanup.Object, log.Object, accountValidationForLifecycle.Object, sidResolver.Object);

        bool customDeletion = accountDeletion != null;
        accountDeletion ??= new Mock<IAccountDeletionService>();
        if (!customDeletion)
        {
            // Default: DeleteAccount succeeds (no exception) and removes the AccountEntry + credential
            accountDeletion.Setup(d => d.DeleteAccount(
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
                });
        }

        var pinKey = new ProtectedBuffer(new byte[32], protect: false);
        var session = new SessionContext { Database = database, CredentialStore = store, PinDerivedKey = pinKey };

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

        var pathGrantService = new Mock<IPathGrantService>();
        var service = new EphemeralAccountService(
            lifecycleManager, accountDeletion.Object, persistenceHelper, localUserProvider.Object, log.Object, resolvedValidation,
            new LambdaSessionProvider(() => session), new InlineUiThreadInvoker(a => a()), sidResolver.Object,
            pathGrantService.Object);
        service.Start();

        return new ServiceScope(service, credRepo, localUserProvider, accountDeletion, pathGrantService, pinKey);
    }

    [Fact]
    public void ProcessExpiredAccounts_ExpiredEntry_DeletesAndRaisesAccountsChangedAndSaves()
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
        scope.Service.ProcessExpiredAccounts();

        // Assert
        Assert.Null(database.GetAccount(sid));
        Assert.Empty(store.Credentials);
        Assert.True(accountsChangedRaised);
        scope.AccountDeletion.Verify(d => d.DeleteAccount(sid, username, store, true), Times.Once);
        scope.LocalUserProvider.Verify(s => s.InvalidateCache(), Times.Once);
        scope.CredRepo.Verify(r => r.SaveCredentialStoreAndConfig(
            store, database, It.IsAny<byte[]>()), Times.Once);
        // Normal deletion path: grants cleared by DeleteAccount/CleanupSidFromAppData, not by pathGrantService
        scope.PathGrantService.Verify(p => p.RemoveAll(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void ProcessExpiredAccounts_OrphanedEntry_RemovesWithoutDeletionAndRaisesEvent()
    {
        // Arrange: SID not resolvable, no credential → orphaned
        var database = new AppDatabase();
        var store = new CredentialStore();
        const string orphanSid = "S-1-5-21-0-0-0-9902";
        database.GetOrCreateAccount(orphanSid).DeleteAfterUtc = DateTime.UtcNow.AddHours(24);

        using var scope = BuildService(database, store);
        bool accountsChangedRaised = false;
        scope.Service.AccountsChanged += () => accountsChangedRaised = true;

        // Act
        scope.Service.ProcessExpiredAccounts();

        // Assert: entry removed (AccountEntry becomes empty → RemoveAccountIfEmpty removes it), no OS-level deletion, event raised, save called
        Assert.Null(database.GetAccount(orphanSid));
        Assert.True(accountsChangedRaised);
        scope.AccountDeletion.Verify(d => d.DeleteAccount(
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CredentialStore>(), It.IsAny<bool>()), Times.Never);
        scope.PathGrantService.Verify(p => p.RemoveAll(orphanSid, false), Times.Once);
        scope.CredRepo.Verify(r => r.SaveCredentialStoreAndConfig(
            It.IsAny<CredentialStore>(), It.IsAny<AppDatabase>(), It.IsAny<byte[]>()), Times.Once);
    }

    [Fact]
    public void ProcessExpiredAccounts_ExpiredEntryUsernameUnresolvable_PermanentizesAndClearsGrants()
    {
        // Arrange: entry has a credential (not orphaned) but SID cannot be resolved to a username
        var database = new AppDatabase();
        var store = new CredentialStore();
        const string sid = "S-1-5-21-0-0-0-9905";
        store.Credentials.Add(new CredentialEntry { Sid = sid, EncryptedPassword = [1] });
        var entry = database.GetOrCreateAccount(sid);
        entry.DeleteAfterUtc = DateTime.UtcNow.AddHours(-1);
        // SidNames and sidResolver both return null → username unresolvable

        using var scope = BuildService(database, store);
        bool accountsChangedRaised = false;
        scope.Service.AccountsChanged += () => accountsChangedRaised = true;

        // Act
        scope.Service.ProcessExpiredAccounts();

        // Assert: entry permanentized (DeleteAfterUtc cleared → entry becomes empty → removed), grants reverted, no OS-level deletion
        Assert.Null(database.GetAccount(sid));
        Assert.True(accountsChangedRaised);
        scope.PathGrantService.Verify(p => p.RemoveAll(sid, false), Times.Once);
        scope.AccountDeletion.Verify(d => d.DeleteAccount(
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CredentialStore>(), It.IsAny<bool>()), Times.Never);
        scope.CredRepo.Verify(r => r.SaveCredentialStoreAndConfig(
            store, database, It.IsAny<byte[]>()), Times.Once);
    }

    [Fact]
    public void ProcessExpiredAccounts_NothingToProcess_NoEventNoSave()
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
        scope.Service.ProcessExpiredAccounts();

        // Assert: no changes, no event, no save
        Assert.False(accountsChangedRaised);
        Assert.Equal(1, database.Accounts.Count(a => a.DeleteAfterUtc.HasValue));
        scope.CredRepo.Verify(r => r.SaveCredentialStoreAndConfig(
            It.IsAny<CredentialStore>(), It.IsAny<AppDatabase>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public void ProcessExpiredAccounts_ExpiredEntryWithRunningProcesses_PostponedBy24h()
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
        scope.Service.ProcessExpiredAccounts();

        // Assert: entry preserved and expiry extended by 24h; no deletion performed
        var entry = database.GetAccount(sid)!;
        Assert.True(entry.DeleteAfterUtc > originalExpiry.AddHours(23));
        scope.AccountDeletion.Verify(d => d.DeleteAccount(
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CredentialStore>(), It.IsAny<bool>()), Times.Never);
        // AccountsChanged is still raised because DeleteAfterUtc was updated (changed=true)
        Assert.True(accountsChangedRaised);
        scope.CredRepo.Verify(r => r.SaveCredentialStoreAndConfig(
            store, database, It.IsAny<byte[]>()), Times.Once);
    }

    [Fact]
    public void ProcessExpiredAccounts_DeleteAccountThrows_EntryKeptAndContinues()
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
        accountDeletion.Setup(d => d.DeleteAccount(
                sid, username, It.IsAny<CredentialStore>(), It.IsAny<bool>()))
            .Throws(new InvalidOperationException("Access denied"));

        using var scope = BuildService(database, store, accountDeletion: accountDeletion);

        // Act
        scope.Service.ProcessExpiredAccounts();

        // Assert: deletion failed → entry NOT removed, no change committed (no save/event)
        Assert.NotNull(database.GetAccount(sid));
        Assert.True(database.GetAccount(sid)!.DeleteAfterUtc.HasValue);
        Assert.Single(store.Credentials);
        scope.CredRepo.Verify(r => r.SaveCredentialStoreAndConfig(
            It.IsAny<CredentialStore>(), It.IsAny<AppDatabase>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public void ProcessExpiredAccounts_MultipleEntries_OneFailsOneSucceeds_PartialSuccess()
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
        accountDeletion.Setup(d => d.DeleteAccount(goodSid, goodUsername, It.IsAny<CredentialStore>(), It.IsAny<bool>()))
            .Callback<string, string, CredentialStore, bool>((s, _, cs, _) =>
            {
                var entry = database.GetAccount(s);
                if (entry != null)
                    database.Accounts.Remove(entry);
                cs.Credentials.RemoveAll(c => string.Equals(c.Sid, s, StringComparison.OrdinalIgnoreCase));
            });

        // failSid fails: throws, entry must be kept
        accountDeletion.Setup(d => d.DeleteAccount(failSid, failUsername, It.IsAny<CredentialStore>(), It.IsAny<bool>()))
            .Throws(new InvalidOperationException("Access denied"));

        using var scope = BuildService(database, store, accountDeletion: accountDeletion);
        bool accountsChangedRaised = false;
        scope.Service.AccountsChanged += () => accountsChangedRaised = true;

        // Act
        scope.Service.ProcessExpiredAccounts();

        // Assert: successful entry removed, failed entry kept
        Assert.Null(database.GetAccount(goodSid));
        Assert.NotNull(database.GetAccount(failSid));
        Assert.True(database.GetAccount(failSid)!.DeleteAfterUtc.HasValue);

        // The one successful deletion caused a save and event
        Assert.True(accountsChangedRaised);
        scope.CredRepo.Verify(r => r.SaveCredentialStoreAndConfig(
            store, database, It.IsAny<byte[]>()), Times.Once);
    }
}