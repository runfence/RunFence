using Moq;
using RunFence.Acl;
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

public class RunAsAppEntryManagerTests : IDisposable
{
    private const string UserSid = "S-1-5-21-1000-1000-1000-1001";

    private readonly Mock<IAppStateProvider> _appState = new();
    private readonly Mock<IUiThreadInvoker> _uiThreadInvoker = new();
    private readonly Mock<IDataChangeNotifier> _dataChangeNotifier = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<IAppConfigService> _appConfigService = new();
    private readonly Mock<IAclService> _aclService = new();
    private readonly Mock<IShortcutService> _shortcutService = new();
    private readonly Mock<IIconService> _iconService = new();
    private readonly Mock<ILicenseService> _licenseService = new();
    private readonly Mock<IRunAsLaunchErrorHandler> _launchErrorHandler = new();
    private readonly AppDatabase _database = new();
    private readonly CredentialStore _credentialStore = new();
    private readonly ProtectedBuffer _pinKey = new(new byte[32], protect: false);

    public RunAsAppEntryManagerTests()
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

    private readonly Mock<ISidResolver> _sidResolver = new();

    private AppEntryEnforcementHelper CreateEnforcementHelper()
        => new(_aclService.Object, _shortcutService.Object, _iconService.Object, _sidResolver.Object, new Mock<IInteractiveUserDesktopProvider>().Object);

    private RunAsAppEntryManager CreateManager()
        => new(
            _appState.Object,
            _uiThreadInvoker.Object,
            _dataChangeNotifier.Object,
            _log.Object,
            CreateSession(),
            _appConfigService.Object,
            _aclService.Object,
            _shortcutService.Object,
            _iconService.Object,
            CreateEnforcementHelper(),
            _sidResolver.Object,
            _licenseService.Object,
            _launchErrorHandler.Object);

    // ── PersistNewAppEntry — success path ─────────────────────────────────

    [Fact]
    public void PersistNewAppEntry_Success_AddsAppToDatabase()
    {
        // Arrange
        var app = new AppEntry { Name = "MyApp", AccountSid = UserSid };
        var manager = CreateManager();

        // Act
        var result = manager.PersistNewAppEntry(app, configPath: null);

        // Assert
        Assert.True(result);
        Assert.Contains(app, _database.Apps);
    }

    [Fact]
    public void PersistNewAppEntry_Success_SavesConfig()
    {
        // Arrange
        var app = new AppEntry { Name = "MyApp", AccountSid = UserSid };
        var manager = CreateManager();

        // Act
        manager.PersistNewAppEntry(app, configPath: null);

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
        var manager = CreateManager();

        // Act
        manager.PersistNewAppEntry(app, configPath);

        // Assert
        _appConfigService.Verify(s => s.AssignApp(app.Id, configPath), Times.Once);
    }

    [Fact]
    public void PersistNewAppEntry_NoConfigPath_DoesNotAssignApp()
    {
        // Arrange
        var app = new AppEntry { Name = "MyApp", AccountSid = UserSid };
        var manager = CreateManager();

        // Act
        manager.PersistNewAppEntry(app, configPath: null);

        // Assert
        _appConfigService.Verify(s => s.AssignApp(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void PersistNewAppEntry_RestrictAcl_AppliesAcl()
    {
        // Arrange
        var app = new AppEntry { Name = "MyApp", AccountSid = UserSid, RestrictAcl = true };
        var manager = CreateManager();

        // Act
        manager.PersistNewAppEntry(app, configPath: null);

        // Assert
        _aclService.Verify(s => s.ApplyAcl(app, It.IsAny<IReadOnlyList<AppEntry>>()), Times.Once);
    }

    [Fact]
    public void PersistNewAppEntry_NoRestrictAcl_SkipsAclApply()
    {
        // Arrange
        var app = new AppEntry { Name = "MyApp", AccountSid = UserSid, RestrictAcl = false };
        var manager = CreateManager();

        // Act
        manager.PersistNewAppEntry(app, configPath: null);

        // Assert
        _aclService.Verify(s => s.ApplyAcl(It.IsAny<AppEntry>(), It.IsAny<IReadOnlyList<AppEntry>>()), Times.Never);
    }

    [Fact]
    public void PersistNewAppEntry_Success_RecomputesAncestorAcls()
    {
        // Arrange
        var app = new AppEntry { Name = "MyApp", AccountSid = UserSid };
        var manager = CreateManager();

        // Act
        manager.PersistNewAppEntry(app, configPath: null);

        // Assert
        _aclService.Verify(s => s.RecomputeAllAncestorAcls(It.IsAny<IReadOnlyList<AppEntry>>()), Times.Once);
    }

    [Fact]
    public void PersistNewAppEntry_Success_NotifiesDataChanged()
    {
        // Arrange
        var app = new AppEntry { Name = "MyApp", AccountSid = UserSid };
        var manager = CreateManager();

        // Act
        manager.PersistNewAppEntry(app, configPath: null);

        // Assert
        _dataChangeNotifier.Verify(c => c.NotifyDataChanged(), Times.Once);
    }

    // ── PersistNewAppEntry — failure / rollback path ──────────────────────

    [Fact]
    public void PersistNewAppEntry_SaveConfigThrows_RollsBackAndReturnsFalse()
    {
        // Arrange: SaveConfigForApp fails
        _appConfigService
            .Setup(s => s.SaveConfigForApp(It.IsAny<string>(), It.IsAny<AppDatabase>(),
                It.IsAny<byte[]>(), It.IsAny<byte[]>()))
            .Throws(new IOException("Disk full"));
        var app = new AppEntry { Name = "MyApp", AccountSid = UserSid };
        var manager = CreateManager();

        // Act
        var result = manager.PersistNewAppEntry(app, configPath: null);

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
        var manager = CreateManager();

        // Act
        manager.PersistNewAppEntry(app, configPath: null);

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
        var manager = CreateManager();

        // Act
        manager.PersistNewAppEntry(app, configPath);

        // Assert: RemoveApp called on rollback since app was assigned to config
        _appConfigService.Verify(s => s.RemoveApp(app.Id), Times.Once);
    }

    // ── PersistNewAppEntry — license enforcement ──────────────────────────

    [Fact]
    public void PersistNewAppEntry_LicenseLimitReached_ReturnsFalseWithoutAdding()
    {
        // Arrange
        _licenseService.Setup(l => l.CanAddApp(It.IsAny<int>())).Returns(false);
        _licenseService.Setup(l => l.GetRestrictionMessage(It.IsAny<EvaluationFeature>(), It.IsAny<int>()))
            .Returns("License limit reached");
        var app = new AppEntry { Name = "MyApp", AccountSid = UserSid };
        var manager = CreateManager();

        // Act
        var result = manager.PersistNewAppEntry(app, configPath: null);

        // Assert: rejected before adding — BeginInvokeOnUIThread dispatches the MessageBox
        // (not executed synchronously in tests to avoid blocking on the dialog)
        Assert.False(result);
        Assert.DoesNotContain(app, _database.Apps);
        _uiThreadInvoker.Verify(c => c.BeginInvoke(It.IsAny<Action>()), Times.Once);
        _appConfigService.Verify(s => s.SaveConfigForApp(It.IsAny<string>(), It.IsAny<AppDatabase>(),
            It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Never);
    }

    // ── RevertAppChanges ──────────────────────────────────────────────────

    [Fact]
    public void RevertAppChanges_CallsEnforcementHelperAndRecomputesAncestorAcls()
    {
        // Arrange
        var app = new AppEntry { Name = "MyApp", AccountSid = UserSid, RestrictAcl = true };
        _database.Apps.Add(app);
        var manager = CreateManager();

        // Act
        manager.RevertAppChanges(app);

        // Assert: ACL reverted and ancestor ACLs recomputed (without the reverted app)
        _aclService.Verify(s => s.RevertAcl(app, It.IsAny<IReadOnlyList<AppEntry>>()), Times.Once);
        _aclService.Verify(s => s.RecomputeAllAncestorAcls(It.IsAny<IReadOnlyList<AppEntry>>()), Times.Once);
    }

    [Fact]
    public void RevertAppChanges_RevertAclThrows_LogsError()
    {
        // Arrange
        var app = new AppEntry { Name = "BrokenApp", AccountSid = UserSid, RestrictAcl = true };
        _database.Apps.Add(app);
        _aclService.Setup(s => s.RevertAcl(It.IsAny<AppEntry>(), It.IsAny<IReadOnlyList<AppEntry>>()))
            .Throws(new UnauthorizedAccessException("Access denied"));
        var manager = CreateManager();

        // Act — must not throw
        manager.RevertAppChanges(app);

        // Assert
        _log.Verify(l => l.Error(It.Is<string>(s => s.Contains("BrokenApp")), It.IsAny<Exception>()), Times.Once);
    }

    // ── ApplyAppChanges ───────────────────────────────────────────────────

    [Fact]
    public void ApplyAppChanges_RestrictAcl_AppliesAclAndRecomputes()
    {
        // Arrange
        var app = new AppEntry { Name = "MyApp", AccountSid = UserSid, RestrictAcl = true };
        _database.Apps.Add(app);
        var manager = CreateManager();

        // Act
        manager.ApplyAppChanges(app);

        // Assert
        _aclService.Verify(s => s.ApplyAcl(app, It.IsAny<IReadOnlyList<AppEntry>>()), Times.Once);
        _aclService.Verify(s => s.RecomputeAllAncestorAcls(It.IsAny<IReadOnlyList<AppEntry>>()), Times.Once);
    }
}