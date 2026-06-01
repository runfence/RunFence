using Moq;
using RunFence.Acl;
using RunFence.Account;
using RunFence.Apps;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.Persistence;
using RunFence.RunAs;
using Xunit;

namespace RunFence.Tests;

public class AppEditCommitServiceTests : IDisposable
{
    private const string UserSid = "S-1-5-21-1000-1000-1000-1001";

    private readonly Mock<IAppConfigService> _appConfigService = new();
    private readonly Mock<IDataChangeNotifier> _dataChangeNotifier = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<IAppStateProvider> _appState = new();
    private readonly Mock<IAclService> _aclService = new();
    private readonly Mock<IShortcutService> _shortcutService = new();
    private readonly Mock<IBesideTargetShortcutService> _besideTargetShortcutService = new();
    private readonly Mock<IIconService> _iconService = new();
    private readonly Mock<ISidNameCacheService> _sidNameCache = new();
    private readonly Mock<IShortcutDiscoveryService> _shortcutDiscovery = new();
    private readonly Mock<IUiThreadInvoker> _uiThreadInvoker = new();
    private readonly Mock<ILicenseService> _licenseService = new();
    private readonly AppDatabase _database = new();
    private readonly CredentialStore _credentialStore = new();
    private readonly SecureSecret _pinKey = TestSecretFactory.Create(32);
    private readonly List<SessionContext> _sessions = [];

    public AppEditCommitServiceTests()
    {
        _appState.Setup(c => c.Database).Returns(_database);
        _licenseService.Setup(l => l.CanAddApp(It.IsAny<int>())).Returns(true);
        _shortcutDiscovery.Setup(d => d.CreateTraversalCache()).Returns(() => new ShortcutTraversalCache([]));
    }

    public void Dispose()
    {
        foreach (var session in _sessions)
            session.Dispose();

        _pinKey.Dispose();
    }

    [Fact]
    public void Commit_ExistingAppCleanupFails_ReturnsSaveFailedBeforeMutation()
    {
        var previousApp = CreateApp("old-app");
        var newApp = previousApp.Clone();
        newApp.Name = "Updated App";
        _database.Apps.Add(previousApp);
        _besideTargetShortcutService.Setup(s => s.RemoveBesideTargetShortcut(previousApp))
            .Throws(new InvalidOperationException("cleanup failed"));

        var result = Commit(CreateSut(), newApp, previousApp, @"C:\Configs\new.rfn");

        Assert.Equal(RunAsAppEntryPersistenceStatus.SaveFailed, result.Status);
        Assert.Equal("cleanup failed", result.ErrorMessage);
        Assert.Same(previousApp, _database.Apps.Single());
        _appConfigService.Verify(s => s.AssignApp(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
        _appConfigService.Verify(
            s => s.SaveAllConfigs(
                It.IsAny<AppDatabase>(),
                It.IsAny<ISecureSecretSnapshotSource>(),
                It.IsAny<byte[]>()),
            Times.Never);
        _iconService.Verify(s => s.CreateBadgedIcon(previousApp, null), Times.Once);
        _aclService.Verify(s => s.ApplyAcl(previousApp, It.IsAny<IReadOnlyList<AppEntry>>()), Times.Never);
        _dataChangeNotifier.Verify(s => s.NotifyDataChanged(), Times.Never);
    }

    [Fact]
    public void Commit_ExistingAppIdChanged_ReturnsSaveFailedBeforeCleanup()
    {
        var previousApp = CreateApp("old-app");
        var newApp = previousApp.Clone();
        newApp.Id = "new-app";
        _database.Apps.Add(previousApp);

        var result = Commit(
            CreateSut(),
            newApp,
            previousApp,
            @"C:\Configs\new.rfn",
            previousConfigPath: @"C:\Configs\old.rfn");

        Assert.Equal(RunAsAppEntryPersistenceStatus.SaveFailed, result.Status);
        Assert.Equal("Existing application edits must preserve the application ID.", result.ErrorMessage);
        _aclService.Verify(s => s.RevertAcl(It.IsAny<AppEntry>(), It.IsAny<IReadOnlyList<AppEntry>>()), Times.Never);
        _appConfigService.Verify(s => s.AssignApp(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void Commit_ExistingAppMissing_ReturnsSaveFailed()
    {
        var previousApp = CreateApp("old-app");
        var newApp = previousApp.Clone();

        var result = Commit(
            CreateSut(),
            newApp,
            previousApp,
            @"C:\Configs\new.rfn",
            previousConfigPath: @"C:\Configs\old.rfn");

        Assert.Equal(RunAsAppEntryPersistenceStatus.SaveFailed, result.Status);
        Assert.Equal("The application no longer exists.", result.ErrorMessage);
        Assert.Same(newApp, result.AppEntry);
        _aclService.Verify(s => s.RevertAcl(It.IsAny<AppEntry>(), It.IsAny<IReadOnlyList<AppEntry>>()), Times.Never);
        _appConfigService.Verify(s => s.AssignApp(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void Commit_SaveFailure_RestoresPreviousAppConfigAndEnforcement()
    {
        var previousApp = CreateApp("old-app");
        var newApp = previousApp.Clone();
        newApp.Name = "Updated App";
        _database.Apps.Add(previousApp);
        _appConfigService.Setup(s => s.GetConfigPath(previousApp.Id)).Returns(@"C:\Configs\old.rfn");

        var events = new List<string>();
        _besideTargetShortcutService.Setup(s => s.RemoveBesideTargetShortcut(previousApp))
            .Callback(() => events.Add("cleanup-beside-remove"));
        _iconService.Setup(s => s.CreateBadgedIcon(previousApp))
            .Callback(() => events.Add("restore-icon"))
            .Returns(string.Empty);
        _appConfigService.Setup(s => s.AssignApp(previousApp.Id, It.IsAny<string?>()))
            .Callback<string, string?>((_, path) => events.Add($"assign:{path ?? "<main>"}"));
        int saveAttempt = 0;
        _appConfigService.Setup(s => s.SaveAllConfigs(
                It.IsAny<AppDatabase>(),
                It.IsAny<ISecureSecretSnapshotSource>(),
                It.IsAny<byte[]>()))
            .Callback(() =>
            {
                events.Add(++saveAttempt == 1 ? "save-new" : "save-restore");
                if (saveAttempt == 1)
                    throw new IOException("disk full");
            });

        var result = Commit(
            CreateSut(),
            newApp,
            previousApp,
            @"C:\Configs\new.rfn",
            previousConfigPath: @"C:\Configs\old.rfn");

        Assert.Equal(RunAsAppEntryPersistenceStatus.SaveFailed, result.Status);
        Assert.Equal("disk full", result.ErrorMessage);
        Assert.Same(previousApp, _database.Apps.Single());
        Assert.Equal(
            ["cleanup-beside-remove", "assign:C:\\Configs\\new.rfn", "save-new", "assign:C:\\Configs\\old.rfn", "save-restore", "restore-icon"],
            events);
        _aclService.Verify(s => s.ApplyAcl(newApp, It.IsAny<IReadOnlyList<AppEntry>>()), Times.Never);
        _aclService.Verify(s => s.RevertAcl(previousApp, It.IsAny<IReadOnlyList<AppEntry>>()), Times.Never);
        _aclService.Verify(s => s.RecomputeAllAncestorAcls(It.IsAny<IReadOnlyList<AppEntry>>()), Times.Never);
        _dataChangeNotifier.Verify(s => s.NotifyDataChanged(), Times.Never);
    }

    [Fact]
    public void Commit_SaveFailureAndRestoreFailure_ReturnsCombinedErrorAndKeepsPreviousAppInMemory()
    {
        var previousApp = CreateApp("old-app");
        var newApp = previousApp.Clone();
        newApp.Name = "Updated App";
        _database.Apps.Add(previousApp);
        _appConfigService.Setup(s => s.GetConfigPath(previousApp.Id)).Returns(@"C:\Configs\old.rfn");
        int saveAttempt = 0;
        _appConfigService.Setup(s => s.SaveAllConfigs(
                It.IsAny<AppDatabase>(),
                It.IsAny<ISecureSecretSnapshotSource>(),
                It.IsAny<byte[]>()))
            .Callback(() =>
            {
                saveAttempt++;
                if (saveAttempt == 1)
                    throw new IOException("disk full");
            });
        _iconService.Setup(s => s.CreateBadgedIcon(previousApp))
            .Throws(new InvalidOperationException("restore enforcement failed"));

        var result = Commit(
            CreateSut(),
            newApp,
            previousApp,
            @"C:\Configs\new.rfn",
            previousConfigPath: @"C:\Configs\old.rfn");

        Assert.Equal(RunAsAppEntryPersistenceStatus.SaveFailed, result.Status);
        Assert.Contains("disk full", result.ErrorMessage);
        Assert.Contains("restore enforcement failed", result.ErrorMessage);
        Assert.Same(previousApp, _database.Apps.Single());
        _appConfigService.Verify(s => s.AssignApp(previousApp.Id, @"C:\Configs\new.rfn"), Times.Once);
        _appConfigService.Verify(s => s.AssignApp(previousApp.Id, @"C:\Configs\old.rfn"), Times.Once);
        _appConfigService.Verify(s => s.SaveAllConfigs(
                It.IsAny<AppDatabase>(),
                It.IsAny<ISecureSecretSnapshotSource>(),
                It.IsAny<byte[]>()),
            Times.Exactly(2));
        _dataChangeNotifier.Verify(s => s.NotifyDataChanged(), Times.Never);
    }

    [Fact]
    public void Commit_SaveFailureAndRestoreSaveFailure_StillRestoresPreviousEnforcement()
    {
        var previousApp = CreateApp("old-app");
        var newApp = previousApp.Clone();
        newApp.Name = "Updated App";
        _database.Apps.Add(previousApp);
        _appConfigService.Setup(s => s.GetConfigPath(previousApp.Id)).Returns(@"C:\Configs\old.rfn");
        int saveAttempt = 0;
        _appConfigService.Setup(s => s.SaveAllConfigs(
                It.IsAny<AppDatabase>(),
                It.IsAny<ISecureSecretSnapshotSource>(),
                It.IsAny<byte[]>()))
            .Callback(() =>
            {
                saveAttempt++;
                throw new IOException(saveAttempt == 1 ? "disk full" : "restore save failed");
            });

        var result = Commit(CreateSut(), newApp, previousApp, @"C:\Configs\new.rfn");

        Assert.Equal(RunAsAppEntryPersistenceStatus.SaveFailed, result.Status);
        Assert.Contains("disk full", result.ErrorMessage);
        Assert.Contains("restore save failed", result.ErrorMessage);
        Assert.Same(previousApp, _database.Apps.Single());
        _iconService.Verify(s => s.CreateBadgedIcon(previousApp), Times.Once);
        _appConfigService.Verify(s => s.SaveAllConfigs(
                It.IsAny<AppDatabase>(),
                It.IsAny<ISecureSecretSnapshotSource>(),
                It.IsAny<byte[]>()),
            Times.Exactly(2));
    }

    [Fact]
    public void Commit_ExistingAppSave_InvokesSaveAllConfigsOutsideKeySnapshot()
    {
        var previousApp = CreateApp("old-app");
        var newApp = previousApp.Clone();
        newApp.Name = "Updated App";
        _database.Apps.Add(previousApp);
        _appConfigService.Setup(s => s.GetConfigPath(previousApp.Id)).Returns(@"C:\Configs\old.rfn");

        _appConfigService.Setup(s => s.SaveAllConfigs(
                It.IsAny<AppDatabase>(),
                It.IsAny<ISecureSecretSnapshotSource>(),
                It.IsAny<byte[]>()))
            .Callback<AppDatabase, ISecureSecretSnapshotSource, byte[]>((_, keySource, _) =>
            {
                _pinKey.TransformSnapshot(_ => true);
                keySource.UseSnapshot(_ => { });
            });

        var result = Commit(CreateSut(), newApp, previousApp, @"C:\Configs\new.rfn");

        Assert.Equal(RunAsAppEntryPersistenceStatus.Succeeded, result.Status);
    }

    [Fact]
    public void Commit_ConfigOwnershipMovesBetweenMainAndAdditionalConfig_SavesAllConfigs()
    {
        var previousApp = CreateApp("old-app");
        var newApp = previousApp.Clone();
        newApp.Name = "Updated App";
        _database.Apps.Add(previousApp);
        _appConfigService.Setup(s => s.GetConfigPath(previousApp.Id)).Returns(null as string);

        var result = Commit(CreateSut(), newApp, previousApp, @"C:\Configs\extra.rfn");

        Assert.Equal(RunAsAppEntryPersistenceStatus.Succeeded, result.Status);
        _appConfigService.Verify(s => s.AssignApp(previousApp.Id, @"C:\Configs\extra.rfn"), Times.Once);
        _appConfigService.Verify(s => s.SaveAllConfigs(
                It.IsAny<AppDatabase>(),
                It.IsAny<ISecureSecretSnapshotSource>(),
                It.IsAny<byte[]>()),
            Times.Once);
        _appConfigService.Verify(s => s.SaveConfigForApp(
                It.IsAny<string>(),
                It.IsAny<AppDatabase>(),
                It.IsAny<ISecureSecretSnapshotSource>(),
                It.IsAny<byte[]>()),
            Times.Never);
    }

    [Fact]
    public void Commit_PreclassifiedConfigMove_UsesOriginalConfigPathFromDialogContext()
    {
        var previousApp = CreateApp("old-app");
        var newApp = previousApp.Clone();
        newApp.Name = "Updated App";
        _database.Apps.Add(previousApp);
        _appConfigService.Setup(s => s.GetConfigPath(previousApp.Id)).Returns(@"C:\Configs\extra.rfn");

        var changeSet = CreateChangeSet(AppEditConfigSaveScope.AllConfigs);

        var result = CreateSut().Commit(new RunAsAppEditCommitRequest(
            newApp,
            previousApp,
            PreviousConfigPath: null,
            ConfigPath: @"C:\Configs\extra.rfn",
            ChangeSet: changeSet));

        Assert.Equal(RunAsAppEntryPersistenceStatus.Succeeded, result.Status);
        _appConfigService.Verify(s => s.SaveAllConfigs(
                It.IsAny<AppDatabase>(),
                It.IsAny<ISecureSecretSnapshotSource>(),
                It.IsAny<byte[]>()),
            Times.Once);
        _appConfigService.Verify(s => s.SaveConfigForApp(
                It.IsAny<string>(),
                It.IsAny<AppDatabase>(),
                It.IsAny<ISecureSecretSnapshotSource>(),
                It.IsAny<byte[]>()),
            Times.Never);
    }

    [Fact]
    public void Commit_SameConfigExistingEdit_SavesCurrentAppConfigOnly()
    {
        var previousApp = CreateApp("old-app");
        var newApp = previousApp.Clone();
        newApp.Name = "Updated App";
        _database.Apps.Add(previousApp);
        _appConfigService.Setup(s => s.GetConfigPath(previousApp.Id)).Returns(@"C:\Configs\same.rfn");

        var result = Commit(
            CreateSut(),
            newApp,
            previousApp,
            @"C:\Configs\same.rfn",
            previousConfigPath: @"C:\Configs\same.rfn");

        Assert.Equal(RunAsAppEntryPersistenceStatus.Succeeded, result.Status);
        _appConfigService.Verify(s => s.AssignApp(previousApp.Id, @"C:\Configs\same.rfn"), Times.Once);
        _appConfigService.Verify(s => s.SaveConfigForApp(
                previousApp.Id,
                It.IsAny<AppDatabase>(),
                It.IsAny<ISecureSecretSnapshotSource>(),
                It.IsAny<byte[]>()),
            Times.Once);
        _appConfigService.Verify(s => s.SaveAllConfigs(
                It.IsAny<AppDatabase>(),
                It.IsAny<ISecureSecretSnapshotSource>(),
                It.IsAny<byte[]>()),
            Times.Never);
    }

    [Fact]
    public void Commit_ExistingEditWithoutShortcutRefresh_CopiesShortcutProtectionStates()
    {
        var previousApp = CreateApp("old-app");
        previousApp.ShortcutProtectionStates =
        [
            new ShortcutProtectionState(@"C:\Links\App.lnk", true, false, true)
        ];
        var newApp = previousApp.Clone();
        newApp.Name = previousApp.Name;
        _database.Apps.Add(previousApp);
        _appConfigService.Setup(s => s.GetConfigPath(previousApp.Id)).Returns(@"C:\Configs\same.rfn");

        var request = new RunAsAppEditCommitRequest(
            newApp,
            previousApp,
            PreviousConfigPath: @"C:\Configs\same.rfn",
            ConfigPath: @"C:\Configs\same.rfn",
            ChangeSet: new AppEntryChangeSet(
                RequiresAclReapply: false,
                RequiresBesideTargetRefresh: false,
                RequiresHandlerSync: false,
                RequiresManagedShortcutRefresh: false,
                RequiresIconRefresh: false,
                ConfigSaveScope: AppEditConfigSaveScope.CurrentAppConfigOnly));

        var result = CreateSut().Commit(request);

        Assert.Equal(RunAsAppEntryPersistenceStatus.Succeeded, result.Status);
        Assert.NotNull(newApp.ShortcutProtectionStates);
        var copiedState = Assert.Single(newApp.ShortcutProtectionStates);
        Assert.Equal(@"C:\Links\App.lnk", copiedState.ShortcutPath);
        Assert.NotSame(previousApp.ShortcutProtectionStates, newApp.ShortcutProtectionStates);
    }

    [Fact]
    public void Commit_ExistingEditWithShortcutRefresh_ClearsShortcutProtectionStates()
    {
        var previousApp = CreateApp("old-app");
        previousApp.ShortcutProtectionStates =
        [
            new ShortcutProtectionState(@"C:\Links\App.lnk", true, false, true)
        ];
        var newApp = previousApp.Clone();
        _database.Apps.Add(previousApp);
        _appConfigService.Setup(s => s.GetConfigPath(previousApp.Id)).Returns(@"C:\Configs\same.rfn");

        var request = new RunAsAppEditCommitRequest(
            newApp,
            previousApp,
            PreviousConfigPath: @"C:\Configs\same.rfn",
            ConfigPath: @"C:\Configs\same.rfn",
            ChangeSet: new AppEntryChangeSet(
                RequiresAclReapply: false,
                RequiresBesideTargetRefresh: true,
                RequiresHandlerSync: false,
                RequiresManagedShortcutRefresh: false,
                RequiresIconRefresh: false,
                ConfigSaveScope: AppEditConfigSaveScope.CurrentAppConfigOnly));

        var result = CreateSut().Commit(request);

        Assert.Equal(RunAsAppEntryPersistenceStatus.Succeeded, result.Status);
        Assert.Null(newApp.ShortcutProtectionStates);
    }

    private AppEditCommitService CreateSut()
    {
        var session = CreateSession();
        var appEntryManager = new RunAsAppEntryManager(
            _appState.Object,
            _log.Object,
            AppEntryEnforcementTestFactory.CreateCoordinator(
                _aclService.Object,
                _shortcutService.Object,
                _besideTargetShortcutService.Object,
                _iconService.Object,
                _sidNameCache.Object,
                new Mock<IInteractiveUserDesktopProvider>().Object,
                new Mock<IInteractiveUserSidResolver>().Object,
                new TestRunFenceLauncherPathProvider(@"C:\RunFence\RunFence.Launcher.exe", exists: true),
                _log.Object),
            _shortcutDiscovery.Object);
        var persistenceOrchestrator = new AppEntryPersistenceOrchestrator(
            _appState.Object,
            _uiThreadInvoker.Object,
            session,
            _appConfigService.Object,
            _aclService.Object,
            _dataChangeNotifier.Object,
            _licenseService.Object,
            _log.Object,
            new RunAsAppShortcutCreator(
                _iconService.Object,
                _sidNameCache.Object,
                _shortcutService.Object,
                _besideTargetShortcutService.Object,
                new LambdaSessionProvider(() => session),
                new Mock<IInteractiveUserSidResolver>().Object,
                new TestRunFenceLauncherPathProvider(@"C:\RunFence\RunFence.Launcher.exe", exists: true),
                _log.Object));
        return new AppEditCommitService(
            _appConfigService.Object,
            _dataChangeNotifier.Object,
            _log.Object,
            session,
            appEntryManager,
            persistenceOrchestrator,
            _appState.Object);
    }

    private static RunAsAppEntryPersistenceResult Commit(
        AppEditCommitService service,
        AppEntry newApp,
        AppEntry? previousApp,
        string? configPath,
        string? previousConfigPath = null)
        => service.Commit(new RunAsAppEditCommitRequest(
            newApp,
            previousApp,
            previousConfigPath,
            configPath,
            CreateChangeSet(string.Equals(previousConfigPath, configPath, StringComparison.OrdinalIgnoreCase)
                ? AppEditConfigSaveScope.CurrentAppConfigOnly
                : AppEditConfigSaveScope.AllConfigs)));

    private static AppEntryChangeSet CreateChangeSet(AppEditConfigSaveScope configSaveScope)
        => new(
            RequiresAclReapply: false,
            RequiresBesideTargetRefresh: true,
            RequiresHandlerSync: false,
            RequiresManagedShortcutRefresh: false,
            RequiresIconRefresh: true,
            ConfigSaveScope: configSaveScope);

    private SessionContext CreateSession()
    {
        var session = new SessionContext
{
            Database = _database,
            CredentialStore = _credentialStore,
        }.WithClonedPinDerivedKey(_pinKey);
        _sessions.Add(session);
        return session;
    }

    private static AppEntry CreateApp(string id) => new()
    {
        Id = id,
        Name = "Original App",
        AccountSid = UserSid,
        RestrictAcl = true,
        ManageShortcuts = false
    };
}
