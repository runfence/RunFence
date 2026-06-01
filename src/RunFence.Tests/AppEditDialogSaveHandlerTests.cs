using Moq;
using RunFence.Apps;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public class AppEditDialogSaveHandlerTests
{
    [Fact]
    public async Task TrySaveAndApply_SaveThrows_RollsBackConfigAndAssociations()
    {
        var database = new AppDatabase();
        database.Settings.HandlerMappings = new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["http"] = new HandlerMappingEntry("app1")
        };

        var handlerMappingService = new InMemoryHandlerMappingService(database);
        var associationHandler = CreateAssociationHandler(database, handlerMappingService: handlerMappingService);
        var appConfig = new AppConfigTestContext();
        appConfig.Service.AssignApp("app1", @"C:\original.rfn");

        var sut = new AppEditDialogSaveHandler(
            associationHandler,
            appConfig.Service,
            Mock.Of<ILoggingService>());

        var result = await sut.TrySaveAndApply(
            database: database,
            applyContext: CreateApplyContext(
                changeSet: CreateChangeSet(requiresHandlerSync: true),
                previousConfigPath: @"C:\original.rfn",
                selectedConfigPath: @"C:\extra.rfn",
                currentAssociations: [new HandlerAssociationItem("ftp", null, null, false)]),
            applyAsync: _ => throw new IOException("disk full"));

        Assert.Equal(AppEditSaveStatus.SaveFailed, result.Status);
        Assert.Equal("disk full", result.SaveError);
        Assert.Equal(@"C:\original.rfn", appConfig.Service.GetConfigPath("app1"));
        Assert.Equal(["http"], associationHandler.GetCurrentAssociations("app1")!.Select(a => a.Key));
    }

    [Fact]
    public async Task TrySaveAndApply_OperationCanceled_ReturnsCanceledAndRollsBackPreSaveMutations()
    {
        var database = new AppDatabase();
        var appConfig = new AppConfigTestContext();
        appConfig.Service.AssignApp("app1", @"C:\original.rfn");
        var sut = new AppEditDialogSaveHandler(
            CreateAssociationHandler(database),
            appConfig.Service,
            Mock.Of<ILoggingService>());

        var result = await sut.TrySaveAndApply(
            database: database,
            applyContext: CreateApplyContext(
                previousConfigPath: @"C:\original.rfn",
                selectedConfigPath: @"C:\extra.rfn"),
            applyAsync: _ => throw new OperationCanceledException("blocked"));

        Assert.Equal(AppEditSaveStatus.Canceled, result.Status);
        Assert.Equal("blocked", result.SaveError);
        Assert.Equal(@"C:\original.rfn", appConfig.Service.GetConfigPath("app1"));
    }

    [Fact]
    public async Task TrySaveAndApply_PreSaveApplyFails_RestoresOriginalConfigAssignment()
    {
        var database = new AppDatabase();
        var appConfig = new AppConfigTestContext();
        appConfig.Service.AssignApp("app1", @"C:\original.rfn");

        var associationHandler = CreateAssociationHandler(
            database,
            handlerMappingSetup: handlerMappingService =>
                handlerMappingService
                    .Setup(s => s.SetHandlerMapping(
                        "http",
                        It.IsAny<HandlerMappingEntry>(),
                        database))
                    .Throws(new InvalidOperationException("apply failed")));
        var sut = new AppEditDialogSaveHandler(
            associationHandler,
            appConfig.Service,
            Mock.Of<ILoggingService>());

        var result = await sut.TrySaveAndApply(
            database: database,
            applyContext: CreateApplyContext(
                changeSet: CreateChangeSet(requiresHandlerSync: true),
                previousConfigPath: @"C:\original.rfn",
                selectedConfigPath: @"C:\extra.rfn",
                currentAssociations: [new HandlerAssociationItem("http", null, null, false)]),
            applyAsync: _ => Task.CompletedTask);

        Assert.Equal(AppEditSaveStatus.ValidationOrSystemFailed, result.Status);
        Assert.Equal("apply failed", result.SaveError);
        Assert.Equal(@"C:\original.rfn", appConfig.Service.GetConfigPath("app1"));
    }

    [Fact]
    public async Task TrySaveAndApply_RegistrySyncThrows_ReturnsSavedWithRegistryWarning()
    {
        var database = new AppDatabase();
        var associationHandler = CreateAssociationHandler(
            database,
            registrationSyncException: new InvalidOperationException("sync failed"));
        var appConfig = new AppConfigTestContext();
        var sut = new AppEditDialogSaveHandler(
            associationHandler,
            appConfig.Service,
            Mock.Of<ILoggingService>());

        var result = await sut.TrySaveAndApply(
            database: database,
            applyContext: CreateApplyContext(
                changeSet: CreateChangeSet(requiresHandlerSync: true),
                currentAssociations: [new HandlerAssociationItem("http", null, null, false)]),
            applyAsync: _ => Task.CompletedTask);

        Assert.Equal(AppEditSaveStatus.SavedWithRegistryWarning, result.Status);
        Assert.Equal("sync failed", result.RegistrySyncWarning);
    }

    [Fact]
    public async Task TrySaveAndApply_WhenHandlerSyncNotRequired_DoesNotApplyOrSyncAssociations()
    {
        var database = new AppDatabase();
        var associationHandler = CreateAssociationHandler(
            database,
            registrationSyncException: new InvalidOperationException("sync should not run"),
            handlerMappingSetup: handlerMappingService =>
            {
                handlerMappingService
                    .Setup(s => s.SetHandlerMapping(
                        It.IsAny<string>(),
                        It.IsAny<HandlerMappingEntry>(),
                        It.IsAny<AppDatabase>()))
                    .Throws(new InvalidOperationException("apply should not run"));
            });
        var appConfig = new AppConfigTestContext();
        var sut = new AppEditDialogSaveHandler(
            associationHandler,
            appConfig.Service,
            Mock.Of<ILoggingService>());

        var result = await sut.TrySaveAndApply(
            database: database,
            applyContext: CreateApplyContext(
                changeSet: CreateChangeSet(requiresHandlerSync: false),
                currentAssociations: [new HandlerAssociationItem("http", null, null, false)]),
            applyAsync: _ => Task.CompletedTask);

        Assert.Equal(AppEditSaveStatus.Saved, result.Status);
    }

    private static AppEntryChangeSet CreateChangeSet(bool requiresHandlerSync = false)
        => new(
            RequiresAclReapply: false,
            RequiresBesideTargetRefresh: false,
            RequiresHandlerSync: requiresHandlerSync,
            RequiresManagedShortcutRefresh: false,
            RequiresIconRefresh: false,
            ConfigSaveScope: AppEditConfigSaveScope.CurrentAppConfigOnly);

    private static AppEditDialogApplyContext CreateApplyContext(
        AppEntry? result = null,
        AppEntryChangeSet? changeSet = null,
        string? previousConfigPath = null,
        string? selectedConfigPath = null,
        IReadOnlyList<HandlerAssociationItem>? currentAssociations = null)
        => new(
            result ?? new AppEntry { Id = "app1", Name = "App" },
            PreviousApp: null,
            changeSet ?? CreateChangeSet(),
            PreviousConfigPath: previousConfigPath,
            SelectedConfigPath: selectedConfigPath,
            CurrentAssociations: currentAssociations ?? []);

    private static AppEditAssociationHandler CreateAssociationHandler(
        AppDatabase? database = null,
        Exception? registrationSyncException = null,
        Action<Mock<IHandlerMappingService>>? handlerMappingSetup = null,
        InMemoryHandlerMappingService? handlerMappingService = null)
    {
        database ??= new AppDatabase();
        var mappingService = handlerMappingService ?? new InMemoryHandlerMappingService(database);
        var handlerMappingServiceMock = new Mock<IHandlerMappingService>();
        handlerMappingServiceMock.Setup(s => s.GetAllHandlerMappings(It.IsAny<AppDatabase>()))
            .Returns<AppDatabase>(mappingService.GetAllHandlerMappings);
        handlerMappingServiceMock.Setup(s => s.GetEffectiveHandlerMappings(It.IsAny<AppDatabase>()))
            .Returns<AppDatabase>(mappingService.GetEffectiveHandlerMappings);
        handlerMappingServiceMock.Setup(s => s.SetHandlerMapping(It.IsAny<string>(), It.IsAny<HandlerMappingEntry>(), It.IsAny<AppDatabase>()))
            .Callback<string, HandlerMappingEntry, AppDatabase>((k, e, db) => mappingService.SetHandlerMapping(k, e, db));
        handlerMappingServiceMock.Setup(s => s.RemoveHandlerMapping(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AppDatabase>()))
            .Callback<string, string, AppDatabase>((k, appId, db) => mappingService.RemoveHandlerMapping(k, appId, db));
        handlerMappingServiceMock.Setup(s => s.GetEffectiveDirectHandlerMappings(It.IsAny<AppDatabase>()))
            .Returns(new Dictionary<string, DirectHandlerEntry>(StringComparer.OrdinalIgnoreCase));

        handlerMappingSetup?.Invoke(handlerMappingServiceMock);

        var registrationService = new Mock<IAppHandlerRegistrationService>();
        if (registrationSyncException != null)
        {
            registrationService.Setup(s => s.Sync(It.IsAny<Dictionary<string, HandlerMappingEntry>>(), It.IsAny<List<AppEntry>>()))
                .Throws(registrationSyncException);
        }

        var databaseProvider = new LambdaDatabaseProvider(() => database);
        return new AppEditAssociationHandler(
            handlerMappingServiceMock.Object,
            registrationService.Object,
            Mock.Of<IAssociationAutoSetService>(),
            databaseProvider,
            () => new HandlerMappingMutationHandler(
                handlerMappingServiceMock.Object));
    }

    private sealed class InMemoryHandlerMappingService
    {
        private readonly Dictionary<string, List<HandlerMappingEntry>> _allMappings;

        public InMemoryHandlerMappingService(AppDatabase database)
        {
            _allMappings = database.Settings.HandlerMappings is null
                ? new Dictionary<string, List<HandlerMappingEntry>>(StringComparer.OrdinalIgnoreCase)
                : database.Settings.HandlerMappings.ToDictionary(
                    kv => kv.Key,
                    kv => new List<HandlerMappingEntry> { kv.Value },
                    StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyDictionary<string, IReadOnlyList<HandlerMappingEntry>> GetAllHandlerMappings(AppDatabase database)
        {
            return _allMappings.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyList<HandlerMappingEntry>)[..kv.Value],
                StringComparer.OrdinalIgnoreCase);
        }

        public Dictionary<string, HandlerMappingEntry> GetEffectiveHandlerMappings(AppDatabase database)
        {
            return _allMappings.ToDictionary(
                kv => kv.Key,
                kv => kv.Value[0],
                StringComparer.OrdinalIgnoreCase);
        }

        public void SetHandlerMapping(string key, HandlerMappingEntry entry, AppDatabase database)
        {
            if (!_allMappings.TryGetValue(key, out var items))
            {
                _allMappings[key] = items = [];
            }

            var existingIndex = items.FindIndex(item => string.Equals(item.AppId, entry.AppId, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
                items[existingIndex] = entry;
            else
                items.Add(entry);
        }

        public void RemoveHandlerMapping(string key, string appId, AppDatabase database)
        {
            if (!_allMappings.TryGetValue(key, out var items))
                return;

            items.RemoveAll(item => string.Equals(item.AppId, appId, StringComparison.OrdinalIgnoreCase));
            if (items.Count == 0)
                _allMappings.Remove(key);
        }
    }
}
