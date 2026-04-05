using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
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
    private readonly Mock<IFirewallService> _firewallService = new();
    private readonly AppDatabase _database = new();

    public RunAsAccountSettingsApplierTests()
    {
        _appState.Setup(a => a.Database).Returns(_database);
    }

    private RunAsAccountSettingsApplier CreateApplier()
        => new(_appState.Object, new SessionContext(), _databaseService.Object, _log.Object,
            _settingsTransferService.Object, _firewallService.Object);

    // ── ApplyLaunchDefaults ─────────────────────────────────────────────────

    [Fact]
    public void ApplyLaunchDefaults_SplitTokenDefault_True_SplitTokenOptOut_False()
    {
        // useSplitTokenDefault=true → entry.SplitTokenOptOut = false (opted in)
        CreateApplier().ApplyLaunchDefaults(TestSid, useSplitTokenDefault: true, useLowIntegrityDefault: false);

        Assert.False(_database.GetAccount(TestSid)?.SplitTokenOptOut);
    }

    [Fact]
    public void ApplyLaunchDefaults_SplitTokenDefault_False_SplitTokenOptOut_True()
    {
        // useSplitTokenDefault=false → entry.SplitTokenOptOut = true (opted out)
        CreateApplier().ApplyLaunchDefaults(TestSid, useSplitTokenDefault: false, useLowIntegrityDefault: false);

        Assert.True(_database.GetAccount(TestSid)?.SplitTokenOptOut);
    }

    [Fact]
    public void ApplyLaunchDefaults_LowIntegrityDefault_True_SetsLowIntegrityDefault()
    {
        CreateApplier().ApplyLaunchDefaults(TestSid, useSplitTokenDefault: true, useLowIntegrityDefault: true);

        Assert.True(_database.GetAccount(TestSid)?.LowIntegrityDefault);
    }

    [Fact]
    public void ApplyLaunchDefaults_LowIntegrityDefault_False_DoesNotSetLowIntegrityDefault()
    {
        CreateApplier().ApplyLaunchDefaults(TestSid, useSplitTokenDefault: true, useLowIntegrityDefault: false);

        // Entry may not exist at all, or LowIntegrityDefault is false
        Assert.False(_database.GetAccount(TestSid)?.LowIntegrityDefault ?? false);
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
}