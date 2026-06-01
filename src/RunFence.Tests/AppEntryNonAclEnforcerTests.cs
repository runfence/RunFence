using Moq;
using RunFence.Account;
using RunFence.Apps;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public class AppEntryNonAclEnforcerTests
{
    private readonly Mock<IShortcutService> _shortcutService = new();
    private readonly Mock<IBesideTargetShortcutService> _besideTargetShortcutService = new();
    private readonly Mock<IIconService> _iconService = new();
    private readonly Mock<ISidNameCacheService> _sidNameCache = new();
    private readonly Mock<IInteractiveUserDesktopProvider> _desktopProvider = new();
    private readonly Mock<IInteractiveUserSidResolver> _interactiveUserSidResolver = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly TestRunFenceLauncherPathProvider _launcherPathProvider = new(
        @"C:\RunFence\RunFence.Launcher.exe",
        exists: true);

    private AppEntryNonAclEnforcer CreateEnforcer()
        => new(
            _shortcutService.Object,
            _besideTargetShortcutService.Object,
            _iconService.Object,
            _sidNameCache.Object,
            _desktopProvider.Object,
            _interactiveUserSidResolver.Object,
            _launcherPathProvider,
            _log.Object);

    [Fact]
    public void ApplyAll_FolderApp_SetsLastKnownExeTimestampNull()
    {
        var app = new AppEntry
        {
            Name = "FolderApp",
            IsFolder = true,
            ExePath = @"C:\Tools",
            LastKnownExeTimestamp = DateTime.UtcNow
        };
        _iconService.Setup(i => i.CreateBadgedIcon(app, null)).Returns(string.Empty);

        CreateEnforcer().ApplyAll(app, new ShortcutTraversalCache([]));

        Assert.Null(app.LastKnownExeTimestamp);
    }

    [Fact]
    public void ApplyTargeted_ManagedShortcutOnly_UsesExistingIconPathWithoutRecreatingIcon()
    {
        var app = new AppEntry { Id = "app1", Name = "MyApp", ManageShortcuts = true };
        _iconService.Setup(s => s.GetIconPath(app.Id)).Returns(@"C:\icons\app.ico");

        CreateEnforcer().ApplyTargeted(
            app,
            new ShortcutTraversalCache([]),
            new AppEntryChangeSet(
                RequiresAclReapply: false,
                RequiresBesideTargetRefresh: false,
                RequiresHandlerSync: false,
                RequiresManagedShortcutRefresh: true,
                RequiresIconRefresh: false,
                ConfigSaveScope: AppEditConfigSaveScope.CurrentAppConfigOnly));

        _iconService.Verify(s => s.CreateBadgedIcon(It.IsAny<AppEntry>(), It.IsAny<string?>()), Times.Never);
        _iconService.Verify(s => s.GetIconPath(app.Id), Times.Once);
    }

    [Fact]
    public void ApplyAll_AppContainer_UsesResolvedInteractiveSidForBesideTargetShortcut()
    {
        var app = new AppEntry
        {
            Name = "ContainerApp",
            AppContainerName = "rfn_testapp",
            AccountSid = "S-1-5-21-999-999-999-1001"
        };
        _iconService.Setup(i => i.CreateBadgedIcon(app, null)).Returns(@"C:\icon.ico");
        _interactiveUserSidResolver.Setup(r => r.GetInteractiveUserSid()).Returns("S-1-5-21-100-200-300-1000");
        _sidNameCache.Setup(c => c.GetDisplayName("S-1-5-21-100-200-300-1000")).Returns("InteractiveUser");

        CreateEnforcer().ApplyAll(app, new ShortcutTraversalCache([]));

        _besideTargetShortcutService.Verify(
            s => s.CreateBesideTargetShortcut(app, _launcherPathProvider.GetLauncherPath(), @"C:\icon.ico", "InteractiveUser"),
            Times.Once);
    }

    [Fact]
    public void ApplyAll_AppContainerWithoutInteractiveSid_WarnsAndSkipsBesideTargetShortcut()
    {
        var app = new AppEntry
        {
            Name = "ContainerApp",
            AppContainerName = "rfn_testapp",
            AccountSid = "S-1-5-21-999-999-999-1001"
        };
        _iconService.Setup(i => i.CreateBadgedIcon(app, null)).Returns(@"C:\icon.ico");

        CreateEnforcer().ApplyAll(app, new ShortcutTraversalCache([]));

        _besideTargetShortcutService.Verify(
            s => s.CreateBesideTargetShortcut(It.IsAny<AppEntry>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public void CreateDesktopShortcut_NormalApp_CallsSaveShortcut()
    {
        var app = new AppEntry { Name = "MyApp", IsUrlScheme = false };
        var desktopPath = Path.Combine(Path.GetTempPath(), "TestDesktop");
        _desktopProvider.Setup(d => d.GetDesktopPath()).Returns(desktopPath);

        CreateEnforcer().CreateDesktopShortcut(app);

        _shortcutService.Verify(
            s => s.SaveShortcut(app, Path.Combine(desktopPath, "MyApp.lnk")),
            Times.Once);
    }
}
