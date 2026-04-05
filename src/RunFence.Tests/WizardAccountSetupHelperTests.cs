using System.Security;
using Moq;
using RunFence.Account;
using RunFence.Acl.Permissions;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
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
    private readonly Mock<IPermissionGrantService> _permissionGrantService = new();
    private readonly Mock<IFirewallService> _firewallService = new();
    private readonly Mock<IProcessLaunchService> _processLaunchService = new();
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

    private AccountLauncher CreateAccountLauncher() =>
        new(_processLaunchService.Object, _permissionGrantService.Object, _sidResolver.Object);

    private WizardAccountSetupHelper CreateHelper(SessionContext session) =>
        new(_credentialManager.Object, _localUserProvider.Object, _sidNameCache.Object,
            _settingsTransferService.Object, _firewallService.Object, CreateAccountLauncher(), session);

    private WizardAccountSetupHelper.SetupRequest MakeRequest(
        bool storeCredential = false,
        FirewallAccountSettings? firewallSettings = null,
        List<InstallablePackage>? installPackages = null)
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
            SplitTokenOptOut: false,
            LowIntegrityDefault: false,
            FirewallSettings: firewallSettings,
            DesktopSettingsPath: null,
            InstallPackages: installPackages,
            TrayTerminal: false);
    }

    // --- Credential storage ---

    [Fact]
    public async Task SetupAsync_StoresCredential_WhenStoreCredentialTrue()
    {
        var session = CreateSession();
        var helper = CreateHelper(session);
        var request = MakeRequest(storeCredential: true);

        await helper.SetupAsync(request, _progress.Object);

        _credentialManager.Verify(c => c.StoreCreatedUserCredential(
                TestSid, request.Password, session.CredentialStore, session.PinDerivedKey),
            Times.Once);
    }

    [Fact]
    public async Task SetupAsync_DoesNotStoreCredential_WhenStoreCredentialFalse()
    {
        var session = CreateSession();
        var helper = CreateHelper(session);
        var request = MakeRequest(storeCredential: false);

        await helper.SetupAsync(request, _progress.Object);

        _credentialManager.Verify(c => c.StoreCreatedUserCredential(
                It.IsAny<string>(), It.IsAny<SecureString>(),
                It.IsAny<CredentialStore>(), It.IsAny<ProtectedBuffer>()),
            Times.Never);
    }

    // --- Install packages before firewall block ---

    [Fact]
    public async Task SetupAsync_InstallsPackagesBeforeFirewallBlock_WhenInternetBlockedAndPackagesSet()
    {
        // Arrange: firewall blocks internet, packages are set, credential entry is in the store.
        // AccountLauncher.InstallPackages calls IProcessLaunchService.LaunchExe.
        // IFirewallService.ApplyFirewallRules is called afterward.
        // We track call order to verify packages (via LaunchExe) come before firewall.
        var session = CreateSession();
        var credEntry = new CredentialEntry { Sid = TestSid };
        session.CredentialStore.Credentials.Add(credEntry);

        var callOrder = new List<string>();
        _processLaunchService
            .Setup(p => p.LaunchExe(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchCredentials>(), It.IsAny<LaunchFlags>()))
            .Callback(() => callOrder.Add("install"));
        _firewallService
            .Setup(f => f.ApplyFirewallRules(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<FirewallAccountSettings>(), null))
            .Callback(() => callOrder.Add("firewall"));

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
            SplitTokenOptOut: false,
            LowIntegrityDefault: false,
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

    // --- Split-token / low-integrity defaults ---

    [Fact]
    public async Task SetupAsync_SplitTokenOptOut_SetsSplitTokenOptOut()
    {
        var session = CreateSession();
        var helper = CreateHelper(session);

        using var pw = new SecureString();
        pw.AppendChar('P');
        pw.MakeReadOnly();
        var request = new WizardAccountSetupHelper.SetupRequest(
            Sid: TestSid,
            Username: "testuser",
            Password: pw,
            StoreCredential: false,
            IsEphemeral: false,
            SplitTokenOptOut: true,
            LowIntegrityDefault: false,
            FirewallSettings: null,
            DesktopSettingsPath: null,
            InstallPackages: null,
            TrayTerminal: false);

        await helper.SetupAsync(request, _progress.Object);

        var entry = session.Database.GetAccount(TestSid);
        Assert.NotNull(entry);
        Assert.True(entry.SplitTokenOptOut);
    }

    [Fact]
    public async Task SetupAsync_LowIntegrityDefault_SetsLowIntegrityDefault()
    {
        var session = CreateSession();
        var helper = CreateHelper(session);

        using var pw = new SecureString();
        pw.AppendChar('P');
        pw.MakeReadOnly();
        var request = new WizardAccountSetupHelper.SetupRequest(
            Sid: TestSid,
            Username: "testuser",
            Password: pw,
            StoreCredential: false,
            IsEphemeral: false,
            SplitTokenOptOut: false,
            LowIntegrityDefault: true,
            FirewallSettings: null,
            DesktopSettingsPath: null,
            InstallPackages: null,
            TrayTerminal: false);

        await helper.SetupAsync(request, _progress.Object);

        var entry = session.Database.GetAccount(TestSid);
        Assert.NotNull(entry);
        Assert.True(entry.LowIntegrityDefault);
    }

    // --- Tray terminal configuration ---

    [Fact]
    public async Task SetupAsync_TrayTerminalTrue_SetsTrayTerminalOnEntry()
    {
        var session = CreateSession();
        var helper = CreateHelper(session);

        using var pw = new SecureString();
        pw.AppendChar('P');
        pw.MakeReadOnly();
        var request = new WizardAccountSetupHelper.SetupRequest(
            Sid: TestSid,
            Username: "testuser",
            Password: pw,
            StoreCredential: false,
            IsEphemeral: false,
            SplitTokenOptOut: false,
            LowIntegrityDefault: false,
            FirewallSettings: null,
            DesktopSettingsPath: null,
            InstallPackages: null,
            TrayTerminal: true);

        await helper.SetupAsync(request, _progress.Object);

        var entry = session.Database.GetAccount(TestSid);
        Assert.NotNull(entry);
        Assert.True(entry.TrayTerminal);
    }

    [Fact]
    public async Task SetupAsync_TrayTerminalFalse_DoesNotSetTrayTerminal()
    {
        var session = CreateSession();
        var helper = CreateHelper(session);
        var request = MakeRequest();

        await helper.SetupAsync(request, _progress.Object);

        var entry = session.Database.GetAccount(TestSid);
        Assert.NotNull(entry);
        Assert.False(entry.TrayTerminal);
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
                    It.IsAny<string>(), It.IsAny<LaunchCredentials>(), It.IsAny<string>(),
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
                SplitTokenOptOut: false,
                LowIntegrityDefault: false,
                FirewallSettings: firewallSettings,
                DesktopSettingsPath: tempFile,
                InstallPackages: null,
                TrayTerminal: false);

            var helper = CreateHelper(session);

            // Act
            await helper.SetupAsync(request, _progress.Object);

            // Assert: error was reported (not thrown) and setup continued to apply firewall
            _progress.Verify(p => p.ReportError(It.Is<string>(s => s.Contains("Settings import"))), Times.Once);
            _firewallService.Verify(f => f.ApplyFirewallRules(
                TestSid, It.IsAny<string>(), It.IsAny<FirewallAccountSettings>(), null), Times.Once);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}