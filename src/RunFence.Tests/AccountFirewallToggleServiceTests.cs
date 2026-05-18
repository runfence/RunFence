using Moq;
using RunFence.Account;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public class AccountFirewallToggleServiceTests
{
    private const string Sid = "S-1-5-21-100-200-300-1001";
    private const string Username = "testuser";

    private readonly Mock<IFirewallSettingsService> _firewallSettingsService = new();
    private readonly Mock<IAccountFirewallSettingsApplier> _firewallSettingsApplier = new();
    private readonly Mock<ISessionSaver> _sessionSaver = new();
    private readonly Mock<IDataChangeNotifier> _dataChangeNotifier = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly AppDatabase _database = new();

    private AccountFirewallToggleService CreateService()
    {
        _firewallSettingsService
            .Setup(s => s.GetDatabaseAndUsername(Sid))
            .Returns((_database, Username));
        return new(
            _firewallSettingsService.Object,
            _firewallSettingsApplier.Object,
            _sessionSaver.Object,
            _dataChangeNotifier.Object,
            _log.Object);
    }

    private static FirewallApplyResult SuccessfulApplyResult() => new(
        ConfigSaved: true,
        PendingDomains: [],
        EnforcementEntries:
        [
            new FirewallEnforcementEntry(FirewallEnforcementLayer.AccountRules, FirewallEnforcementStatus.Succeeded),
            new FirewallEnforcementEntry(FirewallEnforcementLayer.WfpFilters, FirewallEnforcementStatus.Succeeded),
            new FirewallEnforcementEntry(FirewallEnforcementLayer.GlobalIcmp, FirewallEnforcementStatus.Succeeded)
        ]);

    [Fact]
    public void SetAllowInternet_Success_ReturnsNull()
    {
        // Arrange
        _database.GetOrCreateAccount(Sid).Firewall = new FirewallAccountSettings { AllowInternet = false };
        _firewallSettingsApplier
            .Setup(f => f.ApplyAccountFirewallSettings(
                Sid, Username,
                It.IsAny<FirewallAccountSettings?>(),
                It.IsAny<FirewallAccountSettings>(),
                _database,
                It.IsAny<Action?>()))
            .Returns(SuccessfulApplyResult());

        // Act
        var result = CreateService().SetAllowInternet(Sid, allow: true, existing: _database.GetAccount(Sid)!.Firewall);

        // Assert: no error returned
        Assert.Null(result.Message);
        _dataChangeNotifier.Verify(n => n.NotifyDataChanged(), Times.Once);
        _firewallSettingsApplier.Verify(f => f.ApplyAccountFirewallSettings(
            Sid, Username,
            It.IsAny<FirewallAccountSettings?>(),
            It.IsAny<FirewallAccountSettings>(),
            _database,
            It.IsAny<Action?>()), Times.Once);
    }

    [Fact]
    public void SetAllowInternet_AccountRuleFailure_RestoresPreviousSettingsInDatabase()
    {
        // Arrange
        _database.GetOrCreateAccount(Sid).Firewall = new FirewallAccountSettings
        {
            AllowInternet = true,
            AllowLan = false
        };
        var existing = _database.GetAccount(Sid)!.Firewall;

        FirewallAccountSettings? capturedPrevious = null;
        FirewallAccountSettings? capturedApplied = null;
        _firewallSettingsApplier
            .Setup(f => f.ApplyAccountFirewallSettings(
                Sid, Username,
                It.IsAny<FirewallAccountSettings?>(),
                It.IsAny<FirewallAccountSettings>(),
                _database,
                It.IsAny<Action?>()))
            .Returns((string _, string _, FirewallAccountSettings? previous, FirewallAccountSettings settings, AppDatabase database, Action? _) =>
            {
                capturedPrevious = previous;
                capturedApplied = settings;
                FirewallAccountSettings.UpdateOrRemove(database, Sid, settings.Clone());
                return new FirewallApplyResult(
                    ConfigSaved: true,
                    PendingDomains: [],
                    EnforcementEntries:
                    [
                        new FirewallEnforcementEntry(FirewallEnforcementLayer.AccountRules, FirewallEnforcementStatus.Failed, "firewall unavailable")
                    ]);
            });

        // Act
        var result = CreateService().SetAllowInternet(Sid, allow: false, existing: existing);

        // Assert: error returned and DB reverted to previous settings
        Assert.Equal("firewall unavailable", result.Message);
        Assert.NotNull(capturedPrevious);
        Assert.NotNull(capturedApplied);
        Assert.True(capturedPrevious!.AllowInternet);
        Assert.False(capturedPrevious.AllowLan);
        Assert.False(capturedApplied!.AllowInternet);
        Assert.True(_database.GetAccount(Sid)!.Firewall.AllowInternet);
        Assert.False(_database.GetAccount(Sid)!.Firewall.AllowLan);
        _sessionSaver.Verify(s => s.SaveConfig(), Times.Once);
        _dataChangeNotifier.Verify(n => n.NotifyDataChanged(), Times.Once);
    }

    [Fact]
    public void SetAllowInternet_WfpFailure_RestoresPreviousSettingsInDatabase()
    {
        // Arrange
        _database.GetOrCreateAccount(Sid).Firewall = new FirewallAccountSettings
        {
            AllowInternet = true,
            AllowLan = false
        };
        var existing = _database.GetAccount(Sid)!.Firewall;

        _firewallSettingsApplier
            .Setup(f => f.ApplyAccountFirewallSettings(
                Sid, Username,
                It.IsAny<FirewallAccountSettings?>(),
                It.IsAny<FirewallAccountSettings>(),
                _database,
                It.IsAny<Action?>()))
            .Returns((string _, string _, FirewallAccountSettings? _, FirewallAccountSettings settings, AppDatabase database, Action? _) =>
            {
                FirewallAccountSettings.UpdateOrRemove(database, Sid, settings.Clone());
                return new FirewallApplyResult(
                    ConfigSaved: true,
                    PendingDomains: [],
                    EnforcementEntries:
                    [
                        new FirewallEnforcementEntry(FirewallEnforcementLayer.WfpFilters, FirewallEnforcementStatus.Failed, "wfp failed")
                    ]);
            });

        // Act
        var result = CreateService().SetAllowInternet(Sid, allow: false, existing: existing);

        // Assert: error returned and DB reverted to previous settings
        Assert.Equal("wfp failed", result.Message);
        Assert.True(_database.GetAccount(Sid)!.Firewall.AllowInternet);
        Assert.False(_database.GetAccount(Sid)!.Firewall.AllowLan);
        _sessionSaver.Verify(s => s.SaveConfig(), Times.Once);
        _dataChangeNotifier.Verify(n => n.NotifyDataChanged(), Times.Once);
    }

    [Fact]
    public void SetAllowInternet_BlockingFailureAfterPersist_SavesRollbackState()
    {
        // Arrange
        _database.GetOrCreateAccount(Sid).Firewall = new FirewallAccountSettings
        {
            AllowInternet = true,
            AllowLan = false
        };
        var existing = _database.GetAccount(Sid)!.Firewall;
        _firewallSettingsApplier
            .Setup(f => f.ApplyAccountFirewallSettings(
                Sid, Username,
                It.IsAny<FirewallAccountSettings?>(),
                It.IsAny<FirewallAccountSettings>(),
                _database,
                It.IsAny<Action?>()))
            .Returns((string _, string _, FirewallAccountSettings? _, FirewallAccountSettings settings, AppDatabase database, Action? _) =>
            {
                FirewallAccountSettings.UpdateOrRemove(database, Sid, settings.Clone());
                return new FirewallApplyResult(
                    ConfigSaved: true,
                    PendingDomains: [],
                    EnforcementEntries:
                    [
                        new FirewallEnforcementEntry(FirewallEnforcementLayer.AccountRules, FirewallEnforcementStatus.Failed, "firewall unavailable")
                    ]);
            });

        // Act
        var result = CreateService().SetAllowInternet(Sid, allow: false, existing: existing);

        // Assert
        Assert.Equal("firewall unavailable", result.Message);
        _sessionSaver.Verify(s => s.SaveConfig(), Times.Once);
        Assert.True(_database.GetAccount(Sid)!.Firewall.AllowInternet);
        Assert.False(_database.GetAccount(Sid)!.Firewall.AllowLan);
        _dataChangeNotifier.Verify(n => n.NotifyDataChanged(), Times.Once);
    }

    [Fact]
    public void SetAllowInternet_GlobalIcmpRetryWarning_KeepsNewSettingsInDatabase()
    {
        // Arrange
        _database.GetOrCreateAccount(Sid).Firewall = new FirewallAccountSettings { AllowInternet = true };
        var existing = _database.GetAccount(Sid)!.Firewall;

        _firewallSettingsApplier
            .Setup(f => f.ApplyAccountFirewallSettings(
                Sid, Username,
                It.IsAny<FirewallAccountSettings?>(),
                It.IsAny<FirewallAccountSettings>(),
                _database,
                It.IsAny<Action?>()))
            .Returns((string _, string _, FirewallAccountSettings? _, FirewallAccountSettings settings, AppDatabase database, Action? _) =>
            {
                FirewallAccountSettings.UpdateOrRemove(database, Sid, settings.Clone());
                return new FirewallApplyResult(
                    ConfigSaved: true,
                    PendingDomains: [],
                    EnforcementEntries:
                    [
                        new FirewallEnforcementEntry(FirewallEnforcementLayer.AccountRules, FirewallEnforcementStatus.Succeeded),
                        new FirewallEnforcementEntry(FirewallEnforcementLayer.WfpFilters, FirewallEnforcementStatus.Succeeded),
                        new FirewallEnforcementEntry(FirewallEnforcementLayer.GlobalIcmp, FirewallEnforcementStatus.RetryScheduled, "global icmp unavailable")
                    ]);
            });

        // Act
        var result = CreateService().SetAllowInternet(Sid, allow: false, existing: existing);

        // Assert: error returned but DB not rolled back (rules were applied)
        Assert.Contains("global icmp unavailable", result.Message);
        Assert.False(_database.GetAccount(Sid)!.Firewall.AllowInternet);
        _dataChangeNotifier.Verify(n => n.NotifyDataChanged(), Times.Once);
    }

    [Fact]
    public void SetAllowInternet_GlobalIcmpRetryBeforePersist_NotifiesDataChangedToRefreshStoredState()
    {
        // Arrange
        _database.GetOrCreateAccount(Sid).Firewall = new FirewallAccountSettings { AllowInternet = false };
        var existing = _database.GetAccount(Sid)!.Firewall;

        _firewallSettingsApplier
            .Setup(f => f.ApplyAccountFirewallSettings(
                Sid, Username,
                It.IsAny<FirewallAccountSettings?>(),
                It.IsAny<FirewallAccountSettings>(),
                _database,
                It.IsAny<Action?>()))
            .Returns(new FirewallApplyResult(
                ConfigSaved: false,
                PendingDomains: [],
                EnforcementEntries:
                [
                    new FirewallEnforcementEntry(FirewallEnforcementLayer.GlobalIcmp, FirewallEnforcementStatus.RetryScheduled, "global icmp unavailable")
                ]));

        // Act
        var result = CreateService().SetAllowInternet(Sid, allow: true, existing: existing);

        // Assert
        Assert.Contains("global icmp unavailable", result.Message);
        Assert.False(_database.GetAccount(Sid)!.Firewall.AllowInternet);
        _sessionSaver.Verify(s => s.SaveConfig(), Times.Never);
        _dataChangeNotifier.Verify(n => n.NotifyDataChanged(), Times.Once);
    }

    [Fact]
    public void SetAllowInternet_PassesPreviousAndNewSettingsToApplier()
    {
        // Arrange
        _database.GetOrCreateAccount(Sid).Firewall = new FirewallAccountSettings
        {
            AllowInternet = true,
            AllowLan = true
        };
        var existing = _database.GetAccount(Sid)!.Firewall;

        FirewallAccountSettings? capturedPrevious = null;
        FirewallAccountSettings? capturedNew = null;
        _firewallSettingsApplier
            .Setup(f => f.ApplyAccountFirewallSettings(
                Sid, Username,
                It.IsAny<FirewallAccountSettings?>(),
                It.IsAny<FirewallAccountSettings>(),
                _database,
                It.IsAny<Action?>()))
            .Callback<string, string, FirewallAccountSettings?, FirewallAccountSettings, AppDatabase, Action?>(
                (_, _, prev, settings, _, _) =>
                {
                    capturedPrevious = prev;
                    capturedNew = settings;
                })
            .Returns(SuccessfulApplyResult());

        // Act
        CreateService().SetAllowInternet(Sid, allow: false, existing: existing);

        // Assert: correct previous and new settings passed
        Assert.NotNull(capturedPrevious);
        Assert.NotNull(capturedNew);
        Assert.True(capturedPrevious!.AllowInternet);
        Assert.False(capturedNew!.AllowInternet);
        Assert.True(capturedNew!.AllowLan);
    }

    [Fact]
    public void SetAllowInternet_NullExistingSettings_UsesDefaults()
    {
        // Arrange: no existing firewall settings (null passed)
        _firewallSettingsApplier
            .Setup(f => f.ApplyAccountFirewallSettings(
                Sid, Username,
                It.IsAny<FirewallAccountSettings?>(),
                It.IsAny<FirewallAccountSettings>(),
                _database,
                It.IsAny<Action?>()))
            .Returns(SuccessfulApplyResult());

        // Act
        var result = CreateService().SetAllowInternet(Sid, allow: false, existing: null);

        // Assert: applier called, no error
        Assert.Null(result.Message);
        _dataChangeNotifier.Verify(n => n.NotifyDataChanged(), Times.Once);
        _firewallSettingsApplier.Verify(f => f.ApplyAccountFirewallSettings(
            Sid, Username,
            It.IsAny<FirewallAccountSettings?>(),
            It.IsAny<FirewallAccountSettings>(),
            _database,
            It.IsAny<Action?>()), Times.Once);
    }

    [Fact]
    public void SetAllowInternet_BlockingFailureBeforePersist_ReturnsRefresh()
    {
        // Arrange
        _database.GetOrCreateAccount(Sid).Firewall = new FirewallAccountSettings { AllowInternet = true };
        var existing = _database.GetAccount(Sid)!.Firewall;
        _firewallSettingsApplier
            .Setup(f => f.ApplyAccountFirewallSettings(
                Sid, Username,
                It.IsAny<FirewallAccountSettings?>(),
                It.IsAny<FirewallAccountSettings>(),
                _database,
                It.IsAny<Action?>()))
            .Returns(new FirewallApplyResult(
                ConfigSaved: false,
                PendingDomains: [],
                EnforcementEntries:
                [
                    new FirewallEnforcementEntry(FirewallEnforcementLayer.AccountRules, FirewallEnforcementStatus.Failed, "firewall unavailable")
                ]));

        // Act
        var result = CreateService().SetAllowInternet(Sid, allow: false, existing: existing);

        // Assert
        Assert.Equal("firewall unavailable", result.Message);
        _sessionSaver.Verify(s => s.SaveConfig(), Times.Never);
        _dataChangeNotifier.Verify(n => n.NotifyDataChanged(), Times.Once);
    }
}
