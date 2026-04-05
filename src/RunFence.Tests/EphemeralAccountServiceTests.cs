#region

using Moq;
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

        var encryptionService = new CredentialEncryptionService();
        var sidNameCache = new Mock<ISidNameCacheService>();
        var credentialManager = new AccountCredentialManager(
            encryptionService, credRepo.Object, configRepo.Object, log.Object, sidNameCache.Object);
        var accountRestriction = new Mock<IAccountRestrictionService>();
        var accountValidationForLifecycle = new Mock<IAccountValidationService>();
        var sidResolver = new Mock<ISidResolver>();
        var lifecycleManager = new AccountLifecycleManager(
            windowsService.Object, accountRestriction.Object, orphanedProfiles.Object,
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

        var service = new EphemeralAccountService(
            lifecycleManager, accountDeletion.Object, credentialManager, localUserProvider.Object, log.Object, resolvedValidation,
            new LambdaSessionProvider(() => session), new InlineUiThreadInvoker(a => a()), sidResolver.Object);
        service.Start();

        return new ServiceScope(service, credRepo, localUserProvider, accountDeletion, pinKey);
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
        scope.CredRepo.Verify(r => r.SaveCredentialStoreAndConfig(
            It.IsAny<CredentialStore>(), It.IsAny<AppDatabase>(), It.IsAny<byte[]>()), Times.Once);
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
}