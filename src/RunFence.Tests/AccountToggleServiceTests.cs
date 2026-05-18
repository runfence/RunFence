using Moq;
using RunFence.Account;
using RunFence.Acl;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Licensing;
using Xunit;

namespace RunFence.Tests;

public class AccountToggleServiceTests
{
    private const string Sid = "S-1-5-21-100-200-300-1001";
    private const string Username = "testuser";

    private readonly Mock<IAccountLoginRestrictionService> _accountRestriction = new();
    private readonly Mock<IGroupPolicyScriptHelper> _groupPolicyScriptHelper = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<ILicenseService> _licenseService = new();
    private readonly Mock<IAccountFirewallToggle> _firewallToggle = new();
    private readonly Mock<IPathGrantService> _pathGrantService = new();
    private readonly AppDatabase _database = new();

    private AccountToggleService CreateService()
    {
        var session = new SessionContext
        {
            Database = _database,
            CredentialStore = new CredentialStore()
        };

        return new(
            _accountRestriction.Object,
            _groupPolicyScriptHelper.Object,
            _log.Object,
            _licenseService.Object,
            _firewallToggle.Object,
            new LambdaSessionProvider(() => session),
            _pathGrantService.Object);
    }

    // ── SetLogonBlocked ──────────────────────────────────────────────────────

    [Fact]
    public void SetLogonBlocked_Blocked_WithScriptPath_CallsUpdateFromPath()
    {
        // Arrange
        const string scriptPath = @"C:\Windows\System32\GroupPolicy\scripts\block.cmd";
        _licenseService.Setup(l => l.CanHideAccount(It.IsAny<int>())).Returns(true);
        _accountRestriction.Setup(r => r.SetLoginBlockedBySid(Sid, Username, true))
            .Returns(new SetLoginBlockedResult(scriptPath, null));

        // Act
        var result = CreateService().SetLogonBlocked(Sid, Username, blocked: true);

        // Assert
        Assert.True(result.Success);
        _pathGrantService.Verify(g => g.UpdateFromPath(scriptPath, Sid), Times.Once);
    }

    [Fact]
    public void SetLogonBlocked_Blocked_WithScriptPathAndTraversePaths_TracksTraverseInDatabase()
    {
        // Arrange
        const string scriptPath = @"C:\Windows\System32\GroupPolicy\scripts\Startup\block.cmd";
        var scriptsDir = Path.GetDirectoryName(scriptPath)!;
        var traversePaths = new List<string> { scriptsDir, Path.GetDirectoryName(scriptsDir)! };
        _licenseService.Setup(l => l.CanHideAccount(It.IsAny<int>())).Returns(true);
        _accountRestriction.Setup(r => r.SetLoginBlockedBySid(Sid, Username, true))
            .Returns(new SetLoginBlockedResult(scriptPath, traversePaths));

        // Act
        CreateService().SetLogonBlocked(Sid, Username, blocked: true);

        // Assert: traverse entry tracked in the database for scriptsDir
        var grants = _database.GetAccount(Sid)?.Grants;
        Assert.NotNull(grants);
        Assert.Contains(grants, e => e.IsTraverseOnly &&
            string.Equals(Path.GetFullPath(e.Path), Path.GetFullPath(scriptsDir), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SetLogonBlocked_Blocked_NoScriptPath_DoesNotCallPathGrantService()
    {
        // Arrange
        _licenseService.Setup(l => l.CanHideAccount(It.IsAny<int>())).Returns(true);
        _accountRestriction.Setup(r => r.SetLoginBlockedBySid(Sid, Username, true))
            .Returns(new SetLoginBlockedResult(null, null));

        // Act
        var result = CreateService().SetLogonBlocked(Sid, Username, blocked: true);

        // Assert
        Assert.True(result.Success);
        _pathGrantService.Verify(g => g.UpdateFromPath(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
        _pathGrantService.Verify(g => g.UntrackGrant(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void SetLogonBlocked_Unblocked_WithScriptPath_CallsRemoveGrant()
    {
        // Arrange
        const string scriptPath = @"C:\Windows\System32\GroupPolicy\scripts\block.cmd";
        _accountRestriction.Setup(r => r.SetLoginBlockedBySid(Sid, Username, false))
            .Returns(new SetLoginBlockedResult(scriptPath, null));

        // Act
        var result = CreateService().SetLogonBlocked(Sid, Username, blocked: false);

        // Assert: tracking is removed without touching NTFS (ACEs already removed by restriction service)
        Assert.True(result.Success);
        _pathGrantService.Verify(g => g.UntrackGrant(Sid, scriptPath, false), Times.Once);
    }

    [Fact]
    public void SetLogonBlocked_Unblocked_WithScriptPath_CallsRemoveTraverseAndCleanupForScriptsDir()
    {
        // Arrange
        const string scriptPath = @"C:\Windows\System32\GroupPolicy\scripts\Startup\block.cmd";
        var scriptsDir = Path.GetDirectoryName(scriptPath)!;

        _accountRestriction.Setup(r => r.SetLoginBlockedBySid(Sid, Username, false))
            .Returns(new SetLoginBlockedResult(scriptPath, null));

        // Act
        CreateService().SetLogonBlocked(Sid, Username, blocked: false);

        // Assert: traverse entry for scriptsDir is untracked (ACEs already reverted)
        _pathGrantService.Verify(g => g.UntrackTraverse(Sid, scriptsDir), Times.Once);
        // Assert: orphaned ancestor traverse entries are also cleaned up
        _pathGrantService.Verify(g => g.CleanupOrphanedTraverse(Sid, scriptsDir), Times.Once);
    }

    [Fact]
    public void SetLogonBlocked_Unblocked_NoScriptPath_DoesNotCallPathGrantService()
    {
        // Arrange
        _accountRestriction.Setup(r => r.SetLoginBlockedBySid(Sid, Username, false))
            .Returns(new SetLoginBlockedResult(null, null));

        // Act
        var result = CreateService().SetLogonBlocked(Sid, Username, blocked: false);

        // Assert
        Assert.True(result.Success);
        _pathGrantService.Verify(g => g.UpdateFromPath(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
        _pathGrantService.Verify(g => g.UntrackGrant(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void SetLogonBlocked_Blocked_PassesCorrectHiddenCountToLicenseCheck()
    {
        // Arrange: two existing hidden accounts identified via IsLoginBlockedBySid
        const string hiddenSid1 = "S-1-5-21-100-200-300-1002";
        const string hiddenSid2 = "S-1-5-21-100-200-300-1003";

        var session = new SessionContext
        {
            Database = _database,
            CredentialStore = new CredentialStore
            {
                Credentials =
                [
                    new CredentialEntry { Sid = hiddenSid1 },
                    new CredentialEntry { Sid = hiddenSid2 }
                ]
            }
        };

        _accountRestriction.Setup(r => r.IsLoginBlockedBySid(hiddenSid1)).Returns(true);
        _accountRestriction.Setup(r => r.IsLoginBlockedBySid(hiddenSid2)).Returns(true);
        _accountRestriction.Setup(r => r.IsLoginBlockedBySid(It.Is<string>(s => s != hiddenSid1 && s != hiddenSid2))).Returns(false);

        int? capturedCount = null;
        _licenseService
            .Setup(l => l.CanHideAccount(It.IsAny<int>()))
            .Callback<int>(c => capturedCount = c)
            .Returns(true);
        _accountRestriction.Setup(r => r.SetLoginBlockedBySid(Sid, Username, true))
            .Returns(new SetLoginBlockedResult(null, null));

        var service = new AccountToggleService(
            _accountRestriction.Object,
            _groupPolicyScriptHelper.Object,
            _log.Object,
            _licenseService.Object,
            _firewallToggle.Object,
            new LambdaSessionProvider(() => session),
            _pathGrantService.Object);

        // Act
        service.SetLogonBlocked(Sid, Username, blocked: true);

        // Assert: count is 2 (only the pre-existing hidden accounts, not the one being hidden now)
        Assert.Equal(2, capturedCount);
    }

    [Fact]
    public void SetLogonBlocked_Blocked_LicenseLimitExceeded_ReturnsFalseWithoutCallingRestrictionService()
    {
        // Arrange: license service rejects at the current hidden count
        _licenseService.Setup(l => l.CanHideAccount(It.IsAny<int>())).Returns(false);
        _licenseService.Setup(l => l.GetRestrictionMessage(EvaluationFeature.HiddenAccounts, It.IsAny<int>()))
            .Returns("License limit reached");

        // Act
        var result = CreateService().SetLogonBlocked(Sid, Username, blocked: true);

        // Assert: blocked by license, restriction service never called
        Assert.False(result.Success);
        Assert.True(result.IsLicenseLimit);
        _accountRestriction.Verify(r => r.SetLoginBlockedBySid(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void SetLogonBlocked_RestrictionServiceThrowsWin32Exception_ReturnsSuccessFalseWithMessage()
    {
        // Arrange — R2_TL3: restriction service throws Win32Exception; SetLogonBlocked catches it and returns Success=false
        _licenseService.Setup(l => l.CanHideAccount(It.IsAny<int>())).Returns(true);
        _accountRestriction.Setup(r => r.SetLoginBlockedBySid(Sid, Username, true))
            .Throws(new System.ComponentModel.Win32Exception(5, "Access is denied"));

        // Act
        var result = CreateService().SetLogonBlocked(Sid, Username, blocked: true);

        // Assert: exception caught → result has Success=false and non-null error message
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("Access is denied", result.ErrorMessage);
    }

    [Fact]
    public void RestoreLogonState_TracksGroupPolicyGrantStateAndHiddenState()
    {
        const string scriptPath = @"C:\Windows\System32\GroupPolicy\scripts\Startup\block.cmd";
        var scriptsDir = Path.GetDirectoryName(scriptPath)!;
        var traversePaths = new List<string> { scriptsDir, Path.GetDirectoryName(scriptsDir)! };
        _groupPolicyScriptHelper.Setup(g => g.SetLoginBlocked(Sid, true))
            .Returns(new SetLoginBlockedResult(scriptPath, traversePaths));

        CreateService().RestoreLogonState(Sid, Username, groupPolicyBlocked: true, hiddenBlocked: false);

        _pathGrantService.Verify(g => g.UpdateFromPath(scriptPath, Sid), Times.Once);
        _accountRestriction.Verify(r => r.RestoreAccountHiddenState(Username, false), Times.Once);
        Assert.Contains(_database.GetAccount(Sid)!.Grants, g => g.IsTraverseOnly &&
            string.Equals(Path.GetFullPath(g.Path), Path.GetFullPath(scriptsDir), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RestoreLogonState_Unblocked_RemovesGrantTracking()
    {
        const string scriptPath = @"C:\Windows\System32\GroupPolicy\scripts\Startup\block.cmd";
        var scriptsDir = Path.GetDirectoryName(scriptPath)!;
        _groupPolicyScriptHelper.Setup(g => g.SetLoginBlocked(Sid, false))
            .Returns(new SetLoginBlockedResult(scriptPath, null));

        CreateService().RestoreLogonState(Sid, Username, groupPolicyBlocked: false, hiddenBlocked: false);

        _pathGrantService.Verify(g => g.UntrackGrant(Sid, scriptPath, false), Times.Once);
        _pathGrantService.Verify(g => g.UntrackTraverse(Sid, scriptsDir), Times.Once);
        _pathGrantService.Verify(g => g.CleanupOrphanedTraverse(Sid, scriptsDir), Times.Once);
        _accountRestriction.Verify(r => r.RestoreAccountHiddenState(Username, false), Times.Once);
    }

    [Fact]
    public void SetLogonBlocked_WhenGrantTrackingThrows_RollsBackAndReturnsRolledBack()
    {
        const string scriptPath = @"C:\Windows\System32\GroupPolicy\scripts\block.cmd";
        _licenseService.Setup(l => l.CanHideAccount(It.IsAny<int>())).Returns(true);
        _accountRestriction.Setup(r => r.IsAccountHidden(Username)).Returns(false);
        _accountRestriction.Setup(r => r.SetLoginBlockedBySid(Sid, Username, true))
            .Returns(new SetLoginBlockedResult(scriptPath, null));
        _groupPolicyScriptHelper.Setup(g => g.IsLoginBlocked(Sid)).Returns(false);
        _pathGrantService.Setup(g => g.UpdateFromPath(scriptPath, Sid))
            .Throws(new InvalidOperationException("db failed"));
        _groupPolicyScriptHelper.Setup(g => g.SetLoginBlocked(Sid, false))
            .Returns(new SetLoginBlockedResult(scriptPath, null));

        var result = CreateService().SetLogonBlocked(Sid, Username, blocked: true);

        Assert.False(result.Success);
        Assert.Equal(AccountRestrictionStatus.RolledBack, result.FailureStatus);
        Assert.True(result.RollbackAttempted);
        _groupPolicyScriptHelper.Verify(g => g.SetLoginBlocked(Sid, false), Times.Once);
        _accountRestriction.Verify(r => r.RestoreAccountHiddenState(Username, false), Times.Once);
    }

    [Fact]
    public void SetLogonBlocked_Unblocked_UntrackWarnings_AreLogged()
    {
        const string scriptPath = @"C:\Windows\System32\GroupPolicy\scripts\Startup\block.cmd";
        var scriptsDir = Path.GetDirectoryName(scriptPath)!;
        var grantWarning = new GrantApplyWarning(
            GrantApplyFailureStep.UntrackGrantSave,
            scriptPath,
            null,
            new InvalidOperationException("grant save failed"));
        var traverseWarning = new GrantApplyWarning(
            GrantApplyFailureStep.UntrackTraverseSave,
            scriptsDir,
            null,
            new InvalidOperationException("traverse save failed"));
        _accountRestriction.Setup(r => r.SetLoginBlockedBySid(Sid, Username, false))
            .Returns(new SetLoginBlockedResult(scriptPath, null));
        _pathGrantService.Setup(g => g.UntrackGrant(Sid, scriptPath, false))
            .Returns(new GrantApplyResult(
                DatabaseModified: true,
                DurableSaveCompleted: false,
                Warnings: [grantWarning]));
        _pathGrantService.Setup(g => g.UntrackTraverse(Sid, scriptsDir))
            .Returns(new GrantApplyResult(
                DatabaseModified: true,
                DurableSaveCompleted: false,
                Warnings: [traverseWarning]));

        var result = CreateService().SetLogonBlocked(Sid, Username, blocked: false);

        Assert.True(result.Success);
        _log.Verify(l => l.Warn(It.Is<string>(s => s.Contains(GrantApplyFailureFormatter.Format(grantWarning)))), Times.Once);
        _log.Verify(l => l.Warn(It.Is<string>(s => s.Contains(GrantApplyFailureFormatter.Format(traverseWarning)))), Times.Once);
    }

    // ── SetAllowInternet ─────────────────────────────────────────────────────

    [Fact]
    public void SetAllowInternet_DelegatesToFirewallToggle_PassesExistingSettings()
    {
        // Arrange: existing firewall settings on account
        var existingFirewall = new FirewallAccountSettings { AllowInternet = true, AllowLan = false };
        _database.GetOrCreateAccount(Sid).Firewall = existingFirewall;
        _firewallToggle.Setup(f => f.SetAllowInternet(Sid, false, existingFirewall)).Returns(new SetAllowInternetResult(null));

        // Act
        var result = CreateService().SetAllowInternet(Sid, allowInternet: false);

        // Assert: delegates to IAccountFirewallToggle with the existing settings
        Assert.Null(result.Message);
        _firewallToggle.Verify(f => f.SetAllowInternet(Sid, false, existingFirewall), Times.Once);
    }

    [Fact]
    public void SetAllowInternet_NoExistingAccount_PassesNullSettings()
    {
        // Arrange: no account entry — existing is null
        _firewallToggle.Setup(f => f.SetAllowInternet(Sid, true, null)).Returns(new SetAllowInternetResult(null));

        // Act
        var result = CreateService().SetAllowInternet(Sid, allowInternet: true);

        // Assert: null existing settings passed when no account
        Assert.Null(result.Message);
        _firewallToggle.Verify(f => f.SetAllowInternet(Sid, true, null), Times.Once);
    }

    [Fact]
    public void SetAllowInternet_FirewallToggleReturnsError_PropagatesError()
    {
        // Arrange: firewall toggle returns an error message
        _firewallToggle.Setup(f => f.SetAllowInternet(Sid, false, null)).Returns(new SetAllowInternetResult("firewall unavailable"));

        // Act
        var result = CreateService().SetAllowInternet(Sid, allowInternet: false);

        // Assert: error propagated from toggle
        Assert.Equal("firewall unavailable", result.Message);
    }
}
