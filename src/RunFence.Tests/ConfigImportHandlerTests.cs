using Moq;
using RunFence.Acl;
using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.Persistence;
using RunFence.Persistence.UI;
using Xunit;

namespace RunFence.Tests;

public class ConfigImportHandlerTests : IDisposable
{
    private readonly List<SessionContext> _sessions = [];

    private sealed record RealAppConfigHarness(
        AppConfigService AppConfigService,
        AppConfigIndex Index,
        GrantIntentOwnershipProjectionService OwnershipProjection,
        HandlerMappingService HandlerMappings,
        Mock<IDatabaseService> DatabaseService);

    public void Dispose()
    {
        foreach (var session in _sessions)
        {
            session.Dispose();
        }
    }

    private ConfigImportHandler BuildHandler(
        AppDatabase database,
        CredentialStore store,
        SecureSecret pinKey,
        Mock<IAppConfigService>? appConfigService = null,
        Mock<IHandlerMappingService>? handlerMappingService = null,
        Mock<ILoggingService>? log = null,
        Mock<IAppEntryIdGenerator>? idGenerator = null,
        Mock<IGrantInspectionService>? grantInspection = null,
        GrantIntentOwnershipProjectionService? ownershipProjection = null)
    {
        // Track which mocks were created here vs passed in — only apply defaults to locally-created mocks
        // so caller-configured setups are not overridden (Moq uses last-setup-wins semantics).
        bool appConfigCreatedHere = appConfigService == null;
        bool idGeneratorCreatedHere = idGenerator == null;
        bool grantInspectionCreatedHere = grantInspection == null;
        appConfigService ??= new Mock<IAppConfigService>();
        handlerMappingService ??= new Mock<IHandlerMappingService>();
        log ??= new Mock<ILoggingService>();
        idGenerator ??= new Mock<IAppEntryIdGenerator>();
        grantInspection ??= new Mock<IGrantInspectionService>();
        var licenseService = new Mock<ILicenseService>();
        var sessionProvider = new Mock<ISessionProvider>();
        ownershipProjection ??= new GrantIntentOwnershipProjectionService();

        licenseService.Setup(l => l.IsLicensed).Returns(true);
        if (appConfigCreatedHere)
        {
            appConfigService.Setup(s => s.HasLoadedConfigs).Returns(false);
            appConfigService.Setup(s => s.GetConfigPath(It.IsAny<string>())).Returns((string?)null);
            appConfigService.Setup(s => s.GetLoadedConfigPaths()).Returns(Array.Empty<string>());
        }

        if (idGeneratorCreatedHere)
        {
            idGenerator.Setup(g => g.GenerateUniqueId(It.IsAny<IEnumerable<string>>()))
                .Returns(() => Guid.NewGuid().ToString("N"));
        }

        if (grantInspectionCreatedHere)
        {
            // Default: no ACE on disk — main-config grants not preserved (Available=0 is Moq default,
            // which would incorrectly preserve all grants, so we set Broken explicitly)
            grantInspection.Setup(g => g.CheckGrantStatus(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                .Returns(PathAclStatus.Broken);
        }

        var session = new SessionContext
{
            Database = database,
            CredentialStore = store,
        }.WithClonedPinDerivedKey(pinKey);
        _sessions.Add(session);
        sessionProvider.Setup(s => s.GetSession()).Returns(session);
        ownershipProjection.CaptureMainOwnershipBaseline(database);

        var fileParserInstance = new ConfigImportFileParser();
        var preservationCollectorInstance = new MainConfigImportPreservationCollector(ownershipProjection);
        var evaluationValidatorInstance = new MainConfigImportEvaluationValidator(licenseService.Object, appConfigService.Object);
        var appIdValidatorInstance = new AppIdValidator();
        var repairServiceInstance = new MainConfigImportRepairService(
            appConfigService.Object,
            handlerMappingService.Object,
            log.Object,
            idGenerator.Object,
            appIdValidatorInstance);
        var applyServiceInstance = new MainConfigImportApplyService(
            appConfigService.Object,
            repairServiceInstance,
            ownershipProjection,
            grantInspection.Object);
        return new ConfigImportHandler(
            appConfigService.Object, sessionProvider.Object, log.Object,
            fileParserInstance, preservationCollectorInstance, evaluationValidatorInstance,
            repairServiceInstance, applyServiceInstance);
    }

    private static RealAppConfigHarness CreateRealAppConfigHarness(
        SessionContext session,
        Mock<ILoggingService>? log = null)
    {
        log ??= new Mock<ILoggingService>();
        var databaseService = new Mock<IDatabaseService>();
        var appIdValidator = new AppIdValidator();
        var sessionProvider = new Mock<ISessionProvider>();
        sessionProvider.Setup(provider => provider.GetSession()).Returns(session);
        var configSaveOrchestrator = new ConfigSaveOrchestrator(
            sessionProvider.Object,
            () => new InlineUiThreadInvoker(action => action()),
            databaseService.Object,
            new Mock<IAppConfigService>().Object,
            new Mock<IHandlerMappingService>().Object);
        var ownershipProjection = new GrantIntentOwnershipProjectionService();
        var mainStore = new MainGrantIntentStore(
            sessionProvider.Object,
            configSaveOrchestrator,
            ownershipProjection);
        var grantIntentStoreProvider = new GrantIntentStoreProvider(
            mainStore,
            configSaveOrchestrator,
            ownershipProjection);
        var index = new AppConfigIndex(ownershipProjection, appIdValidator);
        var handlerMappings = new HandlerMappingService(index);
        var appConfigService = new AppConfigService(
            log.Object,
            index,
            ownershipProjection,
            () => grantIntentStoreProvider,
            handlerMappings,
            databaseService.Object,
            new AppConfigSaveHelper(() => grantIntentStoreProvider, handlerMappings, databaseService.Object),
            new AppEntryIdGenerator(),
            appIdValidator);

        return new RealAppConfigHarness(appConfigService, index, ownershipProjection, handlerMappings, databaseService);
    }

    private static ConfigImportHandler BuildHandlerWithRealAppConfigService(
        SessionContext session,
        AppConfigService appConfigService,
        GrantIntentOwnershipProjectionService ownershipProjection,
        IHandlerMappingService? handlerMappingService = null,
        Mock<ILoggingService>? log = null,
        Mock<IAppEntryIdGenerator>? idGenerator = null,
        Mock<IGrantInspectionService>? grantInspection = null)
    {
        bool idGeneratorCreatedHere = idGenerator == null;
        bool grantInspectionCreatedHere = grantInspection == null;
        handlerMappingService ??= new Mock<IHandlerMappingService>().Object;
        log ??= new Mock<ILoggingService>();
        idGenerator ??= new Mock<IAppEntryIdGenerator>();
        grantInspection ??= new Mock<IGrantInspectionService>();
        var licenseService = new Mock<ILicenseService>();
        var sessionProvider = new Mock<ISessionProvider>();

        licenseService.Setup(l => l.IsLicensed).Returns(true);
        if (idGeneratorCreatedHere)
        {
            idGenerator.Setup(g => g.GenerateUniqueId(It.IsAny<IEnumerable<string>>()))
                .Returns(() => Guid.NewGuid().ToString("N"));
        }

        if (grantInspectionCreatedHere)
        {
            grantInspection.Setup(g => g.CheckGrantStatus(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                .Returns(PathAclStatus.Broken);
        }

        sessionProvider.Setup(s => s.GetSession()).Returns(session);
        ownershipProjection.CaptureMainOwnershipBaseline(session.Database);

        var fileParserInstance = new ConfigImportFileParser();
        var preservationCollectorInstance = new MainConfigImportPreservationCollector(ownershipProjection);
        var evaluationValidatorInstance = new MainConfigImportEvaluationValidator(licenseService.Object, appConfigService);
        var appIdValidatorInstance = new AppIdValidator();
        var repairServiceInstance = new MainConfigImportRepairService(
            appConfigService,
            handlerMappingService,
            log.Object,
            idGenerator.Object,
            appIdValidatorInstance);
        var applyServiceInstance = new MainConfigImportApplyService(
            appConfigService,
            repairServiceInstance,
            ownershipProjection,
            grantInspection.Object);
        return new ConfigImportHandler(
            appConfigService,
            sessionProvider.Object,
            log.Object,
            fileParserInstance,
            preservationCollectorInstance,
            evaluationValidatorInstance,
            repairServiceInstance,
            applyServiceInstance);
    }

    private static ConfigImportHandler CreateHandlerForSession(
        SessionContext session,
        IAppConfigService appConfigService,
        ILoggingService? log = null)
    {
        var sessionProvider = new Mock<ISessionProvider>();
        sessionProvider.Setup(s => s.GetSession()).Returns(session);
        var fileParserInstance = new ConfigImportFileParser();
        var ownershipProjection = new GrantIntentOwnershipProjectionService();
        ownershipProjection.CaptureMainOwnershipBaseline(session.Database);
        var preservationCollectorInstance = new MainConfigImportPreservationCollector(ownershipProjection);
        var licenseService = new Mock<ILicenseService>();
        licenseService.Setup(l => l.IsLicensed).Returns(true);
        var evaluationValidatorInstance = new MainConfigImportEvaluationValidator(licenseService.Object, appConfigService);
        var appIdValidatorInstance = new AppIdValidator();
        var repairServiceInstance = new MainConfigImportRepairService(
            appConfigService,
            new Mock<IHandlerMappingService>().Object,
            log ?? new Mock<ILoggingService>().Object,
            new Mock<IAppEntryIdGenerator>().Object,
            appIdValidatorInstance);
        var applyServiceInstance = new MainConfigImportApplyService(
            appConfigService,
            repairServiceInstance,
            ownershipProjection,
            new Mock<IGrantInspectionService>().Object);
        return new ConfigImportHandler(
            appConfigService,
            sessionProvider.Object,
            log ?? new Mock<ILoggingService>().Object,
            fileParserInstance,
            preservationCollectorInstance,
            evaluationValidatorInstance,
            repairServiceInstance,
            applyServiceInstance);
    }

    private static void WriteJsonToFile(AppDatabase db, string path)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(db, JsonDefaults.Options);
        File.WriteAllText(path, json);
    }

    private static string CaptureDatabaseState(AppDatabase database) =>
        System.Text.Json.JsonSerializer.Serialize(database.CreateSnapshot(), JsonDefaults.Options);

    [Fact]
    public void ImportMainConfig_AppContainers_FullReplaceFromImport()
    {
        // Arrange: database already has "existing-container"; imported config has the same plus "new-container".
        // Full replace: both containers from import present; existing-container has the import's DisplayName.
        using var pinKey = TestSecretFactory.Create(32);
        var database = new AppDatabase();
        database.AppContainers.Add(new AppContainerEntry { Name = "existing-container", DisplayName = "Existing" });
        var handler = BuildHandler(database, new CredentialStore(), pinKey);

        var importedDb = new AppDatabase();
        importedDb.AppContainers.Add(new AppContainerEntry { Name = "existing-container", DisplayName = "Imported" });
        importedDb.AppContainers.Add(new AppContainerEntry { Name = "new-container", DisplayName = "New" });

        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var tempFile = Path.Combine(tempDir.Path, "config.json");
        WriteJsonToFile(importedDb, tempFile);

        // Act
        handler.ImportMainConfig(tempFile, null);

        // Assert: both containers present; existing-container now has import's DisplayName
        Assert.Equal(2, database.AppContainers.Count);
        Assert.Contains(database.AppContainers, c =>
            string.Equals(c.Name, "existing-container", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(database.AppContainers, c =>
            string.Equals(c.Name, "new-container", StringComparison.OrdinalIgnoreCase));
        var existing = database.AppContainers.First(c =>
            string.Equals(c.Name, "existing-container", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Imported", existing.DisplayName);
    }

    // ── AppContainers case-insensitive duplicate check ───────────────────────

    [Fact]
    public void ImportMainConfig_AppContainers_FullReplaceOverwritesExisting()
    {
        // Arrange: existing container named "MyContainer"; imported container "mycontainer" (different case).
        // Full replace: single container with import's name/DisplayName (old one fully replaced).
        using var pinKey = TestSecretFactory.Create(32);
        var database = new AppDatabase();
        database.AppContainers.Add(new AppContainerEntry { Name = "MyContainer", DisplayName = "Original" });
        var handler = BuildHandler(database, new CredentialStore(), pinKey);

        var importedDb = new AppDatabase();
        importedDb.AppContainers.Add(new AppContainerEntry { Name = "mycontainer", DisplayName = "Imported" });

        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var tempFile = Path.Combine(tempDir.Path, "config.json");
        WriteJsonToFile(importedDb, tempFile);

        // Act
        handler.ImportMainConfig(tempFile, null);

        // Assert: single container with import's name and DisplayName
        Assert.Single(database.AppContainers);
        Assert.Equal("mycontainer", database.AppContainers[0].Name);
        Assert.Equal("Imported", database.AppContainers[0].DisplayName);
    }

    // ── ID collision repair ──────────────────────────────────────────────────

    [Fact]
    public void ImportMainConfig_ImportedAppIdCollidesWithAdditionalApp_AdditionalAppGetsNewId()
    {
        // Arrange: an additional-config app has ID "shared-id".
        // The imported main-config app also has "shared-id". The import must assign a new ID
        // to the additional app (preserving the imported app's ID) and update AppConfigIndex.
        using var pinKey = TestSecretFactory.Create(32);
        var database = new AppDatabase();
        var additionalApp = new AppEntry { Id = "shared-id", Name = "AdditionalApp", AccountSid = "S-1-5-21-0-0-0-1" };
        database.Apps.Add(additionalApp);

        var appConfigService = new Mock<IAppConfigService>();
        var newIdCount = 0;
        var idGenerator = new Mock<IAppEntryIdGenerator>();
        idGenerator.Setup(g => g.GenerateUniqueId(It.IsAny<IEnumerable<string>>()))
            .Returns(() => $"new-id-{++newIdCount}");

        var handler = BuildHandler(database, new CredentialStore(), pinKey,
            appConfigService: appConfigService, idGenerator: idGenerator);

        // Configure appConfigService after BuildHandler: BuildHandler skips defaults for caller-provided
        // mocks, so these are the only setups on appConfigService.
        appConfigService.Setup(s => s.GetConfigPath("shared-id")).Returns(@"C:\extra.rfn");
        appConfigService.Setup(s => s.GetConfigPath(It.Is<string>(id => id != "shared-id"))).Returns((string?)null);
        appConfigService.Setup(s => s.GetLoadedConfigPaths()).Returns([@"C:\extra.rfn"]);
        appConfigService.Setup(s => s.HasLoadedConfigs).Returns(true);

        var importedDb = new AppDatabase();
        importedDb.Apps.Add(new AppEntry { Id = "shared-id", Name = "ImportedApp" });

        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var tempFile = Path.Combine(tempDir.Path, "config.json");
        WriteJsonToFile(importedDb, tempFile);

        // Act
        handler.ImportMainConfig(tempFile, null);

        // Assert: two apps in database (imported + additional), additional one got new ID
        Assert.Equal(2, database.Apps.Count);
        var importedApp = database.Apps.First(a => a.Name == "ImportedApp");
        Assert.Equal("shared-id", importedApp.Id);
        var remainingAdditional = database.Apps.First(a => a.Name == "AdditionalApp");
        Assert.NotEqual("shared-id", remainingAdditional.Id);
        appConfigService.Verify(s => s.RemoveApp("shared-id"), Times.Once);
        appConfigService.Verify(s => s.AssignApp(It.IsAny<string>(), @"C:\extra.rfn"), Times.Once);
    }

    [Fact]
    public void ImportMainConfig_TwoImportedAppsWithSameId_SecondGetsNewId()
    {
        // Arrange: the imported file has two apps with the same ID "dup-id".
        // The collision repair must assign a unique ID to the second one.
        using var pinKey = TestSecretFactory.Create(32);
        var database = new AppDatabase();

        var newIdCount = 0;
        var idGenerator = new Mock<IAppEntryIdGenerator>();
        idGenerator.Setup(g => g.GenerateUniqueId(It.IsAny<IEnumerable<string>>()))
            .Returns(() => $"fixed-{++newIdCount}");

        var handler = BuildHandler(database, new CredentialStore(), pinKey, idGenerator: idGenerator);

        var importedDb = new AppDatabase();
        importedDb.Apps.Add(new AppEntry { Id = "dup-id", Name = "App1" });
        importedDb.Apps.Add(new AppEntry { Id = "dup-id", Name = "App2" });

        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var tempFile = Path.Combine(tempDir.Path, "config.json");
        WriteJsonToFile(importedDb, tempFile);

        // Act
        handler.ImportMainConfig(tempFile, null);

        // Assert: both apps present, with distinct IDs
        Assert.Equal(2, database.Apps.Count);
        var ids = database.Apps.Select(a => a.Id).ToList();
        Assert.Equal(2, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    // ── Orphaned SID grant cleanup ───────────────────────────────────────────

    [Fact]
    public void ImportMainConfig_OrphanedSid_NotInIncomingOrAdditional_DoesNotInvokeFilesystemCleanup()
    {
        // Arrange: current main config has an app with SID "S-1-orphan".
        // The imported config does NOT include that SID. No additional config uses it.
        // The import should replace runtime state and not call filesystem grant cleanup.
        const string orphanSid = "S-1-5-21-0-0-0-9999";
        const string orphanGrantPath = @"C:\OrphanGrant";
        using var pinKey = TestSecretFactory.Create(32);
        var database = new AppDatabase();
        database.Apps.Add(new AppEntry { Id = "old-app", Name = "OldApp", AccountSid = orphanSid });
        database.GetOrCreateAccount(orphanSid).Grants.Add(new GrantedPathEntry { Path = orphanGrantPath });

        var handler = BuildHandler(database, new CredentialStore(), pinKey);

        var importedDb = new AppDatabase();
        importedDb.Apps.Add(new AppEntry { Id = "new-app", Name = "NewApp", AccountSid = "S-1-5-21-0-0-0-1001" });

        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var tempFile = Path.Combine(tempDir.Path, "config.json");
        WriteJsonToFile(importedDb, tempFile);

        // Act
        handler.ImportMainConfig(tempFile, null);

        // Assert: runtime state contains only imported SID and no orphaned SID account remains.
        Assert.Null(database.GetAccount(orphanSid));
    }

    [Fact]
    public void ImportMainConfig_OrphanedGrantInspectionSkipped_AppliesImportAndAdditionalAppRename()
    {
        const string orphanSid = "S-1-5-21-0-0-0-9999";
        const string sharedId = "shared-id";
        const string additionalConfigPath = @"C:\extra.rfn";
        const string preservedGrantPath = @"C:\Preserved";

        using var pinKey = TestSecretFactory.Create(32);
        var database = new AppDatabase();
        database.Apps.Add(new AppEntry { Id = "old-main-app", Name = "OldMainApp", AccountSid = orphanSid });
        database.Apps.Add(new AppEntry { Id = sharedId, Name = "AdditionalApp", AccountSid = "S-1-5-21-0-0-0-1002" });
        database.GetOrCreateAccount(orphanSid).Grants.Add(new GrantedPathEntry
        {
            Path = preservedGrantPath,
            IsDeny = false,
            IsTraverseOnly = false
        });

        var appConfigService = new Mock<IAppConfigService>();
        var appConfigSnapshot = new AppConfigRuntimeStateSnapshot(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [sharedId] = additionalConfigPath
            },
            [additionalConfigPath],
            new Dictionary<string, IReadOnlyDictionary<string, HandlerMappingEntry>>(StringComparer.OrdinalIgnoreCase));
        appConfigService.Setup(s => s.GetConfigPath(sharedId)).Returns(additionalConfigPath);
        appConfigService.Setup(s => s.GetConfigPath(It.Is<string>(id => !string.Equals(id, sharedId, StringComparison.OrdinalIgnoreCase))))
            .Returns((string?)null);
        appConfigService.Setup(s => s.GetLoadedConfigPaths()).Returns([additionalConfigPath]);
        appConfigService.Setup(s => s.HasLoadedConfigs).Returns(true);
        appConfigService.Setup(s => s.CaptureRuntimeStateSnapshot()).Returns(appConfigSnapshot);

        var grantInspection = new Mock<IGrantInspectionService>();
        grantInspection.Setup(g => g.CheckGrantStatus(preservedGrantPath, orphanSid, false))
            .Throws(new InvalidOperationException("grant inspection failed"));

        var idGenerator = new Mock<IAppEntryIdGenerator>();
        idGenerator.Setup(g => g.GenerateUniqueId(It.IsAny<IEnumerable<string>>()))
            .Returns("renamed-additional-app");

        var handler = BuildHandler(
            database,
            new CredentialStore(),
            pinKey,
            appConfigService: appConfigService,
            idGenerator: idGenerator,
            grantInspection: grantInspection);

        var importedDb = new AppDatabase();
        importedDb.Apps.Add(new AppEntry { Id = sharedId, Name = "ImportedMainApp", AccountSid = "S-1-5-21-0-0-0-1001" });

        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var tempFile = Path.Combine(tempDir.Path, "config.json");
        WriteJsonToFile(importedDb, tempFile);

        handler.ImportMainConfig(tempFile, null);

        appConfigService.Verify(s => s.RemoveApp(sharedId), Times.Once);
        appConfigService.Verify(s => s.AssignApp("renamed-additional-app", additionalConfigPath), Times.Once);
        appConfigService.Verify(s => s.RestoreRuntimeStateSnapshot(It.IsAny<AppConfigRuntimeStateSnapshot>()), Times.Never);
        grantInspection.Verify(g => g.CheckGrantStatus(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        Assert.DoesNotContain(database.Apps, app => app.Id == "old-main-app");
        Assert.Contains(database.Apps, app => app.Id == sharedId && app.Name == "ImportedMainApp");
        Assert.Contains(database.Apps, app => app.Id == "renamed-additional-app" && app.Name == "AdditionalApp");
    }

    [Fact]
    public void ImportMainConfig_OrphanedGrantInspectionSkipped_KeepsAdditionalOwnershipRename()
    {
        const string orphanSid = "S-1-5-21-0-0-0-9999";
        const string sharedId = "shared-id";
        const string renamedId = "renamed-additional-app";
        const string preservedGrantPath = @"C:\Preserved";

        using var pinKey = TestSecretFactory.Create(32);
        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var additionalConfigPath = Path.Combine(tempDir.Path, "extra-config.ramc");
        var database = new AppDatabase();
        database.Apps.Add(new AppEntry { Id = "old-main-app", Name = "OldMainApp", AccountSid = orphanSid });
        database.GetOrCreateAccount(orphanSid).Grants.Add(new GrantedPathEntry
        {
            Path = preservedGrantPath,
            IsDeny = false,
            IsTraverseOnly = false
        });

        var log = new Mock<ILoggingService>();
        using var session = new SessionContext
        {
            Database = database,
            CredentialStore = new CredentialStore()
        }.WithClonedPinDerivedKey(pinKey);
        var harness = CreateRealAppConfigHarness(session, log);
        harness.DatabaseService
            .Setup(service => service.LoadAppConfigFromPath(additionalConfigPath, It.IsAny<ISecureSecretSnapshotSource>()))
            .Returns(new AppConfig
            {
                Apps =
                [
                    new AppEntry
                    {
                        Id = sharedId,
                        Name = "AdditionalApp",
                        AccountSid = "S-1-5-21-0-0-0-1002"
                    }
                ],
                HandlerMappings = new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase)
                {
                    ["http"] = new(sharedId)
                }
            });
        File.WriteAllText(additionalConfigPath, "stub");
        harness.AppConfigService.LoadAdditionalConfig(additionalConfigPath, database, session.PinDerivedKey);

        var grantInspection = new Mock<IGrantInspectionService>();
        grantInspection.Setup(g => g.CheckGrantStatus(preservedGrantPath, orphanSid, false))
            .Throws(new InvalidOperationException("grant inspection failed"));
        var idGenerator = new Mock<IAppEntryIdGenerator>();
        idGenerator.Setup(g => g.GenerateUniqueId(It.IsAny<IEnumerable<string>>()))
            .Returns(renamedId);

        var handler = BuildHandlerWithRealAppConfigService(
            session,
            harness.AppConfigService,
            harness.OwnershipProjection,
            handlerMappingService: harness.HandlerMappings,
            log: log,
            idGenerator: idGenerator,
            grantInspection: grantInspection);

        var importedDb = new AppDatabase();
        importedDb.Apps.Add(new AppEntry { Id = sharedId, Name = "ImportedMainApp", AccountSid = "S-1-5-21-0-0-0-1001" });

        var tempFile = Path.Combine(tempDir.Path, "config.json");
        WriteJsonToFile(importedDb, tempFile);

        handler.ImportMainConfig(tempFile, null);

        grantInspection.Verify(g => g.CheckGrantStatus(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        Assert.DoesNotContain(database.Apps, app => app.Id == "old-main-app");
        Assert.Contains(database.Apps, app => app.Id == sharedId && app.Name == "ImportedMainApp");
        Assert.Contains(database.Apps, app => app.Id == renamedId && app.Name == "AdditionalApp");
        Assert.Null(harness.AppConfigService.GetConfigPath(sharedId));
        Assert.Equal(additionalConfigPath, harness.AppConfigService.GetConfigPath(renamedId));
        Assert.Equal(renamedId, harness.HandlerMappings.GetHandlerMappingsForConfig(additionalConfigPath)!["http"].AppId);
    }

    [Fact]
    public void ImportMainConfig_OrphanedGrantCleanupSkipped_DoesNotInvokeFilesystemCleanup()
    {
        const string orphanSid = "S-1-5-21-0-0-0-9999";
        const string mainGrantSid = "S-1-5-21-0-0-0-1001";
        const string additionalGrantSid = "S-1-5-21-0-0-0-1002";
        const string oldMainGrantPath = @"C:\OldMainGrant";
        const string importedMainGrantPath = @"C:\ImportedMainGrant";
        using var pinKey = TestSecretFactory.Create(32);
        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var additionalConfigPath = Path.Combine(tempDir.Path, "extra-ownership-rollback.ramc");
        var database = new AppDatabase();
        var credentialStore = new CredentialStore
        {
            ArgonSalt = new byte[32],
            EncryptedCanary = [1]
        };
        using var session = new SessionContext
        {
            Database = database,
            CredentialStore = credentialStore,
        }.WithClonedPinDerivedKey(pinKey);
        var log = new Mock<ILoggingService>();
        var harness = CreateRealAppConfigHarness(session, log);
        harness.DatabaseService
            .Setup(service => service.LoadAppConfigFromPath(additionalConfigPath, It.IsAny<ISecureSecretSnapshotSource>()))
            .Returns(new AppConfig
            {
                Apps = [new AppEntry { Id = "additional-app", Name = "AdditionalApp" }],
                Accounts =
                [
                    new AppConfigAccountEntry
                    {
                        Sid = additionalGrantSid,
                        Grants =
                        [
                            new GrantedPathEntry
                            {
                                Path = @"C:\AdditionalGrant",
                                SavedRights = SavedRightsState.DefaultForMode(isDeny: false)
                            }
                        ]
                    }
                ]
            });
        File.WriteAllText(additionalConfigPath, "stub");
        database.Apps.Add(new AppEntry { Id = "old-main-app", Name = "OldMainApp", AccountSid = orphanSid });
        database.GetOrCreateAccount(mainGrantSid).Grants.Add(new GrantedPathEntry
        {
            Path = oldMainGrantPath,
            SavedRights = SavedRightsState.DefaultForMode(isDeny: false)
        });
        harness.AppConfigService.LoadAdditionalConfig(additionalConfigPath, database, session.PinDerivedKey);

        var importedMainGrant = new GrantedPathEntry
        {
            Path = importedMainGrantPath,
            SavedRights = SavedRightsState.DefaultForMode(isDeny: false)
        };
        var importedDb = new AppDatabase();
        importedDb.Apps.Add(new AppEntry { Id = "imported-main-app", Name = "ImportedMainApp", AccountSid = mainGrantSid });
        importedDb.GetOrCreateAccount(mainGrantSid).Grants.Add(importedMainGrant.Clone());

        var handler = BuildHandlerWithRealAppConfigService(
            session,
            harness.AppConfigService,
            harness.OwnershipProjection,
            log: log);
        var tempFile = Path.Combine(tempDir.Path, "config.json");
        WriteJsonToFile(importedDb, tempFile);

        handler.ImportMainConfig(tempFile, null);

        var filteredAfterImport = harness.Index.FilterForMainConfig(database);
        var remainingGrant = Assert.Single(filteredAfterImport.GetAccount(mainGrantSid)!.Grants);
        Assert.Equal(importedMainGrantPath, remainingGrant.Path);
        Assert.Null(database.GetAccount(orphanSid));
        Assert.True(harness.OwnershipProjection.HasMainOwnership(mainGrantSid, remainingGrant));
        Assert.False(harness.OwnershipProjection.HasMainOwnership(mainGrantSid, new GrantedPathEntry
        {
            Path = oldMainGrantPath,
            SavedRights = SavedRightsState.DefaultForMode(isDeny: false)
        }));
    }

    [Fact]
    public void ImportMainConfig_SystemAccount_HasHighestAllowedAfterImport()
    {
        // Arrange: imported config has no SYSTEM account entry — import must still guarantee the invariant.
        using var pinKey = TestSecretFactory.Create(32);
        var database = new AppDatabase();
        var handler = BuildHandler(database, new CredentialStore(), pinKey);

        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var tempFile = Path.Combine(tempDir.Path, "config.json");
        WriteJsonToFile(new AppDatabase(), tempFile);

        // Act
        handler.ImportMainConfig(tempFile, null);

        // Assert
        var system = database.GetAccount(SidConstants.SystemSid);
        Assert.NotNull(system);
        Assert.Equal(PrivilegeLevel.HighestAllowed, system.PrivilegeLevel);
    }

    [Fact]
    public void ImportMainConfig_SidPresentInIncomingConfig_GrantsNotRemoved()
    {
        // Arrange: current main config has SID "S-1-keep". The imported config also has it.
        // Import must keep the incoming grant state without filesystem grant cleanup.
        const string keepSid = "S-1-5-21-0-0-0-1001";
        using var pinKey = TestSecretFactory.Create(32);
        var database = new AppDatabase();
        database.Apps.Add(new AppEntry { Id = "old-app", Name = "OldApp", AccountSid = keepSid });

        var handler = BuildHandler(database, new CredentialStore(), pinKey);

        var importedDb = new AppDatabase();
        importedDb.Apps.Add(new AppEntry { Id = "new-app", Name = "NewApp", AccountSid = keepSid });
        importedDb.GetOrCreateAccount(keepSid).Grants.Add(new GrantedPathEntry
        {
            Path = @"C:\KeepGrant",
            SavedRights = SavedRightsState.DefaultForMode(isDeny: false)
        });

        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var tempFile = Path.Combine(tempDir.Path, "config.json");
        WriteJsonToFile(importedDb, tempFile);

        // Act
        handler.ImportMainConfig(tempFile, null);

        // Assert: incoming SID grant state remains represented in imported runtime state.
        var account = database.GetAccount(keepSid);
        Assert.NotNull(account);
        var grant = Assert.Single(account.Grants);
        Assert.Equal(@"C:\KeepGrant", grant.Path);
    }

    [Fact]
    public void ImportMainConfig_MergesImportWarningsFromValidationWithMissingGrantsImport()
    {
        const string orphanSid = "S-1-5-21-0-0-0-9999";
        using var pinKey = TestSecretFactory.Create(32);
        var database = new AppDatabase();
        database.Apps.Add(new AppEntry { Id = "old-app", Name = "OldApp", AccountSid = orphanSid });
        var handler = BuildHandler(database, new CredentialStore(), pinKey);

        var importedDb = new AppDatabase();
        importedDb.Apps.Add(new AppEntry
        {
            Id = "new-app",
            Name = "NewApp",
            AccountSid = "S-1-5-21-0-0-0-1001",
            AppContainerName = "missing-container"
        });

        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var tempFile = Path.Combine(tempDir.Path, "config.json");
        WriteJsonToFile(importedDb, tempFile);

        var result = handler.ImportMainConfig(tempFile, null);

        Assert.Contains(
            "App 'NewApp' references container 'missing-container' which is missing from the imported config.",
            result.Warnings);
        Assert.Null(result.SaveError);
    }

    // ── Grant preservation ───────────────────────────────────────────────────

    [Theory]
    [InlineData(PathAclStatus.Available, true)]
    [InlineData(PathAclStatus.Unavailable, false)]
    [InlineData(PathAclStatus.Broken, false)]
    public void ImportMainConfig_GrantPreservation_StatusDeterminesRetention(PathAclStatus status, bool expectPreserved)
    {
        // Arrange: pre-existing main-config grant; retention depends on ACE status on disk.
        // Available → preserved; Unavailable/Broken → dropped.
        const string sid = "S-1-5-21-0-0-0-1001";
        const string path = @"C:\SomeFolder";
        using var pinKey = TestSecretFactory.Create(32);
        var database = new AppDatabase();
        var account = database.GetOrCreateAccount(sid);
        account.Grants.Add(new GrantedPathEntry { Path = path, IsDeny = false, IsTraverseOnly = false });

        var grantInspection = new Mock<IGrantInspectionService>();
        grantInspection.Setup(g => g.CheckGrantStatus(path, sid, false)).Returns(status);

        var handler = BuildHandler(database, new CredentialStore(), pinKey, grantInspection: grantInspection);
        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var tempFile = Path.Combine(tempDir.Path, "config.json");
        WriteJsonToFile(new AppDatabase(), tempFile);

        // Act
        handler.ImportMainConfig(tempFile, null);

        // Assert
        var result = database.GetAccount(sid);
        if (expectPreserved)
        {
            Assert.NotNull(result);
            Assert.Contains(result.Grants, g =>
                string.Equals(g.Path, path, StringComparison.OrdinalIgnoreCase) &&
                !g.IsDeny && !g.IsTraverseOnly);
        }
        else
        {
            Assert.True(result == null || result.Grants.Count == 0);
        }
    }

    [Fact]
    public void ImportMainConfig_GrantPreservation_TraverseEntry_ChecksWithIsDenyFalse()
    {
        // Arrange: traverse-only grant (IsTraverseOnly=true) — isDenyForCheck must be false regardless of IsDeny.
        const string sid = "S-1-5-21-0-0-0-1001";
        const string path = @"C:\TraverseFolder";
        using var pinKey = TestSecretFactory.Create(32);
        var database = new AppDatabase();
        var account = database.GetOrCreateAccount(sid);
        account.Grants.Add(new GrantedPathEntry { Path = path, IsDeny = true, IsTraverseOnly = true });

        var grantInspection = new Mock<IGrantInspectionService>();
        // Only Available when isDeny=false (traverse always checks with false)
        grantInspection.Setup(g => g.CheckGrantStatus(path, sid, false)).Returns(PathAclStatus.Available);
        grantInspection.Setup(g => g.CheckGrantStatus(path, sid, true)).Returns(PathAclStatus.Broken);

        var handler = BuildHandler(database, new CredentialStore(), pinKey, grantInspection: grantInspection);
        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var tempFile = Path.Combine(tempDir.Path, "config.json");
        WriteJsonToFile(new AppDatabase(), tempFile);

        // Act
        handler.ImportMainConfig(tempFile, null);

        // Assert: traverse grant preserved (isDeny=false used for check)
        var result = database.GetAccount(sid);
        Assert.NotNull(result);
        Assert.Contains(result.Grants, g =>
            string.Equals(g.Path, path, StringComparison.OrdinalIgnoreCase) &&
            g.IsTraverseOnly);
    }

    [Fact]
    public void ImportMainConfig_GrantPreservation_ImportAlsoHasPathType_LocalEntrySavedRightsFromImport()
    {
        // Arrange: local grant and imported grant share same path+type.
        // Local entry kept but SavedRights comes from imported grant.
        const string sid = "S-1-5-21-0-0-0-1001";
        const string path = @"C:\SharedFolder";
        using var pinKey = TestSecretFactory.Create(32);
        var database = new AppDatabase();
        var account = database.GetOrCreateAccount(sid);
        account.Grants.Add(new GrantedPathEntry { Path = path, IsDeny = false, IsTraverseOnly = false, SavedRights = null });

        var grantInspection = new Mock<IGrantInspectionService>();
        grantInspection.Setup(g => g.CheckGrantStatus(path, sid, false)).Returns(PathAclStatus.Available);

        // Imported config also has the same SID+path+type with a non-null SavedRights
        var importedDb = new AppDatabase();
        var importedAccount = importedDb.GetOrCreateAccount(sid);
        var importedSavedRights = new SavedRightsState(false, false, true, false, false);
        importedAccount.Grants.Add(new GrantedPathEntry
        {
            Path = path, IsDeny = false, IsTraverseOnly = false, SavedRights = importedSavedRights
        });

        var handler = BuildHandler(database, new CredentialStore(), pinKey, grantInspection: grantInspection);
        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var tempFile = Path.Combine(tempDir.Path, "config.json");
        WriteJsonToFile(importedDb, tempFile);

        // Act
        handler.ImportMainConfig(tempFile, null);

        // Assert: grant present, SavedRights taken from imported entry
        var result = database.GetAccount(sid);
        Assert.NotNull(result);
        var preserved = result.Grants.FirstOrDefault(g =>
            string.Equals(g.Path, path, StringComparison.OrdinalIgnoreCase) &&
            !g.IsDeny && !g.IsTraverseOnly);
        Assert.NotNull(preserved);
        Assert.Equal(importedSavedRights, preserved.SavedRights);
    }

    [Fact]
    public void ImportMainConfig_SharedContainerTraverse_ImportedMainReplacesStaleMain()
    {
        using var pinKey = TestSecretFactory.Create(32);
        var database = new AppDatabase();
        database.GetOrCreateAccount(WellKnownSecuritySids.AllApplicationPackagesSid).Grants.Add(
            new GrantedPathEntry { Path = @"C:\OldShared", IsTraverseOnly = true });

        var importedDb = new AppDatabase();
        importedDb.GetOrCreateAccount(WellKnownSecuritySids.AllApplicationPackagesSid).Grants.Add(
            new GrantedPathEntry { Path = @"C:\ImportedShared", IsTraverseOnly = true });

        var handler = BuildHandler(database, new CredentialStore(), pinKey);
        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var tempFile = Path.Combine(tempDir.Path, "config.json");
        WriteJsonToFile(importedDb, tempFile);

        handler.ImportMainConfig(tempFile, null);

        var account = database.GetAccount(WellKnownSecuritySids.AllApplicationPackagesSid);
        Assert.NotNull(account);
        Assert.Equal(@"C:\ImportedShared", Assert.Single(account!.Grants).Path);
    }

    [Fact]
    public void ImportMainConfig_SharedContainerTraverse_PreservesAdditionalConfigEntries()
    {
        using var pinKey = TestSecretFactory.Create(32);
        var additionalEntry = new GrantedPathEntry { Path = @"C:\AdditionalShared", IsTraverseOnly = true };
        var database = new AppDatabase();
        database.GetOrCreateAccount(WellKnownSecuritySids.AllApplicationPackagesSid).Grants.Add(additionalEntry);
        var ownershipProjection = new GrantIntentOwnershipProjectionService();
        ownershipProjection.CaptureMainOwnershipBaseline(database);
        ownershipProjection.RegisterAdditionalConfig(
            @"C:\extra.rfn",
            [new AppConfigAccountEntry
            {
                Sid = WellKnownSecuritySids.AllApplicationPackagesSid,
                Grants = [additionalEntry.Clone()]
            }]);

        var handler = BuildHandler(
            database,
            new CredentialStore(),
            pinKey,
            ownershipProjection: ownershipProjection);
        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var tempFile = Path.Combine(tempDir.Path, "config.json");
        WriteJsonToFile(new AppDatabase(), tempFile);

        handler.ImportMainConfig(tempFile, null);

        var account = database.GetAccount(WellKnownSecuritySids.AllApplicationPackagesSid);
        Assert.NotNull(account);
        Assert.Equal(@"C:\AdditionalShared", Assert.Single(account!.Grants).Path);
    }

    [Fact]
    public void ImportMainConfig_AdditionalGrantResplice_UsesAdditionalProjectionPayload()
    {
        const string sid = "S-1-5-21-0-0-0-1001";
        using var pinKey = TestSecretFactory.Create(32);
        var database = new AppDatabase();
        var additionalEntry = new GrantedPathEntry
        {
            Path = @"C:\AdditionalPayload",
            SavedRights = SavedRightsState.DefaultForMode(isDeny: false),
            PreviousSaclLabel = "additional-label"
        };
        var mainEntry = additionalEntry.Clone();
        mainEntry.PreviousSaclLabel = "main-label";
        database.GetOrCreateAccount(sid).Grants.Add(mainEntry);
        var ownershipProjection = new GrantIntentOwnershipProjectionService();

        var handler = BuildHandler(
            database,
            new CredentialStore(),
            pinKey,
            ownershipProjection: ownershipProjection);
        ownershipProjection.RegisterAdditionalConfig(
            @"C:\extra.rfn",
            [new AppConfigAccountEntry { Sid = sid, Grants = [additionalEntry.Clone()] }]);
        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var tempFile = Path.Combine(tempDir.Path, "config.json");
        WriteJsonToFile(new AppDatabase(), tempFile);

        handler.ImportMainConfig(tempFile, null);

        var storedEntry = Assert.Single(database.GetAccount(sid)!.Grants);
        Assert.Equal("additional-label", storedEntry.PreviousSaclLabel);
    }

    [Fact]
    public void ImportMainConfig_AdditionalGrantResplice_KeepsDistinctAdditionalIdentityWithSamePath()
    {
        const string sid = "S-1-5-21-0-0-0-1001";
        const string path = @"C:\SharedImportPath";
        using var pinKey = TestSecretFactory.Create(32);
        var database = new AppDatabase();
        var ownershipProjection = new GrantIntentOwnershipProjectionService();
        ownershipProjection.CaptureMainOwnershipBaseline(database);
        var additionalEntry = new GrantedPathEntry
        {
            Path = path,
            SavedRights = SavedRightsState.DefaultForMode(isDeny: false)
        };
        database.GetOrCreateAccount(sid).Grants.Add(additionalEntry.Clone());
        var importedRights = new SavedRightsState(
            Execute: false,
            Write: true,
            Read: true,
            Special: false,
            Own: false);
        var importedDb = new AppDatabase();
        importedDb.GetOrCreateAccount(sid).Grants.Add(new GrantedPathEntry
        {
            Path = path,
            SavedRights = importedRights
        });

        var handler = BuildHandler(
            database,
            new CredentialStore(),
            pinKey,
            ownershipProjection: ownershipProjection);
        ownershipProjection.RegisterAdditionalConfig(
            @"C:\extra.rfn",
            [new AppConfigAccountEntry { Sid = sid, Grants = [additionalEntry.Clone()] }]);
        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var tempFile = Path.Combine(tempDir.Path, "config.json");
        WriteJsonToFile(importedDb, tempFile);

        handler.ImportMainConfig(tempFile, null);

        var grants = database.GetAccount(sid)!.Grants;
        Assert.Equal(2, grants.Count);
        Assert.Contains(grants, grant => grant.SavedRights == additionalEntry.SavedRights);
        Assert.Contains(grants, grant => grant.SavedRights == importedRights);
    }

    [Fact]
    public void ImportMainConfig_AdditionalSharedTraverseResplice_KeepsDistinctAdditionalIdentityWithSamePath()
    {
        const string path = @"C:\SharedTraverseImportPath";
        using var pinKey = TestSecretFactory.Create(32);
        var database = new AppDatabase();
        var ownershipProjection = new GrantIntentOwnershipProjectionService();
        ownershipProjection.CaptureMainOwnershipBaseline(database);
        var additionalEntry = new GrantedPathEntry
        {
            Path = path,
            IsTraverseOnly = true,
            AllAppliedPaths = [path, @"C:\AdditionalRoot"]
        };
        database.GetOrCreateAccount(WellKnownSecuritySids.AllApplicationPackagesSid).Grants.Add(additionalEntry.Clone());
        var importedEntry = new GrantedPathEntry
        {
            Path = path,
            IsTraverseOnly = true,
            AllAppliedPaths = [path, @"C:\ImportedRoot"]
        };
        var importedDb = new AppDatabase();
        importedDb.GetOrCreateAccount(WellKnownSecuritySids.AllApplicationPackagesSid).Grants.Add(importedEntry);

        var handler = BuildHandler(
            database,
            new CredentialStore(),
            pinKey,
            ownershipProjection: ownershipProjection);
        ownershipProjection.RegisterAdditionalConfig(
            @"C:\extra.rfn",
            [new AppConfigAccountEntry
            {
                Sid = WellKnownSecuritySids.AllApplicationPackagesSid,
                Grants = [additionalEntry.Clone()]
            }]);
        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var tempFile = Path.Combine(tempDir.Path, "config.json");
        WriteJsonToFile(importedDb, tempFile);

        handler.ImportMainConfig(tempFile, null);

        var traverseGrants = database.GetAccount(WellKnownSecuritySids.AllApplicationPackagesSid)!.Grants;
        Assert.Equal(2, traverseGrants.Count);
        Assert.Contains(traverseGrants, grant =>
            grant.AllAppliedPaths?.Contains(@"C:\AdditionalRoot", StringComparer.OrdinalIgnoreCase) == true);
        Assert.Contains(traverseGrants, grant =>
            grant.AllAppliedPaths?.Contains(@"C:\ImportedRoot", StringComparer.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void ImportMainConfig_RebuildsMainOwnershipForImportedGrants()
    {
        const string sid = "S-1-5-21-0-0-0-1001";
        const string sharedPath = @"C:\ImportedMainGrant";
        using var pinKey = TestSecretFactory.Create(32);
        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var additionalConfigPath = Path.Combine(tempDir.Path, "extra-main-grant.ramc");
        var database = new AppDatabase();
        var credentialStore = new CredentialStore
        {
            ArgonSalt = new byte[32],
            EncryptedCanary = [1]
        };
        using var session = new SessionContext
{
            Database = database,
            CredentialStore = credentialStore,
        }.WithClonedPinDerivedKey(pinKey);
        var log = new Mock<ILoggingService>();
        var harness = CreateRealAppConfigHarness(session, log);
        harness.DatabaseService
            .Setup(service => service.LoadAppConfigFromPath(additionalConfigPath, It.IsAny<ISecureSecretSnapshotSource>()))
            .Returns(new AppConfig
            {
                Accounts =
                [
                    new AppConfigAccountEntry
                    {
                        Sid = sid,
                        Grants =
                        [
                            new GrantedPathEntry
                            {
                                Path = sharedPath,
                                SavedRights = SavedRightsState.DefaultForMode(isDeny: false),
                                PreviousSaclLabel = "additional-label"
                            }
                        ]
                    }
                ]
            });
        File.WriteAllText(additionalConfigPath, "stub");
        database.GetOrCreateAccount(sid).Grants.Add(new GrantedPathEntry
        {
            Path = sharedPath,
            SavedRights = SavedRightsState.DefaultForMode(isDeny: false),
            PreviousSaclLabel = "stale-main-label"
        });
        harness.AppConfigService.LoadAdditionalConfig(additionalConfigPath, database, session.PinDerivedKey);

        var importedGrant = new GrantedPathEntry
        {
            Path = sharedPath,
            SavedRights = SavedRightsState.DefaultForMode(isDeny: false),
            PreviousSaclLabel = "imported-main-label"
        };
        var importedDb = new AppDatabase();
        importedDb.GetOrCreateAccount(sid).Grants.Add(importedGrant.Clone());

        var handler = BuildHandlerWithRealAppConfigService(
            session,
            harness.AppConfigService,
            harness.OwnershipProjection,
            log: log);
        var tempFile = Path.Combine(tempDir.Path, "config.json");
        WriteJsonToFile(importedDb, tempFile);

        handler.ImportMainConfig(tempFile, null);

        var filteredAfterImport = harness.Index.FilterForMainConfig(database);
        var filteredGrant = Assert.Single(filteredAfterImport.GetAccount(sid)!.Grants);
        Assert.Equal("imported-main-label", filteredGrant.PreviousSaclLabel);
        Assert.True(harness.OwnershipProjection.HasMainOwnership(sid, importedGrant));

        harness.AppConfigService.UnloadConfig(additionalConfigPath, database);

        var remainingGrant = Assert.Single(database.GetAccount(sid)!.Grants);
        Assert.Equal("imported-main-label", remainingGrant.PreviousSaclLabel);
        Assert.True(harness.OwnershipProjection.HasMainOwnership(sid, remainingGrant));
        Assert.False(harness.OwnershipProjection.HasAdditionalOwnership(additionalConfigPath, sid, remainingGrant));
    }

    [Fact]
    public void ImportMainConfig_RebuildsMainOwnershipForImportedSharedTraverseGrants()
    {
        const string sharedPath = @"C:\ImportedSharedTraverse";
        using var pinKey = TestSecretFactory.Create(32);
        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var additionalConfigPath = Path.Combine(tempDir.Path, "extra-main-traverse.ramc");
        var database = new AppDatabase();
        var credentialStore = new CredentialStore
        {
            ArgonSalt = new byte[32],
            EncryptedCanary = [1]
        };
        using var session = new SessionContext
{
            Database = database,
            CredentialStore = credentialStore,
        }.WithClonedPinDerivedKey(pinKey);
        var log = new Mock<ILoggingService>();
        var harness = CreateRealAppConfigHarness(session, log);
        harness.DatabaseService
            .Setup(service => service.LoadAppConfigFromPath(additionalConfigPath, It.IsAny<ISecureSecretSnapshotSource>()))
            .Returns(new AppConfig
            {
                Accounts =
                [
                    new AppConfigAccountEntry
                    {
                        Sid = WellKnownSecuritySids.AllApplicationPackagesSid,
                        Grants =
                        [
                            new GrantedPathEntry
                            {
                                Path = sharedPath,
                                IsTraverseOnly = true,
                                AllAppliedPaths = [sharedPath, @"C:\AdditionalRoot"]
                            }
                        ]
                    }
                ]
            });
        File.WriteAllText(additionalConfigPath, "stub");
        database.GetOrCreateAccount(WellKnownSecuritySids.AllApplicationPackagesSid).Grants.Add(new GrantedPathEntry
        {
            Path = sharedPath,
            IsTraverseOnly = true,
            AllAppliedPaths = [sharedPath, @"C:\StaleRoot"]
        });
        harness.AppConfigService.LoadAdditionalConfig(additionalConfigPath, database, session.PinDerivedKey);

        var importedTraverse = new GrantedPathEntry
        {
            Path = sharedPath,
            IsTraverseOnly = true,
            AllAppliedPaths = [sharedPath, @"C:\ImportedRoot"]
        };
        var importedDb = new AppDatabase();
        importedDb.GetOrCreateAccount(WellKnownSecuritySids.AllApplicationPackagesSid).Grants.Add(importedTraverse.Clone());

        var handler = BuildHandlerWithRealAppConfigService(
            session,
            harness.AppConfigService,
            harness.OwnershipProjection,
            log: log);
        var tempFile = Path.Combine(tempDir.Path, "config.json");
        WriteJsonToFile(importedDb, tempFile);

        handler.ImportMainConfig(tempFile, null);

        var filteredAfterImport = harness.Index.FilterForMainConfig(database);
        var filteredTraverse = Assert.Single(filteredAfterImport.GetAccount(WellKnownSecuritySids.AllApplicationPackagesSid)!.Grants);
        Assert.Equal(importedTraverse.AllAppliedPaths, filteredTraverse.AllAppliedPaths);
        Assert.True(harness.OwnershipProjection.HasMainOwnership(WellKnownSecuritySids.AllApplicationPackagesSid, importedTraverse));

        harness.AppConfigService.UnloadConfig(additionalConfigPath, database);

        var remainingTraverse = Assert.Single(database.GetAccount(WellKnownSecuritySids.AllApplicationPackagesSid)!.Grants);
        Assert.Equal(importedTraverse.AllAppliedPaths, remainingTraverse.AllAppliedPaths);
        Assert.True(harness.OwnershipProjection.HasMainOwnership(WellKnownSecuritySids.AllApplicationPackagesSid, remainingTraverse));
        Assert.False(harness.OwnershipProjection.HasAdditionalOwnership(
            additionalConfigPath,
            WellKnownSecuritySids.AllApplicationPackagesSid,
            remainingTraverse));
    }

    [Fact]
    public void ImportMainConfig_SharedContainerTraverse_PreservesOldMainWhenAceAvailable()
    {
        const string path = @"C:\LocalShared";
        using var pinKey = TestSecretFactory.Create(32);
        var database = new AppDatabase();
        database.GetOrCreateAccount(WellKnownSecuritySids.AllApplicationPackagesSid).Grants.Add(
            new GrantedPathEntry { Path = path, IsTraverseOnly = true });
        var grantInspection = new Mock<IGrantInspectionService>();
        grantInspection.Setup(g => g.CheckGrantStatus(
                path,
                WellKnownSecuritySids.AllApplicationPackagesSid,
                false))
            .Returns(PathAclStatus.Available);

        var handler = BuildHandler(database, new CredentialStore(), pinKey, grantInspection: grantInspection);
        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var tempFile = Path.Combine(tempDir.Path, "config.json");
        WriteJsonToFile(new AppDatabase(), tempFile);

        handler.ImportMainConfig(tempFile, null);

        var account = database.GetAccount(WellKnownSecuritySids.AllApplicationPackagesSid);
        Assert.NotNull(account);
        Assert.Equal(path, Assert.Single(account!.Grants).Path);
    }

    // ── SidNames preservation ────────────────────────────────────────────────

    [Fact]
    public void ImportMainConfig_SidNamesPreservation_ResolvedSidNotInImport_ReAdded()
    {
        // Arrange: database has SID "S-1-local" in SidNames. Import doesn't include it.
        // sidResolutions has it resolved → must be re-added after full replace.
        const string localSid = "S-1-5-21-0-0-0-5000";
        const string localName = "LocalUser";
        using var pinKey = TestSecretFactory.Create(32);
        var database = new AppDatabase();
        database.SidNames[localSid] = localName;

        var handler = BuildHandler(database, new CredentialStore(), pinKey);
        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var tempFile = Path.Combine(tempDir.Path, "config.json");
        WriteJsonToFile(new AppDatabase(), tempFile);

        // Act
        var sidResolutions = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            { localSid, "ResolvedName" }
        };
        handler.ImportMainConfig(tempFile, sidResolutions);

        // Assert: SidNames entry re-added with original name
        Assert.True(database.SidNames.ContainsKey(localSid));
        Assert.Equal(localName, database.SidNames[localSid]);
    }

    [Fact]
    public void ImportMainConfig_SidNamesPreservation_NullSidResolutions_NotReAdded()
    {
        // Arrange: database has SID in SidNames; import doesn't include it; sidResolutions is null.
        // Without resolution data, the entry must NOT be re-added.
        const string localSid = "S-1-5-21-0-0-0-5001";
        using var pinKey = TestSecretFactory.Create(32);
        var database = new AppDatabase();
        database.SidNames[localSid] = "LocalUser";

        var handler = BuildHandler(database, new CredentialStore(), pinKey);
        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var tempFile = Path.Combine(tempDir.Path, "config.json");
        WriteJsonToFile(new AppDatabase(), tempFile);

        // Act
        handler.ImportMainConfig(tempFile, null);

        // Assert: SidNames entry NOT re-added (full replace with no resolution)
        Assert.False(database.SidNames.ContainsKey(localSid));
    }

    // ── AccountEntry preservation ────────────────────────────────────────────

    [Fact]
    public void ImportMainConfig_AccountPreservation_ResolvedSidNotInImport_StubAdded()
    {
        // Arrange: database has an account for "S-1-local" with some settings.
        // Import doesn't include that SID. sidResolutions has it resolved → stub must be added.
        const string localSid = "S-1-5-21-0-0-0-6000";
        using var pinKey = TestSecretFactory.Create(32);
        var database = new AppDatabase();
        var oldAccount = database.GetOrCreateAccount(localSid);
        oldAccount.IsIpcCaller = true;
        oldAccount.Grants.Add(new GrantedPathEntry { Path = @"C:\Foo", IsDeny = false, IsTraverseOnly = false });

        var handler = BuildHandler(database, new CredentialStore(), pinKey);
        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var tempFile = Path.Combine(tempDir.Path, "config.json");
        WriteJsonToFile(new AppDatabase(), tempFile);

        // Act
        var sidResolutions = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            { localSid, "ResolvedName" }
        };
        handler.ImportMainConfig(tempFile, sidResolutions);

        // Assert: account stub added with settings preserved but empty grants
        var result = database.GetAccount(localSid);
        Assert.NotNull(result);
        Assert.True(result.IsIpcCaller);
        Assert.Empty(result.Grants);
    }

    [Fact]
    public void ImportMainConfig_AccountPreservation_NullSidResolutions_StubNotAdded()
    {
        // Arrange: database has an account not in the import; sidResolutions is null.
        // Without resolution data, the account must NOT be preserved.
        const string localSid = "S-1-5-21-0-0-0-6001";
        using var pinKey = TestSecretFactory.Create(32);
        var database = new AppDatabase();
        var oldAccount = database.GetOrCreateAccount(localSid);
        oldAccount.IsIpcCaller = true;

        var handler = BuildHandler(database, new CredentialStore(), pinKey);
        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var tempFile = Path.Combine(tempDir.Path, "config.json");
        WriteJsonToFile(new AppDatabase(), tempFile);

        // Act
        handler.ImportMainConfig(tempFile, null);

        // Assert: account NOT preserved
        Assert.Null(database.GetAccount(localSid));
    }

    // ── LastKnownExeTimestamp cleared ────────────────────────────────────────

    [Fact]
    public void ImportMainConfig_LastKnownExeTimestamp_ClearedOnImport()
    {
        // Arrange: imported app has a non-null LastKnownExeTimestamp.
        using var pinKey = TestSecretFactory.Create(32);
        var database = new AppDatabase();
        var handler = BuildHandler(database, new CredentialStore(), pinKey);

        var importedDb = new AppDatabase();
        importedDb.Apps.Add(new AppEntry
        {
            Id = "app-1",
            Name = "App1",
            LastKnownExeTimestamp = DateTime.UtcNow
        });

        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var tempFile = Path.Combine(tempDir.Path, "config.json");
        WriteJsonToFile(importedDb, tempFile);

        // Act
        handler.ImportMainConfig(tempFile, null);

        // Assert: timestamp cleared
        var app = database.Apps.First(a => a.Id == "app-1");
        Assert.Null(app.LastKnownExeTimestamp);
    }

    // ── ShowSystemInRunAs ────────────────────────────────────────────────────

    [Fact]
    public void ImportMainConfig_ShowSystemInRunAs_ImportedValue()
    {
        // Arrange: import config with ShowSystemInRunAs = true.
        using var pinKey = TestSecretFactory.Create(32);
        var database = new AppDatabase { ShowSystemInRunAs = false };
        var handler = BuildHandler(database, new CredentialStore(), pinKey);

        var importedDb = new AppDatabase { ShowSystemInRunAs = true };
        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var tempFile = Path.Combine(tempDir.Path, "config.json");
        WriteJsonToFile(importedDb, tempFile);

        // Act
        handler.ImportMainConfig(tempFile, null);

        // Assert
        Assert.True(database.ShowSystemInRunAs);
    }

    [Fact]
    public void ImportMainConfig_NagEligibility_IsPreservedWhenImportedValueIsFalse()
    {
        using var pinKey = TestSecretFactory.Create(32);
        var database = new AppDatabase();
        database.Settings.NagEligible = true;
        var handler = BuildHandler(database, new CredentialStore(), pinKey);

        var importedDb = new AppDatabase
        {
            Settings = new AppSettings { NagEligible = false }
        };

        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var tempFile = Path.Combine(tempDir.Path, "config.json");
        WriteJsonToFile(importedDb, tempFile);

        handler.ImportMainConfig(tempFile, null);

        Assert.True(database.Settings.NagEligible);
    }

    // ── Additional-app ID collision updates index mapping ────────────────────

    [Fact]
    public void ImportMainConfig_AdditionalAppIdCollision_UpdatesIndexMapping()
    {
        // Arrange: additional-config app with ID "shared-id" and known configPath.
        // Imported main-config also has "shared-id". After collision repair,
        // appConfigService.RemoveApp(oldId) and AssignApp(newId, configPath) must be called.
        const string configPath = @"C:\extra.rfn";
        const string collisionId = "shared-id";
        using var pinKey = TestSecretFactory.Create(32);
        var database = new AppDatabase();
        var additionalApp = new AppEntry { Id = collisionId, Name = "AdditionalApp" };
        database.Apps.Add(additionalApp);

        var appConfigService = new Mock<IAppConfigService>();
        appConfigService.Setup(s => s.GetConfigPath(collisionId)).Returns(configPath);
        appConfigService.Setup(s => s.GetConfigPath(It.Is<string>(id => id != collisionId))).Returns((string?)null);
        appConfigService.Setup(s => s.GetLoadedConfigPaths()).Returns([configPath]);
        appConfigService.Setup(s => s.HasLoadedConfigs).Returns(true);

        var capturedNewId = "";
        appConfigService.Setup(s => s.AssignApp(It.IsAny<string>(), It.IsAny<string?>()))
            .Callback<string, string?>((id, _) => capturedNewId = id);

        var idGenerator = new Mock<IAppEntryIdGenerator>();
        idGenerator.Setup(g => g.GenerateUniqueId(It.IsAny<IEnumerable<string>>()))
            .Returns("repaired-id");

        var handler = BuildHandler(database, new CredentialStore(), pinKey,
            appConfigService: appConfigService, idGenerator: idGenerator);

        var importedDb = new AppDatabase();
        importedDb.Apps.Add(new AppEntry { Id = collisionId, Name = "ImportedApp" });

        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var tempFile = Path.Combine(tempDir.Path, "config.json");
        WriteJsonToFile(importedDb, tempFile);

        // Act
        handler.ImportMainConfig(tempFile, null);

        // Assert: old mapping removed and new mapping assigned
        appConfigService.Verify(s => s.RemoveApp(collisionId), Times.Once);
        appConfigService.Verify(s => s.AssignApp(It.IsAny<string>(), configPath), Times.Once);
        Assert.NotEqual(collisionId, capturedNewId);
    }

    [Fact]
    public void ImportMainConfig_AdditionalAppIdCollision_RenamesLoadedHandlerMappings()
    {
        const string configPath = @"C:\extra.rfn";
        const string collisionId = "shared-id";
        using var pinKey = TestSecretFactory.Create(32);
        var database = new AppDatabase();
        database.Apps.Add(new AppEntry { Id = collisionId, Name = "AdditionalApp" });

        var appConfigService = new Mock<IAppConfigService>();
        appConfigService.Setup(s => s.GetConfigPath(collisionId)).Returns(configPath);
        appConfigService.Setup(s => s.GetConfigPath(It.Is<string>(id => id != collisionId))).Returns((string?)null);
        appConfigService.Setup(s => s.GetLoadedConfigPaths()).Returns([configPath]);
        appConfigService.Setup(s => s.HasLoadedConfigs).Returns(true);

        var handlerMappingService = new Mock<IHandlerMappingService>();
        var idGenerator = new Mock<IAppEntryIdGenerator>();
        idGenerator.Setup(g => g.GenerateUniqueId(It.IsAny<IEnumerable<string>>()))
            .Returns("repaired-id");

        var handler = BuildHandler(
            database,
            new CredentialStore(),
            pinKey,
            appConfigService: appConfigService,
            handlerMappingService: handlerMappingService,
            idGenerator: idGenerator);

        var importedDb = new AppDatabase();
        importedDb.Apps.Add(new AppEntry { Id = collisionId, Name = "ImportedApp" });

        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var tempFile = Path.Combine(tempDir.Path, "config.json");
        WriteJsonToFile(importedDb, tempFile);

        handler.ImportMainConfig(tempFile, null);

        handlerMappingService.Verify(
            s => s.RenameAppIdInConfigMappings(configPath, collisionId, "repaired-id"),
            Times.Once);
    }

    [Fact]
    public void ImportMainConfig_SaveFailsAfterApply_KeepsLiveDatabaseMutated()
    {
        using var pinKey = TestSecretFactory.Create(32);
        var database = new AppDatabase
        {
            TrackingJobSids = ["S-1-5-21-1-2-3-1001"]
        };
        database.Apps.Add(new AppEntry { Id = "old-app", Name = "OldApp" });

        var appConfigService = new Mock<IAppConfigService>();
        appConfigService.Setup(s => s.GetConfigPath(It.IsAny<string>())).Returns((string?)null);
        appConfigService.Setup(s => s.GetLoadedConfigPaths()).Returns(Array.Empty<string>());
        appConfigService.Setup(s => s.HasLoadedConfigs).Returns(false);
        appConfigService.Setup(s => s.ReencryptAndSaveAll(
                It.IsAny<CredentialStore>(),
                database,
                It.IsAny<ISecureSecretSnapshotSource>()))
            .Throws(new IOException("disk full"));

        var handler = BuildHandler(
            database,
            new CredentialStore(),
            pinKey,
            appConfigService: appConfigService);

        var importedDb = new AppDatabase
        {
            TrackingJobSids = ["S-1-5-21-1-2-3-2001"]
        };
        importedDb.Apps.Add(new AppEntry { Id = "new-app", Name = "ImportedApp" });

        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var tempFile = Path.Combine(tempDir.Path, "config.json");
        WriteJsonToFile(importedDb, tempFile);

        var result = handler.ImportMainConfig(tempFile, null);

        Assert.Equal("disk full", result.SaveError);
        Assert.DoesNotContain(database.Apps, app => app.Name == "OldApp");
        Assert.Contains(database.Apps, app => app.Name == "ImportedApp");
        Assert.Equal(["S-1-5-21-1-2-3-2001"], database.TrackingJobSids);
    }

    [Fact]
    public void ImportMainConfig_ApplyThrows_RollsBackDatabaseAndRuntimeState()
    {
        const string preservedSid = "S-1-5-21-0-0-0-9999";

        using var pinKey = TestSecretFactory.Create(32);
        var database = new AppDatabase
        {
            TrackingJobSids = ["S-1-5-21-1-2-3-1001"]
        };
        database.Apps.Add(new AppEntry { Id = "old-app", Name = "OldApp" });
        database.GetOrCreateAccount(preservedSid).Grants.Add(new GrantedPathEntry
        {
            Path = @"C:\Preserved",
            IsDeny = false,
            IsTraverseOnly = false,
        });

        var databaseBefore = CaptureDatabaseState(database);

        var appConfigService = new Mock<IAppConfigService>();
        appConfigService.Setup(s => s.GetConfigPath(It.IsAny<string>())).Returns((string?)null);
        appConfigService.Setup(s => s.GetLoadedConfigPaths()).Returns(Array.Empty<string>());
        appConfigService.SetupGet(s => s.HasLoadedConfigs).Returns(false);

        var runtimeSnapshot = new AppConfigRuntimeStateSnapshot(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            [],
            new Dictionary<string, IReadOnlyDictionary<string, HandlerMappingEntry>>(StringComparer.OrdinalIgnoreCase));
        appConfigService.Setup(s => s.CaptureRuntimeStateSnapshot()).Returns(runtimeSnapshot);

        var grantInspection = new Mock<IGrantInspectionService>();
        grantInspection
            .Setup(g => g.CheckGrantStatus(@"C:\Preserved", preservedSid, false))
            .Throws(new InvalidOperationException("grant inspection failed"));

        var handler = BuildHandler(
            database,
            new CredentialStore(),
            pinKey,
            appConfigService: appConfigService,
            grantInspection: grantInspection);

        var importedDb = new AppDatabase();
        importedDb.Apps.Add(new AppEntry { Id = "new-app", Name = "ImportedApp" });

        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var tempFile = Path.Combine(tempDir.Path, "config.json");
        WriteJsonToFile(importedDb, tempFile);

        var ex = Assert.Throws<InvalidOperationException>(() => handler.ImportMainConfig(tempFile, null));

        Assert.Equal("grant inspection failed", ex.Message);
        Assert.Equal(databaseBefore, CaptureDatabaseState(database));
        appConfigService.Verify(s => s.CaptureRuntimeStateSnapshot(), Times.Once);
        appConfigService.Verify(s => s.RestoreRuntimeStateSnapshot(runtimeSnapshot), Times.Once);
        appConfigService.Verify(
            s => s.ReencryptAndSaveAll(
                It.IsAny<CredentialStore>(),
                It.IsAny<AppDatabase>(),
                It.IsAny<ISecureSecretSnapshotSource>()),
            Times.Never);
    }

    [Fact]
    public void ImportMainConfig_AfterKeyReplacement_UsesPinDerivedKeySnapshotSource()
    {
        var database = new AppDatabase();
        using var originalPinKey = TestSecretFactory.Create(32);
        using var replacementPinKey = new SecureSecret(32, data => data.Fill(0x5A));
        var session = new SessionContext
{
            Database = database,
            CredentialStore = new CredentialStore(),
        }.WithClonedPinDerivedKey(originalPinKey);
        _sessions.Add(session);
        session.ReplacePinDerivedKey(replacementPinKey);

        ISecureSecretSnapshotSource? capturedSource = null;
        var appConfigService = new Mock<IAppConfigService>();
        appConfigService.Setup(s => s.HasLoadedConfigs).Returns(false);
        appConfigService.Setup(s => s.GetConfigPath(It.IsAny<string>())).Returns((string?)null);
        appConfigService.Setup(s => s.GetLoadedConfigPaths()).Returns(Array.Empty<string>());
        appConfigService
            .Setup(s => s.ReencryptAndSaveAll(session.CredentialStore, database, It.IsAny<ISecureSecretSnapshotSource>()))
            .Callback<CredentialStore, AppDatabase, ISecureSecretSnapshotSource>((_, _, source) => capturedSource = source);

        var handler = CreateHandlerForSession(session, appConfigService.Object);
        var importedDb = new AppDatabase
        {
            TrackingJobSids = ["S-1-5-21-1-2-3-2001"]
        };
        importedDb.Apps.Add(new AppEntry { Id = "new-app", Name = "ImportedApp" });

        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var tempFile = Path.Combine(tempDir.Path, "config.json");
        WriteJsonToFile(importedDb, tempFile);

        handler.ImportMainConfig(tempFile, null);

        Assert.Same(session.PinDerivedKey, capturedSource);
    }

    [Fact]
    public void ImportAdditionalConfig_DeserializesAndSavesImportedConfigWithoutMutation()
    {
        var expectedKey = new byte[32];
        using var pinKey = TestSecretFactory.FromBytes(expectedKey);
        var database = new AppDatabase();
        var appConfigService = new Mock<IAppConfigService>();
        var log = new Mock<ILoggingService>();

        AppConfig? capturedConfig = null;
        byte[]? capturedPinKey = null;
        byte[]? capturedSalt = null;
        appConfigService
            .Setup(s => s.SaveImportedConfig(It.IsAny<string>(), It.IsAny<AppConfig>(), It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()))
            .Callback<string, AppConfig, ISecureSecretSnapshotSource, byte[]>((_, config, key, salt) =>
            {
                capturedConfig = config;
                capturedPinKey = key.TransformSnapshot(data => data.ToArray());
                capturedSalt = salt.ToArray();
            });

        var credentialStore = new CredentialStore { ArgonSalt = [1, 2, 3, 4] };
        var handler = BuildHandler(
            database,
            credentialStore,
            pinKey,
            appConfigService: appConfigService,
            log: log);

        var importConfig = new AppConfig
        {
            Apps =
            [
                new AppEntry { Id = "app-1", Name = "ImportedApp", AccountSid = "S-1-5-21-1" }
            ],
            Accounts =
            [
                new AppConfigAccountEntry
                {
                    Sid = "S-1-5-21-1",
                    Grants =
                    [
                        new GrantedPathEntry { Path = @"C:\Data", IsDeny = false, IsTraverseOnly = false }
                    ]
                }
            ],
            HandlerMappings = new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase)
            {
                [".txt"] = new("app-1", "\"%1\"", ["C:\\Allowed"], true)
            }
        };

        var json = System.Text.Json.JsonSerializer.Serialize(importConfig, JsonDefaults.Options);
        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var importJsonPath = Path.Combine(tempDir.Path, "import.json");
        const string targetConfigPath = @"C:\configs\extra.rfn";
        File.WriteAllText(importJsonPath, json);

        var result = handler.ImportAdditionalConfig(importJsonPath, targetConfigPath);

        Assert.Equal(AdditionalConfigImportStatus.Succeeded, result.Status);
        appConfigService.Verify(
            s => s.SaveImportedConfig(targetConfigPath, It.IsAny<AppConfig>(), It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()),
            Times.Once);
        Assert.NotNull(capturedConfig);
        Assert.NotSame(importConfig, capturedConfig);
        Assert.Single(capturedConfig!.Apps);
        Assert.Equal("app-1", capturedConfig.Apps[0].Id);
        Assert.Equal("ImportedApp", capturedConfig.Apps[0].Name);
        Assert.NotNull(capturedConfig.Accounts);
        Assert.Single(capturedConfig.Accounts!);
        Assert.Equal("S-1-5-21-1", capturedConfig.Accounts[0].Sid);
        Assert.Equal("S-1-5-21-1", capturedConfig.Accounts![0].Sid);
        Assert.Single(capturedConfig.Accounts[0].Grants);
        Assert.Equal(@"C:\Data", capturedConfig.Accounts[0].Grants[0].Path);
        Assert.NotNull(capturedConfig.HandlerMappings);
        Assert.True(capturedConfig.HandlerMappings!.ContainsKey(".txt"));
        var mapping = capturedConfig.HandlerMappings![".txt"];
        Assert.Equal("app-1", mapping.AppId);
        Assert.Equal("\"%1\"", mapping.ArgumentsTemplate);
        Assert.Equal(["C:\\Allowed"], mapping.PathPrefixes);
        Assert.True(mapping.ReplacePrefixes);
        Assert.Equal(expectedKey, capturedPinKey);
        Assert.Equal(credentialStore.ArgonSalt, capturedSalt);
    }

    [Fact]
    public void ImportAdditionalConfig_Succeeds_WithoutMutatingDatabaseState()
    {
        using var pinKey = TestSecretFactory.Create(32);
        var database = new AppDatabase();
        database.Apps.Add(new AppEntry { Id = "main-app", Name = "Existing" });
        database.GetOrCreateAccount("S-1-5-21-0-0-0-42").Grants.Add(new GrantedPathEntry { Path = @"C:\ExistingGrant" });
        var databaseBefore = CaptureDatabaseState(database);

        var appConfigService = new Mock<IAppConfigService>();

        var handler = BuildHandler(database, new CredentialStore(), pinKey, appConfigService: appConfigService);

        var importConfig = new AppConfig { Apps = [new AppEntry { Id = "added", Name = "AddedApp" }] };
        var json = System.Text.Json.JsonSerializer.Serialize(importConfig, JsonDefaults.Options);
        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var importPath = Path.Combine(tempDir.Path, "import.json");
        File.WriteAllText(importPath, json);

        var result = handler.ImportAdditionalConfig(importPath, @"C:\configs\extra.rfn");

        Assert.Equal(AdditionalConfigImportStatus.Succeeded, result.Status);
        Assert.Equal(databaseBefore, CaptureDatabaseState(database));
    }

    [Fact]
    public void ImportAdditionalConfig_PersistenceFailure_DoesNotMutateDatabaseState()
    {
        using var pinKey = TestSecretFactory.Create(32);
        var database = new AppDatabase();
        var initialApp = new AppEntry { Id = "main-app", Name = "Existing" };
        database.Apps.Add(initialApp);
        var databaseBefore = CaptureDatabaseState(database);

        var appConfigService = new Mock<IAppConfigService>();
        appConfigService
            .Setup(s => s.SaveImportedConfig(It.IsAny<string>(), It.IsAny<AppConfig>(), It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()))
            .Throws(new IOException("Disk full."));

        var handler = BuildHandler(database, new CredentialStore(), pinKey, appConfigService: appConfigService);

        var importConfig = new AppConfig { Apps = [] };
        var json = System.Text.Json.JsonSerializer.Serialize(importConfig, JsonDefaults.Options);
        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var importPath = Path.Combine(tempDir.Path, "import.json");
        File.WriteAllText(importPath, json);

        var result = handler.ImportAdditionalConfig(importPath, @"C:\configs\extra.rfn");

        Assert.Equal(AdditionalConfigImportStatus.PersistenceFailed, result.Status);
        Assert.Equal(databaseBefore, CaptureDatabaseState(database));
    }

    [Fact]
    public void ImportAdditionalConfig_AfterKeyReplacement_UsesPinDerivedKeySnapshotSource()
    {
        var database = new AppDatabase();
        using var originalPinKey = TestSecretFactory.Create(32);
        using var replacementPinKey = new SecureSecret(32, data => data.Fill(0x33));
        var session = new SessionContext
{
            Database = database,
            CredentialStore = new CredentialStore { ArgonSalt = [1, 2, 3, 4] },
        }.WithClonedPinDerivedKey(originalPinKey);
        _sessions.Add(session);
        session.ReplacePinDerivedKey(replacementPinKey);

        ISecureSecretSnapshotSource? capturedSource = null;
        var appConfigService = new Mock<IAppConfigService>();
        appConfigService
            .Setup(s => s.SaveImportedConfig(It.IsAny<string>(), It.IsAny<AppConfig>(), It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()))
            .Callback<string, AppConfig, ISecureSecretSnapshotSource, byte[]>((_, _, source, _) => capturedSource = source);

        var handler = CreateHandlerForSession(session, appConfigService.Object);
        var importConfig = new AppConfig { Apps = [] };

        var json = System.Text.Json.JsonSerializer.Serialize(importConfig, JsonDefaults.Options);
        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var importJsonPath = Path.Combine(tempDir.Path, "import.json");
        File.WriteAllText(importJsonPath, json);

        var result = handler.ImportAdditionalConfig(importJsonPath, @"C:\configs\extra.rfn");

        Assert.Equal(AdditionalConfigImportStatus.Succeeded, result.Status);
        Assert.Same(session.PinDerivedKey, capturedSource);
    }

    [Fact]
    public void ImportAdditionalConfig_FileNotFound_ReturnsValidationFailed()
    {
        using var pinKey = TestSecretFactory.Create(32);
        var database = new AppDatabase();
        var databaseBefore = CaptureDatabaseState(database);
        var handler = BuildHandler(database, new CredentialStore(), pinKey);

        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".rfn");

        var result = handler.ImportAdditionalConfig(nonExistentPath, @"C:\configs\extra.rfn");
        Assert.Equal(AdditionalConfigImportStatus.ValidationFailed, result.Status);
        Assert.Equal(databaseBefore, CaptureDatabaseState(database));
    }

    [Fact]
    public void ImportAdditionalConfig_MalformedJson_ReturnsValidationFailedAndLeavesDatabaseUnchanged()
    {
        using var pinKey = TestSecretFactory.Create(32);
        var database = new AppDatabase();
        var databaseBefore = CaptureDatabaseState(database);
        var handler = BuildHandler(database, new CredentialStore(), pinKey);

        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var malformedPath = Path.Combine(tempDir.Path, "malformed.json");
        File.WriteAllText(malformedPath, "{ this is not valid json {{{{");

        var result = handler.ImportAdditionalConfig(malformedPath, @"C:\configs\extra.rfn");
        Assert.Equal(AdditionalConfigImportStatus.ValidationFailed, result.Status);
        Assert.Equal(databaseBefore, CaptureDatabaseState(database));
    }

    [Fact]
    public void ImportAdditionalConfig_SaveImportedConfigFails_RollsBackExistingFileContents()
    {
        using var pinKey = TestSecretFactory.Create(32);
        var database = new AppDatabase();
        var appConfigService = new Mock<IAppConfigService>();
        appConfigService.Setup(s => s.SaveImportedConfig(
                It.IsAny<string>(), It.IsAny<AppConfig>(), It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()))
            .Throws(new IOException("Disk full."));

        var handler = BuildHandler(database, new CredentialStore(), pinKey, appConfigService: appConfigService);

        var importConfig = new AppConfig { Apps = [] };
        var json = System.Text.Json.JsonSerializer.Serialize(importConfig, JsonDefaults.Options);
        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        using var importTargetDir = new TempDirectory("RunFence_ConfigImport_Target");
        var targetConfigPath = Path.Combine(importTargetDir.Path, "extra.rfn");
        var originalPayload = "original file content"u8.ToArray();
        File.WriteAllBytes(targetConfigPath, originalPayload);
        var importJsonPath = Path.Combine(tempDir.Path, "import.json");
        File.WriteAllText(importJsonPath, json);

        var result = handler.ImportAdditionalConfig(importJsonPath, targetConfigPath);

        Assert.Equal(AdditionalConfigImportStatus.PersistenceFailed, result.Status);
        Assert.Equal(originalPayload, File.ReadAllBytes(targetConfigPath));
    }

    [Fact]
    public void ImportAdditionalConfig_InvalidImportedAppId_ReturnsValidationFailed()
    {
        using var pinKey = TestSecretFactory.Create(32);
        var database = new AppDatabase();
        var appConfigService = new Mock<IAppConfigService>();
        appConfigService.Setup(s => s.SaveImportedConfig(
                It.IsAny<string>(), It.IsAny<AppConfig>(), It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()))
            .Throws(new InvalidAppIdException(@"..\bad", "Imported app ID is invalid."));

        var handler = BuildHandler(database, new CredentialStore(), pinKey, appConfigService: appConfigService);

        var importConfig = new AppConfig { Apps = [] };
        var json = System.Text.Json.JsonSerializer.Serialize(importConfig, JsonDefaults.Options);
        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var importJsonPath = Path.Combine(tempDir.Path, "import.json");
        File.WriteAllText(importJsonPath, json);

        var result = handler.ImportAdditionalConfig(importJsonPath, @"C:\configs\extra.rfn");

        Assert.Equal(AdditionalConfigImportStatus.ValidationFailed, result.Status);
    }

    [Fact]
    public void ImportAdditionalConfigCoordinator_WhenTargetIsLoaded_UnloadsThenReloads()
    {
        using var pinKey = TestSecretFactory.Create(32);
        var database = new AppDatabase();
        var appConfigService = new Mock<IAppConfigService>();
        var configManager = new Mock<IAdditionalConfigLoadService>();
        var log = new Mock<ILoggingService>();
        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var configPath = Path.Combine(tempDir.Path, "extra.rfn");

        appConfigService.Setup(s => s.GetLoadedConfigPaths()).Returns([configPath]);
        configManager.Setup(c => c.UnloadApps(configPath)).Returns(true);
        configManager.Setup(c => c.LoadApps(configPath)).Returns(new LoadAppsResult(true, null));

        var handler = BuildHandler(
            database,
            new CredentialStore(),
            pinKey,
            appConfigService: appConfigService);
        var coordinator = new AdditionalConfigImportCoordinator(
            appConfigService.Object,
            configManager.Object,
            handler,
            log.Object);

        var importConfig = new AppConfig { Apps = [] };
        var json = System.Text.Json.JsonSerializer.Serialize(importConfig, JsonDefaults.Options);
        var importJsonPath = Path.Combine(tempDir.Path, "import.json");
        File.WriteAllText(importJsonPath, json);
        File.WriteAllText(configPath, "existing");

        var result = coordinator.ImportAdditionalConfig(importJsonPath, configPath);

        Assert.Equal(AdditionalConfigImportStatus.Succeeded, result.Status);
        configManager.Verify(c => c.UnloadApps(configPath), Times.Once);
        configManager.Verify(c => c.LoadApps(configPath), Times.Once);
    }

    [Fact]
    public void ImportAdditionalConfigCoordinator_ReloadFailure_RestoresPreviousFileAndReturnsReloadFailed()
    {
        using var pinKey = TestSecretFactory.Create(32);
        var database = new AppDatabase();
        var appConfigService = new Mock<IAppConfigService>();
        var configManager = new Mock<IAdditionalConfigLoadService>();
        var log = new Mock<ILoggingService>();
        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var configPath = Path.Combine(tempDir.Path, "extra.rfn");
        const string oldFile = "old-content";
        File.WriteAllText(configPath, oldFile);

        appConfigService.Setup(s => s.GetLoadedConfigPaths()).Returns([configPath]);
        configManager.Setup(c => c.UnloadApps(configPath)).Returns(true);
        configManager.SetupSequence(c => c.LoadApps(configPath))
            .Returns(new LoadAppsResult(false, "reload failed"))
            .Returns(new LoadAppsResult(true, null));

        var handler = BuildHandler(
            database,
            new CredentialStore(),
            pinKey,
            appConfigService: appConfigService);
        var coordinator = new AdditionalConfigImportCoordinator(
            appConfigService.Object,
            configManager.Object,
            handler,
            log.Object);

        var importConfig = new AppConfig { Apps = [new AppEntry { Id = "app1", Name = "New" }] };
        var json = System.Text.Json.JsonSerializer.Serialize(importConfig, JsonDefaults.Options);
        var importJsonPath = Path.Combine(tempDir.Path, "import.json");
        File.WriteAllText(importJsonPath, json);

        var result = coordinator.ImportAdditionalConfig(importJsonPath, configPath);

        Assert.Equal(AdditionalConfigImportStatus.ReloadFailed, result.Status);
        Assert.Equal(oldFile, File.ReadAllText(configPath));
        configManager.Verify(c => c.LoadApps(configPath), Times.Exactly(2));
    }

    [Fact]
    public void ImportAdditionalConfigCoordinator_ImportFailurePreservesRootCauseBeforePreviousReloadFailure()
    {
        using var pinKey = TestSecretFactory.Create(32);
        var database = new AppDatabase();
        var appConfigService = new Mock<IAppConfigService>();
        appConfigService.Setup(service => service.SaveImportedConfig(
                It.IsAny<string>(),
                It.IsAny<AppConfig>(),
                It.IsAny<ISecureSecretSnapshotSource>(),
                It.IsAny<byte[]>()))
            .Throws(new InvalidAppIdException(@"..\bad", "Imported app ID is invalid."));
        var configManager = new Mock<IAdditionalConfigLoadService>();
        var log = new Mock<ILoggingService>();
        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var configPath = Path.Combine(tempDir.Path, "extra.rfn");

        appConfigService.Setup(service => service.GetLoadedConfigPaths()).Returns([configPath]);
        configManager.Setup(service => service.UnloadApps(configPath)).Returns(true);
        configManager.Setup(service => service.LoadApps(configPath))
            .Returns(new LoadAppsResult(false, "previous reload failed"));

        var handler = BuildHandler(
            database,
            new CredentialStore(),
            pinKey,
            appConfigService: appConfigService);
        var coordinator = new AdditionalConfigImportCoordinator(
            appConfigService.Object,
            configManager.Object,
            handler,
            log.Object);

        var importJsonPath = Path.Combine(tempDir.Path, "import.json");
        File.WriteAllText(importJsonPath, System.Text.Json.JsonSerializer.Serialize(new AppConfig(), JsonDefaults.Options));
        File.WriteAllText(configPath, "existing");

        var result = coordinator.ImportAdditionalConfig(importJsonPath, configPath);

        Assert.Equal(AdditionalConfigImportStatus.RollbackFailed, result.Status);
        Assert.Equal(
            [
                "Imported app ID is invalid.",
                "Failed to reload/parse previous config: previous reload failed"
            ],
            result.Errors);
    }

    [Fact]
    public void ImportAdditionalConfigCoordinator_ReloadFailurePreservesImportedReloadErrorBeforePreviousReloadFailure()
    {
        using var pinKey = TestSecretFactory.Create(32);
        var database = new AppDatabase();
        var appConfigService = new Mock<IAppConfigService>();
        var configManager = new Mock<IAdditionalConfigLoadService>();
        var log = new Mock<ILoggingService>();
        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var configPath = Path.Combine(tempDir.Path, "extra.rfn");
        File.WriteAllText(configPath, "old-content");

        appConfigService.Setup(service => service.GetLoadedConfigPaths()).Returns([configPath]);
        configManager.Setup(service => service.UnloadApps(configPath)).Returns(true);
        configManager.SetupSequence(service => service.LoadApps(configPath))
            .Returns(new LoadAppsResult(false, "imported reload failed"))
            .Returns(new LoadAppsResult(false, "previous reload failed"));

        var handler = BuildHandler(
            database,
            new CredentialStore(),
            pinKey,
            appConfigService: appConfigService);
        var coordinator = new AdditionalConfigImportCoordinator(
            appConfigService.Object,
            configManager.Object,
            handler,
            log.Object);

        var importConfig = new AppConfig { Apps = [new AppEntry { Id = "app1", Name = "Imported" }] };
        var importJsonPath = Path.Combine(tempDir.Path, "import.json");
        File.WriteAllText(importJsonPath, System.Text.Json.JsonSerializer.Serialize(importConfig, JsonDefaults.Options));

        var result = coordinator.ImportAdditionalConfig(importJsonPath, configPath);

        Assert.Equal(AdditionalConfigImportStatus.RollbackFailed, result.Status);
        Assert.Equal(
            [
                "Failed to reload imported config: imported reload failed",
                "Failed to reload/parse previous config: previous reload failed"
            ],
            result.Errors);
    }

    [Fact]
    public void ImportAdditionalConfigCoordinator_BackupRestoreFailurePreservesImportedReloadError()
    {
        using var pinKey = TestSecretFactory.Create(32);
        var database = new AppDatabase();
        var appConfigService = new Mock<IAppConfigService>();
        var configManager = new Mock<IAdditionalConfigLoadService>();
        var log = new Mock<ILoggingService>();
        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        using var targetDir = new TempDirectory("RunFence_ConfigImport_Target");
        var configPath = Path.Combine(targetDir.Path, "extra.rfn");
        File.WriteAllText(configPath, "old-content");

        appConfigService.Setup(service => service.GetLoadedConfigPaths()).Returns([configPath]);
        configManager.Setup(service => service.UnloadApps(configPath)).Returns(true);
        configManager.Setup(service => service.LoadApps(configPath))
            .Callback(() =>
            {
                if (Directory.Exists(targetDir.Path))
                    Directory.Delete(targetDir.Path, recursive: true);
            })
            .Returns(new LoadAppsResult(false, "imported reload failed"));

        var handler = BuildHandler(
            database,
            new CredentialStore(),
            pinKey,
            appConfigService: appConfigService);
        var coordinator = new AdditionalConfigImportCoordinator(
            appConfigService.Object,
            configManager.Object,
            handler,
            log.Object);

        var importConfig = new AppConfig { Apps = [new AppEntry { Id = "app1", Name = "Imported" }] };
        var importJsonPath = Path.Combine(tempDir.Path, "import.json");
        File.WriteAllText(importJsonPath, System.Text.Json.JsonSerializer.Serialize(importConfig, JsonDefaults.Options));

        var result = coordinator.ImportAdditionalConfig(importJsonPath, configPath);

        Assert.Equal(AdditionalConfigImportStatus.RollbackFailed, result.Status);
        Assert.Equal(
            ["Failed to reload imported config: imported reload failed"],
            result.Errors);
    }
}
