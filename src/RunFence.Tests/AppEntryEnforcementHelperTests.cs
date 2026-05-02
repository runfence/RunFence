using Moq;
using RunFence.Acl;
using RunFence.Account;
using RunFence.Apps;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Tests for AppEntryEnforcementHelper, including the ordering invariant relied on by
/// AccountContainerOrchestrator.DeleteContainer (T1: container delete reverts ACLs before removal).
/// </summary>
public class AppEntryEnforcementHelperTests
{
    private readonly Mock<IAclService> _aclService = new();
    private readonly Mock<IShortcutService> _shortcutService = new();
    private readonly Mock<IBesideTargetShortcutService> _besideTargetShortcutService = new();
    private readonly Mock<IIconService> _iconService = new();
    private readonly Mock<ISidNameCacheService> _sidNameCache = new();
    private readonly Mock<IInteractiveUserSidResolver> _interactiveUserSidResolver = new();
    private readonly AppEntryEnforcementHelper _helper;

    public AppEntryEnforcementHelperTests()
    {
        _helper = new AppEntryEnforcementHelper(_aclService.Object, _shortcutService.Object,
            _besideTargetShortcutService.Object, _iconService.Object, _sidNameCache.Object,
            new Mock<IInteractiveUserDesktopProvider>().Object, _interactiveUserSidResolver.Object,
            new Mock<ILoggingService>().Object);
    }

    private static ShortcutTraversalCache Cache() => new([]);

    // --- T1: Container delete ordering invariant ---

    [Fact]
    public void RevertChanges_AppInAllApps_RevertAclReceivesListContainingApp()
    {
        // Verifies the ordering invariant: AccountContainerOrchestrator.DeleteContainer calls
        // RevertChanges(app, db.Apps) BEFORE CleanupContainerFromAppData removes the app.
        // At revert time, db.Apps still contains the app — this test confirms AclService
        // receives the full list including the target app, as required by RevertAcl.

        // Arrange
        var app = new AppEntry { Id = "cnt01", Name = "ContainerApp", RestrictAcl = true, ExePath = @"C:\test.exe" };
        var otherApp = new AppEntry { Id = "oth01", Name = "OtherApp" };
        var allApps = new List<AppEntry> { app, otherApp }; // app still in list — correct ordering

        IReadOnlyList<AppEntry>? capturedAllApps = null;
        _aclService
            .Setup(a => a.RevertAcl(app, It.IsAny<IReadOnlyList<AppEntry>>()))
            .Callback<AppEntry, IReadOnlyList<AppEntry>>((_, apps) => capturedAllApps = apps);

        // Act — RevertChanges called while app is in allApps (before database cleanup)
        _helper.RevertChanges(app, allApps, Cache());

        // Assert: RevertAcl received allApps containing the target app
        Assert.NotNull(capturedAllApps);
        Assert.Contains(app, capturedAllApps);
        _aclService.Verify(a => a.RevertAcl(app, allApps), Times.Once);
    }

    // --- RevertChanges behavior ---

    [Fact]
    public void RevertChanges_NoRestrictAcl_SkipsAclRevert()
    {
        var app = new AppEntry { Name = "App", RestrictAcl = false, ManageShortcuts = false };
        _helper.RevertChanges(app, new List<AppEntry> { app }, Cache());
        _aclService.Verify(a => a.RevertAcl(It.IsAny<AppEntry>(), It.IsAny<IReadOnlyList<AppEntry>>()), Times.Never);
    }

    [Fact]
    public void RevertChanges_UrlSchemeApp_SkipsAclRevertAndShortcuts()
    {
        var app = new AppEntry { Name = "UrlApp", IsUrlScheme = true, RestrictAcl = true, ManageShortcuts = true };
        _helper.RevertChanges(app, new List<AppEntry> { app }, Cache());
        _aclService.Verify(a => a.RevertAcl(It.IsAny<AppEntry>(), It.IsAny<IReadOnlyList<AppEntry>>()), Times.Never);
        _besideTargetShortcutService.Verify(s => s.RemoveBesideTargetShortcut(It.IsAny<AppEntry>()), Times.Never);
    }

    [Fact]
    public void RevertChanges_ManageShortcutsTrue_RevokesShortcuts()
    {
        var app = new AppEntry { Name = "ShortcutApp", RestrictAcl = false, ManageShortcuts = true };
        var cache = Cache();
        _helper.RevertChanges(app, new List<AppEntry>(), cache);
        _shortcutService.Verify(s => s.RevertShortcuts(app, cache), Times.Once);
    }

    [Fact]
    public void RevertChanges_NonUrlApp_RemovesBesideTargetShortcut()
    {
        var app = new AppEntry { Name = "App", IsUrlScheme = false, RestrictAcl = false, ManageShortcuts = false };
        _helper.RevertChanges(app, new List<AppEntry>(), Cache());
        _besideTargetShortcutService.Verify(s => s.RemoveBesideTargetShortcut(app), Times.Once);
    }

    // --- ApplyChanges behavior ---

    [Fact]
    public void ApplyChanges_RestrictAclTrue_CallsApplyAcl()
    {
        var app = new AppEntry { Name = "App", RestrictAcl = true, ExePath = @"C:\app.exe" };
        var allApps = new List<AppEntry> { app };
        _iconService.Setup(i => i.CreateBadgedIcon(app, null)).Returns(string.Empty);

        _helper.ApplyChanges(app, allApps, Cache());

        _aclService.Verify(a => a.ApplyAcl(app, allApps), Times.Once);
    }

    [Fact]
    public void ApplyChanges_RestrictAclFalse_SkipsApplyAcl()
    {
        var app = new AppEntry { Name = "App", RestrictAcl = false, ExePath = @"C:\app.exe" };
        _iconService.Setup(i => i.CreateBadgedIcon(app, null)).Returns(string.Empty);

        _helper.ApplyChanges(app, new List<AppEntry> { app }, Cache());

        _aclService.Verify(a => a.ApplyAcl(It.IsAny<AppEntry>(), It.IsAny<IReadOnlyList<AppEntry>>()), Times.Never);
    }

    [Fact]
    public void ApplyChanges_UrlSchemeApp_SkipsAcl()
    {
        // URL scheme apps skip ACL even when RestrictAcl = true
        var app = new AppEntry { Name = "UrlApp", IsUrlScheme = true, RestrictAcl = true, ManageShortcuts = false };

        _helper.ApplyChanges(app, new List<AppEntry> { app }, Cache());

        _aclService.Verify(a => a.ApplyAcl(It.IsAny<AppEntry>(), It.IsAny<IReadOnlyList<AppEntry>>()), Times.Never);
        // Icon not created (ManageShortcuts = false and IsUrlScheme = true → condition is false)
        _iconService.Verify(i => i.CreateBadgedIcon(It.IsAny<AppEntry>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void ApplyChanges_ManageShortcutsTrue_IconIsCreated()
    {
        var app = new AppEntry { Name = "App", ManageShortcuts = true, ExePath = @"C:\app.exe" };
        _iconService.Setup(i => i.CreateBadgedIcon(app, null)).Returns(@"C:\icon.ico");

        _helper.ApplyChanges(app, new List<AppEntry> { app }, Cache());

        // ReplaceShortcuts is only called when the launcher exe exists on disk; in tests it
        // does not exist, so the call is skipped — but the icon IS created.
        _iconService.Verify(i => i.CreateBadgedIcon(app, null), Times.Once);
    }

    [Fact]
    public void ApplyChanges_ManageShortcutsFalse_DoesNotCallReplaceShortcuts()
    {
        var app = new AppEntry { Name = "App", ManageShortcuts = false, ExePath = @"C:\app.exe" };
        _iconService.Setup(i => i.CreateBadgedIcon(app, null)).Returns(string.Empty);

        _helper.ApplyChanges(app, new List<AppEntry> { app }, Cache());

        _shortcutService.Verify(s => s.ReplaceShortcuts(
            It.IsAny<AppEntry>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<ShortcutTraversalCache>()), Times.Never);
    }

    [Fact]
    public void ApplyChanges_FolderApp_SetsLastKnownExeTimestampNull()
    {
        // Folder apps must never have a timestamp — IsFolder forces it to null
        var app = new AppEntry
        {
            Name = "FolderApp",
            IsFolder = true,
            ExePath = @"C:\Tools",
            LastKnownExeTimestamp = DateTime.UtcNow // pre-set to a non-null value
        };
        _iconService.Setup(i => i.CreateBadgedIcon(app, null)).Returns(string.Empty);

        _helper.ApplyChanges(app, new List<AppEntry> { app }, Cache());

        Assert.Null(app.LastKnownExeTimestamp);
    }

    // --- F-79: CreateDesktopShortcut ---

    [Fact]
    public void CreateDesktopShortcut_UrlSchemeApp_Skip()
    {
        // URL scheme apps cannot have desktop shortcuts — method returns early
        var app = new AppEntry { Name = "UrlApp", IsUrlScheme = true };

        _helper.CreateDesktopShortcut(app);

        _shortcutService.Verify(s => s.SaveShortcut(It.IsAny<AppEntry>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void CreateDesktopShortcut_NullDesktopPath_Skip()
    {
        // When the interactive user's desktop is unavailable, no shortcut is created.
        // _helper is constructed with a default Mock<IInteractiveUserDesktopProvider> whose
        // GetDesktopPath() returns null — desktop unavailable path is taken.
        var app = new AppEntry { Name = "App", IsUrlScheme = false };

        _helper.CreateDesktopShortcut(app);

        _shortcutService.Verify(s => s.SaveShortcut(It.IsAny<AppEntry>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void CreateDesktopShortcut_NormalApp_CallsSaveShortcut()
    {
        // When desktop path is available and app is not URL scheme, SaveShortcut is called
        var app = new AppEntry { Name = "MyApp", IsUrlScheme = false };
        var desktopPath = Path.Combine(Path.GetTempPath(), "TestDesktop");

        // Create a local helper instance with a desktop provider that returns a real path
        var desktopProvider = new Mock<IInteractiveUserDesktopProvider>();
        desktopProvider.Setup(d => d.GetDesktopPath()).Returns(desktopPath);

        var helper = new AppEntryEnforcementHelper(
            _aclService.Object, _shortcutService.Object,
            _besideTargetShortcutService.Object, _iconService.Object, _sidNameCache.Object,
            desktopProvider.Object, _interactiveUserSidResolver.Object, new Mock<ILoggingService>().Object);

        helper.CreateDesktopShortcut(app);

        _shortcutService.Verify(
            s => s.SaveShortcut(app, Path.Combine(desktopPath, "MyApp.lnk")),
            Times.Once);
    }

    // --- F-80: ApplyChanges AppContainer beside-target shortcut with resolved interactive SID ---

    [Fact]
    public void ApplyChanges_AppContainerApp_UsesResolvedInteractiveSidForBesideTargetShortcut()
    {
        var app = new AppEntry
        {
            Name = "ContainerApp",
            AppContainerName = "rfn_testapp",
            AccountSid = "S-1-5-21-999-999-999-1001",
            ManageShortcuts = false,
            RestrictAcl = false
        };
        _iconService.Setup(i => i.CreateBadgedIcon(app, null)).Returns(@"C:\icon.ico");
        _interactiveUserSidResolver.Setup(r => r.GetInteractiveUserSid()).Returns("S-1-5-21-100-200-300-1000");
        _sidNameCache.Setup(c => c.GetDisplayName("S-1-5-21-100-200-300-1000")).Returns("InteractiveUser");

        _helper.ApplyChanges(app, new List<AppEntry> { app }, Cache());

        _besideTargetShortcutService.Verify(
            s => s.CreateBesideTargetShortcut(app, It.IsAny<string>(), @"C:\icon.ico", "InteractiveUser"),
            Times.Once);
    }

    [Fact]
    public void ApplyChanges_AppContainerApp_NullInteractiveSidWarnsAndSkipsBesideTargetShortcut()
    {
        var app = new AppEntry
        {
            Name = "ContainerApp",
            AppContainerName = "rfn_testapp",
            AccountSid = "S-1-5-21-999-999-999-1001",
            ManageShortcuts = false,
            RestrictAcl = false
        };
        _iconService.Setup(i => i.CreateBadgedIcon(app, null)).Returns(@"C:\icon.ico");

        var logMock = new Mock<ILoggingService>();
        var helper = new AppEntryEnforcementHelper(
            _aclService.Object, _shortcutService.Object,
            _besideTargetShortcutService.Object, _iconService.Object, _sidNameCache.Object,
            new Mock<IInteractiveUserDesktopProvider>().Object, _interactiveUserSidResolver.Object,
            logMock.Object);

        helper.ApplyChanges(app, new List<AppEntry> { app }, Cache());

        _besideTargetShortcutService.Verify(
            s => s.CreateBesideTargetShortcut(
                It.IsAny<AppEntry>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
        logMock.Verify(
            l => l.Warn("AppEntryEnforcementHelper: interactive user SID unavailable; skipping AppContainer beside-target shortcut for 'ContainerApp'."),
            Times.Once);
    }

    [Fact]
    public void ApplyChanges_AppContainerApp_EmptyInteractiveSidWarnsAndSkipsBesideTargetShortcut()
    {
        var app = new AppEntry
        {
            Name = "ContainerApp",
            AppContainerName = "rfn_testapp",
            AccountSid = "S-1-5-21-999-999-999-1001",
            ManageShortcuts = false,
            RestrictAcl = false
        };
        _iconService.Setup(i => i.CreateBadgedIcon(app, null)).Returns(@"C:\icon.ico");
        _interactiveUserSidResolver.Setup(r => r.GetInteractiveUserSid()).Returns(string.Empty);

        var logMock = new Mock<ILoggingService>();
        var helper = new AppEntryEnforcementHelper(
            _aclService.Object, _shortcutService.Object,
            _besideTargetShortcutService.Object, _iconService.Object, _sidNameCache.Object,
            new Mock<IInteractiveUserDesktopProvider>().Object, _interactiveUserSidResolver.Object,
            logMock.Object);

        helper.ApplyChanges(app, new List<AppEntry> { app }, Cache());

        _besideTargetShortcutService.Verify(
            s => s.CreateBesideTargetShortcut(
                It.IsAny<AppEntry>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
        logMock.Verify(
            l => l.Warn("AppEntryEnforcementHelper: interactive user SID unavailable; skipping AppContainer beside-target shortcut for 'ContainerApp'."),
            Times.Once);
    }
}
