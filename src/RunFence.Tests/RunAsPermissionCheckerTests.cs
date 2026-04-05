using Moq;
using RunFence.Acl.Permissions;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launch.Container;
using RunFence.RunAs;
using Xunit;

namespace RunFence.Tests;

public class RunAsPermissionCheckerTests
{
    private const string UserSid1 = "S-1-5-21-1000-1000-1000-1001";
    private const string UserSid2 = "S-1-5-21-1000-1000-1000-1002";
    private const string ContainerSid = "S-1-15-2-99-1-2-3-4-5-6";
    private const string SafePath = @"C:\Apps\MyApp\app.exe";

    private readonly Mock<IAclPermissionService> _aclPermission = new();
    private readonly Mock<IAppContainerService> _appContainerService = new();

    private RunAsPermissionChecker CreateChecker()
        => new(_aclPermission.Object, _appContainerService.Object);

    // ── ComputeSidsNeedingPermission — blocked paths ──────────────────────

    [Fact]
    public void ComputeSidsNeedingPermission_BlockedAclRoot_ReturnsNull()
    {
        // Arrange: C:\Windows is an exact blocked ACL root (IsBlockedAclRoot match)
        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var checker = CreateChecker();

        // Act
        var result = checker.ComputeSidsNeedingPermission(windowsDir, [], []);

        // Assert: null signals "cannot grant, blocked root"
        Assert.Null(result);
    }

    [Fact]
    public void ComputeSidsNeedingPermission_ChildOfBlockedRoot_IsAllowed()
    {
        // Arrange: children of blocked roots are safe for allow ACEs (IsBlockedAclRoot uses exact match only)
        var cmdExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        var checker = CreateChecker();

        // Act
        var result = checker.ComputeSidsNeedingPermission(cmdExe, [], []);

        // Assert: child of blocked root is allowed (returns empty set, not null)
        Assert.NotNull(result);
    }

    // ── ComputeSidsNeedingPermission — credential accounts ────────────────

    [Fact]
    public void ComputeSidsNeedingPermission_CredentialNeedsPermission_SidIncluded()
    {
        // Arrange
        _aclPermission.Setup(p => p.NeedsPermissionGrantOrParent(SafePath, UserSid1)).Returns(true);
        var credentials = new List<CredentialEntry>
        {
            new() { Sid = UserSid1 }
        };
        var checker = CreateChecker();

        // Act
        var result = checker.ComputeSidsNeedingPermission(SafePath, credentials, []);

        // Assert
        Assert.NotNull(result);
        Assert.Contains(UserSid1, result, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ComputeSidsNeedingPermission_CredentialAlreadyHasPermission_SidNotIncluded()
    {
        // Arrange: NeedsPermissionGrantOrParent returns false → already has access
        _aclPermission.Setup(p => p.NeedsPermissionGrantOrParent(SafePath, UserSid1)).Returns(false);
        var credentials = new List<CredentialEntry>
        {
            new() { Sid = UserSid1 }
        };
        var checker = CreateChecker();

        // Act
        var result = checker.ComputeSidsNeedingPermission(SafePath, credentials, []);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void ComputeSidsNeedingPermission_CurrentAccountCredential_NotChecked()
    {
        // Arrange: the current-process SID is "current account" — always excluded from grants
        var currentSid = SidResolutionHelper.GetCurrentUserSid();
        _aclPermission.Setup(p => p.NeedsPermissionGrantOrParent(SafePath, currentSid)).Returns(true);
        var credentials = new List<CredentialEntry>
        {
            new() { Sid = currentSid }
        };
        var checker = CreateChecker();

        // Act
        var result = checker.ComputeSidsNeedingPermission(SafePath, credentials, []);

        // Assert: current account is IsCurrentAccount=true → skipped
        Assert.NotNull(result);
        Assert.Empty(result);
        _aclPermission.Verify(p => p.NeedsPermissionGrantOrParent(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void ComputeSidsNeedingPermission_MultipleCredentials_OnlyNeedingPermissionIncluded()
    {
        // Arrange: two non-current credentials; only one needs a grant
        _aclPermission.Setup(p => p.NeedsPermissionGrantOrParent(SafePath, UserSid1)).Returns(true);
        _aclPermission.Setup(p => p.NeedsPermissionGrantOrParent(SafePath, UserSid2)).Returns(false);
        var credentials = new List<CredentialEntry>
        {
            new() { Sid = UserSid1 },
            new() { Sid = UserSid2 }
        };
        var checker = CreateChecker();

        // Act
        var result = checker.ComputeSidsNeedingPermission(SafePath, credentials, []);

        // Assert
        Assert.NotNull(result);
        Assert.Contains(UserSid1, result, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(UserSid2, result, StringComparer.OrdinalIgnoreCase);
    }

    // ── ComputeSidsNeedingPermission — AppContainer SIDs ──────────────────

    [Fact]
    public void ComputeSidsNeedingPermission_ContainerNeedsPermission_ContainerSidIncluded()
    {
        // Arrange: container SID needs a grant
        _appContainerService.Setup(s => s.GetSid("rfn_test")).Returns(ContainerSid);
        _aclPermission.Setup(p => p.NeedsPermissionGrantOrParent(SafePath, ContainerSid)).Returns(true);
        var containers = new List<AppContainerEntry> { new() { Name = "rfn_test" } };
        var checker = CreateChecker();

        // Act
        var result = checker.ComputeSidsNeedingPermission(SafePath, [], containers);

        // Assert
        Assert.NotNull(result);
        Assert.Contains(ContainerSid, result, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ComputeSidsNeedingPermission_ContainerAlreadyHasPermission_ContainerSidNotIncluded()
    {
        // Arrange
        _appContainerService.Setup(s => s.GetSid("rfn_test")).Returns(ContainerSid);
        _aclPermission.Setup(p => p.NeedsPermissionGrantOrParent(SafePath, ContainerSid)).Returns(false);
        var containers = new List<AppContainerEntry> { new() { Name = "rfn_test" } };
        var checker = CreateChecker();

        // Act
        var result = checker.ComputeSidsNeedingPermission(SafePath, [], containers);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void ComputeSidsNeedingPermission_GetSidThrows_ContainerSkipped()
    {
        // Arrange: GetSid throws (container not yet created or OS error) — must not crash
        _appContainerService.Setup(s => s.GetSid("rfn_broken")).Throws(new InvalidOperationException("OS error"));
        var containers = new List<AppContainerEntry> { new() { Name = "rfn_broken" } };
        var checker = CreateChecker();

        // Act — must not throw
        var result = checker.ComputeSidsNeedingPermission(SafePath, [], containers);

        // Assert: empty set, no crash
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void ComputeSidsNeedingPermission_EmptyLists_ReturnsEmptySet()
    {
        // Arrange
        var checker = CreateChecker();

        // Act
        var result = checker.ComputeSidsNeedingPermission(SafePath, [], []);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }
}