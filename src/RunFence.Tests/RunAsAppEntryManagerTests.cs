using Moq;
using RunFence.Acl;
using RunFence.Account;
using RunFence.Apps;
using RunFence.Apps.Shortcuts;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.RunAs;
using Xunit;

namespace RunFence.Tests;

public class RunAsAppEntryManagerTests
{
    private const string UserSid = "S-1-5-21-1000-1000-1000-1001";

    private readonly Mock<IAppStateProvider> _appState = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<IAclService> _aclService = new();
    private readonly Mock<IShortcutService> _shortcutService = new();
    private readonly Mock<IBesideTargetShortcutService> _besideTargetShortcutService = new();
    private readonly Mock<IShortcutDiscoveryService> _shortcutDiscovery = new();
    private readonly Mock<IIconService> _iconService = new();
    private readonly Mock<ISidNameCacheService> _sidNameCache = new();
    private readonly AppDatabase _database = new();

    public RunAsAppEntryManagerTests()
    {
        _appState.Setup(c => c.Database).Returns(_database);
        _shortcutDiscovery.Setup(d => d.CreateTraversalCache()).Returns(() => new ShortcutTraversalCache([]));
    }

    private RunAsAppEntryManager CreateManager()
        => new(
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
                new Mock<ILoggingService>().Object),
            _shortcutDiscovery.Object);

    // ── RevertAppChanges ──────────────────────────────────────────────────

    [Fact]
    public void RevertAppChanges_CallsEnforcementHelperAndRecomputesAncestorAcls()
    {
        // Arrange
        var app = new AppEntry { Name = "MyApp", AccountSid = UserSid, RestrictAcl = true };
        _database.Apps.Add(app);
        var manager = CreateManager();

        // Act
        var result = manager.RevertAppChanges(app, FullEnforcementChangeSet());

        // Assert: ACL reverted and ancestor ACLs recomputed (without the reverted app)
        Assert.Equal(RunAsAppEntryPersistenceStatus.Succeeded, result.Status);
        _aclService.Verify(s => s.RevertAcl(app, It.IsAny<IReadOnlyList<AppEntry>>()), Times.Once);
        _aclService.Verify(s => s.RecomputeAllAncestorAcls(It.IsAny<IReadOnlyList<AppEntry>>()), Times.Once);
    }

    [Fact]
    public void RevertAppChanges_RevertAclThrows_ReturnsSaveFailedAndLogsError()
    {
        // Arrange
        var app = new AppEntry { Name = "BrokenApp", AccountSid = UserSid, RestrictAcl = true };
        _database.Apps.Add(app);
        _aclService.Setup(s => s.RevertAcl(It.IsAny<AppEntry>(), It.IsAny<IReadOnlyList<AppEntry>>()))
            .Throws(new UnauthorizedAccessException("Access denied"));
        var manager = CreateManager();

        // Act — must not throw
        var result = manager.RevertAppChanges(app, FullEnforcementChangeSet());

        // Assert
        Assert.Equal(RunAsAppEntryPersistenceStatus.SaveFailed, result.Status);
        Assert.Equal("Access denied", result.ErrorMessage);
        _aclService.Verify(s => s.ApplyAcl(app, It.IsAny<IReadOnlyList<AppEntry>>()), Times.Once);
    }

    [Fact]
    public void RevertAppChanges_RecomputeAncestorAclsThrows_RestoresPreviousEnforcementAndReturnsSaveFailed()
    {
        var app = new AppEntry { Name = "BrokenApp", AccountSid = UserSid, RestrictAcl = true };
        _database.Apps.Add(app);
        var callOrder = new List<string>();
        _aclService.Setup(s => s.RevertAcl(app, It.IsAny<IReadOnlyList<AppEntry>>()))
            .Callback(() => callOrder.Add("revert"));
        int recomputeCount = 0;
        _aclService.Setup(s => s.RecomputeAllAncestorAcls(It.IsAny<IReadOnlyList<AppEntry>>()))
            .Callback(() =>
            {
                callOrder.Add("recompute");
                recomputeCount++;
                if (recomputeCount == 1)
                    throw new InvalidOperationException("ancestor failed");
            });
        _aclService.Setup(s => s.ApplyAcl(app, It.IsAny<IReadOnlyList<AppEntry>>()))
            .Callback(() => callOrder.Add("apply"));

        var result = CreateManager().RevertAppChanges(app, FullEnforcementChangeSet());

        Assert.Equal(RunAsAppEntryPersistenceStatus.SaveFailed, result.Status);
        Assert.Equal("ancestor failed", result.ErrorMessage);
        Assert.Equal(["revert", "recompute", "apply", "recompute"], callOrder);
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
        var result = manager.ApplyAppChanges(app, FullEnforcementChangeSet());

        // Assert
        Assert.Equal(RunAsAppEntryPersistenceStatus.Succeeded, result.Status);
        _aclService.Verify(s => s.ApplyAcl(app, It.IsAny<IReadOnlyList<AppEntry>>()), Times.Once);
        _aclService.Verify(s => s.RecomputeAllAncestorAcls(It.IsAny<IReadOnlyList<AppEntry>>()), Times.Once);
    }

    [Fact]
    public void ApplyAppChanges_DenyAclFailure_ReturnsConvenienceEnforcementFailed()
    {
        var app = new AppEntry { Name = "MyApp", AccountSid = UserSid, RestrictAcl = true, AclMode = AclMode.Deny };
        _database.Apps.Add(app);
        _aclService.Setup(s => s.ApplyAcl(app, It.IsAny<IReadOnlyList<AppEntry>>()))
            .Throws(new InvalidOperationException("acl failed"));

        var result = CreateManager().ApplyAppChanges(app, FullEnforcementChangeSet());

        Assert.Equal(RunAsAppEntryPersistenceStatus.ConvenienceEnforcementFailed, result.Status);
        Assert.Equal("acl failed", result.WarningMessage);
    }

    [Fact]
    public void ApplyAppChanges_ShortcutRelatedFailure_ReturnsConvenienceEnforcementFailed()
    {
        var app = new AppEntry { Name = "MyApp", AccountSid = UserSid, ManageShortcuts = true };
        _database.Apps.Add(app);
        _iconService.Setup(s => s.CreateBadgedIcon(app))
            .Throws(new InvalidOperationException("icon failed"));

        var result = CreateManager().ApplyAppChanges(app, FullEnforcementChangeSet());

        Assert.Equal(RunAsAppEntryPersistenceStatus.ConvenienceEnforcementFailed, result.Status);
        Assert.Equal("icon failed", result.WarningMessage);
    }

    [Fact]
    public void ApplyAppChanges_AncestorAclFailure_ReturnsRequiredEnforcementFailed()
    {
        var app = new AppEntry { Name = "MyApp", AccountSid = UserSid };
        _database.Apps.Add(app);
        _aclService.Setup(s => s.RecomputeAllAncestorAcls(It.IsAny<IReadOnlyList<AppEntry>>()))
            .Throws(new InvalidOperationException("ancestor failed"));

        var result = CreateManager().ApplyAppChanges(app, FullEnforcementChangeSet());

        Assert.Equal(RunAsAppEntryPersistenceStatus.RequiredEnforcementFailed, result.Status);
        Assert.Equal("ancestor failed", result.WarningMessage);
    }

    // ── TEST-10: RevertShortcuts + RemoveBesideTargetShortcut ordering ────

    [Fact]
    public void RevertAppChanges_ManageShortcuts_CallsRevertShortcutsAndRemoveBesideTarget()
    {
        // Verifies the CLAUDE.md invariant: RevertShortcuts and RemoveBesideTargetShortcut
        // must be called (via EnforcementHelper.RevertChanges) before any deletion from the DB.
        // In RevertAppChanges the app is still in the database during the revert call.
        var app = new AppEntry { Name = "MyApp", AccountSid = UserSid, ManageShortcuts = true };
        _database.Apps.Add(app);

        var callOrder = new List<string>();
        _shortcutService.Setup(s => s.RevertShortcuts(app, It.IsAny<ShortcutTraversalCache>()))
            .Callback(() => callOrder.Add("RevertShortcuts"));
        _besideTargetShortcutService.Setup(s => s.RemoveBesideTargetShortcut(app))
            .Callback(() => callOrder.Add("RemoveBesideTargetShortcut"));

        var manager = CreateManager();

        // Act
        var result = manager.RevertAppChanges(app, FullEnforcementChangeSet());

        // Assert: both shortcut operations called, RevertShortcuts before RemoveBesideTargetShortcut
        Assert.Equal(RunAsAppEntryPersistenceStatus.Succeeded, result.Status);
        Assert.Contains("RevertShortcuts", callOrder);
        Assert.Contains("RemoveBesideTargetShortcut", callOrder);
        Assert.True(callOrder.IndexOf("RevertShortcuts") < callOrder.IndexOf("RemoveBesideTargetShortcut"),
            "RevertShortcuts must be called before RemoveBesideTargetShortcut");
    }

    [Fact]
    public void RevertAppChanges_ManagedShortcutOnlyChange_DoesNotRemoveBesideTargetShortcut()
    {
        var app = new AppEntry { Name = "MyApp", AccountSid = UserSid, ManageShortcuts = true };
        _database.Apps.Add(app);

        var manager = CreateManager();
        var result = manager.RevertAppChanges(
            app,
            new AppEntryChangeSet(
                RequiresAclReapply: false,
                RequiresBesideTargetRefresh: false,
                RequiresHandlerSync: false,
                RequiresManagedShortcutRefresh: true,
                RequiresIconRefresh: false,
                ConfigSaveScope: AppEditConfigSaveScope.CurrentAppConfigOnly));

        Assert.Equal(RunAsAppEntryPersistenceStatus.Succeeded, result.Status);
        _shortcutService.Verify(s => s.RevertShortcuts(app, It.IsAny<ShortcutTraversalCache>()), Times.Once);
        _besideTargetShortcutService.Verify(s => s.RemoveBesideTargetShortcut(It.IsAny<AppEntry>()), Times.Never);
    }

    [Fact]
    public void ApplyAppChanges_ManagedShortcutOnlyChange_DoesNotApplyAcl()
    {
        var app = new AppEntry { Name = "MyApp", AccountSid = UserSid, ManageShortcuts = true };
        _database.Apps.Add(app);

        var manager = CreateManager();
        var result = manager.ApplyAppChanges(
            app,
            new AppEntryChangeSet(
                RequiresAclReapply: false,
                RequiresBesideTargetRefresh: false,
                RequiresHandlerSync: false,
                RequiresManagedShortcutRefresh: true,
                RequiresIconRefresh: false,
                ConfigSaveScope: AppEditConfigSaveScope.CurrentAppConfigOnly));

        Assert.Equal(RunAsAppEntryPersistenceStatus.Succeeded, result.Status);
        _aclService.Verify(s => s.ApplyAcl(It.IsAny<AppEntry>(), It.IsAny<IReadOnlyList<AppEntry>>()), Times.Never);
    }

    [Fact]
    public void ApplyAppChanges_ManagedShortcutOnlyChange_DoesNotRecreateIcon()
    {
        var app = new AppEntry { Id = "app1", Name = "MyApp", AccountSid = UserSid, ManageShortcuts = true };
        _database.Apps.Add(app);
        _iconService.Setup(s => s.GetIconPath(app.Id)).Returns(@"C:\icons\app.ico");

        var manager = CreateManager();
        var result = manager.ApplyAppChanges(
            app,
            new AppEntryChangeSet(
                RequiresAclReapply: false,
                RequiresBesideTargetRefresh: false,
                RequiresHandlerSync: false,
                RequiresManagedShortcutRefresh: true,
                RequiresIconRefresh: false,
                ConfigSaveScope: AppEditConfigSaveScope.CurrentAppConfigOnly));

        Assert.Equal(RunAsAppEntryPersistenceStatus.Succeeded, result.Status);
        _iconService.Verify(s => s.CreateBadgedIcon(It.IsAny<AppEntry>(), It.IsAny<string?>()), Times.Never);
        _iconService.Verify(s => s.GetIconPath(app.Id), Times.Once);
    }

    private static AppEntryChangeSet FullEnforcementChangeSet()
        => new(
            RequiresAclReapply: true,
            RequiresBesideTargetRefresh: true,
            RequiresHandlerSync: false,
            RequiresManagedShortcutRefresh: true,
            RequiresIconRefresh: true,
            ConfigSaveScope: AppEditConfigSaveScope.CurrentAppConfigOnly);
}
