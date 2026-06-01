using Autofac;
using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.Persistence.UI;
using RunFence.Startup;
using Xunit;

namespace RunFence.Tests;

public sealed class GrantIntentStoreTests : IDisposable
{
    private readonly SecureSecret _pinKey = TestSecretFactory.Create(32);
    private readonly List<SessionContext> _sessions = [];

    public void Dispose()
    {
        foreach (var session in _sessions)
            session.Dispose();

        _pinKey.Dispose();
    }

    [Fact]
    public void MainStore_AddGetReplaceRemove_UsesSnapshotsAndKeyMatching()
    {
        var database = new AppDatabase();
        var configSave = CreateConfigSaveOrchestrator(database, out _, out _, out _, out _);
        var store = CreateMainStore(database, configSave, out _, out _);
        var sid = "S-1-5-21-100";
        var original = new GrantedPathEntry
        {
            Path = @"C:\grant",
            SavedRights = SavedRightsState.DefaultForMode(isDeny: false)
        };

        store.AddEntry(sid, original);
        original.Path = @"C:\mutated-after-add";

        var snapshot = Assert.Single(store.GetEntries(sid));
        snapshot.Path = @"C:\mutated-snapshot";

        var stored = Assert.Single(store.GetEntries(sid));
        Assert.Equal(@"C:\grant", stored.Path);

        var replacement = new GrantedPathEntry
        {
            Path = @"C:\grant-renamed",
            IsDeny = true,
            SavedRights = SavedRightsState.DefaultForMode(isDeny: true)
        };
        Assert.True(store.ReplaceEntry(sid, stored, replacement));

        var replaced = Assert.Single(store.GetEntries(sid));
        Assert.Equal(@"C:\grant-renamed", replaced.Path);
        Assert.True(replaced.IsDeny);

        Assert.True(store.RemoveEntry(sid, replaced));
        Assert.Empty(store.GetEntries(sid));
        Assert.Null(database.GetAccount(sid));
    }

    [Fact]
    public void MainStore_AllApplicationPackagesSid_UsesAccountGrantBucket()
    {
        var database = new AppDatabase();
        var configSave = CreateConfigSaveOrchestrator(database, out _, out _, out _, out _);
        var store = CreateMainStore(database, configSave, out _, out _);
        var traverse = new GrantedPathEntry { Path = @"C:\shared", IsTraverseOnly = true };

        store.AddEntry(WellKnownSecuritySids.AllApplicationPackagesSid, traverse);

        var account = Assert.Single(database.Accounts);
        Assert.Equal(WellKnownSecuritySids.AllApplicationPackagesSid, account.Sid);
        Assert.Single(account.Grants);
        var snapshot = Assert.Single(store.GetEntries(WellKnownSecuritySids.AllApplicationPackagesSid));
        snapshot.Path = @"C:\mutated";
        Assert.Equal(@"C:\shared", Assert.Single(account.Grants).Path);
    }

    [Fact]
    public void MainStore_AllApplicationPackagesSid_NonTraverseGrant_UsesSingleAccountGrantBucket()
    {
        var database = new AppDatabase();
        var configSave = CreateConfigSaveOrchestrator(database, out _, out _, out _, out _);
        var store = CreateMainStore(database, configSave, out _, out _);
        var grant = new GrantedPathEntry
        {
            Path = @"C:\grant-aap",
            SavedRights = SavedRightsState.DefaultForMode(isDeny: false)
        };
        var traverse = new GrantedPathEntry { Path = @"C:\traverse-aap", IsTraverseOnly = true };

        store.AddEntry(WellKnownSecuritySids.AllApplicationPackagesSid, grant);
        store.AddEntry(WellKnownSecuritySids.AllApplicationPackagesSid, traverse);

        var account = database.GetAccount(WellKnownSecuritySids.AllApplicationPackagesSid);
        Assert.NotNull(account);
        Assert.Equal(2, account!.Grants.Count);
        var entries = store.GetEntries(WellKnownSecuritySids.AllApplicationPackagesSid);
        var storedGrant = Assert.Single(entries, entry => !entry.IsTraverseOnly);
        Assert.Equal(@"C:\grant-aap", storedGrant.Path);
        var storedTraverse = Assert.Single(entries, entry => entry.IsTraverseOnly);
        Assert.Equal(@"C:\traverse-aap", storedTraverse.Path);
    }

    [Fact]
    public void MainStore_Save_UsesMainConfigSavePath()
    {
        var database = new AppDatabase();
        var configSave = CreateConfigSaveOrchestrator(
            database,
            out _,
            out _,
            out var databaseService,
            out _);
        var store = CreateMainStore(database, configSave, out _, out _);

        store.Save();

        databaseService.Verify(service => service.SaveConfig(
            database,
            It.IsAny<ISecureSecretSnapshotSource>(),
            It.IsAny<byte[]>()), Times.Once);
    }

    [Fact]
    public void MainStore_GetEntries_ExcludesMergedEntriesOwnedOnlyByAdditionalConfigs()
    {
        var database = new AppDatabase();
        var configSave = CreateConfigSaveOrchestrator(database, out _, out _, out _, out _);
        var mainStore = CreateMainStore(database, configSave, out var provider, out _);
        var sid = "S-1-5-21-103";
        var sharedEntry = new GrantedPathEntry { Path = @"C:\merged-extra" };
        database.GetOrCreateAccount(sid).Grants.Add(sharedEntry);

        provider.RegisterAdditionalStore(
            @"C:\configs\extra-owned.rfn",
            [new AppConfigAccountEntry { Sid = sid, Grants = [sharedEntry.Clone()] }]);

        Assert.Empty(mainStore.GetEntries(sid));
        Assert.Single(database.GetAccount(sid)!.Grants);
    }

    [Fact]
    public void MainStore_AddEntry_UpdatesMainPayloadWhenAdditionalOwnershipHasSameIdentity()
    {
        var database = new AppDatabase();
        var configSave = CreateConfigSaveOrchestrator(database, out _, out _, out _, out _);
        var mainStore = CreateMainStore(database, configSave, out var provider, out _);
        var sid = "S-1-5-21-107";
        var additionalEntry = new GrantedPathEntry
        {
            Path = @"C:\add-shared-payload",
            SavedRights = SavedRightsState.DefaultForMode(isDeny: false),
            PreviousSaclLabel = "additional-label"
        };
        var mainEntry = additionalEntry.Clone();
        mainEntry.PreviousSaclLabel = "main-label";
        var additionalConfigPath = @"C:\configs\add-same-identity-owned.rfn";

        database.GetOrCreateAccount(sid).Grants.Add(additionalEntry.Clone());
        provider.RegisterAdditionalStore(
            additionalConfigPath,
            [new AppConfigAccountEntry { Sid = sid, Grants = [additionalEntry.Clone()] }]);

        mainStore.AddEntry(sid, mainEntry);

        var runtimeEntry = Assert.Single(database.GetAccount(sid)!.Grants);
        Assert.Equal("main-label", runtimeEntry.PreviousSaclLabel);
        var savedMainEntry = Assert.Single(mainStore.GetEntries(sid));
        Assert.Equal("main-label", savedMainEntry.PreviousSaclLabel);
        var savedAdditionalEntry = Assert.Single(provider.ResolveStore(additionalConfigPath).GetEntries(sid));
        Assert.Equal("additional-label", savedAdditionalEntry.PreviousSaclLabel);

        Assert.True(mainStore.RemoveEntry(sid, mainEntry));
        Assert.Empty(mainStore.GetEntries(sid));
        var restoredRuntimeEntry = Assert.Single(database.GetAccount(sid)!.Grants);
        Assert.Equal("additional-label", restoredRuntimeEntry.PreviousSaclLabel);
    }

    [Fact]
    public void MainStore_ReplaceEntry_PreservesRuntimeProjectionWhenOldEntryStillHasAdditionalOwnership()
    {
        var database = new AppDatabase();
        var configSave = CreateConfigSaveOrchestrator(database, out _, out _, out _, out _);
        var mainStore = CreateMainStore(database, configSave, out var provider, out _);
        var sid = "S-1-5-21-104";
        var sharedEntry = new GrantedPathEntry
        {
            Path = @"C:\shared",
            SavedRights = SavedRightsState.DefaultForMode(isDeny: false)
        };
        var replacement = new GrantedPathEntry
        {
            Path = @"C:\shared",
            IsDeny = false,
            SavedRights = new SavedRightsState(Read: true, Execute: false, Write: true, Special: false, Own: false)
        };
        var additionalConfigPath = @"C:\configs\shared-owned.rfn";

        mainStore.AddEntry(sid, sharedEntry);
        provider.RegisterAdditionalStore(
            additionalConfigPath,
            [new AppConfigAccountEntry { Sid = sid, Grants = [sharedEntry.Clone()] }]);

        Assert.True(mainStore.ReplaceEntry(sid, sharedEntry, replacement));

        var runtimeEntries = database.GetAccount(sid)!.Grants;
        Assert.Equal(2, runtimeEntries.Count);
        Assert.Contains(runtimeEntries, entry => entry.SavedRights == sharedEntry.SavedRights);
        Assert.Contains(runtimeEntries, entry => entry.SavedRights == replacement.SavedRights);

        var mainEntry = Assert.Single(mainStore.GetEntries(sid));
        Assert.Equal(replacement.SavedRights, mainEntry.SavedRights);
        var additionalEntry = Assert.Single(provider.ResolveStore(additionalConfigPath).GetEntries(sid));
        Assert.Equal(sharedEntry.SavedRights, additionalEntry.SavedRights);
    }

    [Fact]
    public void MainStore_ReplaceEntry_UpdatesMainPayloadWhenAdditionalOwnershipHasSameIdentity()
    {
        var database = new AppDatabase();
        var configSave = CreateConfigSaveOrchestrator(database, out _, out _, out _, out _);
        var mainStore = CreateMainStore(database, configSave, out var provider, out _);
        var sid = "S-1-5-21-105";
        var sharedEntry = new GrantedPathEntry
        {
            Path = @"C:\shared-payload",
            SavedRights = SavedRightsState.DefaultForMode(isDeny: false),
            PreviousSaclLabel = "old-label"
        };
        var replacement = sharedEntry.Clone();
        replacement.PreviousSaclLabel = "new-label";
        var additionalConfigPath = @"C:\configs\same-identity-owned.rfn";

        mainStore.AddEntry(sid, sharedEntry);
        provider.RegisterAdditionalStore(
            additionalConfigPath,
            [new AppConfigAccountEntry { Sid = sid, Grants = [sharedEntry.Clone()] }]);

        Assert.True(mainStore.ReplaceEntry(sid, sharedEntry, replacement));

        var runtimeEntry = Assert.Single(database.GetAccount(sid)!.Grants);
        Assert.Equal("new-label", runtimeEntry.PreviousSaclLabel);
        var mainEntry = Assert.Single(mainStore.GetEntries(sid));
        Assert.Equal("new-label", mainEntry.PreviousSaclLabel);
        var additionalEntry = Assert.Single(provider.ResolveStore(additionalConfigPath).GetEntries(sid));
        Assert.Equal("old-label", additionalEntry.PreviousSaclLabel);
    }

    [Fact]
    public void MainStore_RemoveEntry_RestoresAdditionalProjectionPayloadWhenMainPayloadDiffers()
    {
        var database = new AppDatabase();
        var configSave = CreateConfigSaveOrchestrator(database, out _, out _, out _, out _);
        var mainStore = CreateMainStore(database, configSave, out var provider, out _);
        var sid = "S-1-5-21-106";
        var additionalEntry = new GrantedPathEntry
        {
            Path = @"C:\shared-remove-payload",
            SavedRights = SavedRightsState.DefaultForMode(isDeny: false),
            PreviousSaclLabel = "additional-label"
        };
        var mainEntry = additionalEntry.Clone();
        mainEntry.PreviousSaclLabel = "main-label";
        var additionalConfigPath = @"C:\configs\remove-same-identity-owned.rfn";

        mainStore.AddEntry(sid, additionalEntry);
        provider.RegisterAdditionalStore(
            additionalConfigPath,
            [new AppConfigAccountEntry { Sid = sid, Grants = [additionalEntry.Clone()] }]);
        Assert.True(mainStore.ReplaceEntry(sid, additionalEntry, mainEntry));

        Assert.True(mainStore.RemoveEntry(sid, mainEntry));

        Assert.Empty(mainStore.GetEntries(sid));
        var runtimeEntry = Assert.Single(database.GetAccount(sid)!.Grants);
        Assert.Equal("additional-label", runtimeEntry.PreviousSaclLabel);
        var storedAdditionalEntry = Assert.Single(provider.ResolveStore(additionalConfigPath).GetEntries(sid));
        Assert.Equal("additional-label", storedAdditionalEntry.PreviousSaclLabel);
    }

    [Fact]
    public void OwnershipProjection_AdditionalGrantOwnership_IsCloneSafeAndSemantic()
    {
        var ownershipProjection = new GrantIntentOwnershipProjectionService();
        var configPath = @"C:\configs\grant-ownership.rfn";
        var sid = "S-1-5-21-777";
        var entry = new GrantedPathEntry
        {
            Path = @"C:\Grant",
            IsDeny = true,
            SavedRights = SavedRightsState.DefaultForMode(true),
            AllAppliedPaths = [@"C:\Grant", @"C:\Parent", @"C:\Grant"],
            SourceSids = ["S-1-5-21-2", "S-1-5-21-1", "S-1-5-21-2"]
        };

        ownershipProjection.RegisterAdditionalConfig(
            configPath,
            [new AppConfigAccountEntry { Sid = sid, Grants = [entry.Clone()] }]);

        Assert.True(ownershipProjection.HasAdditionalOwnership(configPath, sid, entry.Clone()));
        Assert.True(ownershipProjection.HasAdditionalOwnership(
            configPath,
            sid,
            new GrantedPathEntry
            {
                Path = @"C:\Grant",
                IsDeny = true,
                SavedRights = SavedRightsState.DefaultForMode(true),
                AllAppliedPaths = [@"C:\Parent", @"C:\Grant"],
                SourceSids = ["S-1-5-21-1", "S-1-5-21-2"]
            }));
        Assert.False(ownershipProjection.HasAdditionalOwnership(
            configPath,
            sid,
            CreateClone(entry, clone => clone.SavedRights = SavedRightsState.DefaultForMode(false))));
        Assert.False(ownershipProjection.HasAdditionalOwnership(
            configPath,
            sid,
            CreateClone(entry, clone => clone.Path = @"C:\Other")));
        Assert.False(ownershipProjection.HasAdditionalOwnership(
            configPath,
            sid,
            CreateClone(entry, clone => clone.IsDeny = false)));
        Assert.False(ownershipProjection.HasAdditionalOwnership(
            configPath,
            sid,
            CreateClone(entry, clone => clone.AllAppliedPaths = [@"C:\Other"])));
        Assert.False(ownershipProjection.HasAdditionalOwnership(
            configPath,
            sid,
            CreateClone(entry, clone => clone.SourceSids = ["S-1-5-21-9"])));
    }

    [Fact]
    public void OwnershipProjection_AdditionalAllApplicationPackagesTraverseOwnership_IsCloneSafeAndSemantic()
    {
        var ownershipProjection = new GrantIntentOwnershipProjectionService();
        var configPath = @"C:\configs\traverse-ownership.rfn";
        var entry = new GrantedPathEntry
        {
            Path = @"C:\Traverse",
            IsTraverseOnly = true,
            AllAppliedPaths = [@"C:\Traverse", @"C:\Root"],
            SourceSids = ["S-1-15-2-1", "S-1-15-2-2"]
        };

        ownershipProjection.RegisterAdditionalConfig(
            configPath,
            [new AppConfigAccountEntry
            {
                Sid = WellKnownSecuritySids.AllApplicationPackagesSid,
                Grants = [entry.Clone()]
            }]);

        Assert.True(ownershipProjection.HasAdditionalOwnership(
            configPath,
            WellKnownSecuritySids.AllApplicationPackagesSid,
            new GrantedPathEntry
            {
                Path = @"C:\Traverse",
                IsTraverseOnly = true,
                AllAppliedPaths = [@"C:\Root", @"C:\Traverse"],
                SourceSids = ["S-1-15-2-2", "S-1-15-2-1"]
            }));
        Assert.False(ownershipProjection.HasAdditionalOwnership(
            configPath,
            WellKnownSecuritySids.AllApplicationPackagesSid,
            CreateClone(entry, clone => clone.IsTraverseOnly = false)));
        Assert.False(ownershipProjection.HasAdditionalOwnership(
            configPath,
            WellKnownSecuritySids.AllApplicationPackagesSid,
            CreateClone(entry, clone => clone.SourceSids = ["S-1-15-2-9"])));
    }

    [Fact]
    public void AdditionalStore_AddGetReplaceRemoveAndSave_UsesSnapshotsAndNormalizedPath()
    {
        var database = new AppDatabase();
        var relativeConfigPath = Path.Combine(Path.GetTempPath(), "grant-store-tests", "..", "grant-store-tests", "extra.rfn");
        var accounts = new List<AppConfigAccountEntry>();
        var ownershipProjection = new GrantIntentOwnershipProjectionService();
        var configSave = CreateConfigSaveOrchestrator(
            database,
            out _,
            out var appConfigService,
            out var databaseService,
            out var handlerMappingService);
        var store = new AdditionalGrantIntentStore(
            relativeConfigPath,
            accounts,
            configSave,
            ownershipProjection);
        var sid = "S-1-5-21-101";
        database.Apps.Add(new AppEntry { Id = "extra-app", Name = "Extra App" });
        appConfigService.Setup(service => service.GetAppsForConfig(Path.GetFullPath(relativeConfigPath), database))
            .Returns(database.Apps);
        handlerMappingService.Setup(service => service.GetHandlerMappingsForConfig(Path.GetFullPath(relativeConfigPath)))
            .Returns(new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase)
            {
                [".txt"] = new("extra-app")
            });

        store.AddEntry(sid, new GrantedPathEntry { Path = @"C:\extra" });
        store.AddEntry(
            WellKnownSecuritySids.AllApplicationPackagesSid,
            new GrantedPathEntry
            {
                Path = @"C:\aap-grant",
                SavedRights = SavedRightsState.DefaultForMode(isDeny: false)
            });
        store.AddEntry(
            WellKnownSecuritySids.AllApplicationPackagesSid,
            new GrantedPathEntry { Path = @"C:\shared-extra", IsTraverseOnly = true });

        Assert.Equal(Path.GetFullPath(relativeConfigPath), store.ConfigPath);
        Assert.Equal(2, accounts.Count);
        var aapAccount = Assert.Single(accounts, account =>
            string.Equals(account.Sid, WellKnownSecuritySids.AllApplicationPackagesSid, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, aapAccount.Grants.Count);
        Assert.Contains(aapAccount.Grants, entry => string.Equals(entry.Path, @"C:\aap-grant", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(aapAccount.Grants, entry => string.Equals(entry.Path, @"C:\shared-extra", StringComparison.OrdinalIgnoreCase) && entry.IsTraverseOnly);

        var additionalSnapshot = Assert.Single(store.GetEntries(sid));
        additionalSnapshot.Path = @"C:\mutated";
        Assert.Equal(@"C:\extra", Assert.Single(store.GetEntries(sid)).Path);

        Assert.True(store.ReplaceEntry(sid, new GrantedPathEntry { Path = @"C:\extra" }, new GrantedPathEntry { Path = @"C:\extra-2" }));
        Assert.True(store.RemoveEntry(sid, new GrantedPathEntry { Path = @"C:\extra-2" }));
        Assert.Single(accounts);
        Assert.Equal(WellKnownSecuritySids.AllApplicationPackagesSid, accounts[0].Sid);

        store.AddEntry(sid, new GrantedPathEntry { Path = @"C:\saved" });
        store.Save();

        databaseService.Verify(service => service.SaveAppConfig(
            It.Is<AppConfig>(config =>
                config.Apps.Count == 1 &&
                config.Accounts != null &&
                config.Accounts.Count == 2 &&
                config.Accounts.Any(account =>
                    account.Sid == sid &&
                    account.Grants.Count == 1 &&
                    account.Grants[0].Path == @"C:\saved") &&
                config.Accounts.Any(account =>
                    account.Sid == WellKnownSecuritySids.AllApplicationPackagesSid &&
                    account.Grants.Count == 2 &&
                    account.Grants.Any(grant => grant.Path == @"C:\aap-grant" && !grant.IsTraverseOnly) &&
                    account.Grants.Any(grant => grant.Path == @"C:\shared-extra" && grant.IsTraverseOnly)) &&
                config.HandlerMappings != null &&
                config.HandlerMappings.ContainsKey(".txt")),
            Path.GetFullPath(relativeConfigPath),
            It.IsAny<ISecureSecretSnapshotSource>(),
            It.IsAny<byte[]>()), Times.Once);
    }

    [Fact]
    public void Provider_ResolveStoreAndGetLoadedStores_AreDeterministic()
    {
        var database = new AppDatabase();
        var configSave = CreateConfigSaveOrchestrator(database, out _, out _, out _, out _);
        var mainStore = CreateMainStore(database, configSave, out var provider, out _);
        var tempRoot = Path.Combine(Path.GetTempPath(), "grant-provider-tests");
        var zuluPath = Path.Combine(tempRoot, "Zulu.rfn");
        var alphaPath = Path.Combine(tempRoot, "alpha.rfn");

        var zuluStore = provider.RegisterAdditionalStore(zuluPath, []);
        var alphaStore = provider.RegisterAdditionalStore(alphaPath, []);

        var loadedStores = provider.GetLoadedStores();

        Assert.Same(mainStore, provider.ResolveStore(null));
        Assert.Same(mainStore, loadedStores[0]);
        Assert.Same(alphaStore, loadedStores[1]);
        Assert.Same(zuluStore, loadedStores[2]);
        Assert.Same(alphaStore, provider.ResolveStore(Path.Combine(tempRoot, ".", "alpha.rfn")));
    }

    [Fact]
    public void Provider_UnregisterAndReregister_UpdatesLoadedStoresAndRejectsUnknownPath()
    {
        var database = new AppDatabase();
        var configSave = CreateConfigSaveOrchestrator(database, out _, out _, out _, out _);
        _ = CreateMainStore(database, configSave, out var provider, out _);
        var tempRoot = Path.Combine(Path.GetTempPath(), "grant-provider-reregister-tests");
        var betaPath = Path.Combine(tempRoot, "beta.rfn");
        var alphaPath = Path.Combine(tempRoot, "alpha.rfn");

        provider.RegisterAdditionalStore(betaPath, []);
        provider.RegisterAdditionalStore(alphaPath, []);
        provider.UnregisterAdditionalStore(alphaPath);

        var missingEx = Assert.Throws<InvalidOperationException>(() => provider.ResolveStore(alphaPath));
        Assert.Contains(Path.GetFullPath(alphaPath), missingEx.Message, StringComparison.OrdinalIgnoreCase);

        var reloadedAlphaStore = provider.RegisterAdditionalStore(alphaPath, []);
        var loadedStores = provider.GetLoadedStores();

        Assert.Equal(3, loadedStores.Count);
        Assert.Same(reloadedAlphaStore, loadedStores[1]);
        Assert.Same(provider.ResolveStore(betaPath), loadedStores[2]);
    }

    [Fact]
    public void Repository_FindsEquivalentEntriesAcrossStores_InDeterministicOrder()
    {
        var database = new AppDatabase();
        var configSave = CreateConfigSaveOrchestrator(database, out _, out _, out _, out _);
        var mainStore = CreateMainStore(database, configSave, out var provider, out _);
        var repository = new GrantIntentRepository(provider);
        var sid = "S-1-5-21-102";
        var entry = new GrantedPathEntry { Path = @"C:\same" };
        var sharedTraverseEntry = new GrantedPathEntry { Path = @"C:\shared-same", IsTraverseOnly = true };
        var tempRoot = Path.Combine(Path.GetTempPath(), "grant-repository-tests");
        var pathB = Path.Combine(tempRoot, "b.rfn");
        var pathA = Path.Combine(tempRoot, "a.rfn");

        mainStore.AddEntry(sid, entry);
        mainStore.AddEntry(WellKnownSecuritySids.AllApplicationPackagesSid, sharedTraverseEntry);

        provider.RegisterAdditionalStore(
            pathB,
            [new AppConfigAccountEntry
            {
                Sid = sid,
                Grants = [entry.Clone()]
            },
            new AppConfigAccountEntry
            {
                Sid = WellKnownSecuritySids.AllApplicationPackagesSid,
                Grants = [sharedTraverseEntry.Clone()]
            }]);
        provider.RegisterAdditionalStore(
            pathA,
            [new AppConfigAccountEntry
            {
                Sid = sid,
                Grants = [entry.Clone()]
            },
            new AppConfigAccountEntry
            {
                Sid = WellKnownSecuritySids.AllApplicationPackagesSid,
                Grants = [sharedTraverseEntry.Clone()]
            }]);

        var firstGrant = repository.FindGrant(sid, entry.Clone());
        var grantLocations = repository.FindGrantLocations(sid, entry.Clone());
        var traverseLocations = repository.FindTraverseLocations(
            WellKnownSecuritySids.AllApplicationPackagesSid,
            sharedTraverseEntry.Clone());
        var sidEntries = repository.FindEntriesForSid(sid);

        Assert.NotNull(firstGrant);
        Assert.Same(mainStore, firstGrant!.Store);
        Assert.Equal(3, grantLocations.Count);
        Assert.Null(grantLocations[0].Store.ConfigPath);
        Assert.Equal(Path.GetFullPath(pathA), grantLocations[1].Store.ConfigPath);
        Assert.Equal(Path.GetFullPath(pathB), grantLocations[2].Store.ConfigPath);
        Assert.Equal(3, traverseLocations.Count);
        Assert.Equal(3, sidEntries.Count);

        grantLocations[0].Entry.Path = @"C:\mutated";
        var unchangedLocation = repository.FindGrant(sid, entry.Clone());
        Assert.Equal(@"C:\same", unchangedLocation!.Entry.Path);
    }

    private SessionContext CreateSession(AppDatabase database)
    {
        var session = new SessionContext
{
            Database = database,
            CredentialStore = new CredentialStore { ArgonSalt = new byte[32], EncryptedCanary = [1] },
        }.WithClonedPinDerivedKey(_pinKey);
        _sessions.Add(session);
        return session;
    }

    private ISessionProvider CreateSessionProvider(AppDatabase database)
    {
        var sessionProvider = new Mock<ISessionProvider>();
        sessionProvider.Setup(provider => provider.GetSession()).Returns(CreateSession(database));
        return sessionProvider.Object;
    }

    private ConfigSaveOrchestrator CreateConfigSaveOrchestrator(
        AppDatabase database,
        out Mock<IConfigRepository> configRepository,
        out Mock<IAppConfigService> appConfigService,
        out Mock<IDatabaseService> databaseService,
        out Mock<IHandlerMappingService> handlerMappingService)
    {
        var session = CreateSession(database);
        var sessionProvider = new Mock<ISessionProvider>();
        sessionProvider.Setup(provider => provider.GetSession()).Returns(session);
        configRepository = new Mock<IConfigRepository>();
        appConfigService = new Mock<IAppConfigService>();
        databaseService = new Mock<IDatabaseService>();
        handlerMappingService = new Mock<IHandlerMappingService>();
        return new ConfigSaveOrchestrator(
            sessionProvider.Object,
            () => new InlineUiThreadInvoker(action => action()),
            databaseService.Object,
            appConfigService.Object,
            handlerMappingService.Object);
    }

    private MainGrantIntentStore CreateMainStore(
        AppDatabase database,
        ConfigSaveOrchestrator configSave,
        out GrantIntentStoreProvider provider,
        out GrantIntentOwnershipProjectionService ownershipProjection)
    {
        ownershipProjection = new GrantIntentOwnershipProjectionService();
        var mainStore = new MainGrantIntentStore(
            CreateSessionProvider(database),
            configSave,
            ownershipProjection);
        provider = new GrantIntentStoreProvider(mainStore, configSave, ownershipProjection);
        return mainStore;
    }

    private static GrantedPathEntry CreateClone(GrantedPathEntry entry, Action<GrantedPathEntry> mutate)
    {
        var clone = entry.Clone();
        mutate(clone);
        return clone;
    }
}
