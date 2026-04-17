using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Firewall.UI;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.PrefTrans;
using RunFence.RunAs;
using Xunit;

namespace RunFence.Tests;

public class RunAsAccountSettingsApplierTests
{
    private const string TestSid = "S-1-5-21-100-200-300-1001";

    private readonly Mock<IAppStateProvider> _appState = new();
    private readonly Mock<IDatabaseService> _databaseService = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<ISettingsTransferService> _settingsTransferService = new();
    private readonly Mock<IAccountFirewallSettingsApplier> _firewallSettingsApplier = new();
    private readonly AppDatabase _database = new();

    public RunAsAccountSettingsApplierTests()
    {
        _appState.Setup(a => a.Database).Returns(_database);
    }

    private RunAsAccountSettingsApplier CreateApplier()
        => new(_appState.Object, new SessionContext(), _databaseService.Object, _log.Object,
            _settingsTransferService.Object,
            new FirewallApplyHelper(_firewallSettingsApplier.Object, _log.Object));

    // ── ApplyLaunchDefaults ─────────────────────────────────────────────────

    [Theory]
    [InlineData(PrivilegeLevel.Basic)]
    [InlineData(PrivilegeLevel.HighestAllowed)]
    [InlineData(PrivilegeLevel.LowIntegrity)]
    public void ApplyLaunchDefaults_SetsPrivilegeLevelOnAccount(PrivilegeLevel privilegeLevel)
    {
        CreateApplier().ApplyLaunchDefaults(TestSid, privilegeLevel);

        Assert.Equal(privilegeLevel, _database.GetAccount(TestSid)?.PrivilegeLevel);
    }

    // ── ApplyFirewallDbSettings ─────────────────────────────────────────────

    [Fact]
    public void ApplyFirewallDbSettings_NonDefaultValues_CreatesAccountEntry()
    {
        CreateApplier().ApplyFirewallDbSettings(TestSid, allowInternet: false, allowLocalhost: true, allowLan: true);

        var entry = _database.GetAccount(TestSid);
        Assert.NotNull(entry);
        Assert.False(entry.Firewall.AllowInternet);
        Assert.True(entry.Firewall.AllowLocalhost);
        Assert.True(entry.Firewall.AllowLan);
    }

    [Fact]
    public void ApplyFirewallDbSettings_AllDefaultValues_RemovesAccountEntry()
    {
        // All-default settings (AllowInternet=true, AllowLocalhost=true, AllowLan=true) with empty allowlist
        // → IsDefault = true → UpdateOrRemove removes the entry if it's otherwise empty
        CreateApplier().ApplyFirewallDbSettings(TestSid, allowInternet: true, allowLocalhost: true, allowLan: true);

        // The account entry should not exist (removed because all-default and otherwise empty)
        Assert.Null(_database.GetAccount(TestSid));
    }

    [Theory]
    [InlineData(true, false, false)]   // internet allowed, localhost and LAN blocked
    [InlineData(false, true, false)]   // localhost allowed, others blocked
    [InlineData(false, false, true)]   // LAN allowed, others blocked
    [InlineData(false, false, false)]  // all blocked
    public void ApplyFirewallDbSettings_VariousValues_StoredCorrectly(
        bool allowInternet, bool allowLocalhost, bool allowLan)
    {
        CreateApplier().ApplyFirewallDbSettings(TestSid, allowInternet, allowLocalhost, allowLan);

        var entry = _database.GetAccount(TestSid);
        Assert.NotNull(entry);
        Assert.Equal(allowInternet, entry.Firewall.AllowInternet);
        Assert.Equal(allowLocalhost, entry.Firewall.AllowLocalhost);
        Assert.Equal(allowLan, entry.Firewall.AllowLan);
    }

    [Fact]
    public void ApplyFirewallDbSettings_PreexistingAccount_UpdatesFirewallSettingsOnly()
    {
        // A pre-existing account entry (e.g. with PrivilegeLevel set) should retain non-firewall fields
        var account = _database.GetOrCreateAccount(TestSid);
        account.PrivilegeLevel = PrivilegeLevel.HighestAllowed;

        CreateApplier().ApplyFirewallDbSettings(TestSid, allowInternet: false, allowLocalhost: true, allowLan: false);

        var entry = _database.GetAccount(TestSid);
        Assert.NotNull(entry);
        Assert.Equal(PrivilegeLevel.HighestAllowed, entry.PrivilegeLevel);
        Assert.False(entry.Firewall.AllowInternet);
        Assert.True(entry.Firewall.AllowLocalhost);
    }

    [Fact]
    public void ApplyLaunchDefaults_DoesNotCreateAccountEntryWhenAlreadyExists()
    {
        // GetOrCreateAccount is idempotent — calling again must not lose data
        var account = _database.GetOrCreateAccount(TestSid);
        account.TrayDiscovery = true;

        CreateApplier().ApplyLaunchDefaults(TestSid, PrivilegeLevel.LowIntegrity);

        Assert.True(_database.GetAccount(TestSid)!.TrayDiscovery);
        Assert.Equal(PrivilegeLevel.LowIntegrity, _database.GetAccount(TestSid)!.PrivilegeLevel);
    }
}
