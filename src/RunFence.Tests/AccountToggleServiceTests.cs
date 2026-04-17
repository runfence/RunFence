using System.ComponentModel;
using Moq;
using RunFence.Account;
using RunFence.Acl;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Infrastructure;
using RunFence.Licensing;
using Xunit;

namespace RunFence.Tests;

public class AccountToggleServiceTests
{
    private const string Sid = "S-1-5-21-100-200-300-1001";
    private const string Username = "testuser";

    private readonly Mock<IAccountLoginRestrictionService> _accountRestriction = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<ILicenseService> _licenseService = new();
    private readonly Mock<IAccountFirewallSettingsApplier> _firewallSettingsApplier = new();
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
            _log.Object,
            _licenseService.Object,
            _firewallSettingsApplier.Object,
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
        _accountRestriction.Setup(r => r.IsLoginBlockedBySid(It.IsAny<string>())).Returns(false);
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
        _accountRestriction.Setup(r => r.IsLoginBlockedBySid(It.IsAny<string>())).Returns(false);
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
        _accountRestriction.Setup(r => r.IsLoginBlockedBySid(It.IsAny<string>())).Returns(false);
        _accountRestriction.Setup(r => r.SetLoginBlockedBySid(Sid, Username, true))
            .Returns(new SetLoginBlockedResult(null, null));

        // Act
        var result = CreateService().SetLogonBlocked(Sid, Username, blocked: true);

        // Assert
        Assert.True(result.Success);
        _pathGrantService.Verify(g => g.UpdateFromPath(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
        _pathGrantService.Verify(g => g.RemoveGrant(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
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

        // Assert: RemoveGrant called with updateFileSystem=false (ACEs already removed by restriction service)
        Assert.True(result.Success);
        _pathGrantService.Verify(g => g.RemoveGrant(Sid, scriptPath, false, false), Times.Once);
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

        // Assert: traverse entry for scriptsDir is removed (updateFileSystem=false since ACEs already reverted)
        _pathGrantService.Verify(g => g.RemoveTraverse(Sid, scriptsDir, false), Times.Once);
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
        _pathGrantService.Verify(g => g.RemoveGrant(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
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
        _accountRestriction.Setup(r => r.IsLoginBlockedBySid(It.IsAny<string>())).Returns(false);
        _accountRestriction.Setup(r => r.SetLoginBlockedBySid(Sid, Username, true))
            .Throws(new System.ComponentModel.Win32Exception(5, "Access is denied"));

        // Act
        var result = CreateService().SetLogonBlocked(Sid, Username, blocked: true);

        // Assert: exception caught → result has Success=false and non-null error message
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("Access is denied", result.ErrorMessage);
    }

    // ── SetAllowInternet ─────────────────────────────────────────────────────

    [Fact]
    public void SetAllowInternet_AccountRuleFailure_RestoresPreviousSettingsInMemory()
    {
        var account = _database.GetOrCreateAccount(Sid);
        account.Firewall = new FirewallAccountSettings
        {
            AllowInternet = true,
            AllowLan = false
        };

        FirewallAccountSettings? previousSettings = null;
        FirewallAccountSettings? appliedSettings = null;
        _firewallSettingsApplier
            .Setup(f => f.ApplyAccountFirewallSettings(
                Sid,
                Username,
                It.IsAny<FirewallAccountSettings?>(),
                It.IsAny<FirewallAccountSettings>(),
                _database))
            .Callback<string, string, FirewallAccountSettings?, FirewallAccountSettings, AppDatabase>(
                (_, _, previous, settings, _) =>
                {
                    previousSettings = previous;
                    appliedSettings = settings;
                })
            .Throws(new FirewallApplyException(
                FirewallApplyPhase.AccountRules,
                Sid,
                new InvalidOperationException("firewall unavailable")));

        var error = CreateService().SetAllowInternet(Sid, Username, allowInternet: false);

        Assert.Equal("firewall unavailable", error);
        Assert.NotNull(previousSettings);
        Assert.NotNull(appliedSettings);
        Assert.True(previousSettings.AllowInternet);
        Assert.False(previousSettings.AllowLan);
        Assert.False(appliedSettings.AllowInternet);
        Assert.True(_database.GetAccount(Sid)!.Firewall.AllowInternet);
        Assert.False(_database.GetAccount(Sid)!.Firewall.AllowLan);
    }

    [Fact]
    public void SetAllowInternet_GlobalIcmpFailure_KeepsNewSettingsInMemory()
    {
        _database.GetOrCreateAccount(Sid).Firewall = new FirewallAccountSettings
        {
            AllowInternet = true
        };

        _firewallSettingsApplier
            .Setup(f => f.ApplyAccountFirewallSettings(
                Sid,
                Username,
                It.IsAny<FirewallAccountSettings?>(),
                It.IsAny<FirewallAccountSettings>(),
                _database))
            .Throws(new FirewallApplyException(
                FirewallApplyPhase.GlobalIcmp,
                Sid,
                new InvalidOperationException("global icmp unavailable")));

        var error = CreateService().SetAllowInternet(Sid, Username, allowInternet: false);

        Assert.Equal("global icmp unavailable", error);
        Assert.False(_database.GetAccount(Sid)!.Firewall.AllowInternet);
    }
}
