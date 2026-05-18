using Moq;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Acl;
using RunFence.Acl.UI;
using RunFence.Acl.UI.Forms;
using RunFence.Apps;
using RunFence.Apps.Shortcuts;
using RunFence.Apps.UI;
using RunFence.Apps.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Launching.Resolution;
using RunFence.Persistence;
using RunFence.Tests.Helpers;
using RunFence.UI;
using RunFence.UI.Forms;
using Xunit;

namespace RunFence.Tests;

public class AppEditInitializationTests
{
    private const string AppId = "app01";
    private const string AccountSid = "S-1-5-21-1-2-3-1001";
    private const string CallerSid = "S-1-5-21-1-2-3-1002";
    private const string ExtraConfigPath = @"C:\configs\extra.rfn";

    [Fact]
    public void CreateExistingInitializationModel_AssemblesDomainState()
    {
        var app = CreateExistingApp();
        var database = new AppDatabase();
        var initializer = CreateInitializer(database, out var appConfigService, out var handlerMappingService);
        appConfigService.Setup(s => s.GetConfigPath(AppId)).Returns(ExtraConfigPath);
        handlerMappingService.Setup(s => s.GetAllHandlerMappings(database)).Returns(
            new Dictionary<string, IReadOnlyList<HandlerMappingEntry>>(StringComparer.OrdinalIgnoreCase)
            {
                [".txt"] = [new HandlerMappingEntry(AppId, "\"%1\"", ["C:\\"], true)],
                [".jpg"] = [new HandlerMappingEntry("other")]
            });

        var model = initializer.CreateExistingInitializationModel(app, database);

        Assert.Equal("Existing App", model.State.Name);
        Assert.Equal(app.ExePath, model.State.ExePath);
        Assert.True(model.AclState.RestrictAcl);
        Assert.Equal(AclMode.Deny, model.AclState.AclMode);
        Assert.Equal(DeniedRights.Execute, model.AclState.DeniedRights);
        Assert.Equal(AclTarget.Folder, model.AclState.AclTarget);
        Assert.Equal(0, model.AclState.FolderAclDepth);
        Assert.Equal(PrivilegeLevel.Basic, model.State.SelectedPrivilegeLevel);
        Assert.True(model.State.OverrideIpcCallers);
        Assert.Equal(AccountSid, model.AccountSelection.AccountSid);
        Assert.False(model.AccountSelection.IsAppContainer);
        Assert.Equal(ExtraConfigPath, model.SelectedConfigPath);
        Assert.Equal([CallerSid], model.IpcCallers);
        Assert.Equal("value", model.EnvironmentVariables!["RF_TEST"]);
        var association = Assert.Single(model.Associations!);
        Assert.Equal(".txt", association.Key);
        Assert.Equal("\"%1\"", association.ArgumentsTemplate);
        Assert.Equal(["C:\\"], association.PathPrefixes);
        Assert.True(association.ReplacePrefixes);
        Assert.Equal(["C:\\Allowed"], model.PathPrefixes);
    }

    [Fact]
    public void AppEditDialog_ApplyExistingInitialization_PopulatesDialogState()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var app = CreateExistingApp();
            var database = new AppDatabase();
            database.Apps.Add(app);
            using var dialog = CreateDialog(database, out var appConfigService, out var handlerMappingService);
            appConfigService.Setup(s => s.HasLoadedConfigs).Returns(true);
            appConfigService.Setup(s => s.GetLoadedConfigPaths()).Returns([ExtraConfigPath]);
            appConfigService.Setup(s => s.GetConfigPath(AppId)).Returns(ExtraConfigPath);
            handlerMappingService.Setup(s => s.GetAllHandlerMappings(database)).Returns(
                new Dictionary<string, IReadOnlyList<HandlerMappingEntry>>(StringComparer.OrdinalIgnoreCase)
                {
                    [".txt"] = [new HandlerMappingEntry(AppId, "\"%1\"")]
                });

            dialog.Initialize(
                existing: app,
                credentials: [new CredentialEntry { Sid = AccountSid }],
                existingApps: [app],
                commandContext: AppEditDialogTestsAccessor.CreateNoOpCommandContext(),
                sidNames: new Dictionary<string, string> { [AccountSid] = "Test User" },
                database: database);

            var snapshot = dialog.CaptureInputSnapshot();
            Assert.Equal("Existing App", snapshot.NameText);
            Assert.Equal(app.ExePath, snapshot.FilePathText);
            Assert.False(snapshot.IsFolder);
            Assert.Equal("--default", snapshot.DefaultArgsText);
            Assert.True(snapshot.AllowPassArgs);
            Assert.Equal("\"%1\"", snapshot.ArgumentsTemplateText);
            Assert.Equal(@"C:\Work", snapshot.WorkingDirText);
            Assert.True(snapshot.AllowPassWorkDir);
            Assert.True(snapshot.ManageShortcuts);
            Assert.Equal(PrivilegeLevel.Basic, snapshot.SelectedPrivilegeLevel);
            Assert.True(snapshot.OverrideIpcCallers);
            Assert.Equal(["C:\\Allowed\\"], snapshot.AppPathPrefixes);
            Assert.Equal(ExtraConfigPath, dialog.SelectedConfigPath);
            Assert.Equal(AccountSid, snapshot.SelectedAccountSid);
        });
    }

    private static AppEntry CreateExistingApp() => new()
    {
        Id = AppId,
        Name = "Existing App",
        ExePath = @"C:\Missing\app.exe",
        AccountSid = AccountSid,
        DefaultArguments = "--default",
        AllowPassingArguments = true,
        ArgumentsTemplate = "\"%1\"",
        WorkingDirectory = @"C:\Work",
        AllowPassingWorkingDirectory = true,
        ManageShortcuts = true,
        PrivilegeLevel = PrivilegeLevel.Basic,
        AllowedIpcCallers = [CallerSid],
        EnvironmentVariables = new Dictionary<string, string> { ["RF_TEST"] = "value" },
        PathPrefixes = ["C:\\Allowed"],
        RestrictAcl = true,
        AclMode = AclMode.Deny,
        AclTarget = AclTarget.Folder,
        DeniedRights = DeniedRights.Execute,
        FolderAclDepth = 0
    };

    private static AppEditDialogInitializer CreateInitializer(
        AppDatabase database,
        out Mock<IAppConfigService> appConfigService,
        out Mock<IHandlerMappingService> handlerMappingService)
    {
        appConfigService = new Mock<IAppConfigService>();
        handlerMappingService = new Mock<IHandlerMappingService>();
        handlerMappingService.Setup(s => s.GetAllHandlerMappings(database))
            .Returns(new Dictionary<string, IReadOnlyList<HandlerMappingEntry>>(StringComparer.OrdinalIgnoreCase));
        var associationHandler = CreateAssociationHandler(database, handlerMappingService.Object);

        return new AppEditDialogInitializer(
            new AppEditPopulator(),
            new AppEntryIdGenerator(),
            appConfigService.Object,
            associationHandler);
    }

    private static AppEditDialog CreateDialog(
        AppDatabase database,
        out Mock<IAppConfigService> appConfigService,
        out Mock<IHandlerMappingService> handlerMappingService)
    {
        var sidResolver = CreateSidResolver();
        var profilePathResolver = new Mock<IProfilePathResolver>();
        var initializer = CreateInitializer(database, out appConfigService, out handlerMappingService);
        var associationHandler = CreateAssociationHandler(database, handlerMappingService.Object);
        var aclService = new Mock<IAclService>();
        aclService.Setup(s => s.ResolveAclTargetPath(It.IsAny<AppEntry>()))
            .Returns((AppEntry app) => app.ExePath);
        aclService.Setup(s => s.IsBlockedPath(It.IsAny<string>())).Returns(false);
        var log = new Mock<ILoggingService>();
        var displayNameResolver = new SidDisplayNameResolver(sidResolver.Object, profilePathResolver.Object);
        var aclConfigValidator = new AclConfigValidator(aclService.Object, log.Object);
        var aclSection = new AclConfigSection(
            new AclAllowListGridHandler(),
            new AllowListEntryFactory(
                new Mock<ILocalUserProvider>().Object,
                new Mock<ILocalGroupMembershipService>().Object,
                new Mock<ISidEntryHelper>().Object,
                displayNameResolver),
            aclConfigValidator,
            new FolderDepthHelper(aclService.Object, log.Object));
        var executablePathResolver = new Mock<IExecutablePathResolver>();
        executablePathResolver.Setup(r => r.TryResolvePath(It.IsAny<string>(), It.IsAny<ExecutablePathResolutionContext>()))
            .Returns((string path, ExecutablePathResolutionContext _) => path);
        var browseHelper = new AppEditBrowseHelper(
            new Mock<IShortcutDiscoveryService>().Object,
            new Mock<IShortcutIconHelper>().Object,
            new ShortcutTargetResolver(new Mock<IShortcutComHelper>().Object),
            new Mock<ISessionProvider>().Object,
            new Mock<IExecutableKindService>().Object);
        var controller = new AppEditDialogController(
            new AppEntryBuilder(new AppEntryIdGenerator()),
            executablePathResolver.Object,
            new AppEditDialogInputValidator(),
            new AppEditDialogAclConfigBuilder(aclConfigValidator));
        var saveHandler = new AppEditDialogSaveHandler(associationHandler, appConfigService.Object);
        var submitController = new AppEditDialogSubmitController(
            controller,
            saveHandler);
        var credentialDisplayItemFactory = new CredentialDisplayItemFactory(sidResolver.Object, profilePathResolver.Object);
        var populator = new AppEditDialogPopulator(
            appConfigService.Object,
            credentialDisplayItemFactory,
            new CredentialFilterHelper(sidResolver.Object));
        var binder = new AppEditDialogInitializationBinder(
            populator,
            initializer,
            credentialDisplayItemFactory,
            () => new IpcCallerSection(
                () => [],
                new Mock<ISidEntryHelper>().Object,
                new SidDisplayNameResolver(sidResolver.Object, profilePathResolver.Object)));

        return new AppEditDialog(
            appConfigService.Object,
            aclSection,
            browseHelper,
            new AppEditAccountSwitchHandler(),
            controller,
            submitController,
            executablePathResolver.Object,
            new HandlerAssociationsSection(),
            binder,
            new Mock<IUserConfirmationService>().Object);
    }

    private static AppEditAssociationHandler CreateAssociationHandler(
        AppDatabase database,
        IHandlerMappingService handlerMappingService)
    {
        var databaseProvider = new LambdaDatabaseProvider(() => database);
        return new AppEditAssociationHandler(
            handlerMappingService,
            new Mock<IAppHandlerRegistrationService>().Object,
            new Mock<IAssociationAutoSetService>().Object,
            databaseProvider,
            () => new HandlerMappingMutationHandler(
                handlerMappingService));
    }

    private static Mock<ISidResolver> CreateSidResolver()
    {
        var sidResolver = new Mock<ISidResolver>();
        sidResolver.Setup(r => r.TryResolveName(AccountSid)).Returns("Test User");
        sidResolver.Setup(r => r.TryResolveName(CallerSid)).Returns("Caller User");
        sidResolver.Setup(r => r.GetCurrentUserSid()).Returns("S-1-5-21-current");
        return sidResolver;
    }
}
