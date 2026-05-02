using Moq;
using RunFence.Acl;
using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public class ConfigImportHandlerTests
{
    private static ConfigImportHandler BuildHandler(
        AppDatabase database,
        CredentialStore store,
        ProtectedBuffer pinKey,
        Mock<IAppConfigService>? appConfigService = null,
        Mock<IHandlerMappingService>? handlerMappingService = null,
        Mock<IPathGrantService>? pathGrantService = null,
        Mock<ILoggingService>? log = null,
        Mock<IAppEntryIdGenerator>? idGenerator = null,
        Mock<IGrantInspectionService>? grantInspection = null,
        Mock<IGrantConfigTracker>? grantTracker = null)
    {
        // Track which mocks were created here vs passed in — only apply defaults to locally-created mocks
        // so caller-configured setups are not overridden (Moq uses last-setup-wins semantics).
        bool appConfigCreatedHere = appConfigService == null;
        bool idGeneratorCreatedHere = idGenerator == null;
        bool grantInspectionCreatedHere = grantInspection == null;
        bool grantTrackerCreatedHere = grantTracker == null;
        appConfigService ??= new Mock<IAppConfigService>();
        handlerMappingService ??= new Mock<IHandlerMappingService>();
        pathGrantService ??= new Mock<IPathGrantService>();
        log ??= new Mock<ILoggingService>();
        idGenerator ??= new Mock<IAppEntryIdGenerator>();
        grantInspection ??= new Mock<IGrantInspectionService>();
        var licenseService = new Mock<ILicenseService>();
        var sessionProvider = new Mock<ISessionProvider>();
        grantTracker ??= new Mock<IGrantConfigTracker>();

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

        if (grantTrackerCreatedHere)
        {
            // Default: treat all pre-existing grants as main-config (correct default: no additional configs loaded in tests)
            grantTracker.Setup(t => t.IsInMainConfig(It.IsAny<string>(), It.IsAny<GrantedPathEntry>())).Returns(true);
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
            PinDerivedKey = pinKey
        };
        sessionProvider.Setup(s => s.GetSession()).Returns(session);

        var fileParserInstance = new ConfigImportFileParser();
        var preservationCollectorInstance = new MainConfigImportPreservationCollector(grantTracker.Object);
        var evaluationValidatorInstance = new MainConfigImportEvaluationValidator(licenseService.Object, appConfigService.Object);
        var repairServiceInstance = new MainConfigImportRepairService(appConfigService.Object, handlerMappingService.Object, pathGrantService.Object, log.Object, idGenerator.Object);
        var applyServiceInstance = new MainConfigImportApplyService(appConfigService.Object, grantInspection.Object);
        var saveHelperInstance = new MainConfigImportSaveHelper(appConfigService.Object);

        return new ConfigImportHandler(
            appConfigService.Object, sessionProvider.Object, log.Object,
            fileParserInstance, preservationCollectorInstance, evaluationValidatorInstance,
            repairServiceInstance, applyServiceInstance, saveHelperInstance);
    }

    private static void WriteJsonToFile(AppDatabase db, string path)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(db, JsonDefaults.Options);
        File.WriteAllText(path, json);
    }

    [Fact]
    public void ImportMainConfig_AppContainers_FullReplaceFromImport()
    {
        // Arrange: database already has "existing-container"; imported config has the same plus "new-container".
        // Full replace: both containers from import present; existing-container has the import's DisplayName.
        using var pinKey = new ProtectedBuffer(new byte[32], protect: false);
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
        using var pinKey = new ProtectedBuffer(new byte[32], protect: false);
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
        using var pinKey = new ProtectedBuffer(new byte[32], protect: false);
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
        using var pinKey = new ProtectedBuffer(new byte[32], protect: false);
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
    public void ImportMainConfig_OrphanedSid_NotInIncomingOrAdditional_RemovesGrantsFromFileSystem()
    {
        // Arrange: current main config has an app with SID "S-1-orphan".
        // The imported config does NOT include that SID. No additional config uses it.
        // The import must call RemoveAll for the orphaned SID.
        const string orphanSid = "S-1-5-21-0-0-0-9999";
        using var pinKey = new ProtectedBuffer(new byte[32], protect: false);
        var database = new AppDatabase();
        database.Apps.Add(new AppEntry { Id = "old-app", Name = "OldApp", AccountSid = orphanSid });

        var pathGrantService = new Mock<IPathGrantService>();
        var handler = BuildHandler(database, new CredentialStore(), pinKey,
            pathGrantService: pathGrantService);

        var importedDb = new AppDatabase();
        importedDb.Apps.Add(new AppEntry { Id = "new-app", Name = "NewApp", AccountSid = "S-1-5-21-0-0-0-1001" });

        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var tempFile = Path.Combine(tempDir.Path, "config.json");
        WriteJsonToFile(importedDb, tempFile);

        // Act
        handler.ImportMainConfig(tempFile, null);

        // Assert: RemoveAll called for the orphaned SID
        pathGrantService.Verify(p => p.RemoveAll(orphanSid, true), Times.Once);
    }

    [Fact]
    public void ImportMainConfig_SystemAccount_HasHighestAllowedAfterImport()
    {
        // Arrange: imported config has no SYSTEM account entry — import must still guarantee the invariant.
        using var pinKey = new ProtectedBuffer(new byte[32], protect: false);
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
        // RemoveAll must NOT be called for that SID.
        const string keepSid = "S-1-5-21-0-0-0-1001";
        using var pinKey = new ProtectedBuffer(new byte[32], protect: false);
        var database = new AppDatabase();
        database.Apps.Add(new AppEntry { Id = "old-app", Name = "OldApp", AccountSid = keepSid });

        var pathGrantService = new Mock<IPathGrantService>();
        var handler = BuildHandler(database, new CredentialStore(), pinKey,
            pathGrantService: pathGrantService);

        var importedDb = new AppDatabase();
        importedDb.Apps.Add(new AppEntry { Id = "new-app", Name = "NewApp", AccountSid = keepSid });

        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var tempFile = Path.Combine(tempDir.Path, "config.json");
        WriteJsonToFile(importedDb, tempFile);

        // Act
        handler.ImportMainConfig(tempFile, null);

        // Assert: RemoveAll NOT called because SID is in incoming config
        pathGrantService.Verify(p => p.RemoveAll(keepSid, It.IsAny<bool>()), Times.Never);
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
        using var pinKey = new ProtectedBuffer(new byte[32], protect: false);
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
        using var pinKey = new ProtectedBuffer(new byte[32], protect: false);
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
        using var pinKey = new ProtectedBuffer(new byte[32], protect: false);
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
        using var pinKey = new ProtectedBuffer(new byte[32], protect: false);
        var database = new AppDatabase
        {
            SharedContainerTraverseGrants =
            [
                new GrantedPathEntry { Path = @"C:\OldShared", IsTraverseOnly = true }
            ]
        };

        var importedDb = new AppDatabase
        {
            SharedContainerTraverseGrants =
            [
                new GrantedPathEntry { Path = @"C:\ImportedShared", IsTraverseOnly = true }
            ]
        };

        var handler = BuildHandler(database, new CredentialStore(), pinKey);
        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var tempFile = Path.Combine(tempDir.Path, "config.json");
        WriteJsonToFile(importedDb, tempFile);

        handler.ImportMainConfig(tempFile, null);

        Assert.Single(database.SharedContainerTraverseGrants);
        Assert.Equal(@"C:\ImportedShared", database.SharedContainerTraverseGrants[0].Path);
    }

    [Fact]
    public void ImportMainConfig_SharedContainerTraverse_PreservesAdditionalConfigEntries()
    {
        using var pinKey = new ProtectedBuffer(new byte[32], protect: false);
        var additionalEntry = new GrantedPathEntry { Path = @"C:\AdditionalShared", IsTraverseOnly = true };
        var database = new AppDatabase
        {
            SharedContainerTraverseGrants = [additionalEntry]
        };
        var grantTracker = new Mock<IGrantConfigTracker>();
        grantTracker.Setup(t => t.IsInMainConfig(
                WellKnownSecuritySids.AllApplicationPackagesSid,
                additionalEntry))
            .Returns(false);
        grantTracker.Setup(t => t.IsInMainConfig(
                It.IsAny<string>(),
                It.Is<GrantedPathEntry>(e => !ReferenceEquals(e, additionalEntry))))
            .Returns(true);

        var handler = BuildHandler(database, new CredentialStore(), pinKey, grantTracker: grantTracker);
        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var tempFile = Path.Combine(tempDir.Path, "config.json");
        WriteJsonToFile(new AppDatabase(), tempFile);

        handler.ImportMainConfig(tempFile, null);

        Assert.Single(database.SharedContainerTraverseGrants);
        Assert.Equal(@"C:\AdditionalShared", database.SharedContainerTraverseGrants[0].Path);
    }

    [Fact]
    public void ImportMainConfig_SharedContainerTraverse_PreservesOldMainWhenAceAvailable()
    {
        const string path = @"C:\LocalShared";
        using var pinKey = new ProtectedBuffer(new byte[32], protect: false);
        var database = new AppDatabase
        {
            SharedContainerTraverseGrants =
            [
                new GrantedPathEntry { Path = path, IsTraverseOnly = true }
            ]
        };
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

        Assert.Single(database.SharedContainerTraverseGrants);
        Assert.Equal(path, database.SharedContainerTraverseGrants[0].Path);
    }

    // ── SidNames preservation ────────────────────────────────────────────────

    [Fact]
    public void ImportMainConfig_SidNamesPreservation_ResolvedSidNotInImport_ReAdded()
    {
        // Arrange: database has SID "S-1-local" in SidNames. Import doesn't include it.
        // sidResolutions has it resolved → must be re-added after full replace.
        const string localSid = "S-1-5-21-0-0-0-5000";
        const string localName = "LocalUser";
        using var pinKey = new ProtectedBuffer(new byte[32], protect: false);
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
        using var pinKey = new ProtectedBuffer(new byte[32], protect: false);
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
        using var pinKey = new ProtectedBuffer(new byte[32], protect: false);
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
        using var pinKey = new ProtectedBuffer(new byte[32], protect: false);
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
        using var pinKey = new ProtectedBuffer(new byte[32], protect: false);
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
        using var pinKey = new ProtectedBuffer(new byte[32], protect: false);
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

    // ── Additional-app ID collision updates index mapping ────────────────────

    [Fact]
    public void ImportMainConfig_AdditionalAppIdCollision_UpdatesIndexMapping()
    {
        // Arrange: additional-config app with ID "shared-id" and known configPath.
        // Imported main-config also has "shared-id". After collision repair,
        // appConfigService.RemoveApp(oldId) and AssignApp(newId, configPath) must be called.
        const string configPath = @"C:\extra.rfn";
        const string collisionId = "shared-id";
        using var pinKey = new ProtectedBuffer(new byte[32], protect: false);
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
        using var pinKey = new ProtectedBuffer(new byte[32], protect: false);
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
    public void ImportAdditionalConfig_DeserializesAndSavesImportedConfigWithoutMutation()
    {
        var expectedKey = new byte[32];
        using var pinKey = new ProtectedBuffer(expectedKey, protect: false);
        var database = new AppDatabase();
        var appConfigService = new Mock<IAppConfigService>();
        var log = new Mock<ILoggingService>();

        AppConfig? capturedConfig = null;
        byte[]? capturedPinKey = null;
        byte[]? capturedSalt = null;
        appConfigService
            .Setup(s => s.SaveImportedConfig(It.IsAny<string>(), It.IsAny<AppConfig>(), It.IsAny<byte[]>(), It.IsAny<byte[]>()))
            .Callback<string, AppConfig, byte[], byte[]>((_, config, key, salt) =>
            {
                capturedConfig = config;
                capturedPinKey = key.ToArray();
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

        handler.ImportAdditionalConfig(importJsonPath, targetConfigPath);

        appConfigService.Verify(
            s => s.SaveImportedConfig(targetConfigPath, It.IsAny<AppConfig>(), It.IsAny<byte[]>(), It.IsAny<byte[]>()),
            Times.Once);
        Assert.NotNull(capturedConfig);
        Assert.NotSame(importConfig, capturedConfig);
        Assert.Single(capturedConfig!.Apps);
        Assert.Equal("app-1", capturedConfig.Apps[0].Id);
        Assert.Equal("ImportedApp", capturedConfig.Apps[0].Name);
        Assert.NotNull(capturedConfig.Accounts);
        Assert.Single(capturedConfig.Accounts!);
        Assert.Equal("S-1-5-21-1", capturedConfig.Accounts[0].Sid);
        Assert.Single(capturedConfig.Accounts[0].Grants);
        Assert.Equal(@"C:\Data", capturedConfig.Accounts[0].Grants[0].Path);
        Assert.NotNull(capturedConfig.HandlerMappings);
        Assert.True(capturedConfig.HandlerMappings!.ContainsKey(".txt"));
        var mapping = capturedConfig.HandlerMappings[".txt"];
        Assert.Equal("app-1", mapping.AppId);
        Assert.Equal("\"%1\"", mapping.ArgumentsTemplate);
        Assert.Equal(["C:\\Allowed"], mapping.PathPrefixes);
        Assert.True(mapping.ReplacePrefixes);
        Assert.Equal(expectedKey, capturedPinKey);
        Assert.Equal(credentialStore.ArgonSalt, capturedSalt);
        log.Verify(
            l => l.Info($"Additional config imported from {importJsonPath} into {targetConfigPath}"),
            Times.Once);
    }

    [Fact]
    public void ImportAdditionalConfig_FileNotFound_PropagatesException()
    {
        using var pinKey = new ProtectedBuffer(new byte[32], protect: false);
        var database = new AppDatabase();
        var handler = BuildHandler(database, new CredentialStore(), pinKey);

        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".rfn");

        Assert.Throws<FileNotFoundException>(() =>
            handler.ImportAdditionalConfig(nonExistentPath, @"C:\configs\extra.rfn"));
    }

    [Fact]
    public void ImportAdditionalConfig_MalformedJson_PropagatesJsonException()
    {
        using var pinKey = new ProtectedBuffer(new byte[32], protect: false);
        var database = new AppDatabase();
        var handler = BuildHandler(database, new CredentialStore(), pinKey);

        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var malformedPath = Path.Combine(tempDir.Path, "malformed.json");
        File.WriteAllText(malformedPath, "{ this is not valid json {{{{");

        Assert.Throws<System.Text.Json.JsonException>(() =>
            handler.ImportAdditionalConfig(malformedPath, @"C:\configs\extra.rfn"));
    }

    [Fact]
    public void ImportAdditionalConfig_SaveImportedConfigFails_PropagatesException()
    {
        using var pinKey = new ProtectedBuffer(new byte[32], protect: false);
        var database = new AppDatabase();
        var appConfigService = new Mock<IAppConfigService>();
        appConfigService.Setup(s => s.SaveImportedConfig(
                It.IsAny<string>(), It.IsAny<AppConfig>(), It.IsAny<byte[]>(), It.IsAny<byte[]>()))
            .Throws(new InvalidOperationException("Disk full."));

        var handler = BuildHandler(database, new CredentialStore(), pinKey, appConfigService: appConfigService);

        var importConfig = new AppConfig { Apps = [] };
        var json = System.Text.Json.JsonSerializer.Serialize(importConfig, JsonDefaults.Options);
        using var tempDir = new TempDirectory("RunFence_ConfigImport");
        var importJsonPath = Path.Combine(tempDir.Path, "import.json");
        File.WriteAllText(importJsonPath, json);

        Assert.Throws<InvalidOperationException>(() =>
            handler.ImportAdditionalConfig(importJsonPath, @"C:\configs\extra.rfn"));
    }
}
