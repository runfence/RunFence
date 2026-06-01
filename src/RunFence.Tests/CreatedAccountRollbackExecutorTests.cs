using Moq;
using RunFence.Account;
using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public class CreatedAccountRollbackExecutorTests
{
    [Fact]
    public async Task RollbackAsync_DeleteSucceedsAndProfileDeleted_RestoresStateWithoutAssociationRestore()
    {
        const string sid = "S-1-5-21-1-2-3-1001";
        var credentialId = Guid.NewGuid();
        var database = new AppDatabase();
        database.SidNames[sid] = "newuser";
        database.Accounts.Add(new AccountEntry
        {
            Sid = sid,
            PrivilegeLevel = PrivilegeLevel.HighestAllowed,
            Firewall = new FirewallAccountSettings { AllowInternet = false }
        });

        var previousAccount = new AccountEntry
        {
            Sid = sid,
            PrivilegeLevel = PrivilegeLevel.Isolated,
            TrayDiscovery = true,
            Firewall = new FirewallAccountSettings()
        };

        var credentialStore = new CredentialStore();
        credentialStore.Credentials.Add(new CredentialEntry { Id = credentialId, Sid = sid });

        var sequence = new MockSequence();
        var lifecycleManager = new Mock<IAccountLifecycleManager>(MockBehavior.Strict);
        lifecycleManager.InSequence(sequence)
            .Setup(s => s.DeleteSamAccount(sid))
            .Returns(new AccountDeletionResult(true, sid, null));
        lifecycleManager.InSequence(sequence)
            .Setup(s => s.DeleteProfileAsync(sid))
            .ReturnsAsync((string?)null);
        lifecycleManager.InSequence(sequence)
            .Setup(s => s.ClearAccountRestrictions(sid, "newuser", database.Settings));

        var credentialManager = new Mock<IAccountCredentialManager>(MockBehavior.Strict);
        credentialManager.InSequence(sequence)
            .Setup(s => s.RemoveCredential(credentialId, credentialStore))
            .Callback(() => credentialStore.Credentials.RemoveAll(c => c.Id == credentialId));

        var associationService = new Mock<IAssociationAutoSetService>(MockBehavior.Strict);
        var localUserProvider = new Mock<ILocalUserProvider>(MockBehavior.Strict);
        localUserProvider.InSequence(sequence)
            .Setup(s => s.InvalidateCache());
        var log = new Mock<ILoggingService>(MockBehavior.Strict);

        var executor = new CreatedAccountRollbackExecutor(
            lifecycleManager.Object,
            credentialManager.Object,
            associationService.Object,
            localUserProvider.Object,
            log.Object);

        var rollbackState = new CreatedAccountRollbackState
        {
            Sid = sid,
            Username = "newuser",
            CredentialId = credentialId,
            PreviousAccount = previousAccount,
            HadPreviousAccount = true,
            PreviousSidName = "olduser",
            HadPreviousSidName = true,
            PreviousFirewallSettings = null,
            HadPreviousFirewallSettings = false
        };

        await executor.RollbackAsync(rollbackState, credentialStore, database);

        Assert.Empty(credentialStore.Credentials);
        Assert.Equal("olduser", database.SidNames[sid]);
        var restored = Assert.Single(database.Accounts);
        Assert.Equal(PrivilegeLevel.Isolated, restored.PrivilegeLevel);
        Assert.True(restored.TrayDiscovery);
        Assert.True(restored.Firewall.IsDefault);
        associationService.Verify(s => s.RestoreForUser(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RollbackAsync_RestoresPreviousNonDefaultFirewallSettings()
    {
        const string sid = "S-1-5-21-1-2-3-1001";
        var credentialId = Guid.NewGuid();
        var previousFirewall = new FirewallAccountSettings
        {
            AllowInternet = false,
            AllowLocalhost = false,
            AllowLan = false,
            LocalhostPortExemptions = ["5000", "6000-6001"],
            FilterEphemeralLoopback = false
        };

        var database = new AppDatabase();
        database.SidNames[sid] = "newuser";
        database.Accounts.Add(new AccountEntry
        {
            Sid = sid,
            PrivilegeLevel = PrivilegeLevel.HighestAllowed,
            Firewall = new FirewallAccountSettings { AllowInternet = true }
        });

        var previousAccount = new AccountEntry
        {
            Sid = sid,
            PrivilegeLevel = PrivilegeLevel.Isolated,
            Firewall = previousFirewall,
            TrayDiscovery = true
        };

        var credentialStore = new CredentialStore();
        credentialStore.Credentials.Add(new CredentialEntry { Id = credentialId, Sid = sid });

        var lifecycleManager = new Mock<IAccountLifecycleManager>(MockBehavior.Strict);
        lifecycleManager.Setup(s => s.DeleteSamAccount(sid))
            .Returns(new AccountDeletionResult(true, sid, null));
        lifecycleManager.Setup(s => s.DeleteProfileAsync(sid))
            .ReturnsAsync((string?)null);
        lifecycleManager.Setup(s => s.ClearAccountRestrictions(sid, "newuser", database.Settings));

        var credentialManager = new Mock<IAccountCredentialManager>(MockBehavior.Strict);
        credentialManager.Setup(s => s.RemoveCredential(credentialId, credentialStore))
            .Callback(() => credentialStore.Credentials.RemoveAll(c => c.Id == credentialId));

        var associationService = new Mock<IAssociationAutoSetService>(MockBehavior.Strict);
        var localUserProvider = new Mock<ILocalUserProvider>(MockBehavior.Strict);
        localUserProvider.Setup(s => s.InvalidateCache());
        var log = new Mock<ILoggingService>(MockBehavior.Strict);

        var executor = new CreatedAccountRollbackExecutor(
            lifecycleManager.Object,
            credentialManager.Object,
            associationService.Object,
            localUserProvider.Object,
            log.Object);

        var rollbackState = new CreatedAccountRollbackState
        {
            Sid = sid,
            Username = "newuser",
            CredentialId = credentialId,
            PreviousAccount = previousAccount,
            HadPreviousAccount = true,
            PreviousSidName = "olduser",
            HadPreviousSidName = true,
            PreviousFirewallSettings = previousFirewall.Clone(),
            HadPreviousFirewallSettings = true
        };

        await executor.RollbackAsync(rollbackState, credentialStore, database);

        Assert.Empty(credentialStore.Credentials);
        Assert.Equal("olduser", database.SidNames[sid]);
        var restored = Assert.Single(database.Accounts);
        Assert.Equal(PrivilegeLevel.Isolated, restored.PrivilegeLevel);
        Assert.True(restored.TrayDiscovery);
        Assert.False(restored.Firewall.IsDefault);
        Assert.Equal(previousFirewall.AllowInternet, restored.Firewall.AllowInternet);
        Assert.Equal(previousFirewall.AllowLocalhost, restored.Firewall.AllowLocalhost);
        Assert.Equal(previousFirewall.AllowLan, restored.Firewall.AllowLan);
        Assert.Equal(previousFirewall.LocalhostPortExemptions, restored.Firewall.LocalhostPortExemptions);
        Assert.Equal(previousFirewall.FilterEphemeralLoopback, restored.Firewall.FilterEphemeralLoopback);
        Assert.NotSame(previousFirewall, restored.Firewall);
        associationService.Verify(s => s.RestoreForUser(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RollbackAsync_ProfileDeleteFails_RestoresAssociationsAndLogsWarning()
    {
        const string sid = "S-1-5-21-1-2-3-1001";
        var credentialId = Guid.NewGuid();
        var database = new AppDatabase();
        database.SidNames[sid] = "newuser";
        database.Accounts.Add(new AccountEntry { Sid = sid, PrivilegeLevel = PrivilegeLevel.HighestAllowed });

        var credentialStore = new CredentialStore();
        credentialStore.Credentials.Add(new CredentialEntry { Id = credentialId, Sid = sid });

        var sequence = new MockSequence();
        var lifecycleManager = new Mock<IAccountLifecycleManager>(MockBehavior.Strict);
        lifecycleManager.InSequence(sequence)
            .Setup(s => s.DeleteSamAccount(sid))
            .Returns(new AccountDeletionResult(true, sid, null));
        lifecycleManager.InSequence(sequence)
            .Setup(s => s.DeleteProfileAsync(sid))
            .ReturnsAsync("profile cleanup failed");

        var associationService = new Mock<IAssociationAutoSetService>(MockBehavior.Strict);
        associationService.InSequence(sequence)
            .Setup(s => s.RestoreForUser(sid));

        lifecycleManager.InSequence(sequence)
            .Setup(s => s.ClearAccountRestrictions(sid, "newuser", database.Settings));

        var credentialManager = new Mock<IAccountCredentialManager>(MockBehavior.Strict);
        credentialManager.InSequence(sequence)
            .Setup(s => s.RemoveCredential(credentialId, credentialStore))
            .Callback(() => credentialStore.Credentials.RemoveAll(c => c.Id == credentialId));

        var localUserProvider = new Mock<ILocalUserProvider>(MockBehavior.Strict);
        localUserProvider.InSequence(sequence)
            .Setup(s => s.InvalidateCache());

        var log = new Mock<ILoggingService>();
        var executor = new CreatedAccountRollbackExecutor(
            lifecycleManager.Object,
            credentialManager.Object,
            associationService.Object,
            localUserProvider.Object,
            log.Object);

        var rollbackState = new CreatedAccountRollbackState
        {
            Sid = sid,
            Username = "newuser",
            CredentialId = credentialId,
            PreviousAccount = null,
            HadPreviousAccount = false,
            PreviousSidName = null,
            HadPreviousSidName = false,
            PreviousFirewallSettings = null,
            HadPreviousFirewallSettings = false
        };

        await executor.RollbackAsync(rollbackState, credentialStore, database);

        Assert.Empty(credentialStore.Credentials);
        Assert.Empty(database.Accounts);
        Assert.DoesNotContain(sid, database.SidNames.Keys);
    }

    [Fact]
    public async Task RollbackAsync_PostDeleteCleanupThrows_RestoresCredentialAndDatabaseState()
    {
        const string sid = "S-1-5-21-1-2-3-1001";
        var credentialId = Guid.NewGuid();
        var database = new AppDatabase();
        database.SidNames[sid] = "newuser";
        database.Accounts.Add(new AccountEntry
        {
            Sid = sid,
            PrivilegeLevel = PrivilegeLevel.HighestAllowed,
            Firewall = new FirewallAccountSettings { AllowInternet = false }
        });

        var previousAccount = new AccountEntry
        {
            Sid = sid,
            PrivilegeLevel = PrivilegeLevel.Isolated,
            TrayTerminal = true,
            Firewall = new FirewallAccountSettings()
        };

        var credentialStore = new CredentialStore();
        credentialStore.Credentials.Add(new CredentialEntry { Id = credentialId, Sid = sid });

        var lifecycleManager = new Mock<IAccountLifecycleManager>();
        lifecycleManager.Setup(s => s.DeleteSamAccount(sid))
            .Returns(new AccountDeletionResult(true, sid, null));
        lifecycleManager.Setup(s => s.DeleteProfileAsync(sid))
            .ReturnsAsync("profile cleanup failed");
        lifecycleManager.Setup(s => s.ClearAccountRestrictions(sid, "newuser", database.Settings))
            .Throws(new InvalidOperationException("restriction cleanup failed"));

        var credentialManager = new Mock<IAccountCredentialManager>();
        credentialManager.Setup(s => s.RemoveCredential(credentialId, credentialStore))
            .Callback(() => credentialStore.Credentials.RemoveAll(c => c.Id == credentialId));

        var associationService = new Mock<IAssociationAutoSetService>();
        associationService.Setup(s => s.RestoreForUser(sid))
            .Throws(new InvalidOperationException("association restore failed"));

        var localUserProvider = new Mock<ILocalUserProvider>();
        var log = new Mock<ILoggingService>();

        var executor = new CreatedAccountRollbackExecutor(
            lifecycleManager.Object,
            credentialManager.Object,
            associationService.Object,
            localUserProvider.Object,
            log.Object);

        var rollbackState = new CreatedAccountRollbackState
        {
            Sid = sid,
            Username = "newuser",
            CredentialId = credentialId,
            PreviousAccount = previousAccount,
            HadPreviousAccount = true,
            PreviousSidName = "olduser",
            HadPreviousSidName = true,
            PreviousFirewallSettings = null,
            HadPreviousFirewallSettings = false
        };

        await executor.RollbackAsync(rollbackState, credentialStore, database);

        Assert.Empty(credentialStore.Credentials);
        Assert.Equal("olduser", database.SidNames[sid]);
        var restored = Assert.Single(database.Accounts);
        Assert.Equal(PrivilegeLevel.Isolated, restored.PrivilegeLevel);
        Assert.True(restored.TrayTerminal);
        Assert.True(restored.Firewall.IsDefault);
        localUserProvider.Verify(s => s.InvalidateCache(), Times.Once);
    }

    [Fact]
    public async Task RollbackAsync_DeleteFails_StopsBeforeFurtherCleanup()
    {
        const string sid = "S-1-5-21-1-2-3-1001";
        var credentialId = Guid.NewGuid();
        var database = new AppDatabase();
        database.SidNames[sid] = "newuser";
        database.Accounts.Add(new AccountEntry { Sid = sid, PrivilegeLevel = PrivilegeLevel.HighestAllowed });

        var credentialStore = new CredentialStore();
        credentialStore.Credentials.Add(new CredentialEntry { Id = credentialId, Sid = sid });

        var lifecycleManager = new Mock<IAccountLifecycleManager>();
        lifecycleManager.Setup(s => s.DeleteSamAccount(sid))
            .Returns(new AccountDeletionResult(false, sid, "delete failed"));

        var credentialManager = new Mock<IAccountCredentialManager>();
        var associationService = new Mock<IAssociationAutoSetService>();
        var localUserProvider = new Mock<ILocalUserProvider>();
        var log = new Mock<ILoggingService>();
        var executor = new CreatedAccountRollbackExecutor(
            lifecycleManager.Object,
            credentialManager.Object,
            associationService.Object,
            localUserProvider.Object,
            log.Object);

        var rollbackState = new CreatedAccountRollbackState
        {
            Sid = sid,
            Username = "newuser",
            CredentialId = credentialId,
            PreviousAccount = null,
            HadPreviousAccount = false,
            PreviousSidName = null,
            HadPreviousSidName = false,
            PreviousFirewallSettings = null,
            HadPreviousFirewallSettings = false
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await executor.RollbackAsync(rollbackState, credentialStore, database));

        Assert.Equal("delete failed", ex.Message);
        Assert.Single(database.Accounts);
        Assert.Single(credentialStore.Credentials);
        associationService.Verify(s => s.RestoreForUser(It.IsAny<string>()), Times.Never);
        lifecycleManager.Verify(s => s.DeleteProfileAsync(It.IsAny<string>()), Times.Never);
        lifecycleManager.Verify(s => s.ClearAccountRestrictions(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AppSettings?>()), Times.Never);
        credentialManager.Verify(s => s.RemoveCredential(It.IsAny<Guid>(), It.IsAny<CredentialStore>()), Times.Never);
        localUserProvider.Verify(s => s.InvalidateCache(), Times.Never);
    }
}
