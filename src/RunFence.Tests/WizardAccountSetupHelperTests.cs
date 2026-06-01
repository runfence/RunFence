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
    private readonly Mock<ISidNameCacheService> _sidNameCache = new();
    private readonly Mock<ISettingsTransferService> _settingsTransferService = new();
    private readonly Mock<IAccountFirewallSettingsApplier> _firewallSettingsApplier = new();
    private readonly Mock<IPackageInstallService> _packageInstallService = new();
    private readonly Mock<IWizardProgressReporter> _progress = new();
    private readonly Mock<IWizardSessionSaver> _sessionSaver = new();

    private readonly List<SessionContext> _sessions = new();
    private readonly List<ProtectedString> _secureStrings = new();

    private SessionContext CreateSession()
    {
        var pinKey = TestSecretFactory.Create(32);
        var session = new SessionContext
        {
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore(),
        }.WithPinDerivedKeyTakingOwnership(pinKey);
        _sessions.Add(session);
        return session;
    }

    public void Dispose()
    {
        foreach (var session in _sessions)
            session.Dispose();
        foreach (var ss in _secureStrings)
            ss.Dispose();
    }

    private WizardAccountSetupHelper CreateHelper(SessionContext session) =>
        new(_credentialManager.Object, _localUserProvider.Object, _sidNameCache.Object,
            _settingsTransferService.Object,
            new FirewallApplyHelper(_firewallSettingsApplier.Object, new DynamicPortRangeChecker(Mock.Of<ILoggingService>(), new Mock<IUserConfirmationService>().Object, new StandardNetshCommandRunner()), Mock.Of<ILoggingService>()),
            _packageInstallService.Object, _sessionSaver.Object, session);

    private WizardAccountSetupHelper.SetupRequest MakeRequest(
        bool storeCredential = false,
        FirewallAccountSettings? firewallSettings = null,
        List<InstallablePackage>? installPackages = null,
        PrivilegeLevel privilegeLevel = PrivilegeLevel.Isolated,
        bool trayTerminal = false)
    {
        var pw = new ProtectedString();
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
            TrayTerminal: trayTerminal,
            WaitForInstallPackages: false);
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
                It.IsAny<string>(), It.IsAny<ProtectedString>(),
                It.IsAny<CredentialStore>(), It.IsAny<ISecureSecretSnapshotSource>()),
            storeCredential ? Times.Once() : Times.Never());
    }

    // --- Install packages before firewall block ---

    [Fact]
    public async Task SetupAsync_InstallsPackagesBeforeFirewallBlock_WhenInternetBlockedAndPackagesSet()
    {
        var session = CreateSession();
        var credEntry = new CredentialEntry { Sid = TestSid };
        session.CredentialStore.Credentials.Add(credEntry);

        var callOrder = new List<string>();
        _packageInstallService
            .Setup(s => s.InstallPackagesAsync(
                It.IsAny<IReadOnlyList<InstallablePackage>>(),
                It.IsAny<AccountLaunchIdentity>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("install"))
            .ReturnsAsync([]);
        _packageInstallService
            .Setup(s => s.WaitForInstallCompletionAsync(TestSid, null, _progress.Object.CancellationToken))
            .Callback(() => callOrder.Add("wait"))
            .Returns(Task.CompletedTask);
        _firewallSettingsApplier
            .Setup(f => f.ApplyAccountFirewallSettingsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<FirewallAccountSettings?>(),
                It.IsAny<FirewallAccountSettings>(),
                It.IsAny<AppDatabase>(),
                saveAction: It.IsAny<Action?>(),
                cancellationToken: It.IsAny<CancellationToken>()))
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

        Assert.Contains("install", callOrder);
        Assert.Contains("wait", callOrder);
        Assert.Contains("firewall", callOrder);
        Assert.True(callOrder.IndexOf("install") < callOrder.IndexOf("firewall"),
            "Packages should be installed before firewall rules are applied.");
        Assert.True(callOrder.IndexOf("wait") < callOrder.IndexOf("firewall"),
            "Package wait should complete before firewall rules are applied.");
    }

    [Fact]
    public async Task SetupAsync_InstallPackagesMaintenanceWarning_ReportsProgressWarning_WhenInternetBlocked()
    {
        var session = CreateSession();
        session.CredentialStore.Credentials.Add(new CredentialEntry { Sid = TestSid });
        _packageInstallService
            .Setup(s => s.InstallPackagesAsync(
                It.IsAny<IReadOnlyList<InstallablePackage>>(),
                It.IsAny<AccountLaunchIdentity>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(["post-launch maintenance failed"]);
        _packageInstallService
            .Setup(s => s.WaitForInstallCompletionAsync(TestSid, null, _progress.Object.CancellationToken))
            .Returns(Task.CompletedTask);
        _firewallSettingsApplier
            .Setup(f => f.ApplyAccountFirewallSettingsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<FirewallAccountSettings?>(),
                It.IsAny<FirewallAccountSettings>(),
                It.IsAny<AppDatabase>(),
                saveAction: It.IsAny<Action?>(),
                cancellationToken: It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessfulFirewallApply());

        var helper = CreateHelper(session);
        var request = MakeRequest(
            firewallSettings: new FirewallAccountSettings { AllowInternet = false },
            installPackages: [new InstallablePackage("TestPkg", "Write-Host 'test'")]);

        await helper.SetupAsync(request, _progress.Object);

        _progress.Verify(p => p.ReportWarning(It.Is<string>(s =>
            s.Contains("The package installer started") &&
            s.Contains("post-launch maintenance failed"))), Times.Once);
        _progress.Verify(p => p.ReportError(It.Is<string>(s => s.Contains("post-launch maintenance failed"))), Times.Never);
    }

    [Fact]
    public async Task SetupAsync_InstallPackagesMaintenanceWarning_ReportsProgressWarning_WhenInternetNotBlocked()
    {
        var session = CreateSession();
        session.CredentialStore.Credentials.Add(new CredentialEntry { Sid = TestSid });
        _packageInstallService
            .Setup(s => s.InstallPackagesAsync(
                It.IsAny<IReadOnlyList<InstallablePackage>>(),
                It.IsAny<AccountLaunchIdentity>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(["post-launch maintenance failed"]);

        var helper = CreateHelper(session);
        var request = MakeRequest(
            firewallSettings: new FirewallAccountSettings { AllowInternet = true },
            installPackages: [new InstallablePackage("TestPkg", "Write-Host 'test'")]);

        await helper.SetupAsync(request, _progress.Object);

        _progress.Verify(p => p.ReportWarning(It.Is<string>(s =>
            s.Contains("The package installer started") &&
            s.Contains("post-launch maintenance failed"))), Times.Once);
        _progress.Verify(p => p.ReportError(It.Is<string>(s => s.Contains("post-launch maintenance failed"))), Times.Never);
    }

    [Fact]
    public async Task SetupAsync_WaitForInstallPackages_WaitsEvenWhenInternetIsNotBlocked()
    {
        var session = CreateSession();
        session.CredentialStore.Credentials.Add(new CredentialEntry { Sid = TestSid });

        var callOrder = new List<string>();
        _packageInstallService
            .Setup(s => s.InstallPackagesAsync(
                It.IsAny<IReadOnlyList<InstallablePackage>>(),
                It.IsAny<AccountLaunchIdentity>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("install"))
            .ReturnsAsync([]);
        _packageInstallService
            .Setup(s => s.WaitForInstallCompletionAsync(TestSid, null, _progress.Object.CancellationToken))
            .Callback(() => callOrder.Add("wait"))
            .Returns(Task.CompletedTask);
        _firewallSettingsApplier
            .Setup(f => f.ApplyAccountFirewallSettingsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<FirewallAccountSettings?>(),
                It.IsAny<FirewallAccountSettings>(),
                It.IsAny<AppDatabase>(),
                saveAction: It.IsAny<Action?>(),
                cancellationToken: It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("firewall"))
            .ReturnsAsync(SuccessfulFirewallApply());

        var helper = CreateHelper(session);
        var request = new WizardAccountSetupHelper.SetupRequest(
            Sid: TestSid,
            Username: "testuser",
            Password: ProtectedString.FromChars("P".AsSpan()),
            StoreCredential: false,
            IsEphemeral: false,
            PrivilegeLevel: PrivilegeLevel.Isolated,
            FirewallSettings: new FirewallAccountSettings { AllowInternet = true },
            DesktopSettingsPath: null,
            InstallPackages: [new InstallablePackage("TestPkg", "Write-Host 'test'")],
            TrayTerminal: false,
            WaitForInstallPackages: true);

        _secureStrings.Add(request.Password);

        await helper.SetupAsync(request, _progress.Object);

        Assert.Contains("install", callOrder);
        Assert.Contains("wait", callOrder);
        Assert.DoesNotContain("firewall", callOrder);
        _progress.Verify(p => p.ReportStatus("Installing packages..."), Times.Once);
    }

    // --- Ephemeral account setup ---

    [Fact]
    public async Task SetupAsync_EphemeralAccount_SetsDeleteAfterUtc()
    {
        var session = CreateSession();
        var helper = CreateHelper(session);

        using var pw = new ProtectedString();
        pw.AppendChar('P');
        pw.MakeReadOnly();
        var request = new WizardAccountSetupHelper.SetupRequest(
            Sid: TestSid,
            Username: "ephemeral",
            Password: pw,
            StoreCredential: false,
            IsEphemeral: true,
            PrivilegeLevel: PrivilegeLevel.Isolated,
            FirewallSettings: null,
            DesktopSettingsPath: null,
            InstallPackages: null,
            TrayTerminal: false,
            WaitForInstallPackages: false);

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

            using var pw = new ProtectedString();
            pw.AppendChar('P');
            pw.MakeReadOnly();
            var request = new WizardAccountSetupHelper.SetupRequest(
                Sid: TestSid,
                Username: "testuser",
                Password: pw,
                StoreCredential: false,
                IsEphemeral: false,
                PrivilegeLevel: PrivilegeLevel.Isolated,
                FirewallSettings: firewallSettings,
                DesktopSettingsPath: tempFile,
                InstallPackages: null,
                TrayTerminal: false,
                WaitForInstallPackages: false);

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
                saveAction: It.IsAny<Action?>(),
                cancellationToken: It.IsAny<CancellationToken>()), Times.Once);
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
                saveAction: It.IsAny<Action?>(),
                cancellationToken: It.IsAny<CancellationToken>()))
            .Callback<string, string, FirewallAccountSettings?, FirewallAccountSettings, AppDatabase, Action?, CancellationToken>(
                (_, _, _, settings, database, _, _) =>
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

    [Theory]
    [InlineData(FirewallApplyPhase.AccountRules, "firewall unavailable", "Firewall rules: firewall unavailable", true)]
    [InlineData(FirewallApplyPhase.GlobalIcmp, "global icmp unavailable", "Global ICMP firewall rule: global icmp unavailable", false)]
    public async Task SetupAsync_FirewallPhaseFailure_HandledCorrectly(
        FirewallApplyPhase failingPhase, string innerMessage, string expectedError, bool expectAccountNull)
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
                saveAction: It.IsAny<Action?>(),
                cancellationToken: It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FirewallApplyException(
                failingPhase,
                TestSid,
                new InvalidOperationException(innerMessage)));

        var helper = CreateHelper(session);
        var request = MakeRequest(firewallSettings: firewallSettings);

        await helper.SetupAsync(request, _progress.Object);

        if (expectAccountNull)
        {
            Assert.Null(session.Database.GetAccount(TestSid));
        }
        else
        {
            Assert.NotNull(session.Database.GetAccount(TestSid));
            Assert.False(session.Database.GetAccount(TestSid)!.Firewall.AllowInternet);
        }
        _progress.Verify(p => p.ReportError(expectedError), Times.Once);
    }

    [Fact]
    public async Task InstallPackagesAndWaitAsync_InstallPackagesMaintenanceWarning_ReportsProgressWarning()
    {
        var session = CreateSession();
        session.CredentialStore.Credentials.Add(new CredentialEntry { Sid = TestSid });
        _packageInstallService
            .Setup(s => s.InstallPackagesAsync(
                It.IsAny<IReadOnlyList<InstallablePackage>>(),
                It.IsAny<AccountLaunchIdentity>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(["post-launch maintenance failed"]);
        _packageInstallService
            .Setup(s => s.WaitForInstallCompletionAsync(TestSid, It.IsAny<TimeSpan?>(), _progress.Object.CancellationToken))
            .Returns(Task.CompletedTask);
        var helper = CreateHelper(session);

        await helper.InstallPackagesAndWaitAsync(
            [new InstallablePackage("TestPkg", "Write-Host 'test'")],
            TestSid,
            TimeSpan.FromSeconds(1),
            _progress.Object);

        _progress.Verify(p => p.ReportWarning(It.Is<string>(s =>
            s.Contains("The package installer started") &&
            s.Contains("post-launch maintenance failed"))), Times.Once);
        _progress.Verify(p => p.ReportError(It.Is<string>(s => s.Contains("post-launch maintenance failed"))), Times.Never);
    }

    private static FirewallApplyResult SuccessfulFirewallApply() => new(
        ConfigSaved: true,
        PendingDomains: [],
        EnforcementEntries:
        [
            new FirewallEnforcementEntry(FirewallEnforcementLayer.AccountRules, FirewallEnforcementStatus.Succeeded),
            new FirewallEnforcementEntry(FirewallEnforcementLayer.WfpFilters, FirewallEnforcementStatus.Succeeded),
            new FirewallEnforcementEntry(FirewallEnforcementLayer.GlobalIcmp, FirewallEnforcementStatus.Succeeded)
        ]);
}
