using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Firewall.UI;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public class FirewallApplyHelperTests
{
    private const string Sid = "S-1-5-21-100-200-300-1001";
    private const string Username = "testuser";

    [Fact]
    public async Task ApplyWithRollbackAsync_AccountRuleFailure_RollsBackPersistedSettings()
    {
        var applier = new Mock<IAccountFirewallSettingsApplier>();
        var log = new Mock<ILoggingService>();
        var confirmation = new Mock<IUserConfirmationService>();
        var helper = new FirewallApplyHelper(
            applier.Object,
            new DynamicPortRangeChecker(log.Object, confirmation.Object, new StandardNetshCommandRunner()),
            log.Object);

        var database = new AppDatabase();
        var previous = new FirewallAccountSettings { AllowInternet = true, AllowLan = false };
        var final = new FirewallAccountSettings { AllowInternet = false, AllowLan = true };
        FirewallAccountSettings.UpdateOrRemove(database, Sid, previous);

        applier.Setup(a => a.ApplyAccountFirewallSettingsAsync(
                Sid,
                Username,
                It.IsAny<FirewallAccountSettings?>(),
                It.IsAny<FirewallAccountSettings>(),
                database,
                It.IsAny<Action?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FirewallApplyResult(
                ConfigSaved: false,
                PendingDomains: [],
                EnforcementEntries:
                [
                    new FirewallEnforcementEntry(FirewallEnforcementLayer.AccountRules, FirewallEnforcementStatus.Failed, "account rules failed")
                ]));

        var errors = new List<string>();
        var saveCalls = 0;

        var rolledBack = await helper.ApplyWithRollbackAsync(
            sid: Sid,
            username: Username,
            previous: previous,
            final: final,
            database: database,
            saveAction: () => saveCalls++,
            reportError: errors.Add);

        Assert.True(rolledBack);
        Assert.Equal(0, saveCalls);
        Assert.Contains(errors, error => error.Contains("Firewall rules: account rules failed", StringComparison.Ordinal));

        var restored = database.GetAccount(Sid)!.Firewall;
        Assert.True(restored.AllowInternet);
        Assert.False(restored.AllowLan);
    }

    [Fact]
    public async Task ApplyWithRollbackAsync_WfpFailure_RollsBackPersistedSettings()
    {
        var applier = new Mock<IAccountFirewallSettingsApplier>();
        var log = new Mock<ILoggingService>();
        var confirmation = new Mock<IUserConfirmationService>();
        var helper = new FirewallApplyHelper(
            applier.Object,
            new DynamicPortRangeChecker(log.Object, confirmation.Object, new StandardNetshCommandRunner()),
            log.Object);

        var database = new AppDatabase();
        var previous = new FirewallAccountSettings { AllowInternet = true, AllowLocalhost = false };
        var final = new FirewallAccountSettings { AllowInternet = false, AllowLocalhost = true };
        FirewallAccountSettings.UpdateOrRemove(database, Sid, previous);

        applier.Setup(a => a.ApplyAccountFirewallSettingsAsync(
                Sid,
                Username,
                It.IsAny<FirewallAccountSettings?>(),
                It.IsAny<FirewallAccountSettings>(),
                database,
                It.IsAny<Action?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FirewallApplyResult(
                ConfigSaved: false,
                PendingDomains: [],
                EnforcementEntries:
                [
                    new FirewallEnforcementEntry(FirewallEnforcementLayer.WfpFilters, FirewallEnforcementStatus.Failed, "wfp failed")
                ]));

        var errors = new List<string>();
        var saveCalls = 0;

        var rolledBack = await helper.ApplyWithRollbackAsync(
            sid: Sid,
            username: Username,
            previous: previous,
            final: final,
            database: database,
            saveAction: () => saveCalls++,
            reportError: errors.Add);

        Assert.True(rolledBack);
        Assert.Equal(0, saveCalls);
        Assert.Contains(errors, error => error.Contains("Firewall rules: wfp failed", StringComparison.Ordinal));

        var restored = database.GetAccount(Sid)!.Firewall;
        Assert.True(restored.AllowInternet);
        Assert.False(restored.AllowLocalhost);
    }

    [Fact]
    public async Task ApplyWithRollbackAsync_BlockingFailureAfterPersist_PersistsRollback()
    {
        var applier = new Mock<IAccountFirewallSettingsApplier>();
        var log = new Mock<ILoggingService>();
        var confirmation = new Mock<IUserConfirmationService>();
        var helper = new FirewallApplyHelper(
            applier.Object,
            new DynamicPortRangeChecker(log.Object, confirmation.Object, new StandardNetshCommandRunner()),
            log.Object);

        var database = new AppDatabase();
        var previous = new FirewallAccountSettings { AllowInternet = true, AllowLan = false };
        var final = new FirewallAccountSettings { AllowInternet = false, AllowLan = true };
        FirewallAccountSettings.UpdateOrRemove(database, Sid, previous);

        applier.Setup(a => a.ApplyAccountFirewallSettingsAsync(
                Sid,
                Username,
                It.IsAny<FirewallAccountSettings?>(),
                It.IsAny<FirewallAccountSettings>(),
                database,
                It.IsAny<Action?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string _, FirewallAccountSettings? _, FirewallAccountSettings settings, AppDatabase db, Action? _, CancellationToken _) =>
            {
                FirewallAccountSettings.UpdateOrRemove(db, Sid, settings.Clone());
                return new FirewallApplyResult(
                    ConfigSaved: true,
                    PendingDomains: [],
                    EnforcementEntries:
                    [
                        new FirewallEnforcementEntry(FirewallEnforcementLayer.AccountRules, FirewallEnforcementStatus.Failed, "account rules failed")
                    ]);
            });

        var errors = new List<string>();
        var saveCalls = 0;

        var rolledBack = await helper.ApplyWithRollbackAsync(
            sid: Sid,
            username: Username,
            previous: previous,
            final: final,
            database: database,
            saveAction: () => saveCalls++,
            reportError: errors.Add);

        Assert.True(rolledBack);
        Assert.Equal(1, saveCalls);
        Assert.Contains(errors, error => error.Contains("Firewall rules: account rules failed", StringComparison.Ordinal));

        var restored = database.GetAccount(Sid)!.Firewall;
        Assert.True(restored.AllowInternet);
        Assert.False(restored.AllowLan);
    }

    [Fact]
    public async Task ApplyWithRollbackAsync_RetryWarningAfterPersist_ReportsSavedWarning()
    {
        var applier = new Mock<IAccountFirewallSettingsApplier>();
        var log = new Mock<ILoggingService>();
        var confirmation = new Mock<IUserConfirmationService>();
        var helper = new FirewallApplyHelper(
            applier.Object,
            new DynamicPortRangeChecker(log.Object, confirmation.Object, new StandardNetshCommandRunner()),
            log.Object);

        var database = new AppDatabase();
        var previous = new FirewallAccountSettings { AllowInternet = true };
        var final = new FirewallAccountSettings { AllowInternet = false };

        applier.Setup(a => a.ApplyAccountFirewallSettingsAsync(
                Sid,
                Username,
                It.IsAny<FirewallAccountSettings?>(),
                It.IsAny<FirewallAccountSettings>(),
                database,
                It.IsAny<Action?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FirewallApplyResult(
                ConfigSaved: true,
                PendingDomains: [],
                EnforcementEntries:
                [
                    new FirewallEnforcementEntry(FirewallEnforcementLayer.GlobalIcmp, FirewallEnforcementStatus.RetryScheduled, "global icmp failed")
                ]));

        var errors = new List<string>();

        var rolledBack = await helper.ApplyWithRollbackAsync(
            sid: Sid,
            username: Username,
            previous: previous,
            final: final,
            database: database,
            saveAction: () => { },
            reportError: errors.Add);

        Assert.False(rolledBack);
        var error = Assert.Single(errors);
        Assert.Contains("Firewall settings were saved", error, StringComparison.Ordinal);
        Assert.Contains("GlobalIcmp: global icmp failed", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyWithRollbackAsync_RetryWarningBeforePersist_ReportsNotSavedWarning()
    {
        var applier = new Mock<IAccountFirewallSettingsApplier>();
        var log = new Mock<ILoggingService>();
        var confirmation = new Mock<IUserConfirmationService>();
        var helper = new FirewallApplyHelper(
            applier.Object,
            new DynamicPortRangeChecker(log.Object, confirmation.Object, new StandardNetshCommandRunner()),
            log.Object);

        var database = new AppDatabase();
        var previous = new FirewallAccountSettings { AllowInternet = false };
        var final = new FirewallAccountSettings { AllowInternet = true };

        applier.Setup(a => a.ApplyAccountFirewallSettingsAsync(
                Sid,
                Username,
                It.IsAny<FirewallAccountSettings?>(),
                It.IsAny<FirewallAccountSettings>(),
                database,
                It.IsAny<Action?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FirewallApplyResult(
                ConfigSaved: false,
                PendingDomains: [],
                EnforcementEntries:
                [
                    new FirewallEnforcementEntry(FirewallEnforcementLayer.GlobalIcmp, FirewallEnforcementStatus.RetryScheduled, "global icmp failed")
                ]));

        var errors = new List<string>();

        var rolledBack = await helper.ApplyWithRollbackAsync(
            sid: Sid,
            username: Username,
            previous: previous,
            final: final,
            database: database,
            saveAction: () => { },
            reportError: errors.Add);

        Assert.False(rolledBack);
        var error = Assert.Single(errors);
        Assert.Contains("Firewall settings were not saved", error, StringComparison.Ordinal);
        Assert.Contains("GlobalIcmp: global icmp failed", error, StringComparison.Ordinal);
    }
}
