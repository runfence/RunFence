using System.Security.AccessControl;
using Moq;
using RunFence.Acl;
using RunFence.Core.Models;
using RunFence.Launch;
using Xunit;

namespace RunFence.Tests;

public class LaunchAccessManagerTests
{
    private const string AccountSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
    private const string ContainerSid = "S-1-15-2-99-1-2-3-4-5-6";
    private const string TestPath = @"C:\apps\myapp";
    private const FileSystemRights TestRights = FileSystemRights.ReadAndExecute;

    private readonly Mock<IGrantMutatorService> _grantMutatorService = new();

    private LaunchAccessManager CreateManager() => new(_grantMutatorService.Object);

    private void SetupEnsureAccess(string sid, GrantApplyResult result) =>
        _grantMutatorService
            .Setup(s => s.EnsureAccess(sid, It.IsAny<string>(), It.IsAny<FileSystemRights>(),
                It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()))
            .Returns(result);

    [Fact]
    public void EnsureAccess_LowIntegrityAccount_CallsEnsureAccessForBothSids()
    {
        var identity = new AccountLaunchIdentity(AccountSid) { PrivilegeLevel = PrivilegeLevel.LowIntegrity };
        SetupEnsureAccess(AccountSid, new GrantApplyResult());
        SetupEnsureAccess(AclHelper.LowIntegritySid, new GrantApplyResult());

        var manager = CreateManager();
        manager.EnsureAccess(identity, TestPath, TestRights, null);

        _grantMutatorService.Verify(s => s.EnsureAccess(AccountSid, TestPath, TestRights, null, true), Times.Once);
        _grantMutatorService.Verify(s => s.EnsureAccess(AclHelper.LowIntegritySid, TestPath, TestRights, null, true), Times.Once);
    }

    [Fact]
    public void EnsureAccess_LowIntegrityAccount_CombinesDatabaseModifiedFlags()
    {
        var identity = new AccountLaunchIdentity(AccountSid) { PrivilegeLevel = PrivilegeLevel.LowIntegrity };
        SetupEnsureAccess(AccountSid, new GrantApplyResult());
        SetupEnsureAccess(AclHelper.LowIntegritySid, new GrantApplyResult(DatabaseModified: true));

        var manager = CreateManager();
        var result = manager.EnsureAccess(identity, TestPath, TestRights, null);

        Assert.True(result.DatabaseModified);
    }

    [Fact]
    public void EnsureAccess_IsolatedAccount_DoesNotCallLowIntegrityEnsureAccess()
    {
        var identity = new AccountLaunchIdentity(AccountSid) { PrivilegeLevel = PrivilegeLevel.Isolated };
        SetupEnsureAccess(AccountSid, new GrantApplyResult());

        var manager = CreateManager();
        manager.EnsureAccess(identity, TestPath, TestRights, null);

        _grantMutatorService.Verify(s => s.EnsureAccess(AccountSid, TestPath, TestRights, null, true), Times.Once);
        _grantMutatorService.Verify(
            s => s.EnsureAccess(AclHelper.LowIntegritySid, It.IsAny<string>(),
                It.IsAny<FileSystemRights>(), It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public void EnsureAccess_BasicAccount_UsesUnelevatedGrantCheck()
    {
        var identity = new AccountLaunchIdentity(AccountSid) { PrivilegeLevel = PrivilegeLevel.Basic };
        SetupEnsureAccess(AccountSid, new GrantApplyResult());

        var manager = CreateManager();
        manager.EnsureAccess(identity, TestPath, TestRights, null);

        _grantMutatorService.Verify(s => s.EnsureAccess(AccountSid, TestPath, TestRights, null, true), Times.Once);
        _grantMutatorService.Verify(s => s.EnsureAccess(AccountSid, TestPath, TestRights, null, false), Times.Never);
    }

    [Fact]
    public void EnsureAccess_HighestAllowedAccount_UsesElevatedGrantCheck()
    {
        var identity = new AccountLaunchIdentity(AccountSid) { PrivilegeLevel = PrivilegeLevel.HighestAllowed };
        SetupEnsureAccess(AccountSid, new GrantApplyResult());

        var manager = CreateManager();
        manager.EnsureAccess(identity, TestPath, TestRights, null);

        _grantMutatorService.Verify(s => s.EnsureAccess(AccountSid, TestPath, TestRights, null, false), Times.Once);
        _grantMutatorService.Verify(s => s.EnsureAccess(AccountSid, TestPath, TestRights, null, true), Times.Never);
    }

    [Fact]
    public void EnsureAccess_UnresolvedAccount_UsesUnelevatedGrantCheck()
    {
        var identity = new AccountLaunchIdentity(AccountSid);
        SetupEnsureAccess(AccountSid, new GrantApplyResult());

        var manager = CreateManager();
        manager.EnsureAccess(identity, TestPath, TestRights, null);

        _grantMutatorService.Verify(s => s.EnsureAccess(AccountSid, TestPath, TestRights, null, true), Times.Once);
    }

    [Fact]
    public void EnsureAccess_AppContainerIdentity_DoesNotCallLowIntegrityEnsureAccess()
    {
        var entry = new AppContainerEntry { Name = "ram_browser", DisplayName = "Browser", Sid = ContainerSid };
        var identity = new AppContainerLaunchIdentity(entry);
        SetupEnsureAccess(ContainerSid, new GrantApplyResult());

        var manager = CreateManager();
        manager.EnsureAccess(identity, TestPath, TestRights, null);

        _grantMutatorService.Verify(s => s.EnsureAccess(ContainerSid, TestPath, TestRights, null, true), Times.Once);
        _grantMutatorService.Verify(
            s => s.EnsureAccess(AclHelper.LowIntegritySid, It.IsAny<string>(),
                It.IsAny<FileSystemRights>(), It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public void EnsureAccess_RawSid_ForwardsSidAndUnelevatedFlag()
    {
        _grantMutatorService
            .Setup(s => s.EnsureAccess(AclHelper.AllApplicationPackagesSid, TestPath, TestRights, null, true))
            .Returns(new GrantApplyResult(GrantApplied: true, DurableSaveCompleted: true));

        var manager = CreateManager();
        manager.EnsureAccess(AclHelper.AllApplicationPackagesSid, TestPath, TestRights, null, unelevated: true);

        _grantMutatorService.Verify(s => s.EnsureAccess(
            AclHelper.AllApplicationPackagesSid,
            TestPath,
            TestRights,
            null,
            true), Times.Once);
    }

    [Fact]
    public void EnsureAccess_LowIntegrityAccount_DoesNotForwardConfirmToLowIlCall()
    {
        var identity = new AccountLaunchIdentity(AccountSid) { PrivilegeLevel = PrivilegeLevel.LowIntegrity };
        Func<string, string, bool> confirm = (_, _) => true;
        const FileSystemRights rights = FileSystemRights.Read;

        SetupEnsureAccess(AccountSid, new GrantApplyResult());
        SetupEnsureAccess(AclHelper.LowIntegritySid, new GrantApplyResult());

        var manager = CreateManager();
        manager.EnsureAccess(identity, TestPath, rights, confirm);

        _grantMutatorService.Verify(
            s => s.EnsureAccess(AclHelper.LowIntegritySid, TestPath, rights, null, true),
            Times.Once);
    }

    [Fact]
    public void EnsureAccess_LowIntegrityAccount_ReportsDurableSaveWhenEveryModifiedGrantSaved()
    {
        var identity = new AccountLaunchIdentity(AccountSid) { PrivilegeLevel = PrivilegeLevel.LowIntegrity };
        SetupEnsureAccess(AccountSid, new GrantApplyResult(DatabaseModified: true, DurableSaveCompleted: true));
        SetupEnsureAccess(AclHelper.LowIntegritySid, new GrantApplyResult());

        var manager = CreateManager();
        var result = manager.EnsureAccess(identity, TestPath, TestRights, null);

        Assert.True(result.DurableSaveCompleted);
    }

    [Fact]
    public void EnsureAccess_LowIntegrityAccount_DoesNotReportDurableSaveWhenAnyModifiedGrantIsUnsaved()
    {
        var identity = new AccountLaunchIdentity(AccountSid) { PrivilegeLevel = PrivilegeLevel.LowIntegrity };
        SetupEnsureAccess(AccountSid, new GrantApplyResult(DatabaseModified: true, DurableSaveCompleted: false));
        SetupEnsureAccess(AclHelper.LowIntegritySid, new GrantApplyResult(DatabaseModified: true, DurableSaveCompleted: true));

        var manager = CreateManager();
        var result = manager.EnsureAccess(identity, TestPath, TestRights, null);

        Assert.True(result.DatabaseModified);
        Assert.False(result.DurableSaveCompleted);
    }

    [Fact]
    public void EnsureAccess_LowIntegrityAccount_MergesRegularAndLowIntegrityWarnings()
    {
        var identity = new AccountLaunchIdentity(AccountSid) { PrivilegeLevel = PrivilegeLevel.LowIntegrity };
        var accountWarning = new GrantApplyWarning(
            GrantApplyFailureStep.PostGrantMutationSave,
            TestPath,
            null,
            new InvalidOperationException("account save failed"));
        var lowIntegrityWarning = new GrantApplyWarning(
            GrantApplyFailureStep.PostGrantMutationSave,
            TestPath,
            null,
            new InvalidOperationException("low il save failed"));

        SetupEnsureAccess(AccountSid, new GrantApplyResult(
            DatabaseModified: true,
            DurableSaveCompleted: false,
            Warnings: [accountWarning]));
        SetupEnsureAccess(AclHelper.LowIntegritySid, new GrantApplyResult(
            DatabaseModified: true,
            DurableSaveCompleted: false,
            Warnings: [lowIntegrityWarning]));

        var manager = CreateManager();
        var result = manager.EnsureAccess(identity, TestPath, TestRights, null);

        Assert.Equal([accountWarning, lowIntegrityWarning], result.Warnings);
    }
}
