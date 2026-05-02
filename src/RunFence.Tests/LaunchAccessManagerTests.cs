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

    private readonly Mock<IPathGrantService> _pathGrantService = new();

    private LaunchAccessManager CreateManager() => new(_pathGrantService.Object);

    private void SetupEnsureAccess(string sid, GrantOperationResult result) =>
        _pathGrantService
            .Setup(s => s.EnsureAccess(sid, It.IsAny<string>(), It.IsAny<FileSystemRights>(),
                It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()))
            .Returns(result);

    [Fact]
    public void EnsureAccess_LowIntegrityAccount_CallsEnsureAccessForBothSids()
    {
        var identity = new AccountLaunchIdentity(AccountSid) { PrivilegeLevel = PrivilegeLevel.LowIntegrity };
        SetupEnsureAccess(AccountSid, new GrantOperationResult(false, false, false));
        SetupEnsureAccess(AclHelper.LowIntegritySid, new GrantOperationResult(false, false, false));

        var manager = CreateManager();
        manager.EnsureAccess(identity, TestPath, TestRights, null, true);

        _pathGrantService.Verify(s => s.EnsureAccess(AccountSid, TestPath, TestRights, null, true), Times.Once);
        _pathGrantService.Verify(s => s.EnsureAccess(AclHelper.LowIntegritySid, TestPath, TestRights, null, true), Times.Once);
    }

    [Fact]
    public void EnsureAccess_LowIntegrityAccount_CombinesDatabaseModifiedFlags()
    {
        var identity = new AccountLaunchIdentity(AccountSid) { PrivilegeLevel = PrivilegeLevel.LowIntegrity };
        SetupEnsureAccess(AccountSid, new GrantOperationResult(false, false, false));
        SetupEnsureAccess(AclHelper.LowIntegritySid, new GrantOperationResult(false, false, true));

        var manager = CreateManager();
        var result = manager.EnsureAccess(identity, TestPath, TestRights, null, true);

        Assert.True(result.DatabaseModified);
    }

    [Fact]
    public void EnsureAccess_BasicAccount_DoesNotCallLowIntegrityEnsureAccess()
    {
        var identity = new AccountLaunchIdentity(AccountSid) { PrivilegeLevel = PrivilegeLevel.Basic };
        SetupEnsureAccess(AccountSid, new GrantOperationResult(false, false, false));

        var manager = CreateManager();
        manager.EnsureAccess(identity, TestPath, TestRights, null, true);

        _pathGrantService.Verify(s => s.EnsureAccess(AccountSid, TestPath, TestRights, null, true), Times.Once);
        _pathGrantService.Verify(
            s => s.EnsureAccess(AclHelper.LowIntegritySid, It.IsAny<string>(),
                It.IsAny<FileSystemRights>(), It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public void EnsureAccess_AppContainerIdentity_DoesNotCallLowIntegrityEnsureAccess()
    {
        var entry = new AppContainerEntry { Name = "ram_browser", DisplayName = "Browser", Sid = ContainerSid };
        var identity = new AppContainerLaunchIdentity(entry);
        SetupEnsureAccess(ContainerSid, new GrantOperationResult(false, false, false));

        var manager = CreateManager();
        manager.EnsureAccess(identity, TestPath, TestRights, null, true);

        _pathGrantService.Verify(s => s.EnsureAccess(ContainerSid, TestPath, TestRights, null, true), Times.Once);
        _pathGrantService.Verify(
            s => s.EnsureAccess(AclHelper.LowIntegritySid, It.IsAny<string>(),
                It.IsAny<FileSystemRights>(), It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public void EnsureAccess_LowIntegrityAccount_PassesSameArgsToLowIlCall()
    {
        var identity = new AccountLaunchIdentity(AccountSid) { PrivilegeLevel = PrivilegeLevel.LowIntegrity };
        Func<string, string, bool> confirm = (_, _) => true;
        const bool unelevated = false;
        const FileSystemRights rights = FileSystemRights.Read;

        SetupEnsureAccess(AccountSid, new GrantOperationResult(false, false, false));
        SetupEnsureAccess(AclHelper.LowIntegritySid, new GrantOperationResult(false, false, false));

        var manager = CreateManager();
        manager.EnsureAccess(identity, TestPath, rights, confirm, unelevated);

        _pathGrantService.Verify(
            s => s.EnsureAccess(AclHelper.LowIntegritySid, TestPath, rights, confirm, unelevated),
            Times.Once);
    }
}
