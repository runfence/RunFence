using Moq;
using RunFence.Account;
using RunFence.Account.Lifecycle;
using RunFence.Acl;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.SidMigration;
using Xunit;

namespace RunFence.Tests;

public class AccountDeletionServiceTests
{
    private const string Sid = "S-1-5-21-0-0-0-1001";
    private const string OtherSid = "S-1-5-21-0-0-0-1002";
    private const string Username = "testuser";

    private readonly Mock<IAccountLifecycleManager> _lifecycleManager = new();
    private readonly Mock<IAccountCredentialManager> _credentialManager = new();
    private readonly Mock<IGrantAccountCleanupService> _pathGrantService = new();
    private readonly Mock<IFirewallCleanupService> _firewallCleanupService = new();
    private readonly Mock<IGlobalIcmpSettingsApplier> _globalIcmpSettingsApplier = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<IAclService> _aclService = new();
    private readonly Mock<ILocalUserProvider> _localUserProvider = new();

    private AccountDeletionService BuildService(AppDatabase database)
    {
        _lifecycleManager.SetReturnsDefault(Task.FromResult(AccountDeleteValidationResult.Success));
        var dbProvider = new LambdaDatabaseProvider(() => database);
        var trackingJobStateStore = CreateTrackingJobStateStore(database);
        return new(_lifecycleManager.Object, _credentialManager.Object,
            _pathGrantService.Object,
            _firewallCleanupService.Object,
            _globalIcmpSettingsApplier.Object,
            new SidCleanupHelper(dbProvider, trackingJobStateStore.Object),
            _log.Object,
            _aclService.Object,
            _localUserProvider.Object,
            dbProvider);
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

    private static (AppDatabase database, CredentialStore store) BuildSession(
        string? sid = null, bool addCredential = true, bool addApp = true,
        bool addEphemeral = false, bool addGrant = false, bool addFirewallSettings = false)
    {
        var s = sid ?? Sid;
        var database = new AppDatabase();
        var store = new CredentialStore();

        if (addCredential)
            store.Credentials.Add(new CredentialEntry { Sid = s, EncryptedPassword = [1] });

        if (addApp)
            database.Apps.Add(new AppEntry { AccountSid = s, Name = "TestApp" });

        if (addEphemeral)
            database.GetOrCreateAccount(s).DeleteAfterUtc = DateTime.UtcNow.AddHours(-1);

        if (addGrant)
            database.GetOrCreateAccount(s).Grants.Add(new GrantedPathEntry { Path = @"C:\SomePath" });

        if (addFirewallSettings)
            database.GetOrCreateAccount(s).Firewall = new FirewallAccountSettings { AllowInternet = false };

        return (database, store);
    }

    [Fact]
    public async Task DeleteAccount_FullPipeline_ExecutesAllStepsInOrder()
    {
        // Arrange
        var (database, store) = BuildSession(addGrant: true, addEphemeral: true, addFirewallSettings: true);
        var callOrder = new List<string>();
        _lifecycleManager.Setup(m => m.ClearAccountRestrictions(Sid, Username, It.IsAny<AppSettings?>()))
            .Callback(() => callOrder.Add("ClearRestrictions"));
        _firewallCleanupService.Setup(f => f.RemoveAllRules(Sid))
            .Callback(() => callOrder.Add("RemoveFirewallRules"));
        _lifecycleManager.Setup(m => m.DeleteSamAccount(Sid))
            .Callback(() => callOrder.Add("DeleteUser"))
            .Returns(new AccountDeletionResult(true, Sid, null));
        _credentialManager.Setup(m => m.RemoveCredentialsBySid(Sid, store))
            .Callback(() => callOrder.Add("RemoveCredentials"));
        _pathGrantService.Setup(g => g.RemoveAll(Sid))
            .Callback(() => callOrder.Add("RevertGrants"));
        // R2_TL6: ApplyGlobalIcmpSetting must be tracked in the call-order verification
        _globalIcmpSettingsApplier.Setup(g => g.ApplyGlobalIcmpSetting(database))
            .Callback(() => callOrder.Add("ApplyGlobalIcmp"));
        var service = BuildService(database);

        // Act
        await service.DeleteAccountAsync(Sid, Username, store);

        // Assert — verify order: deleteUser, restrictions, firewall, credentials, grants, global ICMP
        Assert.Equal(["DeleteUser", "ClearRestrictions", "RemoveFirewallRules", "RemoveCredentials", "RevertGrants", "ApplyGlobalIcmp"],
            callOrder);
        // CleanupSidFromAppData removes the AccountEntry entirely
        Assert.Null(database.GetAccount(Sid));
        // R2_TL7: InvalidateCache must be called exactly once to refresh the local user provider
        _localUserProvider.Verify(l => l.InvalidateCache(), Times.Once);
    }

    [Fact]
    public async Task DeleteAccount_AwaitsProfileDeletionAfterDeleteUser()
    {
        var (database, store) = BuildSession();
        var callOrder = new List<string>();
        var profileDeletion = new TaskCompletionSource<string?>();
        _lifecycleManager.Setup(m => m.DeleteSamAccount(Sid))
            .Callback(() => callOrder.Add("DeleteUser"))
            .Returns(new AccountDeletionResult(true, Sid, null));
        _lifecycleManager.Setup(m => m.DeleteProfileAsync(Sid))
            .Callback(() => callOrder.Add("DeleteProfile"))
            .Returns(profileDeletion.Task);
        var service = BuildService(database);

        var deleteTask = service.DeleteAccountAsync(Sid, Username, store);

        Assert.False(deleteTask.IsCompleted);
        Assert.Equal(["DeleteUser", "DeleteProfile"], callOrder.Take(2));

        profileDeletion.SetResult(null);
        await deleteTask;
    }

    [Fact]
    public async Task DeleteAccount_RemovesCredential()
    {
        // Arrange
        var (database, store) = BuildSession(addCredential: true);
        _lifecycleManager.Setup(m => m.DeleteSamAccount(Sid)).Returns(new AccountDeletionResult(true, Sid, null));
        var service = BuildService(database);

        // Act
        await service.DeleteAccountAsync(Sid, Username, store);

        // Assert
        _credentialManager.Verify(m => m.RemoveCredentialsBySid(Sid, store), Times.Once);
    }

[Fact]
    public async Task DeleteAccount_RemovesEphemeralEntry()
    {
        // Arrange
        var (database, store) = BuildSession(addEphemeral: true);
        _lifecycleManager.Setup(m => m.DeleteSamAccount(Sid)).Returns(new AccountDeletionResult(true, Sid, null));
        var service = BuildService(database);

        // Act
        await service.DeleteAccountAsync(Sid, Username, store);

        // Assert — CleanupSidFromAppData removes the AccountEntry (including DeleteAfterUtc)
        Assert.Null(database.GetAccount(Sid));
    }

[Fact]
    public async Task DeleteAccount_RemovesFirewallSettings()
    {
        // Arrange
        var (database, store) = BuildSession(addFirewallSettings: true);
        _lifecycleManager.Setup(m => m.DeleteSamAccount(Sid)).Returns(new AccountDeletionResult(true, Sid, null));
        var service = BuildService(database);

        // Act
        await service.DeleteAccountAsync(Sid, Username, store);

        // Assert — AccountEntry (including Firewall) removed by CleanupSidFromAppData
        Assert.Null(database.GetAccount(Sid));
    }

    [Fact]
    public async Task DeleteAccount_RemovesTrackingJobSid()
    {
        var (database, store) = BuildSession(addApp: false);
        database.TrackingJobSids = [Sid, OtherSid];
        _lifecycleManager.Setup(m => m.DeleteSamAccount(Sid)).Returns(new AccountDeletionResult(true, Sid, null));
        var service = BuildService(database);

        await service.DeleteAccountAsync(Sid, Username, store);

        Assert.DoesNotContain(database.TrackingJobSids!, sid =>
            string.Equals(sid, Sid, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(database.TrackingJobSids!, sid =>
            string.Equals(sid, OtherSid, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DeleteAccount_RevertsGrants_WhenPresent()
    {
        // Arrange
        var (database, store) = BuildSession(addGrant: true);
        _lifecycleManager.Setup(m => m.DeleteSamAccount(Sid)).Returns(new AccountDeletionResult(true, Sid, null));
        var service = BuildService(database);

        // Act
        await service.DeleteAccountAsync(Sid, Username, store);

        // Assert
        _pathGrantService.Verify(g => g.RemoveAll(Sid), Times.Once);
        Assert.Null(database.GetAccount(Sid));
    }

    [Fact]
    public async Task DeleteAccount_ReturnsCleanupWarnings_FromRemoveAll()
    {
        // Arrange
        var (database, store) = BuildSession(addGrant: true);
        _lifecycleManager.Setup(m => m.DeleteSamAccount(Sid)).Returns(new AccountDeletionResult(true, Sid, null));
        var warning = new GrantApplyWarning(
            GrantApplyFailureStep.PostRemoveAllSave,
            @"C:\Apps\app.exe",
            null,
            new InvalidOperationException("store write failed"));
        _pathGrantService.Setup(g => g.RemoveAll(Sid))
            .Returns(new GrantApplyResult(
                GrantApplied: true,
                DatabaseModified: true,
                DurableSaveCompleted: false,
                Warnings: [warning]));
        var service = BuildService(database);

        // Act
        var result = await service.DeleteAccountAsync(Sid, Username, store);

        // Assert: warnings are converted to user-facing strings and returned
        var formattedWarning = GrantApplyFailureFormatter.Format(warning);
        Assert.Single(result.Warnings);
        Assert.Contains(formattedWarning, result.Warnings);
    }

    [Fact]
    public async Task DeleteAccount_RemoveAppsTrue_RemovesApps()
    {
        // Arrange
        var (database, store) = BuildSession(addApp: true);
        _lifecycleManager.Setup(m => m.DeleteSamAccount(Sid)).Returns(new AccountDeletionResult(true, Sid, null));
        var service = BuildService(database);

        // Act
        await service.DeleteAccountAsync(Sid, Username, store, removeApps: true);

        // Assert
        Assert.Empty(database.Apps);
    }

    [Fact]
    public async Task DeleteAccount_RemoveAppsFalse_PreservesApps()
    {
        // Arrange
        var (database, store) = BuildSession(addApp: true);
        _lifecycleManager.Setup(m => m.DeleteSamAccount(Sid)).Returns(new AccountDeletionResult(true, Sid, null));
        var service = BuildService(database);

        // Act
        await service.DeleteAccountAsync(Sid, Username, store, removeApps: false);

        // Assert
        Assert.Single(database.Apps);
        Assert.Equal(Sid, database.Apps[0].AccountSid, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteAccount_DeleteUserFails_ThrowsAndDoesNotCleanupDatabase()
    {
        // Arrange
        var (database, store) = BuildSession(addCredential: true, addEphemeral: true);
        _lifecycleManager.Setup(m => m.DeleteSamAccount(Sid)).Returns(new AccountDeletionResult(false, Sid, "Access denied"));
        var service = BuildService(database);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAccountAsync(Sid, Username, store));
        Assert.Contains("Access denied", ex.Message);

        // DeleteUser failure is terminal: the OS account still exists, so removing restrictions,
        // firewall rules, and credentials would leave the account in an inconsistent half-deleted
        // state. All subsequent steps are skipped intentionally.
        _lifecycleManager.Verify(m => m.ClearAccountRestrictions(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AppSettings?>()), Times.Never);
        _firewallCleanupService.Verify(f => f.RemoveAllRules(It.IsAny<string>()), Times.Never);
        _credentialManager.Verify(m => m.RemoveCredentialsBySid(It.IsAny<string>(), It.IsAny<CredentialStore>()), Times.Never);
        Assert.True(database.GetAccount(Sid)?.DeleteAfterUtc.HasValue);
    }

    [Fact]
    public async Task DeleteAccount_ClearRestrictionsThrows_ContinuesDeletion()
    {
        // Unlike DeleteUser failure (which is terminal because the OS account still exists),
        // post-delete cleanup steps are best-effort: the account is already gone, so partial
        // cleanup is better than stopping entirely. Each non-fatal exception is logged as a warning.
        // Arrange
        var (database, store) = BuildSession();
        _lifecycleManager.Setup(m => m.ClearAccountRestrictions(Sid, Username, It.IsAny<AppSettings?>()))
            .Throws(new InvalidOperationException("Restriction service unavailable"));
        _lifecycleManager.Setup(m => m.DeleteSamAccount(Sid)).Returns(new AccountDeletionResult(true, Sid, null));
        var service = BuildService(database);

        // Act — should NOT throw
        await service.DeleteAccountAsync(Sid, Username, store);

        // Assert — deletion still proceeded
        _lifecycleManager.Verify(m => m.DeleteSamAccount(Sid), Times.Once);
    }

    [Fact]
    public async Task DeleteAccount_FirewallRemoveThrows_ContinuesDeletion()
    {
        // Arrange
        var (database, store) = BuildSession();
        _firewallCleanupService.Setup(f => f.RemoveAllRules(Sid))
            .Throws(new InvalidOperationException("Firewall unavailable"));
        _lifecycleManager.Setup(m => m.DeleteSamAccount(Sid)).Returns(new AccountDeletionResult(true, Sid, null));
        var service = BuildService(database);

        // Act — should NOT throw
        await service.DeleteAccountAsync(Sid, Username, store);

        // Assert — deletion still proceeded
        _lifecycleManager.Verify(m => m.DeleteSamAccount(Sid), Times.Once);
    }

    [Fact]
    public async Task DeleteAccount_NoGrants_SkipsGrantRevert()
    {
        // Arrange — no AccountEntry for this SID (no grants, no ephemeral, no firewall)
        var (database, store) = BuildSession(addGrant: false);
        _lifecycleManager.Setup(m => m.DeleteSamAccount(Sid)).Returns(new AccountDeletionResult(true, Sid, null));
        var service = BuildService(database);

        // Act
        await service.DeleteAccountAsync(Sid, Username, store);

        // Assert
        _pathGrantService.Verify(g => g.RemoveAll(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAccount_DeletesProfileInsideService()
    {
        // Arrange
        var (database, store) = BuildSession();
        _lifecycleManager.Setup(m => m.DeleteSamAccount(Sid)).Returns(new AccountDeletionResult(true, Sid, null));
        var service = BuildService(database);

        // Act
        await service.DeleteAccountAsync(Sid, Username, store);

        // Assert — profile deletion is handled inside AccountDeletionService
        _lifecycleManager.Verify(m => m.DeleteProfileAsync(Sid), Times.Once);
    }

    [Fact]
    public async Task DeleteAccount_ProfileDeletionReturnsError_LogsWarningAndContinues()
    {
        var (database, store) = BuildSession();
        _lifecycleManager.Setup(m => m.DeleteSamAccount(Sid)).Returns(new AccountDeletionResult(true, Sid, null));
        _lifecycleManager.Setup(m => m.DeleteProfileAsync(Sid)).ReturnsAsync("profile cleanup failed");
        var service = BuildService(database);

        await service.DeleteAccountAsync(Sid, Username, store);

        Assert.Null(database.GetAccount(Sid));
    }

    [Fact]
    public async Task DeleteAccount_WithAclService_RevertsRestrictAclAppsOfDeletedAccount()
    {
        // Arrange
        var (database, store) = BuildSession(addApp: false);
        var restrictedApp = new AppEntry { AccountSid = Sid, Name = "ProtectedApp", RestrictAcl = true, ExePath = @"C:\Apps\app.exe" };
        var otherApp = new AppEntry { AccountSid = OtherSid, Name = "OtherApp", RestrictAcl = true, ExePath = @"C:\Apps\other.exe" };
        database.Apps.AddRange([restrictedApp, otherApp]);
        _lifecycleManager.Setup(m => m.DeleteSamAccount(Sid)).Returns(new AccountDeletionResult(true, Sid, null));
        var service = BuildService(database);

        // Act
        await service.DeleteAccountAsync(Sid, Username, store, removeApps: false);

        // Assert: RevertAcl called for the deleted account's app only,
        // with remaining apps that exclude the deleted SID's apps
        _aclService.Verify(a => a.RevertAcl(
                restrictedApp,
                It.Is<IReadOnlyList<AppEntry>>(apps => apps.Contains(otherApp) && !apps.Contains(restrictedApp))),
            Times.Once);
        _aclService.Verify(a => a.RevertAcl(otherApp, It.IsAny<IReadOnlyList<AppEntry>>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAccount_WithAclService_SkipsNonRestrictAclApps()
    {
        // Arrange
        var (database, store) = BuildSession(addApp: false);
        database.Apps.Add(new AppEntry { AccountSid = Sid, Name = "TestApp", RestrictAcl = false });
        _lifecycleManager.Setup(m => m.DeleteSamAccount(Sid)).Returns(new AccountDeletionResult(true, Sid, null));
        var service = BuildService(database);

        // Act
        await service.DeleteAccountAsync(Sid, Username, store);

        // Assert: no RevertAcl calls since the app has RestrictAcl = false
        _aclService.Verify(a => a.RevertAcl(It.IsAny<AppEntry>(), It.IsAny<IReadOnlyList<AppEntry>>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAccount_WithAclService_ReappliesAllowModeAppsOfOtherAccountsThatGrantedDeletedSid()
    {
        // Arrange
        var (database, store) = BuildSession(addApp: false);
        var allowApp = new AppEntry
        {
            AccountSid = OtherSid, Name = "AllowApp", RestrictAcl = true,
            AclMode = AclMode.Allow, ExePath = @"C:\Apps\app.exe",
            AllowedAclEntries = [new AllowAclEntry { Sid = Sid }, new AllowAclEntry { Sid = OtherSid }]
        };
        database.Apps.Add(allowApp);
        _lifecycleManager.Setup(m => m.DeleteSamAccount(Sid)).Returns(new AccountDeletionResult(true, Sid, null));
        var service = BuildService(database);

        // Act
        await service.DeleteAccountAsync(Sid, Username, store);

        // Assert: ApplyAcl called after cleanup (when AllowedAclEntries no longer has deleted SID),
        // with remaining apps excluding the deleted account's apps
        _aclService.Verify(a => a.ApplyAcl(
                allowApp,
                It.Is<IReadOnlyList<AppEntry>>(apps => apps.Contains(allowApp))),
            Times.Once);
        // After cleanup, deleted SID should be gone from entries
        Assert.DoesNotContain(allowApp.AllowedAclEntries!, e =>
            string.Equals(e.Sid, Sid, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DeleteAccount_WithAclService_RecomputesAncestorAclsWithAppsExcludingDeletedSid()
    {
        // Arrange
        var (database, store) = BuildSession(addApp: false);
        var deletedApp = new AppEntry { AccountSid = Sid, Name = "DeletedApp", RestrictAcl = true, ExePath = @"C:\Apps\d.exe" };
        var otherApp = new AppEntry { AccountSid = OtherSid, Name = "OtherApp", RestrictAcl = true, ExePath = @"C:\Apps\o.exe" };
        database.Apps.AddRange([deletedApp, otherApp]);
        _lifecycleManager.Setup(m => m.DeleteSamAccount(Sid)).Returns(new AccountDeletionResult(true, Sid, null));
        var service = BuildService(database);

        // Act
        await service.DeleteAccountAsync(Sid, Username, store, removeApps: false);

        // Assert: RecomputeAllAncestorAcls called with only otherApp (deleted account's apps excluded)
        _aclService.Verify(a => a.RecomputeAllAncestorAcls(
                It.Is<IReadOnlyList<AppEntry>>(apps => apps.Contains(otherApp) && !apps.Contains(deletedApp))),
            Times.Once);
    }

    [Fact]
    public async Task DeleteAccount_WithAclService_RevertAclFailure_LogsWarningAndContinues()
    {
        // Arrange
        var (database, store) = BuildSession(addApp: false);
        var app = new AppEntry { AccountSid = Sid, Name = "BrokenApp", RestrictAcl = true, ExePath = @"C:\Apps\b.exe" };
        database.Apps.Add(app);
        _lifecycleManager.Setup(m => m.DeleteSamAccount(Sid)).Returns(new AccountDeletionResult(true, Sid, null));
        _aclService.Setup(a => a.RevertAcl(It.IsAny<AppEntry>(), It.IsAny<IReadOnlyList<AppEntry>>()))
            .Throws(new UnauthorizedAccessException("Access denied"));
        var service = BuildService(database);

        // Act — must not throw
        await service.DeleteAccountAsync(Sid, Username, store);

        // Assert: warning logged, RecomputeAllAncestorAcls still called
        _aclService.Verify(a => a.RecomputeAllAncestorAcls(It.IsAny<IReadOnlyList<AppEntry>>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAccount_NoRestrictAclApps_DoesNotCallRevertAcl()
    {
        // Arrange: the test app has RestrictAcl = false, so RevertAcl must never be called.
        // RecomputeAllAncestorAcls is always called after deletion.
        var (database, store) = BuildSession(addApp: false);
        database.Apps.Add(new AppEntry { AccountSid = Sid, Name = "TestApp", RestrictAcl = false });
        _lifecycleManager.Setup(m => m.DeleteSamAccount(Sid)).Returns(new AccountDeletionResult(true, Sid, null));
        var service = BuildService(database);

        // Act
        await service.DeleteAccountAsync(Sid, Username, store);

        // Assert
        _aclService.Verify(a => a.RevertAcl(It.IsAny<AppEntry>(), It.IsAny<IReadOnlyList<AppEntry>>()), Times.Never);
        _aclService.Verify(a => a.RecomputeAllAncestorAcls(It.IsAny<IReadOnlyList<AppEntry>>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAccount_RemoveAppsTrue_RestrictAclTrue_RevertAclCalledBeforeAppRemoval()
    {
        // Arrange: app belongs to the deleted SID, has RestrictAcl = true.
        // With removeApps: true, CleanupSidFromAppData removes the app from the database.
        // RevertAcl must be called BEFORE the app is removed so ACEs can still be resolved.
        var (database, store) = BuildSession(addApp: false);
        var restrictedApp = new AppEntry
        {
            AccountSid = Sid, Name = "ProtectedApp",
            RestrictAcl = true, ExePath = @"C:\Apps\app.exe"
        };
        database.Apps.Add(restrictedApp);
        _lifecycleManager.Setup(m => m.DeleteSamAccount(Sid)).Returns(new AccountDeletionResult(true, Sid, null));

        bool appPresentDuringRevertAcl = false;
        _aclService
            .Setup(a => a.RevertAcl(restrictedApp, It.IsAny<IReadOnlyList<AppEntry>>()))
            .Callback(() => appPresentDuringRevertAcl = database.Apps.Contains(restrictedApp));

        var service = BuildService(database);

        // Act
        await service.DeleteAccountAsync(Sid, Username, store, removeApps: true);

        // Assert: RevertAcl was called, the app was present in the database at that point,
        // and the app was subsequently removed by CleanupSidFromAppData.
        _aclService.Verify(a => a.RevertAcl(restrictedApp, It.IsAny<IReadOnlyList<AppEntry>>()), Times.Once);
        Assert.True(appPresentDuringRevertAcl);
        Assert.Empty(database.Apps);
    }
}
