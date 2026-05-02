using Moq;
using RunFence.Account;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.RunAs;
using Xunit;

namespace RunFence.Tests;

public class RunAsAppShortcutCreatorTests
{
    private readonly Mock<IIconService> _iconService = new();
    private readonly Mock<ISidNameCacheService> _sidNameCache = new();
    private readonly Mock<IShortcutService> _shortcutService = new();
    private readonly Mock<IBesideTargetShortcutService> _besideTargetShortcutService = new();
    private readonly Mock<IInteractiveUserSidResolver> _interactiveUserSidResolver = new();
    private readonly Mock<ILoggingService> _log = new();

    [Fact]
    public void CreateBesideTargetShortcut_AppContainer_UsesResolvedInteractiveSid()
    {
        const string interactiveSid = "S-1-5-21-100-200-300-1000";
        var app = new AppEntry { Name = "ContainerApp", AppContainerName = "ram_browser" };
        _iconService.Setup(i => i.CreateBadgedIcon(app, null)).Returns(@"C:\icon.ico");
        _interactiveUserSidResolver.Setup(r => r.GetInteractiveUserSid()).Returns(interactiveSid);
        _sidNameCache.Setup(c => c.GetDisplayName(interactiveSid)).Returns("InteractiveUser");

        RunWithLauncherPresent(() => CreateCreator().CreateBesideTargetShortcut(app));

        _besideTargetShortcutService.Verify(
            s => s.CreateBesideTargetShortcut(app, It.IsAny<string>(), @"C:\icon.ico", "InteractiveUser"),
            Times.Once);
    }

    [Fact]
    public void CreateBesideTargetShortcut_AppContainer_NullInteractiveSidWarnsAndSkipsCreation()
    {
        var app = new AppEntry { Name = "ContainerApp", AppContainerName = "ram_browser" };
        _iconService.Setup(i => i.CreateBadgedIcon(app, null)).Returns(@"C:\icon.ico");
        _interactiveUserSidResolver.Setup(r => r.GetInteractiveUserSid()).Returns((string?)null);

        RunWithLauncherPresent(() => CreateCreator().CreateBesideTargetShortcut(app));

        _besideTargetShortcutService.Verify(
            s => s.CreateBesideTargetShortcut(
                It.IsAny<AppEntry>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
        _log.Verify(
            l => l.Warn("RunAsAppShortcutCreator: interactive user SID unavailable; skipping AppContainer beside-target shortcut for 'ContainerApp'."),
            Times.Once);
    }

    [Fact]
    public void CreateBesideTargetShortcut_AppContainer_EmptyInteractiveSidWarnsAndSkipsCreation()
    {
        var app = new AppEntry { Name = "ContainerApp", AppContainerName = "ram_browser" };
        _iconService.Setup(i => i.CreateBadgedIcon(app, null)).Returns(@"C:\icon.ico");
        _interactiveUserSidResolver.Setup(r => r.GetInteractiveUserSid()).Returns(string.Empty);

        RunWithLauncherPresent(() => CreateCreator().CreateBesideTargetShortcut(app));

        _besideTargetShortcutService.Verify(
            s => s.CreateBesideTargetShortcut(
                It.IsAny<AppEntry>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
        _log.Verify(
            l => l.Warn("RunAsAppShortcutCreator: interactive user SID unavailable; skipping AppContainer beside-target shortcut for 'ContainerApp'."),
            Times.Once);
    }

    private RunAsAppShortcutCreator CreateCreator()
    {
        var session = new SessionContext
        {
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore()
        };
        return new RunAsAppShortcutCreator(
            _iconService.Object,
            _sidNameCache.Object,
            _shortcutService.Object,
            _besideTargetShortcutService.Object,
            new LambdaSessionProvider(() => session),
            _interactiveUserSidResolver.Object,
            _log.Object);
    }

    private static void RunWithLauncherPresent(Action action)
    {
        var launcherPath = Path.Combine(AppContext.BaseDirectory, PathConstants.LauncherExeName);
        var existed = File.Exists(launcherPath);
        if (!existed)
            File.WriteAllBytes(launcherPath, []);
        try
        {
            action();
        }
        finally
        {
            if (!existed)
                File.Delete(launcherPath);
        }
    }
}
