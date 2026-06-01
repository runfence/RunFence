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
    public void Submit_DuplicateEnvironmentVariable_ReturnsValidationStatus()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var sut = CreateSubmitController();
            var request = CreateSubmitRequest(duplicateEnvironmentVariableName: "RF_TEST");

            var result = sut.Submit(request);

            Assert.Null(result.Result);
            Assert.Null(result.DialogResult);
            Assert.False(result.HasUnsavedMutations);
            Assert.Equal("Duplicate environment variable name: RF_TEST", result.StatusText);
            Assert.False(result.StatusIsError);
        });
    }

    [Fact]
    public void Submit_WhenAppContainerSelected_UsesPersistedPrivilegeLevelWithoutControllerBinding()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var sut = CreateSubmitController();
            var request = CreateSubmitRequest(
                selectedAccountSid: null,
                selectedAppContainerName: "ram_browser",
                selectedPrivilegeLevel: PrivilegeLevel.LowIntegrity,
                persistedPrivilegeLevel: PrivilegeLevel.Basic);

            var result = sut.Submit(request);

            Assert.NotNull(result.Result);
            Assert.Equal(string.Empty, result.Result!.AccountSid);
            Assert.Equal("ram_browser", result.Result.AppContainerName);
            Assert.Equal(PrivilegeLevel.Basic, result.Result.PrivilegeLevel);
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
                applyAsync: _ => throw new OperationCanceledException("blocked"));

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
                applyAsync: _ => throw new IOException("disk full"));

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
                applyAsync: _ => Task.CompletedTask,
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
                applyAsync: _ => Task.CompletedTask,
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

    [Fact]
    public void ApplyExistingResultAsync_PassesPreMutationChangeSetToApplyCallback()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            var database = new AppDatabase();
            database.Apps.Add(new AppEntry
            {
                Id = "app1",
                Name = "App",
                ExePath = @"C:\app.exe",
                AccountSid = "S-1-5-21-1"
            });
            var sut = CreateSubmitController(
                database,
                appConfigSetup: appConfig => appConfig.Service.AssignApp("app1", null));
            AppEditDialogApplyContext? capturedContext = null;
            var request = CreateApplyRequest(
                database,
                applyAsync: context =>
                {
                    capturedContext = context;
                    return Task.CompletedTask;
                },
                selectedConfigPath: @"C:\Configs\extra.rfn");

            var result = await sut.ApplyExistingResultAsync(request);

            Assert.Equal(DialogResult.OK, result.DialogResult);
            Assert.NotNull(capturedContext);
            Assert.Equal(AppEditConfigSaveScope.AllConfigs, capturedContext!.ChangeSet.ConfigSaveScope);
            Assert.Null(capturedContext.PreviousConfigPath);
            Assert.Equal(@"C:\Configs\extra.rfn", capturedContext.SelectedConfigPath);
        });
    }

    [Fact]
    public void ApplyExistingResultAsync_ConfigMoveWithoutShortcutRefresh_CopiesShortcutProtectionStates()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            var database = new AppDatabase();
            var previousApp = new AppEntry
            {
                Id = "app1",
                Name = "App",
                ExePath = @"C:\app.exe",
                AccountSid = "S-1-5-21-1",
                ManageShortcuts = false,
                ShortcutProtectionStates =
                [
                    new ShortcutProtectionState(@"C:\Links\App.lnk", true, false, true)
                ]
            };
            database.Apps.Add(previousApp);
            var resultApp = previousApp.Clone();
            var sut = CreateSubmitController(
                database,
                appConfigSetup: appConfig => appConfig.Service.AssignApp("app1", null));

            var request = CreateApplyRequest(
                database,
                applyAsync: _ => Task.CompletedTask,
                result: resultApp,
                selectedConfigPath: @"C:\Configs\extra.rfn");

            var result = await sut.ApplyExistingResultAsync(request);

            Assert.Equal(DialogResult.OK, result.DialogResult);
            Assert.NotNull(resultApp.ShortcutProtectionStates);
            var copiedState = Assert.Single(resultApp.ShortcutProtectionStates);
            Assert.Equal(@"C:\Links\App.lnk", copiedState.ShortcutPath);
            Assert.NotSame(previousApp.ShortcutProtectionStates, resultApp.ShortcutProtectionStates);
        });
    }

    [Fact]
    public void ApplyExistingResultAsync_ShortcutRefreshChange_ClearsShortcutProtectionStates()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            var database = new AppDatabase();
            var previousApp = new AppEntry
            {
                Id = "app1",
                Name = "App",
                ExePath = @"C:\app.exe",
                AccountSid = "S-1-5-21-1",
                ManageShortcuts = false,
                ShortcutProtectionStates =
                [
                    new ShortcutProtectionState(@"C:\Links\App.lnk", true, false, true)
                ]
            };
            database.Apps.Add(previousApp);
            var resultApp = previousApp.Clone();
            resultApp.Name = "Renamed App";
            var sut = CreateSubmitController(database);

            var request = CreateApplyRequest(
                database,
                applyAsync: _ => Task.CompletedTask,
                result: resultApp);

            var result = await sut.ApplyExistingResultAsync(request);

            Assert.Equal(DialogResult.OK, result.DialogResult);
            Assert.Null(resultApp.ShortcutProtectionStates);
        });
    }


    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ApplyExistingResultAsync_WhenExistingFileChangesToFolderOrUrlWithAssociations_ReturnsRequiredMessage(
        bool isFolder,
        bool isUrl)
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            var database = new AppDatabase();
            database.Apps.Add(new AppEntry
            {
                Id = "app1",
                Name = "App",
                ExePath = @"C:\app.exe",
                AccountSid = "S-1-5-21-1"
            });

            var sut = CreateSubmitController(database);
            var request = CreateApplyRequest(
                database,
                applyAsync: _ => throw new InvalidOperationException("apply should not run"),
                currentAssociations: [new HandlerAssociationItem("http", null, null, false)],
                result: new AppEntry
                {
                    Id = "app1",
                    Name = "App",
                    ExePath = isUrl ? "https://example.test" : @"C:\Folder",
                    IsFolder = isFolder,
                    IsUrlScheme = isUrl,
                    AccountSid = "S-1-5-21-1"
                });

            var result = await sut.ApplyExistingResultAsync(request);

            Assert.Equal(
                "Failed: Remove this application's handler associations before changing it to a folder or URL app.",
                result.StatusText);
            Assert.True(result.StatusIsError);
            Assert.Null(result.DialogResult);
        });
    }

    private static AppEditDialogSubmitController CreateSubmitController(
        AppDatabase? database = null,
        Exception? registrationSyncException = null,
        Action<Mock<IHandlerMappingService>>? handlerMappingSetup = null,
        Action<AppConfigTestContext>? appConfigSetup = null)
    {
        database ??= new AppDatabase();
        var appConfig = new AppConfigTestContext();
        appConfigSetup?.Invoke(appConfig);

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

        var associationHandler = CreateAssociationHandler(database, registrationSyncException, handlerMappingSetup);
        var saveHandler = new AppEditDialogSaveHandler(
            associationHandler,
            appConfig.Service,
            Mock.Of<ILoggingService>());

        return new AppEditDialogSubmitController(
            dialogController,
            saveHandler,
            associationHandler,
            appConfig.Service,
            new AppEntryChangeClassifier());
    }

    private static AppEditDialogSubmitRequest CreateSubmitRequest(
        string filePath = @"C:\app.exe",
        string? duplicateEnvironmentVariableName = null,
        string? selectedAccountSid = "S-1-5-21-1",
        string? selectedAppContainerName = null,
        PrivilegeLevel? selectedPrivilegeLevel = null,
        PrivilegeLevel? persistedPrivilegeLevel = null)
    {
        return new AppEditDialogSubmitRequest(new AppEditDialogInputSnapshot(
            NameText: "App",
            FilePathText: filePath,
            IsFolder: false,
            SelectedAccountSid: selectedAccountSid,
            SelectedAppContainerName: selectedAppContainerName,
            ManageShortcuts: false,
            SelectedPrivilegeLevel: selectedPrivilegeLevel,
            PersistedPrivilegeLevel: persistedPrivilegeLevel ?? selectedPrivilegeLevel,
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
                AllowedEntries: []),
            HandlerMappings: null,
            IsUrlScheme: false,
            AclTarget: AclTarget.Folder,
            AclMode: AclMode.Deny,
            RestrictAppEntryAcl: false,
            ReplacePrefixes: false));
    }

    private static AppEditDialogApplyRequest CreateApplyRequest(
        AppDatabase? database,
        Func<AppEditDialogApplyContext, Task> applyAsync,
        IReadOnlyList<HandlerAssociationItem>? currentAssociations = null,
        AppEntry? result = null,
        string? selectedConfigPath = null)
    {
        return new AppEditDialogApplyRequest(
            Result: result ?? new AppEntry
            {
                Id = "app1",
                Name = "App",
                ExePath = @"C:\app.exe",
                AccountSid = "S-1-5-21-1",
                ManageShortcuts = false
            },
            Database: database,
            SelectedConfigPath: selectedConfigPath,
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
