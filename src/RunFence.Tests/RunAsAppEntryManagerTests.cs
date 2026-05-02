using Moq;
using RunFence.Acl;
using RunFence.Account;
using RunFence.Apps;
using RunFence.Apps.Shortcuts;
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

    private AppEntryEnforcementHelper CreateEnforcementHelper()
        => new(_aclService.Object, _shortcutService.Object, _besideTargetShortcutService.Object,
            _iconService.Object, _sidNameCache.Object,
            new Mock<IInteractiveUserDesktopProvider>().Object, new Mock<IInteractiveUserSidResolver>().Object,
            new Mock<ILoggingService>().Object);

    private RunAsAppEntryManager CreateManager()
        => new(
            _appState.Object,
            _log.Object,
            _aclService.Object,
            CreateEnforcementHelper(),
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
        manager.RevertAppChanges(app);

        // Assert: both shortcut operations called, RevertShortcuts before RemoveBesideTargetShortcut
        Assert.Contains("RevertShortcuts", callOrder);
        Assert.Contains("RemoveBesideTargetShortcut", callOrder);
        Assert.True(callOrder.IndexOf("RevertShortcuts") < callOrder.IndexOf("RemoveBesideTargetShortcut"),
            "RevertShortcuts must be called before RemoveBesideTargetShortcut");
    }
}
