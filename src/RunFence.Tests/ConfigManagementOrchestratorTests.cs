using Moq;
using RunFence.Acl;
using RunFence.Acl.QuickAccess;
using RunFence.Account;
using RunFence.Apps;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Licensing;
using RunFence.Persistence;
using RunFence.Persistence.UI;
using RunFence.Security;
using RunFence.Startup;
using Xunit;

namespace RunFence.Tests;

public class ConfigManagementOrchestratorTests : IDisposable
{
    private readonly Mock<IPinService> _pinService = new();
    private readonly Mock<IAppConfigService> _appConfigService = new();
    private readonly Mock<IDatabaseService> _databaseService = new();
    private readonly Mock<IAclService> _aclService = new();
    private readonly Mock<IShortcutService> _shortcutService = new();
    private readonly Mock<IBesideTargetShortcutService> _besideTargetShortcutService = new();
    private readonly Mock<IShortcutDiscoveryService> _shortcutDiscovery = new();
    private readonly Mock<IIconService> _iconService = new();
    private readonly Mock<IContextMenuService> _contextMenuService = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<ILicenseService> _licenseService = new();
    private readonly Mock<IAppHandlerRegistrationService> _handlerRegistrationService = new();
    private readonly Mock<IHandlerMappingService> _handlerMappingService = new();
    private readonly Mock<IAssociationAutoSetService> _associationAutoSetService = new();
    private readonly Mock<IFolderHandlerService> _folderHandlerService = new();
    private readonly Mock<IFirewallCleanupService> _firewallCleanupService = new();
    private readonly List<IReadOnlyList<string>> _shortcutConflicts = [];

    private readonly AppDatabase _database = new();
    private readonly ProtectedBuffer _pinKey;
    private readonly ConfigManagementOrchestrator _handler;

    private const string ConfigPath = @"C:\extra.ramc";

    public ConfigManagementOrchestratorTests()
    {
        _pinKey = new ProtectedBuffer(new byte[32], protect: false);
        _shortcutDiscovery.Setup(d => d.CreateTraversalCache()).Returns(() => new ShortcutTraversalCache([]));
        var session = new SessionContext
        {
            Database = _database,
            CredentialStore = new CredentialStore(),
            PinDerivedKey = _pinKey
        };

        _handlerMappingService
            .Setup(s => s.GetEffectiveHandlerMappings(It.IsAny<AppDatabase>()))
            .Returns(new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase));

        _handler = BuildHandler(session,
            onShortcutConflicts: names => _shortcutConflicts.Add(names));
    }

    public void Dispose() => _pinKey.Dispose();

    [Fact]
    public void UnloadApps_RestrictAcl_RevertsAclWithAppIncludedInAllApps()
    {
        // Arrange
        var app = new AppEntry { Id = AppEntry.GenerateId(), Name = "App", ExePath = @"C:\App\app.exe", RestrictAcl = true };
        SetupUnload([app]);

        IReadOnlyList<AppEntry>? capturedAllApps = null;
        _aclService.Setup(s => s.RevertAcl(app, It.IsAny<IReadOnlyList<AppEntry>>()))
            .Callback<AppEntry, IReadOnlyList<AppEntry>>((_, all) => capturedAllApps = all);

        // Act
        _handler.UnloadApps(ConfigPath);

        // Assert
        _aclService.Verify(s => s.RevertAcl(app, It.IsAny<IReadOnlyList<AppEntry>>()), Times.Once);
        Assert.NotNull(capturedAllApps);
        Assert.Contains(app, capturedAllApps);
    }

    [Fact]
    public void UnloadApps_RestrictAcl_PassesAllAppsUnionToRevertAcl()
    {
        // Arrange: a main-database app AND an extra-config app both exist.
        // When unloading the extra config, RevertAcl must receive the union of
        // remaining main-database apps PLUS the reverted extra-config app.
        var mainApp = new AppEntry { Id = AppEntry.GenerateId(), Name = "MainApp", ExePath = @"C:\Main\main.exe" };
        _database.Apps.Add(mainApp);

        var configApp = new AppEntry { Id = AppEntry.GenerateId(), Name = "ConfigApp", ExePath = @"C:\Config\app.exe", RestrictAcl = true };
        SetupUnload([configApp]);

        IReadOnlyList<AppEntry>? capturedAllApps = null;
        _aclService.Setup(s => s.RevertAcl(configApp, It.IsAny<IReadOnlyList<AppEntry>>()))
            .Callback<AppEntry, IReadOnlyList<AppEntry>>((_, all) => capturedAllApps = all);

        // Act
        _handler.UnloadApps(ConfigPath);

        // Assert: allApps includes both the main-database app AND the reverted config app
        Assert.NotNull(capturedAllApps);
        Assert.Contains(mainApp, capturedAllApps);
        Assert.Contains(configApp, capturedAllApps);
    }

    [Fact]
    public void UnloadApps_UrlSchemeApp_SkipsAclAndBesideTargetRevert()
    {
        // Arrange
        var app = new AppEntry { Id = AppEntry.GenerateId(), Name = "App", ExePath = "myapp://", RestrictAcl = true, IsUrlScheme = true };
        SetupUnload([app]);

        // Act
        _handler.UnloadApps(ConfigPath);

        // Assert
        _aclService.Verify(s => s.RevertAcl(It.IsAny<AppEntry>(), It.IsAny<IReadOnlyList<AppEntry>>()), Times.Never);
        _besideTargetShortcutService.Verify(s => s.RemoveBesideTargetShortcut(It.IsAny<AppEntry>()), Times.Never);
    }

    [Fact]
    public void UnloadApps_ManageShortcuts_RevertsShortcuts()
    {
        // Arrange
        var app = new AppEntry { Id = AppEntry.GenerateId(), Name = "App", ExePath = @"C:\App\app.exe", ManageShortcuts = true };
        SetupUnload([app]);

        // Act
        _handler.UnloadApps(ConfigPath);

        // Assert
        _shortcutService.Verify(s => s.RevertShortcuts(app, It.IsAny<ShortcutTraversalCache>()), Times.Once);
    }

    [Fact]
    public void UnloadApps_NoManageShortcuts_SkipsShortcutRevert()
    {
        // Arrange
        var app = new AppEntry { Id = AppEntry.GenerateId(), Name = "App", ExePath = @"C:\App\app.exe", ManageShortcuts = false };
        SetupUnload([app]);

        // Act
        _handler.UnloadApps(ConfigPath);

        // Assert
        _shortcutService.Verify(s => s.RevertShortcuts(It.IsAny<AppEntry>(), It.IsAny<ShortcutTraversalCache>()), Times.Never);
    }

    [Fact]
    public void UnloadApps_RemovesBesideTargetShortcutAndIcon()
    {
        // Arrange
        var app = new AppEntry { Id = AppEntry.GenerateId(), Name = "App", ExePath = @"C:\App\app.exe" };
        SetupUnload([app]);

        // Act
        _handler.UnloadApps(ConfigPath);

        // Assert
        _besideTargetShortcutService.Verify(s => s.RemoveBesideTargetShortcut(app), Times.Once);
        _iconService.Verify(s => s.DeleteIcon(app.Id), Times.Once);
    }

    [Fact]
    public void UnloadApps_RecomputesAncestorAclsAfterRevert()
    {
        // Arrange
        var app = new AppEntry { Id = AppEntry.GenerateId(), Name = "App", ExePath = @"C:\App\app.exe" };
        SetupUnload([app]);

        // Act
        _handler.UnloadApps(ConfigPath);

        // Assert
        _aclService.Verify(s => s.RecomputeAllAncestorAcls(It.IsAny<IReadOnlyList<AppEntry>>()), Times.Once);
    }

    [Fact]
    public void UnloadApps_MultipleApps_RevertsAll()
    {
        // Arrange
        var app1 = new AppEntry { Id = AppEntry.GenerateId(), Name = "App1", ExePath = @"C:\App\a1.exe", RestrictAcl = true, ManageShortcuts = true };
        var app2 = new AppEntry { Id = AppEntry.GenerateId(), Name = "App2", ExePath = @"C:\App\a2.exe", RestrictAcl = true, ManageShortcuts = true };
        SetupUnload([app1, app2]);

        // Act
        _handler.UnloadApps(ConfigPath);

        // Assert
        _aclService.Verify(s => s.RevertAcl(app1, It.IsAny<IReadOnlyList<AppEntry>>()), Times.Once);
        _aclService.Verify(s => s.RevertAcl(app2, It.IsAny<IReadOnlyList<AppEntry>>()), Times.Once);
        _shortcutService.Verify(s => s.RevertShortcuts(app1, It.IsAny<ShortcutTraversalCache>()), Times.Once);
        _shortcutService.Verify(s => s.RevertShortcuts(app2, It.IsAny<ShortcutTraversalCache>()), Times.Once);
    }

    [Fact]
    public void UnloadApps_RevertFailsForOneApp_ContinuesWithOthers()
    {
        // Arrange
        var app1 = new AppEntry { Id = AppEntry.GenerateId(), Name = "App1", ExePath = @"C:\App\a1.exe", ManageShortcuts = true };
        var app2 = new AppEntry { Id = AppEntry.GenerateId(), Name = "App2", ExePath = @"C:\App\a2.exe", ManageShortcuts = true };
        SetupUnload([app1, app2]);

        _shortcutService.Setup(s => s.RevertShortcuts(app1, It.IsAny<ShortcutTraversalCache>()))
            .Throws(new IOException("Access denied"));

        // Act
        var result = _handler.UnloadApps(ConfigPath);

        // Assert
        Assert.True(result);
        _shortcutService.Verify(s => s.RevertShortcuts(app2, It.IsAny<ShortcutTraversalCache>()), Times.Once);
        _log.Verify(l => l.Error(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
    }

    // --- LoadApps ---

    [Fact]
    public void LoadApps_RestrictAcl_CallsApplyAclAndRecomputesAncestors()
    {
        // Arrange
        var app = new AppEntry { Id = AppEntry.GenerateId(), Name = "App", ExePath = @"C:\App\app.exe", RestrictAcl = true };
        SetupLoad([app]);

        // Act
        var (success, _) = _handler.LoadApps(ConfigPath);

        // Assert
        Assert.True(success);
        _aclService.Verify(s => s.ApplyAcl(app, It.IsAny<IReadOnlyList<AppEntry>>()), Times.Once);
        _aclService.Verify(s => s.RecomputeAllAncestorAcls(It.IsAny<IReadOnlyList<AppEntry>>()), Times.Once);
    }

    [Fact]
    public void LoadApps_RestrictAcl_PassesAllAppsUnionToApplyAcl()
    {
        // Arrange: main-database has an existing app; a new app is loaded from a config file.
        // LoadAdditionalConfig adds the loaded app to database.Apps (production behavior),
        // so ApplyAcl receives the full union via database.Apps.
        var mainApp = new AppEntry { Id = AppEntry.GenerateId(), Name = "MainApp", ExePath = @"C:\Main\main.exe" };
        _database.Apps.Add(mainApp);

        var configApp = new AppEntry { Id = AppEntry.GenerateId(), Name = "ConfigApp", ExePath = @"C:\Config\app.exe", RestrictAcl = true };

        // Mimic production: LoadAdditionalConfig adds the app to database.Apps as a side effect
        _databaseService.Setup(s => s.TryGetAppConfigSalt(ConfigPath)).Returns((byte[]?)null);
        _appConfigService
            .Setup(s => s.LoadAdditionalConfig(ConfigPath, _database, It.IsAny<byte[]>()))
            .Callback<string, AppDatabase, byte[]>((_, db, _) => db.Apps.Add(configApp))
            .Returns([configApp]);
        _appConfigService.Setup(s => s.SaveConfigAtPath(ConfigPath, _database, It.IsAny<byte[]>(), It.IsAny<byte[]>()));

        IReadOnlyList<AppEntry>? capturedAllApps = null;
        _aclService.Setup(s => s.ApplyAcl(configApp, It.IsAny<IReadOnlyList<AppEntry>>()))
            .Callback<AppEntry, IReadOnlyList<AppEntry>>((_, all) => capturedAllApps = all);

        // Act
        var (success, _) = _handler.LoadApps(ConfigPath);

        // Assert: allApps includes both the main-database app and the loaded config app
        Assert.True(success);
        Assert.NotNull(capturedAllApps);
        Assert.Contains(mainApp, capturedAllApps);
        Assert.Contains(configApp, capturedAllApps);
    }

    [Fact]
    public void LoadApps_ManageShortcutsConflict_DisablesManageShortcutsOnLoadedApp()
    {
        // Arrange: existing app has ManageShortcuts=true with same ExePath → conflict
        var existingApp = new AppEntry { Id = AppEntry.GenerateId(), Name = "Existing", ExePath = @"C:\App\app.exe", ManageShortcuts = true };
        _database.Apps.Add(existingApp);

        var loadedApp = new AppEntry { Id = AppEntry.GenerateId(), Name = "Loaded", ExePath = @"C:\App\app.exe", ManageShortcuts = true };
        SetupLoad([loadedApp]);

        // Act
        var (success, _) = _handler.LoadApps(ConfigPath);

        // Assert: conflict → ManageShortcuts disabled on loaded app, conflict callback invoked
        Assert.True(success);
        Assert.False(loadedApp.ManageShortcuts);
        Assert.Single(_shortcutConflicts);
        Assert.Contains("Loaded", _shortcutConflicts[0]);
    }

    [Fact]
    public void LoadApps_ManageShortcutsNoConflict_KeepsManageShortcuts()
    {
        // Arrange: existing app has ManageShortcuts=false so no conflict even with same ExePath
        var existingApp = new AppEntry { Id = AppEntry.GenerateId(), Name = "Existing", ExePath = @"C:\App\app.exe", ManageShortcuts = false };
        _database.Apps.Add(existingApp);

        var loadedApp = new AppEntry { Id = AppEntry.GenerateId(), Name = "Loaded", ExePath = @"C:\App\app.exe", ManageShortcuts = true };
        SetupLoad([loadedApp]);

        // Act
        var (success, _) = _handler.LoadApps(ConfigPath);

        // Assert: no conflict → ManageShortcuts unchanged, no warning
        Assert.True(success);
        Assert.True(loadedApp.ManageShortcuts);
        _log.Verify(l => l.Warn(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void LoadApps_ManageShortcuts_CreatesOneTraversalCacheForLoadedApps()
    {
        var app1 = new AppEntry { Id = AppEntry.GenerateId(), Name = "App1", ExePath = @"C:\App\one.exe", ManageShortcuts = true };
        var app2 = new AppEntry { Id = AppEntry.GenerateId(), Name = "App2", ExePath = @"C:\App\two.exe", ManageShortcuts = true };
        SetupLoad([app1, app2]);

        var (success, _) = _handler.LoadApps(ConfigPath);

        Assert.True(success);
        _shortcutDiscovery.Verify(d => d.CreateTraversalCache(), Times.Once);
    }

    [Fact]
    public void LoadApps_NoManageShortcuts_DoesNotCreateTraversalCache()
    {
        var app = new AppEntry { Id = AppEntry.GenerateId(), Name = "App", ExePath = @"C:\App\app.exe", ManageShortcuts = false };
        SetupLoad([app]);

        var (success, _) = _handler.LoadApps(ConfigPath);

        Assert.True(success);
        _shortcutDiscovery.Verify(d => d.CreateTraversalCache(), Times.Never);
    }

    [Fact]
    public void LoadApps_NoManageShortcutsConflict_KeepsManageShortcutsEnabled()
    {
        // Arrange: no existing apps share the ExePath
        var loadedApp = new AppEntry { Id = AppEntry.GenerateId(), Name = "App", ExePath = @"C:\App\app.exe", ManageShortcuts = true };
        SetupLoad([loadedApp]);

        // Act
        var (success, _) = _handler.LoadApps(ConfigPath);

        // Assert
        Assert.True(success);
        Assert.True(loadedApp.ManageShortcuts);
    }

    [Fact]
    public void LoadApps_UrlSchemeApp_SkipsApplyAcl()
    {
        // Arrange
        var app = new AppEntry { Id = AppEntry.GenerateId(), Name = "App", ExePath = "myapp://", RestrictAcl = true, IsUrlScheme = true };
        SetupLoad([app]);

        // Act
        var (success, _) = _handler.LoadApps(ConfigPath);

        // Assert
        Assert.True(success);
        _aclService.Verify(s => s.ApplyAcl(It.IsAny<AppEntry>(), It.IsAny<IReadOnlyList<AppEntry>>()), Times.Never);
    }

    [Fact]
    public void LoadApps_AlwaysRecomputesAncestorAcls()
    {
        // Arrange: app with no special flags — still calls RecomputeAllAncestorAcls
        var app = new AppEntry { Id = AppEntry.GenerateId(), Name = "App", ExePath = @"C:\App\app.exe" };
        SetupLoad([app]);

        // Act
        _handler.LoadApps(ConfigPath);

        // Assert
        _aclService.Verify(s => s.RecomputeAllAncestorAcls(It.IsAny<IReadOnlyList<AppEntry>>()), Times.Once);
    }

    [Fact]
    public void LoadApps_EnforcementThrows_RollsBackAndUnloadsConfig()
    {
        // Arrange: RecomputeAllAncestorAcls (outside per-app catch) throws after apps were loaded
        var app = new AppEntry { Id = AppEntry.GenerateId(), Name = "App", ExePath = @"C:\App\app.exe" };
        SetupLoad([app]);
        _aclService.Setup(s => s.RecomputeAllAncestorAcls(It.IsAny<IReadOnlyList<AppEntry>>()))
            .Throws(new NullReferenceException("simulated failure"));

        // Act
        var (success, errorMessage) = _handler.LoadApps(ConfigPath);

        // Assert: load fails and config is rolled back
        Assert.False(success);
        Assert.NotNull(errorMessage);
        _appConfigService.Verify(s => s.UnloadConfig(ConfigPath, _database), Times.Once);
    }

    // --- LoadApps: evaluation limit ---

    [Fact]
    public void LoadApps_ExceedsEvaluationLimit_RollsBackAndReturnsError()
    {
        // Arrange: fill database to evaluation limit, then attempt to load one more
        for (int i = 0; i < Constants.EvaluationMaxApps; i++)
            _database.Apps.Add(new AppEntry { Id = AppEntry.GenerateId(), Name = $"App{i}", ExePath = $@"C:\App\app{i}.exe" });

        var newApp = new AppEntry { Id = AppEntry.GenerateId(), Name = "Extra", ExePath = @"C:\App\extra.exe" };
        _databaseService.Setup(s => s.TryGetAppConfigSalt(ConfigPath)).Returns((byte[]?)null);
        _appConfigService
            .Setup(s => s.LoadAdditionalConfig(ConfigPath, _database, It.IsAny<byte[]>()))
            .Callback<string, AppDatabase, byte[]>((_, db, _) => db.Apps.Add(newApp))
            .Returns([newApp]);

        var licenseService = new Mock<ILicenseService>();
        licenseService
            .Setup(l => l.GetRestrictionMessage(EvaluationFeature.Apps, It.IsAny<int>()))
            .Returns((EvaluationFeature _, int count) =>
                count >= Constants.EvaluationMaxApps
                    ? $"Evaluation mode allows up to {Constants.EvaluationMaxApps} app entries. Activate a license to remove this limit."
                    : null);

        using var handler = BuildHandlerWithLicense(licenseService.Object);

        // Act
        var (success, errorMessage) = handler.LoadApps(ConfigPath);

        // Assert: load blocked, config rolled back
        Assert.False(success);
        Assert.NotNull(errorMessage);
        Assert.Contains("Cannot load config", errorMessage);
        _appConfigService.Verify(s => s.UnloadConfig(ConfigPath, _database), Times.Once);
    }

    [Fact]
    public void LoadApps_WithinEvaluationLimit_Succeeds()
    {
        // Arrange: one slot below the limit
        for (int i = 0; i < Constants.EvaluationMaxApps - 1; i++)
            _database.Apps.Add(new AppEntry { Id = AppEntry.GenerateId(), Name = $"App{i}", ExePath = $@"C:\App\app{i}.exe" });

        var newApp = new AppEntry { Id = AppEntry.GenerateId(), Name = "Extra", ExePath = @"C:\App\extra.exe" };
        _databaseService.Setup(s => s.TryGetAppConfigSalt(ConfigPath)).Returns((byte[]?)null);
        _appConfigService
            .Setup(s => s.LoadAdditionalConfig(ConfigPath, _database, It.IsAny<byte[]>()))
            .Callback<string, AppDatabase, byte[]>((_, db, _) => db.Apps.Add(newApp))
            .Returns([newApp]);
        _appConfigService.Setup(s => s.SaveConfigAtPath(ConfigPath, _database, It.IsAny<byte[]>(), It.IsAny<byte[]>()));

        var licenseService = new Mock<ILicenseService>();
        licenseService
            .Setup(l => l.GetRestrictionMessage(EvaluationFeature.Apps, It.IsAny<int>()))
            .Returns((EvaluationFeature _, int count) =>
                count >= Constants.EvaluationMaxApps
                    ? $"Evaluation mode allows up to {Constants.EvaluationMaxApps} app entries. Activate a license to remove this limit."
                    : null);

        using var handler = BuildHandlerWithLicense(licenseService.Object);

        // Act
        var (success, _) = handler.LoadApps(ConfigPath);

        // Assert: load succeeds, no rollback
        Assert.True(success);
        _appConfigService.Verify(s => s.UnloadConfig(ConfigPath, _database), Times.Never);
    }

    // --- LoadApps: CryptographicException ---

    [Fact]
    public void LoadApps_CryptographicExceptionDuringLoad_RollsBackAndReturnsError()
    {
        // Arrange: LoadAdditionalConfig throws CryptographicException (wrong PIN for config file)
        var app = new AppEntry { Id = AppEntry.GenerateId(), Name = "App", ExePath = @"C:\App\app.exe" };
        _databaseService.Setup(s => s.TryGetAppConfigSalt(ConfigPath)).Returns((byte[]?)null);
        _appConfigService
            .Setup(s => s.LoadAdditionalConfig(ConfigPath, _database, It.IsAny<byte[]>()))
            .Throws(new System.Security.Cryptography.CryptographicException("Wrong PIN"));

        // Act
        var (success, errorMessage) = _handler.LoadApps(ConfigPath);

        // Assert: failure propagated, config not modified
        Assert.False(success);
        Assert.NotNull(errorMessage);
        Assert.Contains("Wrong PIN", errorMessage);
        // No rollback needed since loadedApps was never set (exception before assignment)
        _appConfigService.Verify(s => s.UnloadConfig(It.IsAny<string>(), It.IsAny<AppDatabase>()), Times.Never);
    }

    // --- LoadApps: save after successful load ---

    [Fact]
    public void LoadApps_SuccessfulLoad_SavesConfigAtPath()
    {
        // Arrange: config loads successfully and must be re-saved with the current PIN key
        var app = new AppEntry { Id = AppEntry.GenerateId(), Name = "App", ExePath = @"C:\App\app.exe" };
        SetupLoad([app]);

        // Act
        var (success, _) = _handler.LoadApps(ConfigPath);

        // Assert: config saved to the loaded path so re-encryption is applied
        Assert.True(success);
        _appConfigService.Verify(s => s.SaveConfigAtPath(
            ConfigPath, _database, It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Once);
    }

    // --- LoadApps: credentials argon salt copied into re-saved config ---

    [Fact]
    public void LoadApps_SuccessfulLoad_SavesConfigWithCredentialStoreArgonSalt()
    {
        // The session's CredentialStore.ArgonSalt is the source of truth for encrypting
        // additional config files. After a successful load, SaveConfigAtPath must receive
        // that exact salt so the config file is encrypted under the same key parameters
        // as the main credential store — this is the "credentials-copy" invariant.
        var specificSalt = new byte[32];
        new Random(7).NextBytes(specificSalt);

        var session = new SessionContext
        {
            Database = _database,
            CredentialStore = new CredentialStore { ArgonSalt = specificSalt },
            PinDerivedKey = _pinKey
        };

        var app = new AppEntry { Id = AppEntry.GenerateId(), Name = "App", ExePath = @"C:\App\app.exe" };
        _databaseService.Setup(s => s.TryGetAppConfigSalt(ConfigPath)).Returns((byte[]?)null);
        _appConfigService.Setup(s => s.LoadAdditionalConfig(ConfigPath, session.Database, It.IsAny<byte[]>()))
            .Returns([app]);
        byte[]? capturedSalt = null;
        _appConfigService.Setup(s => s.SaveConfigAtPath(
                ConfigPath, session.Database, It.IsAny<byte[]>(), It.IsAny<byte[]>()))
            .Callback<string, AppDatabase, byte[], byte[]>((_, _, _, salt) => capturedSalt = salt);

        using var handler = BuildHandler(session);

        // Act
        var (success, _) = handler.LoadApps(ConfigPath);

        // Assert: config re-saved with the session credential store's argon salt
        Assert.True(success);
        Assert.NotNull(capturedSalt);
        Assert.Equal(specificSalt, capturedSalt);
    }

    [Fact]
    public void CleanupAllApps_RemovesFirewallArtifactsThroughCleanupService()
    {
        // Real AppEntryEnforcementHelper + ConfigEnforcementOrchestrator are used here to verify
        // the full delegation chain: CleanupAllApps must reach IFirewallCleanupService.RemoveAll
        // without calling ACL or shortcut services (cleanup is firewall-specific, not enforcement).
        var session = new SessionContext
        {
            Database = _database,
            CredentialStore = new CredentialStore(),
            PinDerivedKey = _pinKey
        };
        var sessionProvider = new SessionProvider();
        sessionProvider.SetSession(session);
        var enforcementHelper = new AppEntryEnforcementHelper(
            _aclService.Object,
            _shortcutService.Object,
            _besideTargetShortcutService.Object,
            _iconService.Object,
            new Mock<ISidNameCacheService>().Object,
            new Mock<IInteractiveUserDesktopProvider>().Object,
            new Mock<ILoggingService>().Object);
        var enforcementHandler = new ConfigEnforcementOrchestrator(
            sessionProvider,
            _aclService.Object,
            _iconService.Object,
            _contextMenuService.Object,
            _log.Object,
            enforcementHelper,
            _shortcutDiscovery.Object,
            _handlerRegistrationService.Object,
            _associationAutoSetService.Object,
            _folderHandlerService.Object,
            _firewallCleanupService.Object);

        var result = enforcementHandler.CleanupAllApps(isEnforcementInProgress: false, isOperationInProgress: false);

        Assert.Equal(CleanupAllAppsResult.ReadyToExit, result);
        _firewallCleanupService.Verify(f => f.RemoveAll(_database), Times.Once);
        // Cleanup must not trigger ACL or shortcut enforcement — those belong to Enforce/Load paths.
        _aclService.Verify(a => a.ApplyAcl(It.IsAny<AppEntry>(), It.IsAny<IReadOnlyList<AppEntry>>()), Times.Never);
        _shortcutService.Verify(s => s.EnforceShortcuts(It.IsAny<IEnumerable<AppEntry>>(), It.IsAny<string>(), It.IsAny<ShortcutTraversalCache>()), Times.Never);
    }

    // --- LoadApps: salt mismatch ---

    [Fact]
    public void LoadApps_SaltMismatch_PromptsPinViaMismatchKey()
    {
        // Arrange: file has a different salt than the credential store
        var fileSalt = new byte[32];
        fileSalt[0] = 0xFF; // differs from the all-zero CredentialStore.ArgonSalt
        _databaseService.Setup(s => s.TryGetAppConfigSalt(ConfigPath)).Returns(fileSalt);

        var secureDesktop = new Mock<ISecureDesktopRunner>();
        // Do NOT call action — simulates PIN dialog cancel
        secureDesktop.Setup(s => s.Run(It.IsAny<Action>()));

        using var handler = BuildHandler(
            new SessionContext
            {
                Database = _database,
                CredentialStore = new CredentialStore(),
                PinDerivedKey = _pinKey
            },
            secureDesktop: secureDesktop.Object);

        // Act: salt mismatch → secure desktop PIN prompt → cancelled → Cancelled result
        var (success, errorMessage) = handler.LoadApps(ConfigPath);

        // Assert: PIN dialog was invoked and load was cancelled
        Assert.False(success);
        Assert.Equal("Cancelled.", errorMessage);
        secureDesktop.Verify(s => s.Run(It.IsAny<Action>()), Times.Once);
        _appConfigService.Verify(s => s.LoadAdditionalConfig(It.IsAny<string>(), It.IsAny<AppDatabase>(),
            It.IsAny<byte[]>()), Times.Never);
    }

    private ConfigManagementOrchestrator BuildHandler(
        SessionContext session,
        Action<IReadOnlyList<string>>? onShortcutConflicts = null,
        ISecureDesktopRunner? secureDesktop = null,
        ILicenseService? licenseService = null)
    {
        var sessionProvider = new SessionProvider();
        sessionProvider.SetSession(session);
        var enforcementHelper = new AppEntryEnforcementHelper(
            _aclService.Object, _shortcutService.Object, _besideTargetShortcutService.Object,
            _iconService.Object, new Mock<ISidNameCacheService>().Object,
            new Mock<IInteractiveUserDesktopProvider>().Object, new Mock<ILoggingService>().Object);
        var enforcementHandler = new ConfigEnforcementOrchestrator(
            sessionProvider, _aclService.Object, _iconService.Object,
            _contextMenuService.Object, _log.Object, enforcementHelper,
            _shortcutDiscovery.Object,
            _handlerRegistrationService.Object, _associationAutoSetService.Object,
            _folderHandlerService.Object, _firewallCleanupService.Object);
        var mismatchKeyResolver = new ConfigMismatchKeyResolver(
            sessionProvider, _pinService.Object, _databaseService.Object,
            secureDesktop ?? new SecureDesktopHelper(), new OperationGuard());
        var handlerSyncHelper = new HandlerSyncHelper(sessionProvider,
            _handlerRegistrationService.Object, _handlerMappingService.Object,
            _associationAutoSetService.Object);
        return new ConfigManagementOrchestrator(
            sessionProvider, _appConfigService.Object,
            _log.Object, enforcementHandler,
            mismatchKeyResolver, handlerSyncHelper, _handlerMappingService.Object,
            licenseService ?? _licenseService.Object,
            new Mock<IQuickAccessPinService>().Object,
            onShortcutConflicts: onShortcutConflicts);
    }

    private ConfigManagementOrchestrator BuildHandlerWithLicense(ILicenseService licenseService)
    {
        var session = new SessionContext
        {
            Database = _database,
            CredentialStore = new CredentialStore(),
            PinDerivedKey = _pinKey
        };
        return BuildHandler(session, licenseService: licenseService);
    }

    private void SetupLoad(List<AppEntry> apps)
    {
        // Returning null salt means no mismatch (no PIN dialog needed)
        _databaseService.Setup(s => s.TryGetAppConfigSalt(ConfigPath)).Returns((byte[]?)null);
        _appConfigService.Setup(s => s.LoadAdditionalConfig(ConfigPath, _database, It.IsAny<byte[]>()))
            .Returns(apps);
        _appConfigService.Setup(s => s.SaveConfigAtPath(ConfigPath, _database, It.IsAny<byte[]>(), It.IsAny<byte[]>()));
    }

    private void SetupUnload(List<AppEntry> apps)
    {
        _appConfigService.Setup(s => s.UnloadConfig(ConfigPath, _database)).Returns(apps);
    }
}
