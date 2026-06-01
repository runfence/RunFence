using Moq;
using RunFence.Account;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.RunAs;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public class RunAsAppShortcutCreatorTests : IDisposable
{
    private const string LauncherPath = @"C:\RunFence\RunFence.Launcher.exe";

    private readonly Mock<IIconService> _iconService = new();
    private readonly Mock<ISidNameCacheService> _sidNameCache = new();
    private readonly Mock<IShortcutService> _shortcutService = new();
    private readonly Mock<IBesideTargetShortcutService> _besideTargetShortcutService = new();
    private readonly Mock<IInteractiveUserSidResolver> _interactiveUserSidResolver = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly FakeLauncherPathProvider _launcherPathProvider = new(LauncherPath, exists: true);
    private readonly SecureSecret _pinKey = TestSecretFactory.Create(32);
    private readonly List<SessionContext> _sessions = [];

    [Fact]
    public void CreateBesideTargetShortcut_AppContainer_UsesResolvedInteractiveSid()
    {
        const string interactiveSid = "S-1-5-21-100-200-300-1000";
        var app = new AppEntry { Name = "ContainerApp", AppContainerName = "ram_browser" };
        _iconService.Setup(i => i.CreateBadgedIcon(app, null)).Returns(@"C:\icon.ico");
        _interactiveUserSidResolver.Setup(r => r.GetInteractiveUserSid()).Returns(interactiveSid);
        _sidNameCache.Setup(c => c.GetDisplayName(interactiveSid)).Returns("InteractiveUser");

        CreateCreator().CreateBesideTargetShortcut(app);

        _besideTargetShortcutService.Verify(
            s => s.CreateBesideTargetShortcut(app, LauncherPath, @"C:\icon.ico", "InteractiveUser"),
            Times.Once);
    }

    [Fact]
    public void CreateBesideTargetShortcut_AppContainer_NullInteractiveSidWarnsAndSkipsCreation()
    {
        var app = new AppEntry { Name = "ContainerApp", AppContainerName = "ram_browser" };
        _iconService.Setup(i => i.CreateBadgedIcon(app, null)).Returns(@"C:\icon.ico");
        _interactiveUserSidResolver.Setup(r => r.GetInteractiveUserSid()).Returns((string?)null);

        CreateCreator().CreateBesideTargetShortcut(app);

        _besideTargetShortcutService.Verify(
            s => s.CreateBesideTargetShortcut(
                It.IsAny<AppEntry>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public void CreateBesideTargetShortcut_AppContainer_EmptyInteractiveSidWarnsAndSkipsCreation()
    {
        var app = new AppEntry { Name = "ContainerApp", AppContainerName = "ram_browser" };
        _iconService.Setup(i => i.CreateBadgedIcon(app, null)).Returns(@"C:\icon.ico");
        _interactiveUserSidResolver.Setup(r => r.GetInteractiveUserSid()).Returns(string.Empty);

        CreateCreator().CreateBesideTargetShortcut(app);

        _besideTargetShortcutService.Verify(
            s => s.CreateBesideTargetShortcut(
                It.IsAny<AppEntry>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public void TryUpdateOriginalShortcut_UsesValidatedIconPathFromIconService()
    {
        using var tempDir = new TempDirectory("RunFence_RunAsShortcutCreator");
        var iconPath = Path.Combine(tempDir.Path, "icon.ico");
        File.WriteAllBytes(iconPath, []);

        _iconService.Setup(i => i.GetIconPath("app1")).Returns(iconPath);

        CreateCreator().TryUpdateOriginalShortcut(@"C:\Shortcuts\App.lnk", "app1");

        _shortcutService.Verify(s =>
            s.UpdateShortcutToLauncher(@"C:\Shortcuts\App.lnk", "app1", LauncherPath, iconPath),
            Times.Once);
    }

    private RunAsAppShortcutCreator CreateCreator()
    {
        var session = new SessionContext
{
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore(),
        }.WithClonedPinDerivedKey(_pinKey);
        _sessions.Add(session);

        return new RunAsAppShortcutCreator(
            _iconService.Object,
            _sidNameCache.Object,
            _shortcutService.Object,
            _besideTargetShortcutService.Object,
            new LambdaSessionProvider(() => session),
            _interactiveUserSidResolver.Object,
            _launcherPathProvider,
            _log.Object);
    }

    public void Dispose()
    {
        foreach (var session in _sessions)
        {
            session.Dispose();
        }

        _pinKey.Dispose();
    }

    private sealed class FakeLauncherPathProvider(string launcherPath, bool exists) : IRunFenceLauncherPathProvider
    {
        public string GetLauncherPath() => launcherPath;

        public bool Exists() => exists;
    }
}
