using Moq;
using RunFence.Acl;
using RunFence.Account;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.Persistence;
using RunFence.RunAs;
using Xunit;

namespace RunFence.Tests;

public class AppEntryPersistenceOrchestratorTests : IDisposable
{
    private const string UserSid = "S-1-5-21-1000-1000-1000-1001";

    private readonly Mock<IAppStateProvider> _appState = new();
    private readonly Mock<IUiThreadInvoker> _uiThreadInvoker = new();
    private readonly Mock<IDataChangeNotifier> _dataChangeNotifier = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<IAppConfigService> _appConfigService = new();
    private readonly Mock<IAclService> _aclService = new();
    private readonly Mock<IShortcutService> _shortcutService = new();
    private readonly Mock<IBesideTargetShortcutService> _besideTargetShortcutService = new();
    private readonly Mock<IIconService> _iconService = new();
    private readonly Mock<ILicenseService> _licenseService = new();
    private readonly Mock<ISidNameCacheService> _sidNameCache = new();
    private readonly AppDatabase _database = new();
    private readonly CredentialStore _credentialStore = new();
    private readonly SecureSecret _pinKey = TestSecretFactory.Create(32);
    private readonly List<SessionContext> _sessions = [];

    public AppEntryPersistenceOrchestratorTests()
    {
        _appState.Setup(c => c.Database).Returns(_database);
        _licenseService.Setup(l => l.CanAddApp(It.IsAny<int>())).Returns(true);
    }

    public void Dispose()
    {
        foreach (var session in _sessions)
            session.Dispose();

        _pinKey.Dispose();
    }

    [Fact]
    public void PersistNewAppEntry_Success_ReturnsSucceeded()
    {
        var app = new AppEntry { Name = "MyApp", AccountSid = UserSid };
        var orchestrator = CreateOrchestrator();

        var result = orchestrator.PersistNewAppEntry(app, configPath: null);

        Assert.Equal(RunAsAppEntryPersistenceStatus.Succeeded, result.Status);
        Assert.Contains(app, _database.Apps);
        _dataChangeNotifier.Verify(c => c.NotifyDataChanged(), Times.Once);
    }

    [Fact]
    public void PersistNewAppEntry_SaveConfigThrows_ReturnsSaveFailedAndKeepsDatabaseState()
    {
        _appConfigService
            .Setup(s => s.SaveConfigForApp(
                It.IsAny<string>(),
                It.IsAny<AppDatabase>(),
                It.IsAny<ISecureSecretSnapshotSource>(),
                It.IsAny<byte[]>()))
            .Throws(new IOException("disk full"));
        var app = new AppEntry { Name = "MyApp", AccountSid = UserSid };
        var orchestrator = CreateOrchestrator();

        var result = orchestrator.PersistNewAppEntry(app, @"C:\extra.rfn");

        Assert.Equal(RunAsAppEntryPersistenceStatus.SaveFailed, result.Status);
        Assert.Equal("disk full", result.ErrorMessage);
        Assert.Contains(app, _database.Apps);
        _appConfigService.Verify(s => s.AssignApp(app.Id, @"C:\extra.rfn"), Times.Once);
        _appConfigService.Verify(s => s.RemoveApp(It.IsAny<string>()), Times.Never);
        _aclService.Verify(s => s.RevertAcl(It.IsAny<AppEntry>(), It.IsAny<IReadOnlyList<AppEntry>>()), Times.Never);
        _besideTargetShortcutService.Verify(s => s.RemoveBesideTargetShortcut(It.IsAny<AppEntry>()), Times.Never);
    }

    [Fact]
    public void PersistNewAppEntry_SaveConfigForApp_IsInvokedOutsideKeySnapshot()
    {
        _appConfigService
            .Setup(s => s.SaveConfigForApp(
                It.IsAny<string>(),
                It.IsAny<AppDatabase>(),
                It.IsAny<ISecureSecretSnapshotSource>(),
                It.IsAny<byte[]>()))
            .Callback<string, AppDatabase, ISecureSecretSnapshotSource, byte[]>((_, _, keySource, _) =>
            {
                _pinKey.TransformSnapshot(_ => true);
                keySource.UseSnapshot(_ => { });
            });
        var app = new AppEntry { Name = "MyApp", AccountSid = UserSid };

        var result = CreateOrchestrator().PersistNewAppEntry(app, configPath: null);

        Assert.Equal(RunAsAppEntryPersistenceStatus.Succeeded, result.Status);
    }

    [Fact]
    public void PersistNewAppEntry_DenyModeAclFailure_ReturnsConvenienceEnforcementFailed()
    {
        _aclService
            .Setup(s => s.ApplyAcl(It.IsAny<AppEntry>(), It.IsAny<IReadOnlyList<AppEntry>>()))
            .Throws(new InvalidOperationException("acl failed"));
        var app = new AppEntry
        {
            Name = "MyApp",
            AccountSid = UserSid,
            RestrictAcl = true,
            AclMode = AclMode.Deny
        };

        var result = CreateOrchestrator().PersistNewAppEntry(app, configPath: null);

        Assert.Equal(RunAsAppEntryPersistenceStatus.ConvenienceEnforcementFailed, result.Status);
        Assert.Equal("acl failed", result.WarningMessage);
        Assert.Contains(app, _database.Apps);
        _dataChangeNotifier.Verify(c => c.NotifyDataChanged(), Times.Once);
    }

    [Fact]
    public void PersistNewAppEntry_ShortcutFailure_ReturnsConvenienceEnforcementFailed()
    {
        _credentialStore.Credentials.Add(new CredentialEntry { Sid = UserSid });
        _sidNameCache.Setup(c => c.GetDisplayName(UserSid)).Returns("TestUser");
        _besideTargetShortcutService
            .Setup(s => s.CreateBesideTargetShortcut(It.IsAny<AppEntry>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Throws(new InvalidOperationException("shortcut failed"));

        var app = new AppEntry
        {
            Name = "MyApp",
            AccountSid = UserSid,
            ManageShortcuts = true
        };

        var result = CreateOrchestrator().PersistNewAppEntry(app, configPath: null);

        Assert.Equal(RunAsAppEntryPersistenceStatus.ConvenienceEnforcementFailed, result.Status);
        Assert.Equal("shortcut failed", result.WarningMessage);
        Assert.Contains(app, _database.Apps);
        _dataChangeNotifier.Verify(c => c.NotifyDataChanged(), Times.Once);
    }

    [Fact]
    public void PersistNewAppEntry_AllowModeAncestorAclFailure_ReturnsRequiredEnforcementFailed()
    {
        _aclService
            .Setup(s => s.RecomputeAllAncestorAcls(It.IsAny<IReadOnlyList<AppEntry>>()))
            .Throws(new InvalidOperationException("ancestor failed"));
        var app = new AppEntry
        {
            Name = "MyApp",
            AccountSid = UserSid,
            RestrictAcl = true,
            AclMode = AclMode.Allow
        };

        var result = CreateOrchestrator().PersistNewAppEntry(app, configPath: null);

        Assert.Equal(RunAsAppEntryPersistenceStatus.RequiredEnforcementFailed, result.Status);
        Assert.Equal("ancestor failed", result.WarningMessage);
    }

    [Fact]
    public void PersistNewAppEntry_DenyModeAncestorAclFailure_StillReturnsRequiredEnforcementFailed()
    {
        _aclService
            .Setup(s => s.RecomputeAllAncestorAcls(It.IsAny<IReadOnlyList<AppEntry>>()))
            .Throws(new InvalidOperationException("ancestor failed"));
        var app = new AppEntry
        {
            Name = "MyApp",
            AccountSid = UserSid,
            RestrictAcl = true,
            AclMode = AclMode.Deny
        };

        var result = CreateOrchestrator().PersistNewAppEntry(app, configPath: null);

        Assert.Equal(RunAsAppEntryPersistenceStatus.RequiredEnforcementFailed, result.Status);
        Assert.Equal("ancestor failed", result.WarningMessage);
    }

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

    private RunAsAppShortcutCreator CreateShortcutCreator()
    {
        var session = CreateSession();
        var sessionProvider = new LambdaSessionProvider(() => session);
        return new RunAsAppShortcutCreator(
            _iconService.Object,
            _sidNameCache.Object,
            _shortcutService.Object,
            _besideTargetShortcutService.Object,
            sessionProvider,
            new Mock<IInteractiveUserSidResolver>().Object,
            new TestRunFenceLauncherPathProvider(@"C:\RunFence\RunFence.Launcher.exe", exists: true),
            _log.Object);
    }

    private AppEntryPersistenceOrchestrator CreateOrchestrator()
        => new(
            _appState.Object,
            _uiThreadInvoker.Object,
            CreateSession(),
            _appConfigService.Object,
            _aclService.Object,
            _dataChangeNotifier.Object,
            _licenseService.Object,
            _log.Object,
            CreateShortcutCreator());
}
