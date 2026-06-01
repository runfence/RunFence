using Moq;
using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.Persistence;
using RunFence.Persistence.UI;
using RunFence.Security;
using RunFence.Startup;
using Xunit;

namespace RunFence.Tests;

public class ConfigLoadUnloadServiceTests : IDisposable
{
    private const string ConfigPath = @"C:\extra.ramc";
    private const string BackupPath = @"C:\extra.ramc.lastgood";

    private readonly Mock<IAppConfigService> _appConfigService = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<ILicenseService> _licenseService = new();
    private readonly Mock<ILoadedGoodBackupStore> _loadedGoodBackupStore = new();
    private readonly Mock<ILoadedAppsCleanup> _loadedAppsCleanup = new();
    private readonly Mock<IHandlerMappingService> _handlerMappingService = new();
    private readonly Mock<IDatabaseService> _databaseService = new();
    private readonly Mock<IPinService> _pinService = new();
    private readonly Mock<IAppHandlerRegistrationService> _handlerRegistrationService = new();
    private readonly Mock<IAssociationAutoSetService> _associationAutoSetService = new();
    private readonly Mock<ISecureDesktopRunner> _secureDesktopRunner = new();
    private readonly SecureSecret _pinKey = TestSecretFactory.Create(32);
    private readonly SessionProvider _sessionProvider = new();
    private readonly SessionContext _session;
    private readonly ConfigLoadUnloadService _service;

    public ConfigLoadUnloadServiceTests()
    {
        _session = new SessionContext
{
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore { ArgonSalt = new byte[32] },
        }.WithClonedPinDerivedKey(_pinKey);
        _sessionProvider.SetSession(_session);

        _handlerMappingService
            .Setup(s => s.GetEffectiveHandlerMappings(It.IsAny<AppDatabase>()))
            .Returns(new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase));
        _databaseService
            .Setup(s => s.TryGetAppConfigSaltFromPath(It.IsAny<string>()))
            .Returns((byte[]?)null);
        _loadedGoodBackupStore.Setup(s => s.GetBackupPath(ConfigPath)).Returns(BackupPath);
        string? ignoredWarning = null;
        _loadedGoodBackupStore
            .Setup(s => s.TryPreserveCurrentFile(It.IsAny<string>(), out ignoredWarning))
            .Returns(true);

        var mismatchKeyResolver = new ConfigMismatchKeyResolver(
            _sessionProvider,
            _databaseService.Object,
            _databaseService.Object,
            new ConfigMismatchPinVerifier(_pinService.Object),
            _secureDesktopRunner.Object,
            new OperationGuard());
        var handlerSyncHelper = new HandlerSyncHelper(
            _sessionProvider,
            _handlerRegistrationService.Object,
            _handlerMappingService.Object,
            _associationAutoSetService.Object);

        _service = new ConfigLoadUnloadService(
            _sessionProvider,
            _appConfigService.Object,
            _log.Object,
            _licenseService.Object,
            _loadedGoodBackupStore.Object,
            _loadedAppsCleanup.Object,
            mismatchKeyResolver,
            handlerSyncHelper,
            _handlerMappingService.Object,
            _databaseService.Object,
            _databaseService.Object);
    }

    public void Dispose()
    {
        _pinKey.Dispose();
        _session.Dispose();
    }

    [Fact]
    public void LoadAppConfigBackup_LoadsBackupDirectlyAndSavesOriginalPath()
    {
        var app = new AppEntry { Id = "app1", Name = "App", ExePath = @"C:\app.exe" };
        var backupConfig = new AppConfig { Apps = [app] };
        var staged = new AdditionalConfigLoadData(
            ConfigPath,
            [app],
            [],
            new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase));

        _databaseService.Setup(s => s.TryGetAppConfigSaltFromPath(BackupPath)).Returns((byte[]?)null);
        _databaseService
            .Setup(s => s.LoadAppConfigFromPath(BackupPath, It.IsAny<ISecureSecretSnapshotSource>()))
            .Returns(backupConfig);
        _appConfigService
            .Setup(s => s.ReadAdditionalConfigFromBackup(ConfigPath, backupConfig, _session.Database))
            .Returns(staged);
        _appConfigService
            .Setup(s => s.ApplyAdditionalConfig(staged, _session.Database))
            .Callback(() => _session.Database.Apps.Add(app))
            .Returns([app]);

        var result = _service.LoadAppConfigBackup(ConfigPath);

        Assert.True(result.Succeeded, result.ErrorMessage);
        _databaseService.Verify(
            s => s.LoadAppConfigFromPath(BackupPath, It.IsAny<ISecureSecretSnapshotSource>()),
            Times.Once);
        _appConfigService.Verify(
            s => s.ReadAdditionalConfigFromBackup(ConfigPath, backupConfig, _session.Database),
            Times.Once);
        _appConfigService.Verify(
            s => s.SaveConfigAtPath(
                ConfigPath,
                _session.Database,
                It.IsAny<ISecureSecretSnapshotSource>(),
                _session.CredentialStore.ArgonSalt),
            Times.Once);
        _loadedGoodBackupStore.Verify(s => s.Restore(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void LoadAppConfigBackup_SaveFailureAfterCommit_ReturnsWarningWithoutRollback()
    {
        var app = new AppEntry { Id = "app1", Name = "App", ExePath = @"C:\app.exe" };
        var backupConfig = new AppConfig { Apps = [app] };
        var staged = new AdditionalConfigLoadData(
            ConfigPath,
            [app],
            [],
            new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase));

        _databaseService.Setup(s => s.TryGetAppConfigSaltFromPath(BackupPath)).Returns((byte[]?)null);
        _databaseService
            .Setup(s => s.LoadAppConfigFromPath(BackupPath, It.IsAny<ISecureSecretSnapshotSource>()))
            .Returns(backupConfig);
        _appConfigService
            .Setup(s => s.ReadAdditionalConfigFromBackup(ConfigPath, backupConfig, _session.Database))
            .Returns(staged);
        _appConfigService
            .Setup(s => s.ApplyAdditionalConfig(staged, _session.Database))
            .Callback(() => _session.Database.Apps.Add(app))
            .Returns([app]);
        _appConfigService
            .Setup(s => s.SaveConfigAtPath(
                ConfigPath,
                _session.Database,
                It.IsAny<ISecureSecretSnapshotSource>(),
                _session.CredentialStore.ArgonSalt))
            .Throws(new UnauthorizedAccessException("Access is denied."));

        var result = _service.LoadAppConfigBackup(ConfigPath);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.NotNull(result.Warnings);
        Assert.Contains(result.Warnings!, warning => warning.Contains("Access is denied.", StringComparison.Ordinal));
        _appConfigService.Verify(s => s.UnloadConfig(It.IsAny<string>(), It.IsAny<AppDatabase>()), Times.Never);
        _loadedGoodBackupStore.Verify(s => s.Restore(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void LoadAppConfigBackup_BackupSaltMismatch_CancelledPrompt_ReturnsCancelled()
    {
        var backupSalt = Enumerable.Repeat((byte)0x11, 32).ToArray();

        _databaseService.Setup(s => s.TryGetAppConfigSaltFromPath(BackupPath)).Returns(backupSalt);

        var result = _service.LoadAppConfigBackup(ConfigPath);

        Assert.False(result.Succeeded);
        Assert.Equal("Cancelled.", result.ErrorMessage);
        _secureDesktopRunner.Verify(s => s.Run(It.IsAny<Action>()), Times.Once);
        _databaseService.Verify(
            s => s.LoadAppConfigFromPath(BackupPath, It.IsAny<ISecureSecretSnapshotSource>()),
            Times.Never);
    }

    [Fact]
    public void LoadApps_WhenLicenseEvaluationRejectsLoadedApps_DoesNotPreserveLoadedGoodBackup()
    {
        var app = new AppEntry { Id = "app1", Name = "App", ExePath = @"C:\app.exe" };
        var staged = new AdditionalConfigLoadData(
            ConfigPath,
            [app],
            [],
            new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase));

        _appConfigService
            .Setup(s => s.ReadAdditionalConfig(ConfigPath, It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>()))
            .Returns(staged);
        _databaseService.Setup(s => s.TryGetAppConfigSaltFromPath(ConfigPath)).Returns((byte[]?)null);
        _licenseService
            .Setup(s => s.GetRestrictionMessage(EvaluationFeature.Apps, 0))
            .Returns("limit reached");

        var result = _service.LoadApps(ConfigPath);

        Assert.False(result.Succeeded);
        _appConfigService.Verify(s => s.ApplyAdditionalConfig(It.IsAny<AdditionalConfigLoadData>(), It.IsAny<AppDatabase>()), Times.Never);
        _loadedGoodBackupStore.Verify(
            s => s.TryPreserveCurrentFile(It.IsAny<string>(), out It.Ref<string?>.IsAny),
            Times.Never);
    }

    [Fact]
    public void LoadApps_WhenSaveLoadedConfigReturnsWarning_DoesNotPreserveLoadedGoodBackup()
    {
        var app = new AppEntry { Id = "app1", Name = "App", ExePath = @"C:\app.exe" };
        var staged = new AdditionalConfigLoadData(
            ConfigPath,
            [app],
            [],
            new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase));

        _appConfigService
            .Setup(s => s.ReadAdditionalConfig(ConfigPath, It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>()))
            .Returns(staged);
        _databaseService.Setup(s => s.TryGetAppConfigSaltFromPath(ConfigPath)).Returns((byte[]?)null);
        _appConfigService
            .Setup(s => s.ApplyAdditionalConfig(staged, It.IsAny<AppDatabase>()))
            .Callback(() => _session.Database.Apps.Add(app))
            .Returns([app]);
        _appConfigService
            .Setup(s => s.SaveConfigAtPath(
                ConfigPath,
                _session.Database,
                It.IsAny<ISecureSecretSnapshotSource>(),
                _session.CredentialStore.ArgonSalt))
            .Throws(new UnauthorizedAccessException("Access is denied."));

        var result = _service.LoadApps(ConfigPath);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.NotNull(result.Warnings);
        _loadedGoodBackupStore.Verify(
            s => s.TryPreserveCurrentFile(It.IsAny<string>(), out It.Ref<string?>.IsAny),
            Times.Never);
    }

    [Fact]
    public void LoadApps_WhenEnforcementFails_PreservesLoadedGoodBackupAfterSave()
    {
        var app = new AppEntry { Id = "app1", Name = "App", ExePath = @"C:\app.exe" };
        var staged = new AdditionalConfigLoadData(
            ConfigPath,
            [app],
            [],
            new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase));

        _appConfigService
            .Setup(s => s.ReadAdditionalConfig(ConfigPath, It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>()))
            .Returns(staged);
        _databaseService.Setup(s => s.TryGetAppConfigSaltFromPath(ConfigPath)).Returns((byte[]?)null);
        _appConfigService
            .Setup(s => s.ApplyAdditionalConfig(staged, It.IsAny<AppDatabase>()))
            .Callback(() => _session.Database.Apps.Add(app))
            .Returns([app]);
        _loadedAppsCleanup
            .Setup(s => s.ApplyLoadedAppsEnforcement(It.IsAny<List<AppEntry>>()))
            .Throws(new InvalidOperationException("enforcement failed"));

        var result = _service.LoadApps(ConfigPath);

        Assert.True(result.Succeeded);
        Assert.Contains(result.Warnings!, warning => warning.Contains("enforcement failed", StringComparison.Ordinal));
        _loadedGoodBackupStore.Verify(
            s => s.TryPreserveCurrentFile(ConfigPath, out It.Ref<string?>.IsAny),
            Times.Once);
    }

    [Fact]
    public void LoadApps_WhenHandlerSyncFails_PreservesLoadedGoodBackupAfterSave()
    {
        var app = new AppEntry { Id = "app1", Name = "App", ExePath = @"C:\app.exe" };
        var staged = new AdditionalConfigLoadData(
            ConfigPath,
            [app],
            [],
            new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase)
            {
                ["https"] = new HandlerMappingEntry { AppId = "app1" }
            });

        _appConfigService
            .Setup(s => s.ReadAdditionalConfig(ConfigPath, It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>()))
            .Returns(staged);
        _databaseService.Setup(s => s.TryGetAppConfigSaltFromPath(ConfigPath)).Returns((byte[]?)null);
        _appConfigService
            .Setup(s => s.ApplyAdditionalConfig(staged, It.IsAny<AppDatabase>()))
            .Callback(() => _session.Database.Apps.Add(app))
            .Returns([app]);
        _handlerRegistrationService
            .Setup(s => s.Sync(It.IsAny<Dictionary<string, HandlerMappingEntry>>(), It.IsAny<List<AppEntry>>()))
            .Throws(new InvalidOperationException("handler sync failed"));

        var result = _service.LoadApps(ConfigPath);

        Assert.True(result.Succeeded);
        Assert.Contains(result.Warnings!, warning => warning.Contains("handler sync failed", StringComparison.Ordinal));
        _loadedGoodBackupStore.Verify(
            s => s.TryPreserveCurrentFile(ConfigPath, out It.Ref<string?>.IsAny),
            Times.Once);
    }

    [Fact]
    public void LoadApps_WhenBackupPreservationThrowsUnexpectedException_KeepsLoadSuccessfulWithoutUserWarning()
    {
        var app = new AppEntry { Id = "app1", Name = "App", ExePath = @"C:\app.exe" };
        var staged = new AdditionalConfigLoadData(
            ConfigPath,
            [app],
            [],
            new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase));
        _appConfigService
            .Setup(s => s.ReadAdditionalConfig(ConfigPath, It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>()))
            .Returns(staged);
        _databaseService.Setup(s => s.TryGetAppConfigSaltFromPath(ConfigPath)).Returns((byte[]?)null);
        _appConfigService
            .Setup(s => s.ApplyAdditionalConfig(staged, It.IsAny<AppDatabase>()))
            .Callback(() => _session.Database.Apps.Add(app))
            .Returns([app]);
        _loadedGoodBackupStore
            .Setup(s => s.TryPreserveCurrentFile(ConfigPath, out It.Ref<string?>.IsAny))
            .Throws(new InvalidOperationException("backup preserve exploded"));

        var result = _service.LoadApps(ConfigPath);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Null(result.Warnings);
    }

    [Fact]
    public void UnloadAndRevertConfig_UnloadsConfigAndRevertsApps_WithoutSavingConfig()
    {
        var removedApp = new AppEntry { Id = "app1", Name = "App", ExePath = @"C:\app.exe" };
        _appConfigService
            .Setup(service => service.UnloadConfig(ConfigPath, _session.Database))
            .Returns([removedApp]);

        _service.UnloadAndRevertConfig(ConfigPath, _session.Database);

        _loadedAppsCleanup.Verify(service => service.RevertApps(
            It.Is<IEnumerable<AppEntry>>(apps => apps.Single() == removedApp)), Times.Once);
        _appConfigService.Verify(service => service.SaveConfigForApp(
            It.IsAny<string>(),
            It.IsAny<AppDatabase>(),
            It.IsAny<ISecureSecretSnapshotSource>(),
            It.IsAny<byte[]>()), Times.Never);
        _appConfigService.Verify(service => service.SaveConfigAtPath(
            It.IsAny<string>(),
            It.IsAny<AppDatabase>(),
            It.IsAny<ISecureSecretSnapshotSource>(),
            It.IsAny<byte[]>()), Times.Never);
    }
}
