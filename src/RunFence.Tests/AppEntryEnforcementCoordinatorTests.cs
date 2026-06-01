using Moq;
using RunFence.Account;
using RunFence.Acl;
using RunFence.Apps;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public class AppEntryEnforcementCoordinatorTests
{
    [Fact]
    public void ApplyChanges_PathOnlyChange_AppliesAclAndCreatesBesideTargetWithoutManagedShortcutRefresh()
    {
        var aclService = new Mock<IAclService>(MockBehavior.Strict);
        var shortcutService = new Mock<IShortcutService>(MockBehavior.Strict);
        var besideTargetShortcutService = new Mock<IBesideTargetShortcutService>(MockBehavior.Strict);
        var iconService = new Mock<IIconService>(MockBehavior.Strict);
        var sidNameCache = new Mock<ISidNameCacheService>(MockBehavior.Strict);
        var launcherPathProvider = new TestRunFenceLauncherPathProvider(@"C:\RunFence\RunFence.Launcher.exe", exists: true);

        var app = new AppEntry
        {
            Id = "app01",
            Name = "App",
            ExePath = @"C:\apps\new.exe",
            AccountSid = "S-1-5-21-1000-1000-1000-1001",
            RestrictAcl = true,
            ManageShortcuts = true
        };

        aclService.Setup(s => s.ApplyAcl(app, It.IsAny<IReadOnlyList<AppEntry>>()));
        iconService.Setup(s => s.CreateBadgedIcon(app, null)).Returns(@"C:\icons\app.ico");
        sidNameCache.Setup(s => s.GetDisplayName(It.IsAny<string>())).Returns("LocalUser");
        besideTargetShortcutService.Setup(s => s.CreateBesideTargetShortcut(
            app,
            launcherPathProvider.GetLauncherPath(),
            @"C:\icons\app.ico",
            "LocalUser"));

        var coordinator = AppEntryEnforcementTestFactory.CreateCoordinator(
            aclService.Object,
            shortcutService.Object,
            besideTargetShortcutService.Object,
            iconService.Object,
            sidNameCache.Object,
            launcherPathProvider: launcherPathProvider);

        coordinator.ApplyTargetedChanges(
            app,
            new List<AppEntry> { app },
            new ShortcutTraversalCache([]),
            new AppEntryChangeSet(
                RequiresAclReapply: true,
                RequiresBesideTargetRefresh: true,
                RequiresHandlerSync: false,
                RequiresManagedShortcutRefresh: false,
                RequiresIconRefresh: true,
                ConfigSaveScope: AppEditConfigSaveScope.CurrentAppConfigOnly));

        aclService.Verify(s => s.ApplyAcl(app, It.IsAny<IReadOnlyList<AppEntry>>()), Times.Once);
        shortcutService.Verify(
            s => s.ReplaceShortcuts(It.IsAny<AppEntry>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ShortcutTraversalCache>()),
            Times.Never);
        besideTargetShortcutService.Verify(
            s => s.CreateBesideTargetShortcut(app, launcherPathProvider.GetLauncherPath(), @"C:\icons\app.ico", "LocalUser"),
            Times.Once);
    }

    [Fact]
    public void RevertTargetedChanges_ManagedShortcutOnly_DoesNotRemoveBesideTargetShortcut()
    {
        var shortcutService = new Mock<IShortcutService>();
        var besideTargetShortcutService = new Mock<IBesideTargetShortcutService>(MockBehavior.Strict);
        var coordinator = AppEntryEnforcementTestFactory.CreateCoordinator(
            Mock.Of<IAclService>(),
            shortcutService.Object,
            besideTargetShortcutService.Object,
            Mock.Of<IIconService>(),
            Mock.Of<ISidNameCacheService>());
        var app = new AppEntry { Name = "MyApp", ManageShortcuts = true };

        coordinator.RevertTargetedChanges(
            app,
            new List<AppEntry> { app },
            new ShortcutTraversalCache([]),
            new AppEntryChangeSet(
                RequiresAclReapply: false,
                RequiresBesideTargetRefresh: false,
                RequiresHandlerSync: false,
                RequiresManagedShortcutRefresh: true,
                RequiresIconRefresh: false,
                ConfigSaveScope: AppEditConfigSaveScope.CurrentAppConfigOnly));

        shortcutService.Verify(s => s.RevertShortcuts(app, It.IsAny<ShortcutTraversalCache>()), Times.Once);
        besideTargetShortcutService.Verify(s => s.RemoveBesideTargetShortcut(It.IsAny<AppEntry>()), Times.Never);
    }

    [Fact]
    public void ApplyRunAsChanges_DenyAclFailure_ReturnsConvenienceFailureWithoutShortcutOrRecompute()
    {
        var aclService = new Mock<IAclService>(MockBehavior.Strict);
        var coordinator = AppEntryEnforcementTestFactory.CreateCoordinator(
            aclService.Object,
            new Mock<IShortcutService>(MockBehavior.Strict).Object,
            new Mock<IBesideTargetShortcutService>(MockBehavior.Strict).Object,
            new Mock<IIconService>(MockBehavior.Strict).Object,
            new Mock<ISidNameCacheService>(MockBehavior.Strict).Object);
        var app = new AppEntry
        {
            Name = "DenyApp",
            RestrictAcl = true,
            AclMode = AclMode.Deny
        };

        aclService
            .Setup(service => service.ApplyAcl(app, It.IsAny<IReadOnlyList<AppEntry>>()))
            .Throws(new InvalidOperationException("acl failed"));

        var result = coordinator.ApplyRunAsChanges(
            app,
            [app],
            new ShortcutTraversalCache([]),
            new AppEntryChangeSet(
                RequiresAclReapply: true,
                RequiresBesideTargetRefresh: true,
                RequiresHandlerSync: false,
                RequiresManagedShortcutRefresh: true,
                RequiresIconRefresh: true,
                ConfigSaveScope: AppEditConfigSaveScope.CurrentAppConfigOnly));

        Assert.Equal(AppEntryEnforcementCoordinator.EnforcementFailureKind.Convenience, result.FailureKind);
        Assert.Equal("acl failed", result.Message);
        aclService.Verify(service => service.RecomputeAllAncestorAcls(It.IsAny<IReadOnlyList<AppEntry>>()), Times.Never);
    }

    [Fact]
    public void RevertRunAsChanges_RecomputesAfterShortcutCleanupUsingAppListWithoutRevertedApp()
    {
        var aclService = new Mock<IAclService>(MockBehavior.Strict);
        var shortcutService = new Mock<IShortcutService>(MockBehavior.Strict);
        var besideTargetShortcutService = new Mock<IBesideTargetShortcutService>(MockBehavior.Strict);
        var coordinator = AppEntryEnforcementTestFactory.CreateCoordinator(
            aclService.Object,
            shortcutService.Object,
            besideTargetShortcutService.Object,
            new Mock<IIconService>(MockBehavior.Strict).Object,
            new Mock<ISidNameCacheService>(MockBehavior.Strict).Object);
        var app = new AppEntry
        {
            Id = "app1",
            Name = "ManagedApp",
            RestrictAcl = true,
            ManageShortcuts = true
        };
        var otherApp = new AppEntry { Id = "app2", Name = "OtherApp" };
        var callOrder = new List<string>();

        aclService
            .Setup(service => service.RevertAcl(app, It.IsAny<IReadOnlyList<AppEntry>>()))
            .Callback(() => callOrder.Add("revert-acl"));
        shortcutService
            .Setup(service => service.RevertShortcuts(app, It.IsAny<ShortcutTraversalCache>()))
            .Callback(() => callOrder.Add("revert-shortcuts"));
        besideTargetShortcutService
            .Setup(service => service.RemoveBesideTargetShortcut(app))
            .Callback(() => callOrder.Add("remove-beside-target"));
        aclService
            .Setup(service => service.RecomputeAllAncestorAcls(It.IsAny<IReadOnlyList<AppEntry>>()))
            .Callback<IReadOnlyList<AppEntry>>(apps =>
            {
                callOrder.Add("recompute");
                Assert.Single(apps);
                Assert.Same(otherApp, apps[0]);
            });

        var result = coordinator.RevertRunAsChanges(
            app,
            [app, otherApp],
            new ShortcutTraversalCache([]),
            new AppEntryChangeSet(
                RequiresAclReapply: true,
                RequiresBesideTargetRefresh: true,
                RequiresHandlerSync: false,
                RequiresManagedShortcutRefresh: true,
                RequiresIconRefresh: false,
                ConfigSaveScope: AppEditConfigSaveScope.CurrentAppConfigOnly));

        Assert.True(result.Succeeded);
        Assert.Equal(["revert-acl", "revert-shortcuts", "remove-beside-target", "recompute"], callOrder);
    }

    [Fact]
    public void ApplyWizardChanges_ManagedShortcutFailureStillCreatesDesktopShortcutBeforeAncestorRecompute()
    {
        var aclService = new Mock<IAclService>(MockBehavior.Strict);
        var shortcutService = new Mock<IShortcutService>(MockBehavior.Strict);
        var iconService = new Mock<IIconService>(MockBehavior.Strict);
        var sidNameCache = new Mock<ISidNameCacheService>(MockBehavior.Strict);
        var desktopProvider = new Mock<IInteractiveUserDesktopProvider>(MockBehavior.Strict);
        var coordinator = AppEntryEnforcementTestFactory.CreateCoordinator(
            aclService.Object,
            shortcutService.Object,
            new Mock<IBesideTargetShortcutService>(MockBehavior.Strict).Object,
            iconService.Object,
            sidNameCache.Object,
            desktopProvider.Object,
            new Mock<IInteractiveUserSidResolver>(MockBehavior.Strict).Object,
            new TestRunFenceLauncherPathProvider(@"C:\RunFence\RunFence.Launcher.exe", exists: true),
            new Mock<ILoggingService>(MockBehavior.Strict).Object);
        var app = new AppEntry
        {
            Id = "app1",
            Name = "WizardApp",
            ExePath = @"C:\Apps\wizard.exe",
            AccountSid = "S-1-5-21-1",
            RestrictAcl = true,
            ManageShortcuts = true
        };
        var events = new List<string>();

        aclService
            .Setup(service => service.ApplyAcl(app, It.IsAny<IReadOnlyList<AppEntry>>()))
            .Callback(() => events.Add("apply-acl"));
        iconService
            .Setup(service => service.CreateBadgedIcon(app, null))
            .Callback(() => events.Add("create-icon"))
            .Returns(@"C:\icons\wizard.ico");
        shortcutService
            .Setup(service => service.ReplaceShortcuts(app, It.IsAny<string>(), @"C:\icons\wizard.ico", It.IsAny<ShortcutTraversalCache>()))
            .Callback(() => events.Add("replace-shortcuts"))
            .Throws(new InvalidOperationException("replace failed"));
        sidNameCache
            .Setup(service => service.GetDisplayName(app.AccountSid))
            .Returns(app.AccountSid);
        desktopProvider
            .Setup(provider => provider.GetDesktopPath())
            .Returns(@"C:\Users\Test\Desktop");
        shortcutService
            .Setup(service => service.SaveShortcut(app, @"C:\Users\Test\Desktop\WizardApp.lnk"))
            .Callback(() => events.Add("create-desktop-shortcut"));
        aclService
            .Setup(service => service.RecomputeAllAncestorAcls(It.IsAny<IReadOnlyList<AppEntry>>()))
            .Callback(() => events.Add("recompute"));

        var result = coordinator.ApplyWizardChanges(
            [app],
            [app],
            _ => new ShortcutTraversalCache([]),
            createDesktopShortcut: true);

        Assert.Equal(
            ["apply-acl", "create-icon", "replace-shortcuts", "create-desktop-shortcut", "recompute"],
            events);
        var failure = Assert.Single(result.AppFailures);
        Assert.Same(app, failure.App);
        Assert.Equal("replace failed", failure.Exception.Message);
        Assert.Null(result.AncestorAclRecomputeFailure);
    }
}
