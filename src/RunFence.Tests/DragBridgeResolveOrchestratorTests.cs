using System.Security.Principal;
using Moq;
using RunFence.Acl;
using RunFence.Acl.QuickAccess;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.DragBridge;
using RunFence.Infrastructure;
using RunFence.Launch.Tokens;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public class DragBridgeResolveOrchestratorTests
{
    private static readonly SecurityIdentifier TargetSid = new("S-1-5-21-111-222-333-1001");

    [Fact]
    public async Task CreateResolveDelegate_DurableGrantSaved_PinsGrantedPathsAndReturnsResolvedPaths()
    {
        var pasteHandler = new Mock<IDragBridgePasteHandler>();
        var quickAccessPinService = new Mock<IQuickAccessPinService>();
        var notifications = new Mock<INotificationService>();
        var uiThreadInvoker = new Mock<IUiThreadInvoker>();
        uiThreadInvoker.Setup(invoker => invoker.Invoke(It.IsAny<Action>()))
            .Callback<Action>(action => action());
        var pathGrantService = new Mock<IGrantMutatorService>();
        var traverseService = pathGrantService.As<ITraverseService>();
        var resolvedPaths = new List<string> { @"C:\resolved.txt" };
        var grantedPaths = new List<string> { @"C:\source" };
        pasteHandler.Setup(handler => handler.ResolveFileAccessAsync(
                TargetSid,
                null,
                It.IsAny<List<string>>(),
                "S-1-5-21-111-222-333-1002",
                null,
                It.IsAny<AppDatabase?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DragBridgeResolveResult(resolvedPaths, DatabaseModified: true, grantedPaths, RollbackPlan: null)
            {
                DurableSaveCompleted = true
            });
        var orchestrator = new DragBridgeResolveOrchestrator(
            pasteHandler.Object,
            quickAccessPinService.Object,
            uiThreadInvoker.Object,
            notifications.Object,
            pathGrantService.Object,
            traverseService.Object);

        var resolve = orchestrator.CreateResolveDelegate(
            new WindowOwnerInfo(TargetSid, NativeTokenHelper.MandatoryLevelMedium, false),
            [@"C:\captured.txt"],
            "S-1-5-21-111-222-333-1002",
            null,
            new AppDatabase());

        var result = await resolve(CancellationToken.None);

        Assert.Equal(resolvedPaths, result);
        quickAccessPinService.Verify(service => service.PinFolders(TargetSid.Value, grantedPaths), Times.Once);
        pathGrantService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateResolveDelegate_AclReappliedWithoutDatabaseChange_StillPinsGrantedPaths()
    {
        var pasteHandler = new Mock<IDragBridgePasteHandler>();
        var quickAccessPinService = new Mock<IQuickAccessPinService>();
        var notifications = new Mock<INotificationService>();
        var uiThreadInvoker = new Mock<IUiThreadInvoker>();
        uiThreadInvoker.Setup(invoker => invoker.Invoke(It.IsAny<Action>()))
            .Callback<Action>(action => action());
        var pathGrantService = new Mock<IGrantMutatorService>();
        var traverseService = pathGrantService.As<ITraverseService>();
        var grantedPaths = new List<string> { @"C:\folder" };
        pasteHandler.Setup(handler => handler.ResolveFileAccessAsync(
                TargetSid,
                null,
                It.IsAny<List<string>>(),
                "S-1-5-21-111-222-333-1002",
                null,
                It.IsAny<AppDatabase?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DragBridgeResolveResult([@"C:\resolved.txt"], DatabaseModified: false, grantedPaths, RollbackPlan: null));
        var orchestrator = new DragBridgeResolveOrchestrator(
            pasteHandler.Object,
            quickAccessPinService.Object,
            uiThreadInvoker.Object,
            notifications.Object,
            pathGrantService.Object,
            traverseService.Object);

        var resolve = orchestrator.CreateResolveDelegate(
            new WindowOwnerInfo(TargetSid, NativeTokenHelper.MandatoryLevelMedium, false),
            [@"C:\captured.txt"],
            "S-1-5-21-111-222-333-1002",
            null,
            new AppDatabase());

        await resolve(CancellationToken.None);

        quickAccessPinService.Verify(service => service.PinFolders(TargetSid.Value, grantedPaths), Times.Once);
    }

    [Fact]
    public async Task CreateResolveDelegate_DatabaseModifiedWithoutDurableSave_RollsBackAndThrows()
    {
        var pasteHandler = new Mock<IDragBridgePasteHandler>();
        var quickAccessPinService = new Mock<IQuickAccessPinService>();
        var notifications = new Mock<INotificationService>();
        var uiThreadInvoker = new Mock<IUiThreadInvoker>();
        uiThreadInvoker.Setup(invoker => invoker.Invoke(It.IsAny<Action>()))
            .Callback<Action>(action => action());
        var pathGrantService = new Mock<IGrantMutatorService>();
        var traverseService = pathGrantService.As<ITraverseService>();
        using var tempDir = new TempDirectory("DragBridgeResolveRollback");
        var tempFile = Path.Combine(tempDir.Path, "temp.txt");
        File.WriteAllText(tempFile, "temp");
        var repairedPath = @"C:\repaired";
        var persistedPath = @"C:\persisted";
        var traversePath = @"C:\persisted-parent";
        pasteHandler.Setup(handler => handler.ResolveFileAccessAsync(
                TargetSid,
                null,
                It.IsAny<List<string>>(),
                "S-1-5-21-111-222-333-1002",
                null,
                It.IsAny<AppDatabase?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DragBridgeResolveResult(
                [@"C:\resolved.txt"],
                DatabaseModified: true,
                GrantedPaths: [repairedPath, persistedPath],
                RollbackPlan: new DragBridgeRollbackPlan(
                    [new DragBridgeGrantRollbackEntry(TargetSid.Value, persistedPath, new GrantIntentRestoreSnapshot(null, []))],
                    [new DragBridgeTraverseRollbackEntry(TargetSid.Value, traversePath, new GrantIntentRestoreSnapshot(null, []))],
                    [tempFile])));
        var orchestrator = new DragBridgeResolveOrchestrator(
            pasteHandler.Object,
            quickAccessPinService.Object,
            uiThreadInvoker.Object,
            notifications.Object,
            pathGrantService.Object,
            traverseService.Object);

        var resolve = orchestrator.CreateResolveDelegate(
            new WindowOwnerInfo(TargetSid, NativeTokenHelper.MandatoryLevelMedium, false),
            [@"C:\captured.txt"],
            "S-1-5-21-111-222-333-1002",
            null,
            new AppDatabase());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => resolve(CancellationToken.None));

        Assert.Contains("durable save", exception.Message, StringComparison.OrdinalIgnoreCase);
        pathGrantService.Verify(service => service.RestoreGrant(TargetSid.Value, persistedPath, false, It.Is<GrantIntentRestoreSnapshot>(snapshot => snapshot.RuntimeEntry == null && snapshot.Locations.Count == 0)), Times.Once);
        pathGrantService.Verify(service => service.RestoreGrant(TargetSid.Value, repairedPath, false, It.IsAny<GrantIntentRestoreSnapshot>()), Times.Never);
        traverseService.Verify(service => service.RestoreTraverse(TargetSid.Value, traversePath, It.Is<GrantIntentRestoreSnapshot>(snapshot => snapshot.RuntimeEntry == null && snapshot.Locations.Count == 0)), Times.Once);
        Assert.False(File.Exists(tempFile));
        quickAccessPinService.Verify(service => service.PinFolders(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()), Times.Never);
    }

    [Fact]
    public async Task CreateResolveDelegate_DatabaseModifiedWithoutDurableSave_RestoresLegacyGrantSnapshotAndThrows()
    {
        var pasteHandler = new Mock<IDragBridgePasteHandler>();
        var quickAccessPinService = new Mock<IQuickAccessPinService>();
        var notifications = new Mock<INotificationService>();
        var uiThreadInvoker = new Mock<IUiThreadInvoker>();
        uiThreadInvoker.Setup(invoker => invoker.Invoke(It.IsAny<Action>()))
            .Callback<Action>(action => action());
        var pathGrantService = new Mock<IGrantMutatorService>();
        var traverseService = pathGrantService.As<ITraverseService>();
        var legacyGrant = new GrantedPathEntry
        {
            Path = @"C:\persisted",
            IsDeny = false,
            SavedRights = null
        };
        var legacyLocations = new List<GrantIntentRestoreLocation>
        {
            new(new GrantIntentStoreIdentity(null), legacyGrant)
        };
        var legacySnapshot = new GrantIntentRestoreSnapshot(legacyGrant, legacyLocations);
        pasteHandler.Setup(handler => handler.ResolveFileAccessAsync(
                TargetSid,
                null,
                It.IsAny<List<string>>(),
                "S-1-5-21-111-222-333-1002",
                null,
                It.IsAny<AppDatabase?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DragBridgeResolveResult(
                [@"C:\resolved.txt"],
                DatabaseModified: true,
                GrantedPaths: [@"C:\persisted"],
                RollbackPlan: new DragBridgeRollbackPlan(
                    [new DragBridgeGrantRollbackEntry(TargetSid.Value, legacyGrant.Path, legacySnapshot)],
                    [],
                    [])));
        var orchestrator = new DragBridgeResolveOrchestrator(
            pasteHandler.Object,
            quickAccessPinService.Object,
            uiThreadInvoker.Object,
            notifications.Object,
            pathGrantService.Object,
            traverseService.Object);

        var resolve = orchestrator.CreateResolveDelegate(
            new WindowOwnerInfo(TargetSid, NativeTokenHelper.MandatoryLevelMedium, false),
            [@"C:\captured.txt"],
            "S-1-5-21-111-222-333-1002",
            null,
            new AppDatabase());

        await Assert.ThrowsAsync<InvalidOperationException>(() => resolve(CancellationToken.None));

        pathGrantService.Verify(service => service.RestoreGrant(
            TargetSid.Value,
            legacyGrant.Path,
            false,
            It.Is<GrantIntentRestoreSnapshot>(snapshot =>
                snapshot.RuntimeEntry != null &&
                string.Equals(snapshot.RuntimeEntry.Path, legacyGrant.Path, StringComparison.OrdinalIgnoreCase) &&
                snapshot.RuntimeEntry.SavedRights == null &&
                snapshot.Locations.Count == 1 &&
                snapshot.Locations[0].Entry.SavedRights == null)), Times.Once);
        quickAccessPinService.Verify(service => service.PinFolders(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()), Times.Never);
    }

    [Fact]
    public async Task CreateResolveDelegate_DatabaseModifiedWithWarnings_ShowsWarningsAndDoesNotRollback()
    {
        var pasteHandler = new Mock<IDragBridgePasteHandler>();
        var quickAccessPinService = new Mock<IQuickAccessPinService>();
        var notifications = new Mock<INotificationService>();
        var uiThreadInvoker = new Mock<IUiThreadInvoker>();
        uiThreadInvoker.Setup(invoker => invoker.Invoke(It.IsAny<Action>()))
            .Callback<Action>(action => action());
        var pathGrantService = new Mock<IGrantMutatorService>();
        var traverseService = pathGrantService.As<ITraverseService>();
        pasteHandler.Setup(handler => handler.ResolveFileAccessAsync(
                TargetSid,
                null,
                It.IsAny<List<string>>(),
                "S-1-5-21-111-222-333-1002",
                null,
                It.IsAny<AppDatabase?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DragBridgeResolveResult(
                [@"C:\resolved.txt"],
                DatabaseModified: true,
                GrantedPaths: [@"C:\persisted"],
                RollbackPlan: null)
            {
                DurableSaveCompleted = false,
                Warnings = ["folder handler warning"]
            });
        var orchestrator = new DragBridgeResolveOrchestrator(
            pasteHandler.Object,
            quickAccessPinService.Object,
            uiThreadInvoker.Object,
            notifications.Object,
            pathGrantService.Object,
            traverseService.Object);

        var resolve = orchestrator.CreateResolveDelegate(
            new WindowOwnerInfo(TargetSid, NativeTokenHelper.MandatoryLevelMedium, false),
            [@"C:\captured.txt"],
            "S-1-5-21-111-222-333-1002",
            null,
            new AppDatabase());

        var result = await resolve(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(@"C:\resolved.txt", Assert.Single(result));
        notifications.Verify(n => n.ShowWarning("Drag Bridge", "folder handler warning"), Times.Once);
        pathGrantService.VerifyNoOtherCalls();
        quickAccessPinService.Verify(
            service => service.PinFolders(
                TargetSid.Value,
                It.Is<IReadOnlyList<string>>(paths => paths.SequenceEqual(new[] { @"C:\persisted" }))),
            Times.Once);
    }
}
