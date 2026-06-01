using Moq;
using RunFence.Account;
using RunFence.Account.OrphanedProfiles;
using RunFence.Core;
using RunFence.Core.Models;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Tests for <see cref="AccountLifecycleManager"/> orchestration — validation,
/// restriction clearing, user deletion, profile deletion, and ACL cleanup.
/// </summary>
public class AccountLifecycleManagerTests
{
    private const string Sid = "S-1-5-21-0-0-0-1001";
    private const string Username = "testuser";

    private readonly Mock<IWindowsAccountService> _windowsAccountService = new();
    private readonly Mock<IAccountLoginRestrictionService> _loginRestriction = new();
    private readonly Mock<IAccountLsaRestrictionService> _lsaRestriction = new();
    private readonly Mock<IGroupPolicyScriptHelper> _gpHelper = new();
    private readonly Mock<IOrphanedProfileService> _orphanedProfileService = new();
    private readonly Mock<IOrphanedAclCleanupService> _aclCleanupService = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<IAccountValidationService> _accountValidation = new();
    private readonly Mock<IProfilePathResolver> _profilePathResolver = new();

    private AccountLifecycleManager CreateManager() => new(
        _windowsAccountService.Object,
        _loginRestriction.Object,
        _lsaRestriction.Object,
        _gpHelper.Object,
        _orphanedProfileService.Object,
        _aclCleanupService.Object,
        _log.Object,
        _accountValidation.Object,
        _profilePathResolver.Object,
        new ValidationRunner(_log.Object));

    // ── ValidateDeleteAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task ValidateDeleteAsync_AllChecksPass_ReturnsSuccess()
    {
        // Arrange: all validation methods succeed (no exception thrown)
        _accountValidation.Setup(v => v.GetRunningProcesses(Sid)).Returns([]);
        var manager = CreateManager();

        // Act
        var result = await manager.ValidateDeleteAsync(Sid);

        // Assert
        Assert.Null(result.ErrorMessage);
        Assert.Empty(result.RunningProcesses);
    }

    [Fact]
    public async Task ValidateDeleteAsync_IsInteractiveUser_ReturnsErrorMessage()
    {
        // Arrange
        _accountValidation.Setup(v => v.ValidateNotInteractiveUser(Sid, "delete"))
            .Throws(new InvalidOperationException("Cannot delete the interactive user."));
        var manager = CreateManager();

        // Act
        var result = await manager.ValidateDeleteAsync(Sid);

        // Assert
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("interactive", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.RunningProcesses);
    }

    [Fact]
    public async Task ValidateDeleteAsync_IsLastAdmin_ReturnsErrorMessage()
    {
        // Arrange
        _accountValidation.Setup(v => v.ValidateNotLastAdmin(Sid, "delete"))
            .Throws(new InvalidOperationException("Cannot delete the last administrator."));
        var manager = CreateManager();

        // Act
        var result = await manager.ValidateDeleteAsync(Sid);

        // Assert
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("administrator", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.RunningProcesses);
    }

    [Fact]
    public async Task ValidateDeleteAsync_HasRunningProcesses_ReturnsProcessList()
    {
        // Arrange
        _accountValidation.Setup(v => v.GetRunningProcesses(Sid))
            .Returns(
            [
                new ProcessInfo(11, @"C:\Apps\alpha.exe", null),
                new ProcessInfo(22, @"C:\Apps\beta.exe", null)
            ]);
        var manager = CreateManager();

        // Act
        var result = await manager.ValidateDeleteAsync(Sid);

        // Assert
        Assert.Null(result.ErrorMessage);
        Assert.Equal(2, result.RunningProcesses.Count);
        Assert.Contains(result.RunningProcesses, p => p.Pid == 11);
        Assert.Contains(result.RunningProcesses, p => p.Pid == 22);
    }

    // ── ClearAccountRestrictions ─────────────────────────────────────────────

    [Fact]
    public void ClearAccountRestrictions_CallsAllRestrictionServices()
    {
        // Arrange
        var manager = CreateManager();

        // Act
        manager.ClearAccountRestrictions(Sid, Username);

        // Assert: all restriction services called; gpHelper used directly to avoid rollback risk
        _loginRestriction.Verify(r => r.SetAccountHidden(Username, Sid, false), Times.Once);
        _lsaRestriction.Verify(r => r.SetLocalOnlyBySid(Sid, false), Times.Once);
        _gpHelper.Verify(r => r.SetLoginBlocked(Sid, false), Times.Once);
        _lsaRestriction.Verify(r => r.SetNoBgAutostartBySid(Sid, false), Times.Once);
        // SetLoginBlockedBySid must NOT be called — it has a rollback that could re-enable restrictions
        _loginRestriction.Verify(r => r.SetLoginBlockedBySid(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void ClearAccountRestrictions_ServiceThrows_ContinuesSilently()
    {
        // Arrange: restriction service throws — must not propagate
        _loginRestriction.Setup(r => r.SetAccountHidden(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .Throws(new InvalidOperationException("OS error"));
        var manager = CreateManager();

        // Act — must not throw
        var exception = Record.Exception(() => manager.ClearAccountRestrictions(Sid, Username));

        // Assert
        Assert.Null(exception);
        // All remaining services still called despite the earlier exception
        _lsaRestriction.Verify(r => r.SetLocalOnlyBySid(Sid, false), Times.Once);
        _gpHelper.Verify(r => r.SetLoginBlocked(Sid, false), Times.Once);
        _lsaRestriction.Verify(r => r.SetNoBgAutostartBySid(Sid, false), Times.Once);
    }

    [Fact]
    public void ClearAccountRestrictions_WithSettings_DoesNotThrow_WhenRegistryWriteFails()
    {
        // Arrange: settings carry a non-null OriginalUacAdminEnumeration for this SID,
        // causing RevertUacAdminEnumeration to attempt a HKLM registry write.
        // Tests run as a non-elevated user, so the write throws — ClearAccountRestrictions
        // must swallow registry exceptions and not propagate.
        var manager = CreateManager();
        var settings = new AppSettings
        {
            OriginalUacAdminEnumeration = 0,
            UacAdminEnumerationSid = Sid
        };

        // Act — registry revert throws (no admin access in test)
        var exception = Record.Exception(() => manager.ClearAccountRestrictions(Sid, Username, settings));

        Assert.Null(exception);
    }

    // ── DeleteUser ───────────────────────────────────────────────────────────

    [Fact]
    public void DeleteUser_ServiceThrows_ReturnsFalseWithErrorMessage()
    {
        // Arrange
        _windowsAccountService.Setup(s => s.DeleteSamAccount(Sid))
            .Throws(new InvalidOperationException("Account not found."));
        var manager = CreateManager();

        // Act
        var (success, error) = manager.DeleteSamAccount(Sid);

        // Assert
        Assert.False(success);
        Assert.NotNull(error);
        Assert.Contains("Account not found", error);
    }

    [Fact]
    public void DeleteUser_DoesNotDeleteProfile()
    {
        _windowsAccountService.Setup(s => s.DeleteSamAccount(Sid));
        var manager = CreateManager();

        _ = manager.DeleteSamAccount(Sid);

        _orphanedProfileService.Verify(
            s => s.DeleteProfiles(It.IsAny<IEnumerable<OrphanedProfile>>()),
            Times.Never);
    }

    // ── DeleteProfileAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteProfileAsync_NoProfilePath_ReturnsNull()
    {
        // Arrange: TryGetProfilePath returns null → no profile to delete
        _profilePathResolver.Setup(r => r.TryGetProfilePath(Sid)).Returns((string?)null);
        var manager = CreateManager();

        // Act
        var error = await manager.DeleteProfileAsync(Sid);

        // Assert: no deletion attempted, no error
        Assert.Null(error);
        _orphanedProfileService.Verify(
            s => s.DeleteProfiles(It.IsAny<IEnumerable<OrphanedProfile>>()), Times.Never);
    }

    [Fact]
    public async Task DeleteProfileAsync_ProfileDoesNotExistOnDisk_ReturnsNull()
    {
        // Arrange: TryGetProfilePath returns a path that does not exist on disk
        _profilePathResolver.Setup(r => r.TryGetProfilePath(Sid)).Returns(@"C:\NonExistentProfile_12345");
        var manager = CreateManager();

        // Act
        var error = await manager.DeleteProfileAsync(Sid);

        // Assert: no deletion attempted
        Assert.Null(error);
        _orphanedProfileService.Verify(
            s => s.DeleteProfiles(It.IsAny<IEnumerable<OrphanedProfile>>()), Times.Never);
    }

    // ── CleanupAclReferencesAsync ─────────────────────────────────────────────

    [Fact]
    public async Task CleanupAclReferencesAsync_AllSucceed_ReturnsFixedCount()
    {
        // Arrange: cleanup service reports 3 items, all with null error = success
        var report = new List<(string Path, string Action, string? Error)>
        {
            (@"C:\path1", "Fixed", null),
            (@"C:\path2", "Fixed", null),
            (@"C:\path3", "Fixed", null)
        };
        _aclCleanupService
            .Setup(s => s.CleanupAclReferencesAsync(It.IsAny<List<string>>(), It.IsAny<IProgress<AclCleanupProgress>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(report);
        var manager = CreateManager();

        // Act
        var (fixedCount, error) = await manager.CleanupAclReferencesAsync([Sid], null, CancellationToken.None);

        // Assert
        Assert.Equal(3, fixedCount);
        Assert.Null(error);
    }

    [Fact]
    public async Task CleanupAclReferencesAsync_SomeItemsFailed_FixedCountExcludesFailed()
    {
        // Arrange: 2 succeeded, 1 failed
        var report = new List<(string Path, string Action, string? Error)>
        {
            (@"C:\path1", "Fixed", null),
            (@"C:\path2", "Fixed", null),
            (@"C:\path3", "Error", "Access denied")
        };
        _aclCleanupService
            .Setup(s => s.CleanupAclReferencesAsync(It.IsAny<List<string>>(), It.IsAny<IProgress<AclCleanupProgress>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(report);
        var manager = CreateManager();

        // Act
        var (fixedCount, error) = await manager.CleanupAclReferencesAsync([Sid], null, CancellationToken.None);

        // Assert
        Assert.Equal(2, fixedCount);
        Assert.Null(error);
    }

    [Fact]
    public async Task CleanupAclReferencesAsync_Cancelled_Returns0WithNullError()
    {
        // Arrange: service throws OperationCanceledException
        _aclCleanupService
            .Setup(s => s.CleanupAclReferencesAsync(It.IsAny<List<string>>(), It.IsAny<IProgress<AclCleanupProgress>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var manager = CreateManager();

        // Act
        var (fixedCount, error) = await manager.CleanupAclReferencesAsync([Sid], null, CancellationToken.None);

        // Assert: cancellation is treated as expected — no error surfaced
        Assert.Equal(0, fixedCount);
        Assert.Null(error);
    }

    [Fact]
    public async Task CleanupAclReferencesAsync_ServiceThrowsUnexpected_ReturnsErrorMessage()
    {
        // Arrange
        _aclCleanupService
            .Setup(s => s.CleanupAclReferencesAsync(It.IsAny<List<string>>(), It.IsAny<IProgress<AclCleanupProgress>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Unexpected ACL error"));
        var manager = CreateManager();

        // Act
        var (fixedCount, error) = await manager.CleanupAclReferencesAsync([Sid], null, CancellationToken.None);

        // Assert
        Assert.Equal(0, fixedCount);
        Assert.NotNull(error);
        Assert.Contains("Unexpected ACL error", error);
    }
}
