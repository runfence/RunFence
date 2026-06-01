using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Firewall.UI;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.PrefTrans;
using RunFence.RunAs;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public class RunAsAccountSettingsApplierTests : IDisposable
{
    private const string TestSid = "S-1-5-21-100-200-300-1001";

    private readonly Mock<IAppStateProvider> _appState = new();
    private readonly Mock<IDatabaseService> _databaseService = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<ISettingsTransferService> _settingsTransferService = new();
    private readonly Mock<IAccountFirewallSettingsApplier> _firewallSettingsApplier = new();
    private readonly AppDatabase _database = new();
    private readonly CredentialStore _credentialStore = new() { ArgonSalt = [1, 2, 3] };
    private readonly SecureSecret _pinKey = TestSecretFactory.Create(32);
    private readonly List<SessionContext> _sessions = [];

    public RunAsAccountSettingsApplierTests()
    {
        _appState.Setup(a => a.Database).Returns(_database);
    }

    public void Dispose()
    {
        foreach (var session in _sessions)
            session.Dispose();

        _pinKey.Dispose();
    }

    private SessionContext CreateSession()
    {
        var session = new SessionContext
        {
            Database = _database,
            CredentialStore = _credentialStore,
        }.WithClonedPinDerivedKey(_pinKey);
        _sessions.Add(session);
        return session;
    }

    private RunAsAccountSettingsApplier CreateApplier(
        Mock<IDatabaseService>? databaseService = null,
        SessionContext? session = null)
    {
        var currentSession = session ?? CreateSession();

        return new RunAsAccountSettingsApplier(
            _appState.Object,
            currentSession,
            (databaseService ?? _databaseService).Object,
            _log.Object,
            _settingsTransferService.Object,
            new FirewallApplyHelper(_firewallSettingsApplier.Object, new DynamicPortRangeChecker(_log.Object, new Mock<IUserConfirmationService>().Object, new StandardNetshCommandRunner()), _log.Object),
            new ImmediateAccountCreationProgressRunner());
    }

    // ── ApplyLaunchDefaults ─────────────────────────────────────────────────

    // ── ApplyFirewallDbSettings ─────────────────────────────────────────────

    [Fact]
    public void ApplyFirewallDbSettings_AllDefaultValues_RemovesAccountEntry()
    {
        // All-default settings (AllowInternet=true, AllowLocalhost=true, AllowLan=true) with empty allowlist
        // → IsDefault = true → UpdateOrRemove removes the entry if it's otherwise empty
        CreateApplier().ApplyFirewallDbSettings(TestSid, allowInternet: true, allowLocalhost: true, allowLan: true);

        // The account entry should not exist (removed because all-default and otherwise empty)
        Assert.Null(_database.GetAccount(TestSid));
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
