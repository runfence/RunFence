using Moq;
using RunFence.Acl;
using RunFence.Apps;
using RunFence.Apps.Shortcuts;
using RunFence.Apps.UI;
using RunFence.Account;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public class ApplicationsCrudOperationServiceTests
{
    private const string AccountSid = "S-1-5-21-1000-1000-1000-1001";

    [Fact]
    public void ApplyChanges_PathOnlyChange_AppliesNewPathAclAndCreatesBesideTargetShortcutWithoutManagedShortcutRefresh()
    {
        var launcherPathProvider = new TestRunFenceLauncherPathProvider(@"C:\RunFence\RunFence.Launcher.exe", exists: true);
        var context = new RecordingContext(new AppDatabase());
        var aclService = new Mock<IAclService>(MockBehavior.Strict);
        var shortcutService = new Mock<IShortcutService>(MockBehavior.Strict);
        var besideTargetShortcutService = new Mock<IBesideTargetShortcutService>(MockBehavior.Strict);
        var iconService = new Mock<IIconService>(MockBehavior.Strict);
        var sidNameCache = new Mock<ISidNameCacheService>(MockBehavior.Strict);
        var recomputeInputs = new List<IReadOnlyList<AppEntry>>();

        var editedApp = new AppEntry
        {
            Id = "app01",
            Name = "App",
            ExePath = @"C:\apps\new.exe",
            AccountSid = AccountSid,
            RestrictAcl = true,
            ManageShortcuts = true
        };

        context.Database.Apps.Add(editedApp);

        IReadOnlyList<AppEntry>? applyApps = null;
        aclService.Setup(s => s.ApplyAcl(It.IsAny<AppEntry>(), It.IsAny<IReadOnlyList<AppEntry>>()))
            .Callback((AppEntry _, IReadOnlyList<AppEntry> apps) => applyApps = apps);
        aclService.Setup(s => s.RecomputeAllAncestorAcls(It.IsAny<IReadOnlyList<AppEntry>>()))
            .Callback<IReadOnlyList<AppEntry>>(apps => recomputeInputs.Add(apps.ToList()));

        iconService.Setup(s => s.CreateBadgedIcon(editedApp)).Returns(@"C:\icons\app.ico");
        sidNameCache.Setup(s => s.GetDisplayName(AccountSid)).Returns("LocalUser");

        besideTargetShortcutService.Setup(s => s.CreateBesideTargetShortcut(
                editedApp,
                launcherPathProvider.GetLauncherPath(),
                @"C:\icons\app.ico",
                "LocalUser"))
            .Verifiable();

        var operationService = new ApplicationsCrudOperationService(
            aclService.Object,
            AppEntryEnforcementTestFactory.CreateCoordinator(
                aclService.Object,
                shortcutService.Object,
                besideTargetShortcutService.Object,
                iconService.Object,
                sidNameCache.Object,
                new Mock<IInteractiveUserDesktopProvider>(MockBehavior.Strict).Object,
                Mock.Of<IInteractiveUserSidResolver>(),
                launcherPathProvider,
                Mock.Of<ILoggingService>()),
            Mock.Of<ILoggingService>());

        var result = operationService.ApplyChanges(
            context,
            editedApp,
            new ShortcutTraversalCache([]),
            new AppEntryChangeSet(
                RequiresAclReapply: true,
                RequiresBesideTargetRefresh: true,
                RequiresHandlerSync: false,
                RequiresManagedShortcutRefresh: false,
                RequiresIconRefresh: true,
                ConfigSaveScope: AppEditConfigSaveScope.CurrentAppConfigOnly),
            selectAppId: editedApp.Id);

        Assert.Equal(ApplicationsCrudOperationStatus.Succeeded, result.Status);
        Assert.Single(context.SaveCalls);
        Assert.True(context.SaveCalls.Single().targetedSave);
        Assert.NotNull(applyApps);
        Assert.Contains(editedApp, applyApps);
        Assert.Single(recomputeInputs);
        Assert.Single(recomputeInputs[0]);
        Assert.Contains(editedApp, recomputeInputs[0]);

        aclService.Verify(s => s.ApplyAcl(editedApp, It.IsAny<IReadOnlyList<AppEntry>>()), Times.Once);
        aclService.Verify(s => s.RecomputeAllAncestorAcls(It.IsAny<IReadOnlyList<AppEntry>>()), Times.Once);
        iconService.Verify(s => s.CreateBadgedIcon(editedApp), Times.Once);
        iconService.Verify(s => s.GetIconPath(editedApp.Id), Times.Never);
        shortcutService.Verify(
            s => s.ReplaceShortcuts(
                It.IsAny<AppEntry>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ShortcutTraversalCache>()),
            Times.Never);
        besideTargetShortcutService.Verify(s => s.CreateBesideTargetShortcut(
            editedApp,
            launcherPathProvider.GetLauncherPath(),
            @"C:\icons\app.ico",
            "LocalUser"),
            Times.Once);
    }

    [Fact]
    public void RestoreEnforcementAfterFailedEdit_PathOnly_UsesOldPathForReapplyAfterRevertingNewPathWithoutManagedShortcutRefresh()
    {
        var launcherPathProvider = new TestRunFenceLauncherPathProvider(@"C:\RunFence\RunFence.Launcher.exe", exists: true);
        var aclService = new Mock<IAclService>(MockBehavior.Strict);
        var shortcutService = new Mock<IShortcutService>(MockBehavior.Strict);
        var besideTargetShortcutService = new Mock<IBesideTargetShortcutService>(MockBehavior.Strict);
        var iconService = new Mock<IIconService>(MockBehavior.Strict);
        var sidNameCache = new Mock<ISidNameCacheService>(MockBehavior.Strict);
        var execution = new List<string>();
        var recomputeInputs = new List<IReadOnlyList<AppEntry>>();

            var previousApp = new AppEntry
            {
                Id = "app01",
                Name = "App",
                ExePath = @"C:\apps\old.exe",
                AccountSid = AccountSid,
                RestrictAcl = true,
                ManageShortcuts = true
            };
            var editedApp = previousApp.Clone();
            editedApp.ExePath = @"C:\apps\new.exe";

            aclService.Setup(s => s.ApplyAcl(previousApp, It.IsAny<IReadOnlyList<AppEntry>>()))
                .Callback(() => execution.Add("apply-old"));
            aclService.Setup(s => s.ApplyAcl(editedApp, It.IsAny<IReadOnlyList<AppEntry>>()))
                .Callback(() => execution.Add("apply-new"));
            aclService.Setup(s => s.RevertAcl(editedApp, It.IsAny<IReadOnlyList<AppEntry>>()))
                .Callback(() => execution.Add("revert-new"));
            aclService.Setup(s => s.RecomputeAllAncestorAcls(It.IsAny<IReadOnlyList<AppEntry>>()))
                .Callback<IReadOnlyList<AppEntry>>(apps => recomputeInputs.Add(apps.ToList()));

            iconService.Setup(s => s.CreateBadgedIcon(editedApp)).Returns(@"C:\icons\new.ico");
            iconService.Setup(s => s.CreateBadgedIcon(previousApp)).Returns(@"C:\icons\old.ico");
            sidNameCache.Setup(s => s.GetDisplayName(AccountSid)).Returns("LocalUser");

        besideTargetShortcutService.Setup(s => s.CreateBesideTargetShortcut(
            editedApp,
            launcherPathProvider.GetLauncherPath(),
            @"C:\icons\new.ico",
            "LocalUser")).Callback(() => execution.Add("create-beside-new"));
        besideTargetShortcutService.Setup(s => s.CreateBesideTargetShortcut(
            previousApp,
            launcherPathProvider.GetLauncherPath(),
            @"C:\icons\old.ico",
            "LocalUser")).Callback(() => execution.Add("create-beside-old"));
        besideTargetShortcutService.Setup(s => s.RemoveBesideTargetShortcut(It.IsAny<AppEntry>()))
            .Callback<AppEntry>(app => execution.Add($"remove-beside:{app.ExePath}"));

        var operationService = new ApplicationsCrudOperationService(
            aclService.Object,
            AppEntryEnforcementTestFactory.CreateCoordinator(
                aclService.Object,
                shortcutService.Object,
                besideTargetShortcutService.Object,
                iconService.Object,
                sidNameCache.Object,
                new Mock<IInteractiveUserDesktopProvider>(MockBehavior.Strict).Object,
                Mock.Of<IInteractiveUserSidResolver>(),
                launcherPathProvider,
                Mock.Of<ILoggingService>()),
            Mock.Of<ILoggingService>());

        var database = new AppDatabase();
        database.Apps.Add(editedApp);
        var context = new RecordingContext(database);
        var changeSet = new AppEntryChangeSet(
            RequiresAclReapply: true,
            RequiresBesideTargetRefresh: true,
            RequiresHandlerSync: false,
            RequiresManagedShortcutRefresh: false,
            RequiresIconRefresh: true,
            ConfigSaveScope: AppEditConfigSaveScope.CurrentAppConfigOnly);
        var shortcutCache = new ShortcutTraversalCache([]);

        var applyResult = operationService.ApplyChanges(
            context,
            editedApp,
            shortcutCache,
            changeSet,
            selectAppId: editedApp.Id);
        var revertResult = operationService.RevertChanges(
            context,
            editedApp,
            shortcutCache,
            changeSet);
        var restoreResult = operationService.RestoreEnforcementAfterFailedEdit(
            previousApp,
            new List<AppEntry> { previousApp },
            shortcutCache,
            changeSet);

        Assert.Equal(ApplicationsCrudOperationStatus.Succeeded, applyResult.Status);
        Assert.Equal(ApplicationsCrudOperationStatus.Succeeded, revertResult.Status);
        Assert.Equal(ApplicationsCrudOperationStatus.Succeeded, restoreResult.Status);
        Assert.Equal(3, recomputeInputs.Count);
        Assert.Contains(editedApp, recomputeInputs[0]);
        Assert.Empty(recomputeInputs[1]);
        Assert.Contains(previousApp, recomputeInputs[2]);

        aclService.Verify(s => s.ApplyAcl(editedApp, It.IsAny<IReadOnlyList<AppEntry>>()), Times.Once);
        aclService.Verify(s => s.ApplyAcl(previousApp, It.IsAny<IReadOnlyList<AppEntry>>()), Times.Once);
        aclService.Verify(s => s.RevertAcl(editedApp, It.IsAny<IReadOnlyList<AppEntry>>()), Times.Once);
        aclService.Verify(s => s.RecomputeAllAncestorAcls(It.IsAny<IReadOnlyList<AppEntry>>()), Times.Exactly(3));

        shortcutService.Verify(
            s => s.ReplaceShortcuts(
                It.IsAny<AppEntry>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ShortcutTraversalCache>()),
            Times.Never);

        var indexApplyNew = execution.IndexOf("apply-new");
        var indexCreateNew = execution.IndexOf("create-beside-new");
        var indexRevertNew = execution.IndexOf("revert-new");
        var indexRemoveNew = execution.IndexOf("remove-beside:C:\\apps\\new.exe");
        var indexApplyOld = execution.IndexOf("apply-old");
        var indexCreateOld = execution.IndexOf("create-beside-old");

        Assert.True(indexApplyNew >= 0);
        Assert.True(indexCreateNew >= 0);
        Assert.True(indexRevertNew >= 0);
        Assert.True(indexRemoveNew >= 0);
        Assert.True(indexApplyOld >= 0);
        Assert.True(indexCreateOld >= 0);

        Assert.True(indexApplyNew < indexCreateNew);
        Assert.True(indexCreateNew < indexRevertNew);
        Assert.True(indexRevertNew < indexRemoveNew);
        Assert.True(indexRemoveNew < indexApplyOld);
        Assert.True(indexApplyOld < indexCreateOld);

        iconService.Verify(s => s.CreateBadgedIcon(editedApp), Times.AtLeastOnce);
        iconService.Verify(s => s.CreateBadgedIcon(previousApp), Times.AtLeastOnce);
    }

    private sealed class RecordingContext(AppDatabase database) : IApplicationMutationContext
    {
        public AppDatabase Database { get; } = database;
        public List<(string? selectAppId, int fallbackIndex, bool targetedSave)> SaveCalls { get; } = [];

        public void SaveAndRefresh(string? selectAppId = null, int fallbackIndex = -1, bool targetedSave = false)
            => SaveCalls.Add((selectAppId, fallbackIndex, targetedSave));

        public void RefreshAfterInMemoryMutation(string? selectAppId = null, int fallbackIndex = -1)
        {
        }
    }
}
