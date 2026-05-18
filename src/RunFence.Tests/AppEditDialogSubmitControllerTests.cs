using Moq;
using RunFence.Acl;
using RunFence.Acl.UI.Forms;
using RunFence.Apps;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launching.Resolution;
using RunFence.Persistence;
using RunFence.Tests.Helpers;
using System.Windows.Forms;
using Xunit;

namespace RunFence.Tests;

public class AppEditDialogSubmitControllerTests
{
    [Fact]
    public void SubmitAsync_ValidInput_ReturnsBuiltResultForCallerOwnedApplyPath()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            var existingFilePath = Path.GetTempFileName();
            try
            {
                var sut = CreateSubmitController();
                var request = CreateSubmitRequest(filePath: existingFilePath);

                var result = await sut.SubmitAsync(request);

                Assert.NotNull(result.Result);
                Assert.Equal("App", result.Result!.Name);
                Assert.Null(result.DialogResult);
                Assert.False(result.HasUnsavedMutations);
                Assert.Null(result.StatusText);
                Assert.Null(result.NotificationMessage);
            }
            finally
            {
                File.Delete(existingFilePath);
            }
        });
    }

    [Fact]
    public void SubmitAsync_DuplicateEnvironmentVariable_ReturnsValidationStatus()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            var sut = CreateSubmitController();
            var request = CreateSubmitRequest(duplicateEnvironmentVariableName: "RF_TEST");

            var result = await sut.SubmitAsync(request);

            Assert.Null(result.Result);
            Assert.Null(result.DialogResult);
            Assert.False(result.HasUnsavedMutations);
            Assert.Equal("Duplicate environment variable name: RF_TEST", result.StatusText);
            Assert.False(result.StatusIsError);
        });
    }

    [Fact]
    public void ApplyExistingResultAsync_WhenApplyCanceled_ReturnsCanceledStatus()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            var sut = CreateSubmitController();
            var request = CreateApplyRequest(
                database: new AppDatabase(),
                applyAsync: () => throw new OperationCanceledException("blocked"));

            var result = await sut.ApplyExistingResultAsync(request);

            Assert.Equal("app1", result.Result!.Id);
            Assert.Null(result.DialogResult);
            Assert.False(result.HasUnsavedMutations);
            Assert.Equal(string.Empty, result.StatusText);
            Assert.Null(result.NotificationMessage);
        });
    }

    [Fact]
    public void ApplyExistingResultAsync_WhenSaveFails_ReturnsUnsavedFailure()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            var sut = CreateSubmitController();
            var request = CreateApplyRequest(
                database: null,
                applyAsync: () => throw new IOException("disk full"));

            var result = await sut.ApplyExistingResultAsync(request);

            Assert.Equal("app1", result.Result!.Id);
            Assert.Null(result.DialogResult);
            Assert.True(result.HasUnsavedMutations);
            Assert.Equal("Failed: disk full", result.StatusText);
        });
    }

    [Fact]
    public void ApplyExistingResultAsync_WhenPreSaveMutationFails_ReturnsSystemFailureStatus()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            var database = new AppDatabase();
            var sut = CreateSubmitController(
                database,
                handlerMappingSetup: service =>
                    service.Setup(s => s.SetHandlerMapping(
                            "http",
                            It.IsAny<HandlerMappingEntry>(),
                            database))
                        .Throws(new InvalidOperationException("apply failed")));
            var request = CreateApplyRequest(
                database,
                applyAsync: () => Task.CompletedTask,
                currentAssociations: [new HandlerAssociationItem("http", null, null, false)]);

            var result = await sut.ApplyExistingResultAsync(request);

            Assert.Equal("app1", result.Result!.Id);
            Assert.Null(result.DialogResult);
            Assert.False(result.HasUnsavedMutations);
            Assert.True(result.StatusIsError);
            Assert.Contains("apply failed", result.StatusText);
            Assert.Null(result.NotificationMessage);
        });
    }

    [Fact]
    public void ApplyExistingResultAsync_WhenRegistrySyncFails_ReturnsWarningNotification()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            var database = new AppDatabase();
            var sut = CreateSubmitController(database, registrationSyncException: new InvalidOperationException("sync failed"));
            var request = CreateApplyRequest(
                database,
                applyAsync: () => Task.CompletedTask,
                currentAssociations: [new HandlerAssociationItem("http", null, null, false)]);

            var result = await sut.ApplyExistingResultAsync(request);

            Assert.Equal(DialogResult.OK, result.DialogResult);
            Assert.False(result.HasUnsavedMutations);
            Assert.True(result.NotificationIsWarning);
            Assert.Equal(
                "Application was saved, but handler registration sync failed:\n\nsync failed",
                result.NotificationMessage);
        });
    }

    private static AppEditDialogSubmitController CreateSubmitController(
        AppDatabase? database = null,
        Exception? registrationSyncException = null,
        Action<Mock<IHandlerMappingService>>? handlerMappingSetup = null)
    {
        database ??= new AppDatabase();
        var appConfigService = new Mock<IAppConfigService>();
        appConfigService.SetupGet(s => s.HasLoadedConfigs).Returns(false);
        appConfigService.Setup(s => s.GetLoadedConfigPaths()).Returns([]);
        appConfigService.Setup(s => s.GetConfigPath(It.IsAny<string>())).Returns((string? _) => null);

        var executablePathResolver = new Mock<IExecutablePathResolver>();
        executablePathResolver.Setup(r => r.TryResolvePath(It.IsAny<string>(), It.IsAny<ExecutablePathResolutionContext>()))
            .Returns<string, ExecutablePathResolutionContext>((path, _) => path);

        var idGenerator = new Mock<IAppEntryIdGenerator>();
        idGenerator.Setup(g => g.GenerateUniqueId(It.IsAny<IEnumerable<string>>())).Returns("app1");

        var aclService = new Mock<IAclService>();
        aclService.Setup(s => s.IsBlockedPath(It.IsAny<string>())).Returns(false);
        aclService.Setup(s => s.ResolveAclTargetPath(It.IsAny<AppEntry>())).Returns<AppEntry>(app => app.ExePath);
        var aclConfigValidator = new AclConfigValidator(aclService.Object, Mock.Of<ILoggingService>());

        var dialogController = new AppEditDialogController(
            new AppEntryBuilder(idGenerator.Object),
            executablePathResolver.Object,
            new AppEditDialogInputValidator(),
            new AppEditDialogAclConfigBuilder(aclConfigValidator));
        dialogController.Initialize(new AppEditAccountSwitchHandler());

        var associationHandler = CreateAssociationHandler(database, registrationSyncException, handlerMappingSetup);
        var saveHandler = new AppEditDialogSaveHandler(associationHandler, appConfigService.Object);

        return new AppEditDialogSubmitController(dialogController, saveHandler);
    }

    private static AppEditDialogSubmitRequest CreateSubmitRequest(
        string filePath = @"C:\app.exe",
        string? duplicateEnvironmentVariableName = null)
    {
        return new AppEditDialogSubmitRequest(new AppEditDialogInputSnapshot(
            NameText: "App",
            FilePathText: filePath,
            IsFolder: false,
            SelectedAccountSid: "S-1-5-21-1",
            SelectedAppContainerName: null,
            ManageShortcuts: false,
            SelectedPrivilegeLevel: null,
            OverrideIpcCallers: false,
            DefaultArgsText: string.Empty,
            AllowPassArgs: false,
            WorkingDirText: string.Empty,
            AllowPassWorkDir: false,
            ExistingApps: [],
            ExistingApp: null,
            PreGeneratedId: null,
            ArgumentsTemplateText: null,
            AppPathPrefixes: null,
            DuplicateEnvironmentVariableName: duplicateEnvironmentVariableName,
            EnvironmentVariables: null,
            IpcCallers: [],
            AclConfig: new AclConfigSectionSnapshot(
                RestrictAcl: false,
                AclMode: AclMode.Deny,
                SelectedAclTarget: AclTarget.Folder,
                FolderAclDepth: 0,
                DeniedRights: DeniedRights.ExecuteWrite,
                AllowedEntries: [])));
    }

    private static AppEditDialogApplyRequest CreateApplyRequest(
        AppDatabase? database,
        Func<Task> applyAsync,
        IReadOnlyList<HandlerAssociationItem>? currentAssociations = null)
    {
        return new AppEditDialogApplyRequest(
            Result: new AppEntry
            {
                Id = "app1",
                Name = "App",
                ExePath = @"C:\app.exe",
                AccountSid = "S-1-5-21-1",
                ManageShortcuts = false
            },
            Database: database,
            SelectedConfigPath: null,
            CurrentAssociations: currentAssociations ?? [],
            ApplyAsync: applyAsync);
    }

    private static AppEditAssociationHandler CreateAssociationHandler(
        AppDatabase database,
        Exception? registrationSyncException,
        Action<Mock<IHandlerMappingService>>? handlerMappingSetup)
    {
        var mappingService = new InMemoryHandlerMappingService();
        var handlerMappingService = new Mock<IHandlerMappingService>();
        handlerMappingService.Setup(s => s.GetAllHandlerMappings(It.IsAny<AppDatabase>()))
            .Returns<AppDatabase>(_ => mappingService.GetAllHandlerMappings());
        handlerMappingService.Setup(s => s.GetEffectiveHandlerMappings(It.IsAny<AppDatabase>()))
            .Returns<AppDatabase>(_ => mappingService.GetEffectiveHandlerMappings());
        handlerMappingService.Setup(s => s.SetHandlerMapping(It.IsAny<string>(), It.IsAny<HandlerMappingEntry>(), It.IsAny<AppDatabase>()))
            .Callback<string, HandlerMappingEntry, AppDatabase>((key, entry, _) => mappingService.SetHandlerMapping(key, entry));
        handlerMappingService.Setup(s => s.RemoveHandlerMapping(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AppDatabase>()))
            .Callback<string, string, AppDatabase>((key, appId, _) => mappingService.RemoveHandlerMapping(key, appId));
        handlerMappingService.Setup(s => s.GetEffectiveDirectHandlerMappings(It.IsAny<AppDatabase>()))
            .Returns(new Dictionary<string, DirectHandlerEntry>(StringComparer.OrdinalIgnoreCase));
        handlerMappingSetup?.Invoke(handlerMappingService);

        var registrationService = new Mock<IAppHandlerRegistrationService>();
        if (registrationSyncException != null)
        {
            registrationService.Setup(s => s.Sync(It.IsAny<Dictionary<string, HandlerMappingEntry>>(), It.IsAny<List<AppEntry>>()))
                .Throws(registrationSyncException);
        }

        var databaseProvider = new LambdaDatabaseProvider(() => database);
        return new AppEditAssociationHandler(
            handlerMappingService.Object,
            registrationService.Object,
            Mock.Of<IAssociationAutoSetService>(),
            databaseProvider,
            () => new HandlerMappingMutationHandler(handlerMappingService.Object));
    }

    private sealed class InMemoryHandlerMappingService
    {
        private readonly Dictionary<string, List<HandlerMappingEntry>> _mappings =
            new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, IReadOnlyList<HandlerMappingEntry>> GetAllHandlerMappings()
        {
            return _mappings.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyList<HandlerMappingEntry>)[.. kv.Value],
                StringComparer.OrdinalIgnoreCase);
        }

        public Dictionary<string, HandlerMappingEntry> GetEffectiveHandlerMappings()
        {
            return _mappings.ToDictionary(
                kv => kv.Key,
                kv => kv.Value[0],
                StringComparer.OrdinalIgnoreCase);
        }

        public void SetHandlerMapping(string key, HandlerMappingEntry entry)
        {
            if (!_mappings.TryGetValue(key, out var entries))
            {
                entries = [];
                _mappings[key] = entries;
            }

            var existingIndex = entries.FindIndex(item =>
                string.Equals(item.AppId, entry.AppId, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
                entries[existingIndex] = entry;
            else
                entries.Add(entry);
        }

        public void RemoveHandlerMapping(string key, string appId)
        {
            if (!_mappings.TryGetValue(key, out var entries))
                return;

            entries.RemoveAll(item => string.Equals(item.AppId, appId, StringComparison.OrdinalIgnoreCase));
            if (entries.Count == 0)
                _mappings.Remove(key);
        }
    }
}
