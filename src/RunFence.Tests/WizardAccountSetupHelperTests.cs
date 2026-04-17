using System.Security;
using Moq;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Firewall.UI;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.PrefTrans;
using RunFence.Wizard;
using Xunit;

namespace RunFence.Tests;

public class WizardAccountSetupHelperTests : IDisposable
{
    private const string TestSid = "S-1-5-21-9999999999-9999999999-9999999999-9001";

    private readonly Mock<IAccountCredentialManager> _credentialManager = new();
    private readonly Mock<ILocalUserProvider> _localUserProvider = new();
    private readonly Mock<ISidResolver> _sidResolver = new();
    private readonly Mock<ISidNameCacheService> _sidNameCache = new();
    private readonly Mock<ISettingsTransferService> _settingsTransferService = new();
    private readonly Mock<IAccountFirewallSettingsApplier> _firewallSettingsApplier = new();
    private readonly Mock<ILaunchFacade> _facade = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<IWizardProgressReporter> _progress = new();

    private readonly List<ProtectedBuffer> _pinKeys = new();
    private readonly List<SecureString> _secureStrings = new();

    private SessionContext CreateSession()
    {
        var pinKey = new ProtectedBuffer(new byte[32], protect: false);
        _pinKeys.Add(pinKey);
        return new SessionContext
        {
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore(),
            PinDerivedKey = pinKey
        };
    }

    public void Dispose()
    {
        foreach (var key in _pinKeys)
            key.Dispose();
        foreach (var ss in _secureStrings)
            ss.Dispose();
    }

    private PackageInstallService CreatePackageInstallService() =>
        new(_facade.Object, new AccountToolResolver(_sidResolver.Object), _log.Object);

    private WizardAccountSetupHelper CreateHelper(SessionContext session) =>
        new(_credentialManager.Object, _localUserProvider.Object, _sidNameCache.Object,
            _settingsTransferService.Object,
            new FirewallApplyHelper(_firewallSettingsApplier.Object, _log.Object),
            CreatePackageInstallService(), session);

    private WizardAccountSetupHelper.SetupRequest MakeRequest(
        bool storeCredential = false,
        FirewallAccountSettings? firewallSettings = null,
        List<InstallablePackage>? installPackages = null,
        PrivilegeLevel privilegeLevel = PrivilegeLevel.Basic,
        bool trayTerminal = false)
    {
        var pw = new SecureString();
        pw.AppendChar('P');
        pw.MakeReadOnly();
        _secureStrings.Add(pw);
        return new WizardAccountSetupHelper.SetupRequest(
            Sid: TestSid,
            Username: "testuser",
            Password: pw,
            StoreCredential: storeCredential,
            IsEphemeral: false,
            PrivilegeLevel: privilegeLevel,
            FirewallSettings: firewallSettings,
            DesktopSettingsPath: null,
            InstallPackages: installPackages,
            TrayTerminal: trayTerminal);
    }

    // --- Credential storage ---

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetupAsync_StoreCredential_ControlsWhetherCredentialIsPersisted(bool storeCredential)
    {
        var session = CreateSession();
        var helper = CreateHelper(session);
        var request = MakeRequest(storeCredential: storeCredential);

        await helper.SetupAsync(request, _progress.Object);

        _credentialManager.Verify(c => c.StoreCreatedUserCredential(
                It.IsAny<string>(), It.IsAny<SecureString>(),
                It.IsAny<CredentialStore>(), It.IsAny<ProtectedBuffer>()),
            storeCredential ? Times.Once() : Times.Never());
    }

    // --- Install packages before firewall block ---

    [Fact]
    public async Task SetupAsync_InstallsPackagesBeforeFirewallBlock_WhenInternetBlockedAndPackagesSet()
    {
        // Arrange: firewall blocks internet, packages are set, credential entry is in the store.
        // PackageInstallService.InstallPackages calls ILaunchFacade.LaunchFile (powershell.exe).
        // IAccountFirewallSettingsApplier.ApplyAccountFirewallSettingsAsync is called afterward.
        // We track call order to verify packages come before firewall.
        var session = CreateSession();
        var credEntry = new CredentialEntry { Sid = TestSid };
        session.CredentialStore.Credentials.Add(credEntry);

        var callOrder = new List<string>();
        _facade
            .Setup(f => f.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), It.IsAny<Func<string, string, bool>?>()))
            .Callback(() => callOrder.Add("install"));
        _firewallSettingsApplier
            .Setup(f => f.ApplyAccountFirewallSettingsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<FirewallAccountSettings?>(),
                It.IsAny<FirewallAccountSettings>(),
                It.IsAny<AppDatabase>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("firewall"))
            .ReturnsAsync(SuccessfulFirewallApply());

        var firewallSettings = new FirewallAccountSettings { AllowInternet = false };
        var packages = new List<InstallablePackage>
        {
            new("TestPkg", "Write-Host 'test'")
        };
        var helper = CreateHelper(session);
        var request = MakeRequest(firewallSettings: firewallSettings, installPackages: packages);

        await helper.SetupAsync(request, _progress.Object);

        // Both steps should have run, with packages before firewall
        Assert.Contains("install", callOrder);
        Assert.Contains("firewall", callOrder);
        Assert.True(callOrder.IndexOf("install") < callOrder.IndexOf("firewall"),
            "Packages should be installed before firewall rules are applied.");
    }

    // --- Ephemeral account setup ---

    [Fact]
    public async Task SetupAsync_EphemeralAccount_SetsDeleteAfterUtc()
    {
        var session = CreateSession();
        var helper = CreateHelper(session);

        using var pw = new SecureString();
        pw.AppendChar('P');
        pw.MakeReadOnly();
        var request = new WizardAccountSetupHelper.SetupRequest(
            Sid: TestSid,
            Username: "ephemeral",
            Password: pw,
            StoreCredential: false,
            IsEphemeral: true,
            PrivilegeLevel: PrivilegeLevel.Basic,
            FirewallSettings: null,
            DesktopSettingsPath: null,
            InstallPackages: null,
            TrayTerminal: false);

        var before = DateTime.UtcNow;
        await helper.SetupAsync(request, _progress.Object);
        var after = DateTime.UtcNow;

        var entry = session.Database.GetAccount(TestSid);
        Assert.NotNull(entry);
        Assert.NotNull(entry.DeleteAfterUtc);
        // Ephemeral account lives for approximately 24 hours
        Assert.True(entry.DeleteAfterUtc.Value >= before.AddHours(23.9));
        Assert.True(entry.DeleteAfterUtc.Value <= after.AddHours(24.1));
    }

    [Fact]
    public async Task SetupAsync_NonEphemeralAccount_DoesNotSetDeleteAfterUtc()
    {
        var session = CreateSession();
        var helper = CreateHelper(session);
        var request = MakeRequest(storeCredential: false);

        await helper.SetupAsync(request, _progress.Object);

        var entry = session.Database.GetAccount(TestSid);
        Assert.NotNull(entry);
        Assert.Null(entry.DeleteAfterUtc);
    }

    // --- PrivilegeLevel ---

    [Theory]
    [InlineData(PrivilegeLevel.Basic)]
    [InlineData(PrivilegeLevel.HighestAllowed)]
    [InlineData(PrivilegeLevel.LowIntegrity)]
    public async Task SetupAsync_PrivilegeLevel_SetsPrivilegeLevelOnEntry(PrivilegeLevel privilegeLevel)
    {
        var session = CreateSession();
        var helper = CreateHelper(session);
        var request = MakeRequest(privilegeLevel: privilegeLevel);

        await helper.SetupAsync(request, _progress.Object);

        var entry = session.Database.GetAccount(TestSid);
        Assert.NotNull(entry);
        Assert.Equal(privilegeLevel, entry.PrivilegeLevel);
    }

    // --- Tray terminal configuration ---

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetupAsync_TrayTerminal_SetsTrayTerminalOnEntry(bool trayTerminal)
    {
        var session = CreateSession();
        var helper = CreateHelper(session);
        var request = MakeRequest(trayTerminal: trayTerminal);

        await helper.SetupAsync(request, _progress.Object);

        var entry = session.Database.GetAccount(TestSid);
        Assert.NotNull(entry);
        Assert.Equal(trayTerminal, entry.TrayTerminal);
    }

    // --- SidNames update ---

    [Fact]
    public async Task SetupAsync_ResolveAndCacheCalledWithSidAndUsername()
    {
        var session = CreateSession();
        var helper = CreateHelper(session);
        var request = MakeRequest();

        await helper.SetupAsync(request, _progress.Object);

        _sidNameCache.Verify(c => c.ResolveAndCache(TestSid, "testuser"), Times.Once);
    }

    // --- Non-fatal error collection ---

    [Fact]
    public async Task SetupAsync_ContinuesSetup_WhenOptionalStepFails()
    {
        // Arrange: desktop settings path exists but import fails. Setup should continue.
        var session = CreateSession();
        // Simulate a settings file that exists
        var tempFile = Path.GetTempFileName();
        try
        {
            _settingsTransferService
                .Setup(s => s.Import(
                    It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<int>(), It.IsAny<Action?>()))
                .Throws(new InvalidOperationException("Import failed"));

            // Also set up firewall so we can verify setup continued to that step
            var firewallSettings = new FirewallAccountSettings { AllowInternet = false };

            using var pw = new SecureString();
            pw.AppendChar('P');
            pw.MakeReadOnly();
            var request = new WizardAccountSetupHelper.SetupRequest(
                Sid: TestSid,
                Username: "testuser",
                Password: pw,
                StoreCredential: false,
                IsEphemeral: false,
                PrivilegeLevel: PrivilegeLevel.Basic,
                FirewallSettings: firewallSettings,
                DesktopSettingsPath: tempFile,
                InstallPackages: null,
                TrayTerminal: false);

            var helper = CreateHelper(session);

            // Act
            await helper.SetupAsync(request, _progress.Object);

            // Assert: error was reported (not thrown) and setup continued to apply firewall
            _progress.Verify(p => p.ReportError(It.Is<string>(s => s.Contains("Settings import"))), Times.Once);
            _firewallSettingsApplier.Verify(f => f.ApplyAccountFirewallSettingsAsync(
                TestSid,
                It.IsAny<string>(),
                null,
                It.IsAny<FirewallAccountSettings>(),
                It.IsAny<AppDatabase>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task SetupAsync_AppliesFirewallWithLiveDatabaseAndSettings()
    {
        var session = CreateSession();
        var firewallSettings = new FirewallAccountSettings
        {
            AllowInternet = false,
            Allowlist = [new FirewallAllowlistEntry { Value = "example.com", IsDomain = true }]
        };
        AppDatabase? capturedDatabase = null;
        FirewallAccountSettings? capturedSettings = null;
        _firewallSettingsApplier
            .Setup(f => f.ApplyAccountFirewallSettingsAsync(
                TestSid,
                It.IsAny<string>(),
                null,
                It.IsAny<FirewallAccountSettings>(),
                It.IsAny<AppDatabase>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, FirewallAccountSettings?, FirewallAccountSettings, AppDatabase, CancellationToken>(
                (_, _, _, settings, database, _) =>
                {
                    capturedSettings = settings;
                    capturedDatabase = database;
                })
            .ReturnsAsync(SuccessfulFirewallApply());

        var helper = CreateHelper(session);
        var request = MakeRequest(firewallSettings: firewallSettings);

        await helper.SetupAsync(request, _progress.Object);

        var liveSettings = session.Database.GetAccount(TestSid)!.Firewall;
        Assert.NotNull(capturedDatabase);
        Assert.NotNull(capturedSettings);
        Assert.Same(session.Database, capturedDatabase);
        Assert.Same(liveSettings, capturedSettings);
        Assert.False(capturedDatabase.GetAccount(TestSid)!.Firewall.AllowInternet);
        Assert.Equal(["example.com"], capturedSettings.Allowlist.Select(entry => entry.Value));
    }

    [Fact]
    public async Task SetupAsync_AccountRuleFailure_RestoresDefaultFirewallSettingsForNewAccount()
    {
        var session = CreateSession();
        var firewallSettings = new FirewallAccountSettings { AllowInternet = false };
        _firewallSettingsApplier
            .Setup(f => f.ApplyAccountFirewallSettingsAsync(
                TestSid,
                It.IsAny<string>(),
                null,
                It.IsAny<FirewallAccountSettings>(),
                It.IsAny<AppDatabase>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FirewallApplyException(
                FirewallApplyPhase.AccountRules,
                TestSid,
                new InvalidOperationException("firewall unavailable")));

        var helper = CreateHelper(session);
        var request = MakeRequest(firewallSettings: firewallSettings);

        await helper.SetupAsync(request, _progress.Object);

        Assert.Null(session.Database.GetAccount(TestSid));
        _progress.Verify(p => p.ReportError("Firewall rules: firewall unavailable"), Times.Once);
    }

    [Fact]
    public async Task SetupAsync_GlobalIcmpFailure_KeepsSavedFirewallSettingsForNewAccount()
    {
        var session = CreateSession();
        var firewallSettings = new FirewallAccountSettings { AllowInternet = false };
        _firewallSettingsApplier
            .Setup(f => f.ApplyAccountFirewallSettingsAsync(
                TestSid,
                It.IsAny<string>(),
                null,
                It.IsAny<FirewallAccountSettings>(),
                It.IsAny<AppDatabase>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FirewallApplyException(
                FirewallApplyPhase.GlobalIcmp,
                TestSid,
                new InvalidOperationException("global icmp unavailable")));

        var helper = CreateHelper(session);
        var request = MakeRequest(firewallSettings: firewallSettings);

        await helper.SetupAsync(request, _progress.Object);

        Assert.NotNull(session.Database.GetAccount(TestSid));
        Assert.False(session.Database.GetAccount(TestSid)!.Firewall.AllowInternet);
        _progress.Verify(p => p.ReportError("Global ICMP firewall rule: global icmp unavailable"), Times.Once);
    }

    private static FirewallApplyResult SuccessfulFirewallApply() => new(
        AccountRulesApplied: true,
        GlobalIcmpApplied: true,
        PendingDomains: []);
}
