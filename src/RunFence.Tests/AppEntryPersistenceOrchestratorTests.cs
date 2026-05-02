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
    private readonly ProtectedBuffer _pinKey = new(new byte[32], protect: false);

    public AppEntryPersistenceOrchestratorTests()
    {
        _appState.Setup(c => c.Database).Returns(_database);
        _licenseService.Setup(l => l.CanAddApp(It.IsAny<int>())).Returns(true);
    }

    public void Dispose() => _pinKey.Dispose();

    private SessionContext CreateSession() => new()
    {
        Database = _database,
        CredentialStore = _credentialStore,
        PinDerivedKey = _pinKey
    };

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

    // ── Success path ───────────────────────────────────────────────────────

    [Fact]
    public void PersistNewAppEntry_Success_AddsAppToDatabase()
    {
        // Arrange
        var app = new AppEntry { Name = "MyApp", AccountSid = UserSid };
        var orchestrator = CreateOrchestrator();

        // Act
        var result = orchestrator.PersistNewAppEntry(app, configPath: null);

        // Assert
        Assert.True(result);
        Assert.Contains(app, _database.Apps);
    }

    [Fact]
    public void PersistNewAppEntry_Success_SavesConfig()
    {
        // Arrange
        var app = new AppEntry { Name = "MyApp", AccountSid = UserSid };
        var orchestrator = CreateOrchestrator();

        // Act
        orchestrator.PersistNewAppEntry(app, configPath: null);

        // Assert
        _appConfigService.Verify(s => s.SaveConfigForApp(
                app.Id, _database, It.IsAny<byte[]>(), It.IsAny<byte[]>()),
            Times.Once);
    }

    [Fact]
    public void PersistNewAppEntry_WithConfigPath_AssignsApp()
    {
        // Arrange
        var app = new AppEntry { Name = "MyApp", AccountSid = UserSid };
        var configPath = @"C:\configs\extra.rfn";
        var orchestrator = CreateOrchestrator();

        // Act
        orchestrator.PersistNewAppEntry(app, configPath);

        // Assert
        _appConfigService.Verify(s => s.AssignApp(app.Id, configPath), Times.Once);
    }

    [Fact]
    public void PersistNewAppEntry_NoConfigPath_DoesNotAssignApp()
    {
        // Arrange
        var app = new AppEntry { Name = "MyApp", AccountSid = UserSid };
        var orchestrator = CreateOrchestrator();

        // Act
        orchestrator.PersistNewAppEntry(app, configPath: null);

        // Assert
        _appConfigService.Verify(s => s.AssignApp(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void PersistNewAppEntry_RestrictAcl_AppliesAcl()
    {
        // Arrange
        var app = new AppEntry { Name = "MyApp", AccountSid = UserSid, RestrictAcl = true };
        var orchestrator = CreateOrchestrator();

        // Act
        orchestrator.PersistNewAppEntry(app, configPath: null);

        // Assert
        _aclService.Verify(s => s.ApplyAcl(app, It.IsAny<IReadOnlyList<AppEntry>>()), Times.Once);
    }

    [Fact]
    public void PersistNewAppEntry_NoRestrictAcl_SkipsAclApply()
    {
        // Arrange
        var app = new AppEntry { Name = "MyApp", AccountSid = UserSid, RestrictAcl = false };
        var orchestrator = CreateOrchestrator();

        // Act
        orchestrator.PersistNewAppEntry(app, configPath: null);

        // Assert
        _aclService.Verify(s => s.ApplyAcl(It.IsAny<AppEntry>(), It.IsAny<IReadOnlyList<AppEntry>>()), Times.Never);
    }

    [Fact]
    public void PersistNewAppEntry_Success_RecomputesAncestorAcls()
    {
        // Arrange
        var app = new AppEntry { Name = "MyApp", AccountSid = UserSid };
        var orchestrator = CreateOrchestrator();

        // Act
        orchestrator.PersistNewAppEntry(app, configPath: null);

        // Assert
        _aclService.Verify(s => s.RecomputeAllAncestorAcls(It.IsAny<IReadOnlyList<AppEntry>>()), Times.Once);
    }

    [Fact]
    public void PersistNewAppEntry_Success_NotifiesDataChanged()
    {
        // Arrange
        var app = new AppEntry { Name = "MyApp", AccountSid = UserSid };
        var orchestrator = CreateOrchestrator();

        // Act
        orchestrator.PersistNewAppEntry(app, configPath: null);

        // Assert
        _dataChangeNotifier.Verify(c => c.NotifyDataChanged(), Times.Once);
    }

    // ── Failure / rollback path ────────────────────────────────────────────

    [Fact]
    public void PersistNewAppEntry_SaveConfigThrows_RollsBackAndReturnsFalse()
    {
        // Arrange: SaveConfigForApp fails
        _appConfigService
            .Setup(s => s.SaveConfigForApp(It.IsAny<string>(), It.IsAny<AppDatabase>(),
                It.IsAny<byte[]>(), It.IsAny<byte[]>()))
            .Throws(new IOException("Disk full"));
        var app = new AppEntry { Name = "MyApp", AccountSid = UserSid };
        var orchestrator = CreateOrchestrator();

        // Act
        var result = orchestrator.PersistNewAppEntry(app, configPath: null);

        // Assert: returns false, app removed from database
        Assert.False(result);
        Assert.DoesNotContain(app, _database.Apps);
        _log.Verify(l => l.Error(It.Is<string>(s => s.Contains("RunAs app entry")),
            It.IsAny<Exception>()), Times.Once);
    }

    [Fact]
    public void PersistNewAppEntry_SaveConfigThrows_WithRestrictAcl_RevertsAcl()
    {
        // Arrange: save fails before ACL is applied — rollback still calls RevertAcl defensively
        _appConfigService
            .Setup(s => s.SaveConfigForApp(It.IsAny<string>(), It.IsAny<AppDatabase>(),
                It.IsAny<byte[]>(), It.IsAny<byte[]>()))
            .Throws(new IOException("Disk full"));
        var app = new AppEntry { Name = "MyApp", AccountSid = UserSid, RestrictAcl = true };
        var orchestrator = CreateOrchestrator();

        // Act
        orchestrator.PersistNewAppEntry(app, configPath: null);

        // Assert: RevertAcl called during rollback
        _aclService.Verify(s => s.RevertAcl(app, It.IsAny<IReadOnlyList<AppEntry>>()), Times.Once);
    }

    [Fact]
    public void PersistNewAppEntry_SaveConfigThrows_WithConfigPath_RemovesApp()
    {
        // Arrange
        _appConfigService
            .Setup(s => s.SaveConfigForApp(It.IsAny<string>(), It.IsAny<AppDatabase>(),
                It.IsAny<byte[]>(), It.IsAny<byte[]>()))
            .Throws(new IOException("Disk full"));
        var app = new AppEntry { Name = "MyApp", AccountSid = UserSid };
        var configPath = @"C:\configs\extra.rfn";
        var orchestrator = CreateOrchestrator();

        // Act
        orchestrator.PersistNewAppEntry(app, configPath);

        // Assert: RemoveApp called on rollback since app was assigned to config
        _appConfigService.Verify(s => s.RemoveApp(app.Id), Times.Once);
    }

    // ── License enforcement ────────────────────────────────────────────────

    [Fact]
    public void PersistNewAppEntry_LicenseLimitReached_ReturnsFalseWithoutAdding()
    {
        // Arrange
        _licenseService.Setup(l => l.CanAddApp(It.IsAny<int>())).Returns(false);
        _licenseService.Setup(l => l.GetRestrictionMessage(It.IsAny<EvaluationFeature>(), It.IsAny<int>()))
            .Returns("License limit reached");
        var app = new AppEntry { Name = "MyApp", AccountSid = UserSid };
        var orchestrator = CreateOrchestrator();

        // Act
        var result = orchestrator.PersistNewAppEntry(app, configPath: null);

        // Assert: rejected before adding — BeginInvokeOnUIThread dispatches the MessageBox
        // (not executed synchronously in tests to avoid blocking on the dialog)
        Assert.False(result);
        Assert.DoesNotContain(app, _database.Apps);
        _uiThreadInvoker.Verify(c => c.BeginInvoke(It.IsAny<Action>()), Times.Once);
        _appConfigService.Verify(s => s.SaveConfigForApp(It.IsAny<string>(), It.IsAny<AppDatabase>(),
            It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Never);
    }

    // ── ManageShortcuts ────────────────────────────────────────────────────

    [Fact]
    public void PersistNewAppEntry_ManageShortcuts_SuccessPath_DoesNotCallRemoveBesideTarget()
    {
        // On the success path, RemoveBesideTargetShortcut must NOT be called —
        // that method is only for rollback. The creation path calls CreateBesideTargetShortcut
        // which is gated on File.Exists(launcherPath) and is not exercised in unit tests.
        var app = new AppEntry { Name = "MyApp", AccountSid = UserSid, ManageShortcuts = true };
        _credentialStore.Credentials.Add(new CredentialEntry { Sid = UserSid });
        _sidNameCache.Setup(c => c.GetDisplayName(UserSid)).Returns("TestUser");
        var orchestrator = CreateOrchestrator();

        // Act
        var result = orchestrator.PersistNewAppEntry(app, configPath: null);

        // Assert
        Assert.True(result);
        _besideTargetShortcutService.Verify(s => s.RemoveBesideTargetShortcut(app), Times.Never);
    }

    [Fact]
    public void PersistNewAppEntry_ManageShortcuts_SaveConfigFails_RevokesShortcut()
    {
        // Arrange: save fails after ManageShortcuts path; rollback must call RemoveBesideTargetShortcut
        _appConfigService
            .Setup(s => s.SaveConfigForApp(It.IsAny<string>(), It.IsAny<AppDatabase>(),
                It.IsAny<byte[]>(), It.IsAny<byte[]>()))
            .Throws(new IOException("Disk full"));
        var app = new AppEntry { Name = "MyApp", AccountSid = UserSid, ManageShortcuts = true };
        var orchestrator = CreateOrchestrator();

        // Act
        var result = orchestrator.PersistNewAppEntry(app, configPath: null);

        // Assert: failure + shortcut cleanup attempted during rollback
        Assert.False(result);
        _besideTargetShortcutService.Verify(s => s.RemoveBesideTargetShortcut(app), Times.Once);
    }
}
