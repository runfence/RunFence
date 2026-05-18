using Moq;
using RunFence.Account;
using Xunit;

namespace RunFence.Tests;

public class AccountRestrictionCoordinatorTests
{
    private const string SourceSid = "S-1-5-21-0-0-0-1001";
    private const string TargetSid = "S-1-5-21-0-0-0-1002";
    private const string Username = "user1";
    private const string TargetUsername = "user2";

    [Fact]
    public void ApplyRestrictions_WhenLaterStepFails_LeavesEarlierSuccessfulRestrictionsApplied()
    {
        var toggle = new Mock<IAccountToggleService>();
        var lifecycle = new Mock<IAccountLifecycleManager>();
        var login = new Mock<IAccountLoginRestrictionService>();
        var lsa = new Mock<IAccountLsaRestrictionService>();
        var gp = new Mock<IGroupPolicyScriptHelper>();

        toggle.Setup(t => t.SetLogonBlocked(SourceSid, Username, false))
            .Returns(new SetLogonBlockedResult(true, null));
        gp.Setup(g => g.IsLoginBlocked(SourceSid)).Returns(true);
        login.Setup(l => l.IsAccountHidden(Username)).Returns(false);
        lsa.Setup(l => l.CaptureSnapshot(SourceSid))
            .Returns(new AccountLsaRestrictionSnapshot(false, false, false, false));
        lsa.Setup(l => l.SetNoBgAutostartBySid(SourceSid, true))
            .Throws(new InvalidOperationException("bg failed"));

        var coordinator = new AccountRestrictionCoordinator(toggle.Object, lifecycle.Object, login.Object, lsa.Object, gp.Object);

        var result = coordinator.ApplyRestrictions(SourceSid, Username, logonBlocked: false, networkLoginBlocked: false, backgroundAutorunBlocked: true);

        var logonEntry = Assert.Single(result.Entries, e => e.Restriction == AccountRestrictionKind.HideLogon);
        Assert.Equal(AccountRestrictionStatus.Succeeded, logonEntry.Status);
        Assert.False(logonEntry.RollbackAttempted);

        AssertEntry(result, AccountRestrictionKind.LogonScript, AccountRestrictionStatus.Succeeded, rollbackAttempted: false);
        AssertEntry(result, AccountRestrictionKind.NetworkLogin, AccountRestrictionStatus.Succeeded, rollbackAttempted: false);
        AssertEntry(result, AccountRestrictionKind.BackgroundAutorun, AccountRestrictionStatus.Failed, rollbackAttempted: false, "bg failed");
        AssertEntry(result, AccountRestrictionKind.LsaPolicy, AccountRestrictionStatus.Failed, rollbackAttempted: false, "BackgroundAutorun: bg failed");

        toggle.Verify(t => t.RestoreLogonState(SourceSid, Username, It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
        lsa.Verify(l => l.RestoreLocalOnlyState(SourceSid, It.IsAny<AccountLsaRestrictionSnapshot>()), Times.Never);
        lsa.Verify(l => l.RestoreNoBgAutostartState(SourceSid, It.IsAny<AccountLsaRestrictionSnapshot>()), Times.Never);
        gp.Verify(g => g.SetLoginBlocked(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        login.Verify(l => l.SetAccountHidden(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void ApplyRestrictions_WhenBackgroundStepFails_ReportsExactPartialSuccessStatuses()
    {
        var toggle = new Mock<IAccountToggleService>();
        var lifecycle = new Mock<IAccountLifecycleManager>();
        var login = new Mock<IAccountLoginRestrictionService>();
        var lsa = new Mock<IAccountLsaRestrictionService>();
        var gp = new Mock<IGroupPolicyScriptHelper>();

        toggle.Setup(t => t.SetLogonBlocked(SourceSid, Username, true))
            .Returns(new SetLogonBlockedResult(true, null));
        gp.Setup(g => g.IsLoginBlocked(SourceSid)).Returns(false);
        login.Setup(l => l.IsAccountHidden(Username)).Returns(false);
        lsa.Setup(l => l.CaptureSnapshot(SourceSid))
            .Returns(new AccountLsaRestrictionSnapshot(false, false, false, false));
        lsa.Setup(l => l.SetNoBgAutostartBySid(SourceSid, true))
            .Throws(new InvalidOperationException("bg failed"));

        var coordinator = new AccountRestrictionCoordinator(toggle.Object, lifecycle.Object, login.Object, lsa.Object, gp.Object);

        var result = coordinator.ApplyRestrictions(SourceSid, Username, logonBlocked: true, networkLoginBlocked: true, backgroundAutorunBlocked: true);

        AssertEntry(result, AccountRestrictionKind.HideLogon, AccountRestrictionStatus.Succeeded, rollbackAttempted: false);
        AssertEntry(result, AccountRestrictionKind.LogonScript, AccountRestrictionStatus.Succeeded, rollbackAttempted: false);
        AssertEntry(result, AccountRestrictionKind.NetworkLogin, AccountRestrictionStatus.Succeeded, rollbackAttempted: false);
        AssertEntry(result, AccountRestrictionKind.BackgroundAutorun, AccountRestrictionStatus.Failed, rollbackAttempted: false, "bg failed");
        AssertEntry(result, AccountRestrictionKind.LsaPolicy, AccountRestrictionStatus.Failed, rollbackAttempted: false, "BackgroundAutorun: bg failed");
    }

    [Fact]
    public void ApplyRestrictions_WhenSingleLsaActionRollsBack_OnlyThatActionIsReportedRolledBack()
    {
        var toggle = new Mock<IAccountToggleService>();
        var lifecycle = new Mock<IAccountLifecycleManager>();
        var login = new Mock<IAccountLoginRestrictionService>();
        var lsa = new Mock<IAccountLsaRestrictionService>();
        var gp = new Mock<IGroupPolicyScriptHelper>();

        toggle.Setup(t => t.SetLogonBlocked(SourceSid, Username, true))
            .Returns(new SetLogonBlockedResult(true, null));
        lsa.Setup(l => l.SetNoBgAutostartBySid(SourceSid, true))
            .Throws(new AccountRestrictionOperationException(
                "bg failed",
                AccountRestrictionStatus.RolledBack,
                rollbackAttempted: true));

        var coordinator = new AccountRestrictionCoordinator(toggle.Object, lifecycle.Object, login.Object, lsa.Object, gp.Object);

        var result = coordinator.ApplyRestrictions(SourceSid, Username, logonBlocked: true, networkLoginBlocked: true, backgroundAutorunBlocked: true);

        AssertEntry(result, AccountRestrictionKind.HideLogon, AccountRestrictionStatus.Succeeded, rollbackAttempted: false);
        AssertEntry(result, AccountRestrictionKind.LogonScript, AccountRestrictionStatus.Succeeded, rollbackAttempted: false);
        AssertEntry(result, AccountRestrictionKind.NetworkLogin, AccountRestrictionStatus.Succeeded, rollbackAttempted: false);
        AssertEntry(result, AccountRestrictionKind.BackgroundAutorun, AccountRestrictionStatus.RolledBack, rollbackAttempted: true, "bg failed");
        AssertEntry(result, AccountRestrictionKind.LsaPolicy, AccountRestrictionStatus.RolledBack, rollbackAttempted: true);
    }

    [Fact]
    public void MigrateRestrictions_TargetApplyFailure_DoesNotRemoveSourceRestrictions()
    {
        var toggle = new Mock<IAccountToggleService>();
        var lifecycle = new Mock<IAccountLifecycleManager>();
        var login = new Mock<IAccountLoginRestrictionService>();
        var lsa = new Mock<IAccountLsaRestrictionService>();
        var gp = new Mock<IGroupPolicyScriptHelper>();

        gp.Setup(g => g.IsLoginBlocked(SourceSid)).Returns(true);
        login.Setup(l => l.IsAccountHidden(Username)).Returns(true);
        lsa.Setup(l => l.CaptureSnapshot(SourceSid))
            .Returns(new AccountLsaRestrictionSnapshot(true, true, true, true));

        gp.Setup(g => g.IsLoginBlocked(TargetSid)).Returns(false);
        login.Setup(l => l.IsAccountHidden(TargetUsername)).Returns(false);
        lsa.Setup(l => l.CaptureSnapshot(TargetSid))
            .Returns(new AccountLsaRestrictionSnapshot(false, false, false, false));

        toggle.Setup(t => t.SetLogonBlocked(TargetSid, TargetUsername, true))
            .Returns(new SetLogonBlockedResult(true, null));
        lsa.Setup(l => l.SetNoBgAutostartBySid(TargetSid, true))
            .Throws(new InvalidOperationException("target failed"));

        var coordinator = new AccountRestrictionCoordinator(toggle.Object, lifecycle.Object, login.Object, lsa.Object, gp.Object);

        var result = coordinator.MigrateRestrictions(SourceSid, Username, TargetSid, TargetUsername);

        AssertEntry(result, AccountRestrictionKind.HideLogon, AccountRestrictionStatus.Succeeded, rollbackAttempted: false);
        AssertEntry(result, AccountRestrictionKind.LogonScript, AccountRestrictionStatus.Succeeded, rollbackAttempted: false);
        AssertEntry(result, AccountRestrictionKind.NetworkLogin, AccountRestrictionStatus.Succeeded, rollbackAttempted: false);
        AssertEntry(result, AccountRestrictionKind.BackgroundAutorun, AccountRestrictionStatus.Failed, rollbackAttempted: false, "target failed");
        AssertEntry(result, AccountRestrictionKind.LsaPolicy, AccountRestrictionStatus.Failed, rollbackAttempted: false, "BackgroundAutorun: target failed");
        toggle.Verify(t => t.SetLogonBlocked(SourceSid, Username, It.IsAny<bool>()), Times.Never);
        lsa.Verify(l => l.SetLocalOnlyBySid(SourceSid, false), Times.Never);
        lsa.Verify(l => l.SetNoBgAutostartBySid(SourceSid, false), Times.Never);
        toggle.Verify(t => t.RestoreLogonState(TargetSid, TargetUsername, It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
        lsa.Verify(l => l.RestoreLocalOnlyState(TargetSid, It.IsAny<AccountLsaRestrictionSnapshot>()), Times.Never);
        lsa.Verify(l => l.RestoreNoBgAutostartState(TargetSid, It.IsAny<AccountLsaRestrictionSnapshot>()), Times.Never);
    }

    [Fact]
    public void MigrateRestrictions_SourceCleanupFailure_LeavesTargetProtected()
    {
        var toggle = new Mock<IAccountToggleService>();
        var lifecycle = new Mock<IAccountLifecycleManager>();
        var login = new Mock<IAccountLoginRestrictionService>();
        var lsa = new Mock<IAccountLsaRestrictionService>();
        var gp = new Mock<IGroupPolicyScriptHelper>();

        gp.Setup(g => g.IsLoginBlocked(SourceSid)).Returns(true);
        login.Setup(l => l.IsAccountHidden(Username)).Returns(true);
        lsa.Setup(l => l.CaptureSnapshot(SourceSid))
            .Returns(new AccountLsaRestrictionSnapshot(true, true, false, false));

        gp.Setup(g => g.IsLoginBlocked(TargetSid)).Returns(false);
        login.Setup(l => l.IsAccountHidden(TargetUsername)).Returns(false);
        lsa.Setup(l => l.CaptureSnapshot(TargetSid))
            .Returns(new AccountLsaRestrictionSnapshot(false, false, false, false));

        toggle.Setup(t => t.SetLogonBlocked(TargetSid, TargetUsername, true))
            .Returns(new SetLogonBlockedResult(true, null));
        toggle.Setup(t => t.SetLogonBlocked(SourceSid, Username, false))
            .Returns(new SetLogonBlockedResult(true, null));
        lsa.Setup(l => l.SetLocalOnlyBySid(SourceSid, false))
            .Throws(new InvalidOperationException("source cleanup failed"));

        var coordinator = new AccountRestrictionCoordinator(toggle.Object, lifecycle.Object, login.Object, lsa.Object, gp.Object);

        var result = coordinator.MigrateRestrictions(SourceSid, Username, TargetSid, TargetUsername);

        Assert.Contains(result.Entries, e =>
            e.Restriction == AccountRestrictionKind.NetworkLogin &&
            e.Status == AccountRestrictionStatus.Failed &&
            e.Error == "source cleanup failed");
        toggle.Verify(t => t.RestoreLogonState(TargetSid, TargetUsername, It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void MigrateRestrictions_SourcePartialLsaState_FailsClosedOnTarget()
    {
        var toggle = new Mock<IAccountToggleService>();
        var lifecycle = new Mock<IAccountLifecycleManager>();
        var login = new Mock<IAccountLoginRestrictionService>();
        var lsa = new Mock<IAccountLsaRestrictionService>();
        var gp = new Mock<IGroupPolicyScriptHelper>();

        gp.Setup(g => g.IsLoginBlocked(SourceSid)).Returns(false);
        login.Setup(l => l.IsAccountHidden(Username)).Returns(false);
        lsa.Setup(l => l.CaptureSnapshot(SourceSid))
            .Returns(new AccountLsaRestrictionSnapshot(true, false, false, true));

        gp.Setup(g => g.IsLoginBlocked(TargetSid)).Returns(false);
        login.Setup(l => l.IsAccountHidden(TargetUsername)).Returns(false);
        lsa.Setup(l => l.CaptureSnapshot(TargetSid))
            .Returns(new AccountLsaRestrictionSnapshot(false, false, false, false));

        toggle.Setup(t => t.SetLogonBlocked(TargetSid, TargetUsername, false))
            .Returns(new SetLogonBlockedResult(true, null));
        toggle.Setup(t => t.SetLogonBlocked(SourceSid, Username, false))
            .Returns(new SetLogonBlockedResult(true, null));

        var coordinator = new AccountRestrictionCoordinator(toggle.Object, lifecycle.Object, login.Object, lsa.Object, gp.Object);

        _ = coordinator.MigrateRestrictions(SourceSid, Username, TargetSid, TargetUsername);

        lsa.Verify(l => l.SetLocalOnlyBySid(TargetSid, true), Times.Once);
        lsa.Verify(l => l.SetNoBgAutostartBySid(TargetSid, true), Times.Once);
    }

    [Fact]
    public void ApplyRestrictions_WhenLogonFails_StillAttemptsLaterStepsAndReportsEachFailure()
    {
        var toggle = new Mock<IAccountToggleService>();
        var lifecycle = new Mock<IAccountLifecycleManager>();
        var login = new Mock<IAccountLoginRestrictionService>();
        var lsa = new Mock<IAccountLsaRestrictionService>();
        var gp = new Mock<IGroupPolicyScriptHelper>();

        gp.Setup(g => g.IsLoginBlocked(SourceSid)).Returns(false);
        login.Setup(l => l.IsAccountHidden(Username)).Returns(false);
        lsa.Setup(l => l.CaptureSnapshot(SourceSid))
            .Returns(new AccountLsaRestrictionSnapshot(false, false, false, false));
        toggle.Setup(t => t.SetLogonBlocked(SourceSid, Username, true))
            .Returns(new SetLogonBlockedResult(false, "hide failed"));
        lsa.Setup(l => l.SetLocalOnlyBySid(SourceSid, true))
            .Throws(new InvalidOperationException("network failed"));
        lsa.Setup(l => l.SetNoBgAutostartBySid(SourceSid, true))
            .Throws(new InvalidOperationException("bg failed"));

        var coordinator = new AccountRestrictionCoordinator(toggle.Object, lifecycle.Object, login.Object, lsa.Object, gp.Object);

        var result = coordinator.ApplyRestrictions(SourceSid, Username, logonBlocked: true, networkLoginBlocked: true, backgroundAutorunBlocked: true);

        AssertEntry(result, AccountRestrictionKind.HideLogon, AccountRestrictionStatus.Failed, rollbackAttempted: false, "hide failed");
        AssertEntry(result, AccountRestrictionKind.LogonScript, AccountRestrictionStatus.Failed, rollbackAttempted: false, "hide failed");
        AssertEntry(result, AccountRestrictionKind.NetworkLogin, AccountRestrictionStatus.Failed, rollbackAttempted: false, "network failed");
        AssertEntry(result, AccountRestrictionKind.BackgroundAutorun, AccountRestrictionStatus.Failed, rollbackAttempted: false, "bg failed");
        AssertEntry(result, AccountRestrictionKind.LsaPolicy, AccountRestrictionStatus.Failed, rollbackAttempted: false, "NetworkLogin: network failed");
        AssertEntry(result, AccountRestrictionKind.LsaPolicy, AccountRestrictionStatus.Failed, rollbackAttempted: false, "BackgroundAutorun: bg failed");

        lsa.Verify(l => l.SetLocalOnlyBySid(SourceSid, true), Times.Once);
        lsa.Verify(l => l.SetNoBgAutostartBySid(SourceSid, true), Times.Once);
    }

    private static void AssertEntry(
        AccountRestrictionResult result,
        AccountRestrictionKind kind,
        AccountRestrictionStatus status,
        bool rollbackAttempted,
        string? errorContains = null)
    {
        var entry = Assert.Single(result.Entries, e => e.Restriction == kind);
        Assert.Equal(status, entry.Status);
        Assert.Equal(rollbackAttempted, entry.RollbackAttempted);
        if (errorContains == null)
            return;

        Assert.Contains(errorContains, entry.Error);
    }
}
