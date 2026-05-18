using Moq;
using RunFence.Account;
using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.RunAs;
using Xunit;

namespace RunFence.Tests;

public class AccountCreationCommitServiceTests : IDisposable
{
    private readonly SecureSecret _pinKey = TestSecretFactory.Create(32);

    public void Dispose() => _pinKey.Dispose();

    [Fact]
    public void Commit_Succeeds_AutoSetsAssociationsWithoutRollbackSnapshot()
    {
        const string sid = "S-1-5-21-1-2-3-1001";
        var database = new AppDatabase();
        var session = new SessionContext
{
            Database = database,
            CredentialStore = new CredentialStore(),
        }.WithOwnedPinDerivedKey(_pinKey);

        var credentialId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var credentialManager = new Mock<IAccountCredentialManager>();
        credentialManager.Setup(s => s.StoreCreatedUserCredential(
                sid,
                It.IsAny<ProtectedString>(),
                session.CredentialStore,
                session.PinDerivedKey))
            .Returns(credentialId);

        var sidNameCache = new Mock<ISidNameCacheService>();
        sidNameCache.Setup(s => s.ResolveAndCache(sid, "newuser"))
            .Callback(() => database.SidNames[sid] = "newuser");

        var sessionProvider = new Mock<ISessionProvider>();
        sessionProvider.Setup(s => s.GetSession()).Returns(session);

        var localUserProvider = new Mock<ILocalUserProvider>();
        var associationService = new Mock<IAssociationAutoSetService>();
        associationService.Setup(s => s.AutoSetForUser(sid))
            .Returns(AssociationAutoSetResult.Success);

        var persistenceHelper = new SessionPersistenceHelper(
            Mock.Of<ICredentialRepository>(),
            Mock.Of<IConfigRepository>(),
            sidNameCache.Object,
            () => new InlineUiThreadInvoker(action => action()),
            Mock.Of<ILoggingService>());

        var service = new AccountCreationCommitService(
            credentialManager.Object,
            sidNameCache.Object,
            sessionProvider.Object,
            localUserProvider.Object,
            associationService.Object,
            persistenceHelper);

        using var password = ProtectedString.FromChars("P@ssw0rd".AsSpan());
        var outcome = service.Commit(
            new AccountCreationData(
                sid,
                password,
                "newuser",
                IsEphemeral: true,
                PrivilegeLevel: PrivilegeLevel.HighestAllowed,
                FirewallSettingsChanged: true,
                AllowInternet: false,
                AllowLocalhost: true,
                AllowLan: true,
                UsersGroupUnchecked: true,
                AdminGroupChecked: false,
                CreationRollbackState: new CreatedAccountRollbackState
                {
                    Sid = sid,
                    Username = "newuser",
                    HadPreviousAccount = false,
                    HadPreviousSidName = false,
                    HadPreviousFirewallSettings = false
                }),
            database);

        Assert.Equal(AccountCreationCommitStatus.Succeeded, outcome.Status);
        Assert.NotNull(outcome.Result);
        Assert.Null(outcome.RollbackState);
        Assert.Null(outcome.ErrorMessage);
        Assert.True(database.Settings.HasShownFirstAccountWarning);
        Assert.True(database.Settings.HasShownUsersGroupWarning);
        associationService.Verify(s => s.AutoSetForUser(sid), Times.Once);
        localUserProvider.Verify(s => s.InvalidateCache(), Times.Once);
    }

    [Fact]
    public void Commit_SaveFailsAfterMutation_ReturnsRollbackOutcome()
    {
        const string sid = "S-1-5-21-1-2-3-1001";
        var database = new AppDatabase
        {
            Accounts =
            [
                new AccountEntry
                {
                    Sid = sid,
                    DeleteAfterUtc = DateTime.UtcNow.AddHours(1)
                }
            ],
            SidNames =
            {
                [sid] = "newuser"
            }
        };
        var session = new SessionContext
{
            Database = database,
            CredentialStore = new CredentialStore(),
        }.WithOwnedPinDerivedKey(_pinKey);

        var credentialManager = new Mock<IAccountCredentialManager>();
        credentialManager.Setup(s => s.StoreCreatedUserCredential(
                sid,
                It.IsAny<ProtectedString>(),
                session.CredentialStore,
                session.PinDerivedKey))
            .Returns(Guid.Parse("11111111-1111-1111-1111-111111111111"));

        var sidNameCache = new Mock<ISidNameCacheService>();
        sidNameCache.Setup(s => s.ResolveAndCache(sid, "newuser"))
            .Callback(() => database.SidNames[sid] = "newuser");

        var sessionProvider = new Mock<ISessionProvider>();
        sessionProvider.Setup(s => s.GetSession()).Returns(session);

        var localUserProvider = new Mock<ILocalUserProvider>();
        var associationService = new Mock<IAssociationAutoSetService>();
        associationService.Setup(s => s.AutoSetForUser(sid))
            .Returns(AssociationAutoSetResult.Success);

        var credentialRepository = new Mock<ICredentialRepository>();
        credentialRepository.Setup(s => s.SaveCredentialStoreAndConfig(session.CredentialStore, database, It.IsAny<ISecureSecretSnapshotSource>()))
            .Throws(new InvalidOperationException("save failed"));
        var configRepository = new Mock<IConfigRepository>();
        var log = new Mock<ILoggingService>();
        var persistenceHelper = new SessionPersistenceHelper(
            credentialRepository.Object,
            configRepository.Object,
            sidNameCache.Object,
            () => new InlineUiThreadInvoker(action => action()),
            log.Object);

        var service = new AccountCreationCommitService(
            credentialManager.Object,
            sidNameCache.Object,
            sessionProvider.Object,
            localUserProvider.Object,
            associationService.Object,
            persistenceHelper);

        using var password = ProtectedString.FromChars("P@ssw0rd".AsSpan());
        var outcome = service.Commit(
            new AccountCreationData(
                sid,
                password,
                "newuser",
                IsEphemeral: true,
                PrivilegeLevel: PrivilegeLevel.HighestAllowed,
                FirewallSettingsChanged: true,
                AllowInternet: false,
                AllowLocalhost: true,
                AllowLan: true,
                UsersGroupUnchecked: true,
                AdminGroupChecked: false,
                CreationRollbackState: new CreatedAccountRollbackState
                {
                    Sid = sid,
                    Username = "newuser",
                    HadPreviousAccount = false,
                    HadPreviousSidName = false,
                    HadPreviousFirewallSettings = false
                }),
            database);

        Assert.Equal(AccountCreationCommitStatus.SaveFailedAfterMutation, outcome.Status);
        Assert.NotNull(outcome.Result);
        Assert.NotNull(outcome.RollbackState);
        Assert.Equal("save failed", outcome.ErrorMessage);
        Assert.Equal(sid, outcome.RollbackState!.CreatedAccount.Sid);
        Assert.Equal("newuser", outcome.RollbackState.CreatedAccount.Username);
        Assert.False(outcome.RollbackState.CreatedAccount.HadPreviousAccount);
        Assert.False(outcome.RollbackState.CreatedAccount.HadPreviousSidName);
        Assert.False(outcome.RollbackState.PreviousSettings.HasShownFirstAccountWarning);
        Assert.True(database.Settings.HasShownFirstAccountWarning);
        Assert.True(database.Settings.HasShownUsersGroupWarning);
        localUserProvider.Verify(s => s.InvalidateCache(), Times.Once);
        associationService.Verify(s => s.AutoSetForUser(sid), Times.Once);
    }

    [Fact]
    public void Commit_SaveFailsAfterMutation_PreservesPreviousFirewallSnapshot()
    {
        const string sid = "S-1-5-21-1-2-3-1001";
        var previousFirewall = new FirewallAccountSettings
        {
            AllowInternet = false,
            AllowLocalhost = false,
            AllowLan = false,
            LocalhostPortExemptions = ["4000", "5000-6000"],
            FilterEphemeralLoopback = false
        };
        var database = new AppDatabase
        {
            Accounts =
            [
                new AccountEntry
                {
                    Sid = sid,
                    PrivilegeLevel = PrivilegeLevel.Isolated,
                    Firewall = previousFirewall,
                    DeleteAfterUtc = DateTime.UtcNow.AddHours(1)
                }
            ],
            SidNames =
            {
                [sid] = "newuser"
            }
        };
        var session = new SessionContext
{
            Database = database,
            CredentialStore = new CredentialStore(),
        }.WithOwnedPinDerivedKey(_pinKey);

        var credentialManager = new Mock<IAccountCredentialManager>();
        credentialManager.Setup(s => s.StoreCreatedUserCredential(
                sid,
                It.IsAny<ProtectedString>(),
                session.CredentialStore,
                session.PinDerivedKey))
            .Returns(Guid.Parse("11111111-1111-1111-1111-111111111111"));

        var sidNameCache = new Mock<ISidNameCacheService>();
        sidNameCache.Setup(s => s.ResolveAndCache(sid, "newuser"))
            .Callback(() => database.SidNames[sid] = "newuser");

        var sessionProvider = new Mock<ISessionProvider>();
        sessionProvider.Setup(s => s.GetSession()).Returns(session);

        var localUserProvider = new Mock<ILocalUserProvider>();
        var associationService = new Mock<IAssociationAutoSetService>();
        associationService.Setup(s => s.AutoSetForUser(sid))
            .Returns(AssociationAutoSetResult.Success);

        var credentialRepository = new Mock<ICredentialRepository>();
        credentialRepository.Setup(s => s.SaveCredentialStoreAndConfig(session.CredentialStore, database, It.IsAny<ISecureSecretSnapshotSource>()))
            .Throws(new InvalidOperationException("save failed"));
        var configRepository = new Mock<IConfigRepository>();
        var log = new Mock<ILoggingService>();
        var persistenceHelper = new SessionPersistenceHelper(
            credentialRepository.Object,
            configRepository.Object,
            sidNameCache.Object,
            () => new InlineUiThreadInvoker(action => action()),
            log.Object);

        var service = new AccountCreationCommitService(
            credentialManager.Object,
            sidNameCache.Object,
            sessionProvider.Object,
            localUserProvider.Object,
            associationService.Object,
            persistenceHelper);

        var originalAccount = database.GetAccount(sid)!;
        using var password = ProtectedString.FromChars("P@ssw0rd".AsSpan());
        var outcome = service.Commit(
            new AccountCreationData(
                sid,
                password,
                "newuser",
                IsEphemeral: true,
                PrivilegeLevel: PrivilegeLevel.HighestAllowed,
                FirewallSettingsChanged: true,
                AllowInternet: false,
                AllowLocalhost: true,
                AllowLan: true,
                UsersGroupUnchecked: true,
                AdminGroupChecked: false,
                CreationRollbackState: new CreatedAccountRollbackState
                {
                    Sid = sid,
                    Username = "newuser",
                    PreviousAccount = originalAccount.Clone(),
                    HadPreviousAccount = true,
                    PreviousSidName = "newuser",
                    PreviousFirewallSettings = previousFirewall.Clone(),
                    HadPreviousFirewallSettings = true
                }),
            database);

        Assert.Equal(AccountCreationCommitStatus.SaveFailedAfterMutation, outcome.Status);
        Assert.NotNull(outcome.Result);
        Assert.NotNull(outcome.RollbackState);
        Assert.Equal("save failed", outcome.ErrorMessage);
        var rollbackAccount = outcome.RollbackState!.CreatedAccount;
        Assert.True(rollbackAccount.HadPreviousFirewallSettings);
        Assert.NotNull(rollbackAccount.PreviousFirewallSettings);
        Assert.NotSame(previousFirewall, rollbackAccount.PreviousFirewallSettings);
        Assert.Equal(previousFirewall.AllowInternet, rollbackAccount.PreviousFirewallSettings!.AllowInternet);
        Assert.Equal(previousFirewall.AllowLocalhost, rollbackAccount.PreviousFirewallSettings.AllowLocalhost);
        Assert.Equal(previousFirewall.AllowLan, rollbackAccount.PreviousFirewallSettings.AllowLan);
        Assert.Equal(previousFirewall.LocalhostPortExemptions, rollbackAccount.PreviousFirewallSettings.LocalhostPortExemptions);
        Assert.Equal(previousFirewall.FilterEphemeralLoopback, rollbackAccount.PreviousFirewallSettings.FilterEphemeralLoopback);
        localUserProvider.Verify(s => s.InvalidateCache(), Times.Once);
        associationService.Verify(s => s.AutoSetForUser(sid), Times.Once);
    }

    [Fact]
    public void Commit_PostMutationAssociationFailure_ReturnsRollbackOutcome()
    {
        const string sid = "S-1-5-21-1-2-3-1001";
        var database = new AppDatabase
        {
            Accounts =
            [
                new AccountEntry
                {
                    Sid = sid,
                    DeleteAfterUtc = DateTime.UtcNow.AddHours(1)
                }
            ],
            SidNames =
            {
                [sid] = "newuser"
            }
        };
        var session = new SessionContext
{
            Database = database,
            CredentialStore = new CredentialStore(),
        }.WithOwnedPinDerivedKey(_pinKey);

        var credentialId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var credentialManager = new Mock<IAccountCredentialManager>();
        credentialManager.Setup(s => s.StoreCreatedUserCredential(
                sid,
                It.IsAny<ProtectedString>(),
                session.CredentialStore,
                session.PinDerivedKey))
            .Returns(credentialId);

        var sidNameCache = new Mock<ISidNameCacheService>();
        sidNameCache.Setup(s => s.ResolveAndCache(sid, "newuser"))
            .Callback(() => database.SidNames[sid] = "newuser");

        var sessionProvider = new Mock<ISessionProvider>();
        sessionProvider.Setup(s => s.GetSession()).Returns(session);

        var localUserProvider = new Mock<ILocalUserProvider>();
        var associationService = new Mock<IAssociationAutoSetService>();
        associationService.Setup(s => s.AutoSetForUser(sid))
            .Throws(new InvalidOperationException("association failed"));

        var persistenceHelper = new SessionPersistenceHelper(
            Mock.Of<ICredentialRepository>(),
            Mock.Of<IConfigRepository>(),
            sidNameCache.Object,
            () => new InlineUiThreadInvoker(action => action()),
            Mock.Of<ILoggingService>());

        var service = new AccountCreationCommitService(
            credentialManager.Object,
            sidNameCache.Object,
            sessionProvider.Object,
            localUserProvider.Object,
            associationService.Object,
            persistenceHelper);

        using var password = ProtectedString.FromChars("P@ssw0rd".AsSpan());
        var outcome = service.Commit(
            new AccountCreationData(
                sid,
                password,
                "newuser",
                IsEphemeral: true,
                PrivilegeLevel: PrivilegeLevel.HighestAllowed,
                FirewallSettingsChanged: true,
                AllowInternet: false,
                AllowLocalhost: true,
                AllowLan: true,
                UsersGroupUnchecked: true,
                AdminGroupChecked: false,
                CreationRollbackState: new CreatedAccountRollbackState
                {
                    Sid = sid,
                    Username = "newuser",
                    HadPreviousAccount = false,
                    HadPreviousSidName = false,
                    HadPreviousFirewallSettings = false
                }),
            database);

        Assert.Equal(AccountCreationCommitStatus.SaveFailedAfterMutation, outcome.Status);
        Assert.Null(outcome.Result);
        Assert.NotNull(outcome.RollbackState);
        Assert.Equal("association failed", outcome.ErrorMessage);
        Assert.Equal(credentialId, outcome.RollbackState!.CreatedAccount.CredentialId);
        Assert.False(outcome.RollbackState.PreviousSettings.HasShownFirstAccountWarning);
        Assert.True(database.Settings.HasShownFirstAccountWarning);
        localUserProvider.Verify(s => s.InvalidateCache(), Times.Once);
    }
}
