using Moq;
using Microsoft.Win32;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;
using Xunit;

namespace RunFence.Tests;

public class LicenseServiceTests : IDisposable
{
    private static readonly byte[] TestPublicKeyBytes = LicenseTestKey.PublicKeyBytes;

    private readonly string _licenseFilePath = Path.Combine(Path.GetTempPath(), $"license_test_{Guid.NewGuid():N}.dat");
    private readonly string _registryKeyPath = $@"Software\RunFenceTests\{Guid.NewGuid():N}";
    private readonly LicenseValidator _validator = new(TestPublicKeyBytes);

    private static readonly byte[] TestMachineHash =
    [
        0xAA,
        0xBB,
        0xCC,
        0xDD,
        0xEE,
        0xFF,
        0x11,
        0x22,
        0x33,
        0x44,
        0x55,
        0x66
    ];

    public void Dispose()
    {
        if (File.Exists(_licenseFilePath))
            File.Delete(_licenseFilePath);
        if (File.Exists(_licenseFilePath + ".bak"))
            File.Delete(_licenseFilePath + ".bak");
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(_registryKeyPath, throwOnMissingSubKey: false);
        }
        catch
        {
        }
    }

    private sealed class ServiceFixture(
        LicenseService service,
        SessionContext session,
        Mock<ISessionSaver> sessionSaver,
        Mock<ILoggingService> log,
        Mock<IEvaluationCredentialCounter> credentialCounter)
    {
        public LicenseService Service { get; } = service;
        public SessionContext Session { get; } = session;
        public Mock<ISessionSaver> SessionSaver { get; } = sessionSaver;
        public Mock<ILoggingService> Log { get; } = log;
        public Mock<IEvaluationCredentialCounter> CredentialCounter { get; } = credentialCounter;
    }

    private ServiceFixture CreateServiceFixture(
        int appCount = 0,
        bool initialNagEligible = false,
        int countedCredentials = 0,
        IEnumerable<CredentialEntry>? credentials = null,
        Action<Mock<ISessionSaver>>? configureSessionSaver = null,
        Action<Mock<IEvaluationCredentialCounter>>? configureCredentialCounter = null)
    {
        var database = new AppDatabase();
        for (var i = 0; i < appCount; i++)
        {
            database.Apps.Add(new AppEntry
            {
                Id = $"app{i}",
                Name = $"App{i}",
                AccountSid = "S-1-5-21-1000-2000-3000-4000"
            });
        }

        database.Settings.NagEligible = initialNagEligible;

        var store = new CredentialStore
        {
            Credentials = (credentials ?? Array.Empty<CredentialEntry>()).ToList()
        };

        var session = new SessionContext
{
            Database = database,
            CredentialStore = store,
        }.WithOwnedPinDerivedKey(TestSecretFactory.Create(32));

        var machineProvider = new TestMachineIdProvider(TestMachineHash);
        var licenseStore = new LicenseFileStore(_licenseFilePath);
        var validationService = new LicenseValidationService(machineProvider, _validator);
        var policy = new LicenseEvaluationPolicy();
        var restrictionService = new FeatureRestrictionService(policy);
        var formatter = new LicenseMessageFormatter();
        var sessionProvider = new Mock<ISessionProvider>();
        sessionProvider.Setup(p => p.GetSession()).Returns(session);

        var sessionSaver = new Mock<ISessionSaver>();
        configureSessionSaver?.Invoke(sessionSaver);

        var credentialCounter = new Mock<IEvaluationCredentialCounter>();
        credentialCounter
            .Setup(c => c.CountCredentialsExcludingCurrent(It.IsAny<IEnumerable<CredentialEntry>>()))
            .Returns(countedCredentials);
        configureCredentialCounter?.Invoke(credentialCounter);

        var log = new Mock<ILoggingService>();

        var svc = new LicenseService(
            machineProvider,
            licenseStore,
            validationService,
            restrictionService,
            formatter,
            _registryKeyPath,
            sessionProvider.Object,
            sessionSaver.Object,
            credentialCounter.Object,
            log.Object);

        svc.Initialize();
        return new ServiceFixture(svc, session, sessionSaver, log, credentialCounter);
    }

    private LicenseService CreateService(
        int appCount = 0,
        bool initialNagEligible = false,
        int countedCredentials = 0,
        IEnumerable<CredentialEntry>? credentials = null)
    {
        return CreateServiceFixture(appCount, initialNagEligible, countedCredentials, credentials).Service;
    }

    private string BuildValidKey(string name = "Test User", uint expiryDays = 0)
    {
        using var key = LicenseTestKey.CreateSigningKey();
        return TestKeyBuilder.BuildKey(key, TestMachineHash, expiryDays: expiryDays, licenseeName: name);
    }

    [Fact]
    public void ActivateLicense_ValidKey_ReturnsSuccess_AndStoresInFile()
    {
        var svc = CreateService();
        var key = BuildValidKey();
        var result = svc.ActivateLicense(key);
        Assert.Equal(LicenseActivationResult.Success, result);
        Assert.True(svc.IsLicensed);
        Assert.True(File.Exists(_licenseFilePath));
    }

    [Fact]
    public void ActivateLicense_InvalidKey_ReturnsMalformed()
    {
        var svc = CreateService();
        var result = svc.ActivateLicense("RAME-INVALIDKEY");
        Assert.Equal(LicenseActivationResult.Malformed, result);
        Assert.False(svc.IsLicensed);
    }

    [Fact]
    public void ActivateLicense_ValidKey_FiresLicenseStatusChangedOnce()
    {
        var svc = CreateService();
        var count = 0;
        svc.LicenseStatusChanged += () => count++;
        svc.ActivateLicense(BuildValidKey());
        Assert.Equal(1, count);
    }

    [Fact]
    public void DeactivateLicense_FiresLicenseStatusChangedOnce()
    {
        var svc = CreateService();
        svc.ActivateLicense(BuildValidKey());
        var count = 0;
        svc.LicenseStatusChanged += () => count++;
        svc.DeactivateLicense();
        Assert.Equal(1, count);
        Assert.False(svc.IsLicensed);
    }

    [Fact]
    public void LicenseFile_PersistsAcrossServiceInstances()
    {
        var svc1 = CreateService();
        svc1.ActivateLicense(BuildValidKey());

        var svc2 = CreateService();
        Assert.True(svc2.IsLicensed);
    }

    [Fact]
    public void DeactivateLicense_DeletesFile_NewInstanceIsUnlicensed()
    {
        var svc1 = CreateService();
        svc1.ActivateLicense(BuildValidKey());
        Assert.True(svc1.IsLicensed);

        svc1.DeactivateLicense();

        var svc2 = CreateService();
        Assert.False(svc2.IsLicensed);
    }

    // --- Evaluation limit tests ---

    public static IEnumerable<object[]> EvaluationLimitData =>
    [
        ["App", EvaluationConstants.EvaluationMaxApps],
        ["Container", EvaluationConstants.EvaluationMaxContainers],
        ["HiddenAccount", EvaluationConstants.EvaluationMaxHiddenAccounts],
        ["Credential", EvaluationConstants.EvaluationMaxCredentials],
    ];

    private static bool CanAdd(ILicenseService svc, string feature, int count) => feature switch
    {
        "App" => svc.CanAddApp(count),
        "Container" => svc.CanCreateContainer(count),
        "HiddenAccount" => svc.CanHideAccount(count),
        "Credential" => svc.CanAddCredential(count),
        _ => throw new ArgumentException(feature)
    };

    private static EvaluationFeature ToFeature(string feature) => feature switch
    {
        "App" => EvaluationFeature.Apps,
        "Container" => EvaluationFeature.Containers,
        "HiddenAccount" => EvaluationFeature.HiddenAccounts,
        "Credential" => EvaluationFeature.Credentials,
        _ => throw new ArgumentException(feature)
    };

    [Theory]
    [MemberData(nameof(EvaluationLimitData))]
    public void CanAdd_AtLimit_Unlicensed_ReturnsFalse(string feature, int limit)
    {
        var svc = CreateService(initialNagEligible: true, appCount: 1, countedCredentials: 1);
        Assert.False(CanAdd(svc, feature, limit));
    }

    [Theory]
    [MemberData(nameof(EvaluationLimitData))]
    public void CanAdd_AtLimit_Licensed_ReturnsTrue(string feature, int limit)
    {
        var svc = CreateService(initialNagEligible: true, appCount: 1, countedCredentials: 1);
        svc.ActivateLicense(BuildValidKey());
        Assert.True(CanAdd(svc, feature, limit));
    }

    [Theory]
    [MemberData(nameof(EvaluationLimitData))]
    public void CanAdd_BelowLimit_Unlicensed_ReturnsTrue(string feature, int limit)
    {
        var svc = CreateService(initialNagEligible: true, appCount: 1, countedCredentials: 1);
        Assert.True(CanAdd(svc, feature, limit - 1));
    }

    [Theory]
    [MemberData(nameof(EvaluationLimitData))]
    public void CanAdd_OverLimit_StillBlocks(string feature, int limit)
    {
        var svc = CreateService(initialNagEligible: true, appCount: 1, countedCredentials: 1);
        Assert.False(CanAdd(svc, feature, limit + 1));
    }

    // --- GetRestrictionMessage tests ---

    [Theory]
    [MemberData(nameof(EvaluationLimitData))]
    public void GetRestrictionMessage_UnderLimit_ReturnsNull(string feature, int limit)
    {
        var svc = CreateService(initialNagEligible: true, appCount: 1, countedCredentials: 1);
        Assert.Null(svc.GetRestrictionMessage(ToFeature(feature), limit - 1));
    }

    [Theory]
    [MemberData(nameof(EvaluationLimitData))]
    public void GetRestrictionMessage_AtLimit_ReturnsNonNull(string feature, int limit)
    {
        var svc = CreateService(initialNagEligible: true, appCount: 1, countedCredentials: 1);
        var message = svc.GetRestrictionMessage(ToFeature(feature), limit);
        Assert.NotNull(message);
        Assert.Contains(limit.ToString(), message);
    }

    // --- Nag latch and cadence tests ---

    [Fact]
    public void Initialize_NoAppsAndNoRealCredentials_DoesNotSetNagEligible()
    {
        var now = DateTime.Today;
        var fixture = CreateServiceFixture(
            appCount: 0,
            initialNagEligible: false,
            countedCredentials: 0,
            credentials: [new CredentialEntry { Sid = "S-1-5-21-111" }]);

        fixture.SessionSaver.Verify(s => s.SaveConfig(), Times.Never);
        Assert.False(fixture.Session.Database.Settings.NagEligible);
        Assert.False(fixture.Service.ShouldShowNag(now));
    }

    [Fact]
    public void Initialize_OneAppButNoRealCredentials_DoesNotSetNagEligible()
    {
        var now = DateTime.Today;
        var fixture = CreateServiceFixture(
            appCount: 1,
            initialNagEligible: false,
            countedCredentials: 0,
            credentials: [new CredentialEntry { Sid = "S-1-5-21-111" }]);

        fixture.SessionSaver.Verify(s => s.SaveConfig(), Times.Never);
        Assert.False(fixture.Session.Database.Settings.NagEligible);
        Assert.False(fixture.Service.ShouldShowNag(now));
    }

    [Fact]
    public void Initialize_NoAppsButHasRealCredential_DoesNotSetNagEligible()
    {
        var now = DateTime.Today;
        var fixture = CreateServiceFixture(
            appCount: 0,
            initialNagEligible: false,
            countedCredentials: 1,
            credentials: [new CredentialEntry { Sid = "S-1-5-21-111" }]);

        fixture.SessionSaver.Verify(s => s.SaveConfig(), Times.Never);
        Assert.False(fixture.Session.Database.Settings.NagEligible);
        Assert.False(fixture.Service.ShouldShowNag(now));
    }

    [Fact]
    public void Initialize_ThresholdReached_SetsNagEligibleAndPersists()
    {
        var now = DateTime.Today;
        var fixture = CreateServiceFixture(
            appCount: 1,
            initialNagEligible: false,
            countedCredentials: 1,
            credentials: [new CredentialEntry { Sid = "S-1-5-21-333" }]);

        fixture.SessionSaver.Verify(s => s.SaveConfig(), Times.Once);
        Assert.True(fixture.Session.Database.Settings.NagEligible);
        Assert.True(fixture.Service.ShouldShowNag(now));
    }

    [Fact]
    public void Initialize_WhenSaveFails_StillMarksNagEligibleAndKeepsStartupFlow()
    {
        var now = DateTime.Today;
        var fixture = CreateServiceFixture(
            appCount: 1,
            initialNagEligible: false,
            countedCredentials: 1,
            credentials: [new CredentialEntry { Sid = "S-1-5-21-333" }],
            configureSessionSaver: saver =>
                saver.Setup(s => s.SaveConfig())
                    .Throws(new IOException("disk full")));

        fixture.SessionSaver.Verify(s => s.SaveConfig(), Times.Once);
        Assert.True(fixture.Session.Database.Settings.NagEligible);
        fixture.Log.Verify(
            l => l.Warn(It.Is<string>(message => message.Contains("failed to persist NagEligible=true"))),
            Times.Once);
        Assert.True(fixture.Service.ShouldShowNag(now));
    }

    [Fact]
    public void ShouldShowNag_StartsFalseWhenNagNotEligible_RegardlessOfCadence()
    {
        var now = DateTime.Today;
        var svc = CreateService(appCount: 1, countedCredentials: 0);
        Assert.False(svc.ShouldShowNag(now));
        Assert.False(svc.ShouldShowNag(now.AddDays(-1)));
    }

    [Fact]
    public void Initialize_DoesNotPersistWhenAlreadyEligible()
    {
        var now = DateTime.Today;
        var fixture = CreateServiceFixture(
            appCount: 1,
            initialNagEligible: true,
            countedCredentials: 1,
            credentials: [new CredentialEntry { Sid = "S-1-5-21-555" }]);

        fixture.SessionSaver.Verify(s => s.SaveConfig(), Times.Never);
        Assert.True(fixture.Session.Database.Settings.NagEligible);
        Assert.True(fixture.Service.ShouldShowNag(now));
    }

    [Fact]
    public void EligibilityPersistsAfterStateShrinks()
    {
        var now = DateTime.Today;
        var fixture = CreateServiceFixture(
            appCount: 1,
            initialNagEligible: false,
            countedCredentials: 1,
            credentials: [new CredentialEntry { Sid = "S-1-5-21-333" }]);
        Assert.True(fixture.Session.Database.Settings.NagEligible);

        fixture.Session.Database.Apps.Clear();
        fixture.Session.CredentialStore.Credentials.Clear();

        Assert.True(fixture.Session.Database.Settings.NagEligible);
        Assert.True(fixture.Service.ShouldShowNag(now));
    }

    [Fact]
    public void ShouldShowNag_NeverShown_ReturnsTrue()
    {
        var now = DateTime.Today;
        var svc = CreateService(initialNagEligible: true, appCount: 1, countedCredentials: 1);
        Assert.True(svc.ShouldShowNag(now));
    }

    [Fact]
    public void ShouldShowNag_ShownToday_ReturnsFalse()
    {
        var now = DateTime.Today;
        var svc = CreateService(initialNagEligible: true, appCount: 1, countedCredentials: 1);
        svc.RecordNagShown(now);
        Assert.False(svc.ShouldShowNag(now));
    }

    [Fact]
    public void ShouldShowNag_ShownYesterday_ReturnsTrue()
    {
        var now = DateTime.Today;
        var svc = CreateService(initialNagEligible: true, appCount: 1, countedCredentials: 1);
        svc.RecordNagShown(now.AddDays(-1));
        Assert.True(svc.ShouldShowNag(now));
    }

    [Fact]
    public void ShouldShowNag_ForwardDate_ReturnsTrue()
    {
        var now = DateTime.Today;
        var svc = CreateService(initialNagEligible: true, appCount: 1, countedCredentials: 1);
        svc.RecordNagShown(now.AddDays(5)); // future date (user tampered)
        Assert.True(svc.ShouldShowNag(now));
    }

    [Fact]
    public void ShouldShowNag_WhenLicensed_ReturnsFalse()
    {
        var now = DateTime.Today;
        var svc = CreateService(initialNagEligible: true, appCount: 1, countedCredentials: 1);
        svc.ActivateLicense(BuildValidKey());
        Assert.False(svc.ShouldShowNag(now));
    }

    [Fact]
    public void ShouldShowNag_LicenseExpiresMidSession_ReturnsTrueAndTransitionsToUnlicensed()
    {
        var epoch = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var now = DateTime.Today;
        var expiryDate = now.AddDays(30);
        var expiryDays = (uint)(expiryDate - epoch).TotalDays;
        var svc = CreateService(appCount: 1, initialNagEligible: true, countedCredentials: 1);
        var activationResult = svc.ActivateLicense(BuildValidKey(expiryDays: expiryDays));
        Assert.Equal(LicenseActivationResult.Success, activationResult);
        Assert.True(svc.IsLicensed);

        var statusChangedCount = 0;
        svc.LicenseStatusChanged += () => statusChangedCount++;

        var shouldNag = svc.ShouldShowNag(expiryDate.AddDays(1));

        Assert.True(shouldNag);
        Assert.False(svc.IsLicensed);
        Assert.Equal(1, statusChangedCount);
    }

    [Fact]
    public void ShouldShowNag_ExpiredIneligibleSession_DoesNotShow()
    {
        var epoch = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var now = DateTime.Today;
        var expiryDate = now.AddDays(30);
        var expiryDays = (uint)(expiryDate - epoch).TotalDays;

        var svc = CreateService(appCount: 0, initialNagEligible: false, countedCredentials: 0);
        var activationResult = svc.ActivateLicense(BuildValidKey(expiryDays: expiryDays));
        Assert.Equal(LicenseActivationResult.Success, activationResult);
        Assert.True(svc.IsLicensed);

        var shouldNag = svc.ShouldShowNag(expiryDate.AddDays(1));

        Assert.False(shouldNag);
        Assert.False(svc.IsLicensed);
    }

    // --- Helpers ---

    private class TestMachineIdProvider(byte[] machineIdHash) : IMachineIdProvider
    {
        public string MachineCode => MachineIdProvider.FormatMachineCode(machineIdHash);
        public byte[] MachineIdHash => (byte[])machineIdHash.Clone();

        public MachineIdentityResult GetMachineIdentity() =>
            new(
                MachineIdentityStatus.Available,
                MachineIdentitySource.SmbiosUuid,
                "TEST",
                (byte[])machineIdHash.Clone(),
                MachineCode,
                null);
    }
}
