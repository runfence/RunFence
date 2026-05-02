using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
using Xunit;

namespace RunFence.Tests;

public class AccountFirewallToggleServiceTests
{
    private const string Sid = "S-1-5-21-100-200-300-1001";
    private const string Username = "testuser";

    private readonly Mock<IFirewallSettingsService> _firewallSettingsService = new();
    private readonly Mock<IAccountFirewallSettingsApplier> _firewallSettingsApplier = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly AppDatabase _database = new();

    private AccountFirewallToggleService CreateService()
    {
        _firewallSettingsService
            .Setup(s => s.GetDatabaseAndUsername(Sid))
            .Returns((_database, Username));
        return new(_firewallSettingsService.Object, _firewallSettingsApplier.Object, _log.Object);
    }

    [Fact]
    public void SetAllowInternet_Success_ReturnsNull()
    {
        // Arrange
        _database.GetOrCreateAccount(Sid).Firewall = new FirewallAccountSettings { AllowInternet = false };

        // Act
        var error = CreateService().SetAllowInternet(Sid, allow: true, existing: _database.GetAccount(Sid)!.Firewall);

        // Assert: no error returned
        Assert.Null(error);
        _firewallSettingsApplier.Verify(f => f.ApplyAccountFirewallSettings(
            Sid, Username,
            It.IsAny<FirewallAccountSettings?>(),
            It.IsAny<FirewallAccountSettings>(),
            _database), Times.Once);
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
                _database))
            .Callback<string, string, FirewallAccountSettings?, FirewallAccountSettings, AppDatabase>(
                (_, _, previous, settings, _) =>
                {
                    capturedPrevious = previous;
                    capturedApplied = settings;
                })
            .Throws(new FirewallApplyException(
                FirewallApplyPhase.AccountRules,
                Sid,
                new InvalidOperationException("firewall unavailable")));

        // Act
        var error = CreateService().SetAllowInternet(Sid, allow: false, existing: existing);

        // Assert: error returned and DB reverted to previous settings
        Assert.Equal("firewall unavailable", error);
        Assert.NotNull(capturedPrevious);
        Assert.NotNull(capturedApplied);
        Assert.True(capturedPrevious!.AllowInternet);
        Assert.False(capturedPrevious.AllowLan);
        Assert.False(capturedApplied!.AllowInternet);
        Assert.True(_database.GetAccount(Sid)!.Firewall.AllowInternet);
        Assert.False(_database.GetAccount(Sid)!.Firewall.AllowLan);
    }

    [Fact]
    public void SetAllowInternet_GlobalIcmpFailure_KeepsNewSettingsInDatabase()
    {
        // Arrange
        _database.GetOrCreateAccount(Sid).Firewall = new FirewallAccountSettings { AllowInternet = true };
        var existing = _database.GetAccount(Sid)!.Firewall;

        _firewallSettingsApplier
            .Setup(f => f.ApplyAccountFirewallSettings(
                Sid, Username,
                It.IsAny<FirewallAccountSettings?>(),
                It.IsAny<FirewallAccountSettings>(),
                _database))
            .Throws(new FirewallApplyException(
                FirewallApplyPhase.GlobalIcmp,
                Sid,
                new InvalidOperationException("global icmp unavailable")));

        // Act
        var error = CreateService().SetAllowInternet(Sid, allow: false, existing: existing);

        // Assert: error returned but DB not rolled back (rules were applied)
        Assert.Equal("global icmp unavailable", error);
        Assert.False(_database.GetAccount(Sid)!.Firewall.AllowInternet);
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
                _database))
            .Callback<string, string, FirewallAccountSettings?, FirewallAccountSettings, AppDatabase>(
                (_, _, prev, settings, _) =>
                {
                    capturedPrevious = prev;
                    capturedNew = settings;
                });

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

        // Act
        var error = CreateService().SetAllowInternet(Sid, allow: false, existing: null);

        // Assert: applier called, no error
        Assert.Null(error);
        _firewallSettingsApplier.Verify(f => f.ApplyAccountFirewallSettings(
            Sid, Username,
            It.IsAny<FirewallAccountSettings?>(),
            It.IsAny<FirewallAccountSettings>(),
            _database), Times.Once);
    }
}
