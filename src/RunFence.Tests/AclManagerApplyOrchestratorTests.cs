using Moq;
using RunFence.Acl;
using RunFence.Acl.QuickAccess;
using RunFence.Acl.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public class AclManagerApplyOrchestratorTests
{
    private const string TestSid = "S-1-5-21-111-222-333-1001";
    private const string ContainerSid = "S-1-15-2-99-1-2-3-4-5-6";

    /// <summary>
    /// Testable subclass that suppresses the error dialog so tests that trigger
    /// partial failures don't block waiting for user input.
    /// </summary>
    private sealed class TestableOrchestrator(
        ILoggingService log,
        IPathGrantService pathGrantService,
        IGrantConfigTracker grantConfigTracker,
        IDatabaseProvider databaseProvider,
        ISessionSaver sessionSaver,
        IQuickAccessPinService quickAccessPinService)
        : AclManagerApplyOrchestrator(log, pathGrantService, grantConfigTracker,
            databaseProvider, sessionSaver, quickAccessPinService)
    {
        protected override void ShowApplyErrors(List<(string Path, string Error)> errors)
        {
            // Suppress dialog in tests to prevent blocking on user input.
        }
    }

    private static IDatabaseProvider CreateDatabaseProvider()
    {
        var db = new AppDatabase();
        db.GetOrCreateAccount(TestSid);
        var databaseProvider = new Mock<IDatabaseProvider>();
        databaseProvider.Setup(d => d.GetDatabase()).Returns(db);
        return databaseProvider.Object;
    }

    private static AclManagerApplyOrchestrator CreateOrchestrator(IPathGrantService pathGrantService)
        => new TestableOrchestrator(
            new Mock<ILoggingService>().Object, pathGrantService,
            new Mock<IGrantConfigTracker>().Object, CreateDatabaseProvider(),
            new Mock<ISessionSaver>().Object, new Mock<IQuickAccessPinService>().Object);

    private static (AclManagerApplyOrchestrator Orchestrator, Mock<IPathGrantService> PathGrantService)
        CreateOrchestrator()
    {
        var pathGrantService = new Mock<IPathGrantService>();
        return (CreateOrchestrator(pathGrantService.Object), pathGrantService);
    }

    private static Task RunApplyAsync(AclManagerApplyOrchestrator orchestrator)
        => orchestrator.ApplyAsync(
            new Progress<(int current, int total)>(),
            setApplyEnabled: _ => { },
            setDialogEnabled: _ => { },
            refreshGrids: () => { });

    private static async Task ApplyModificationAsync(
        AclManagerApplyOrchestrator orchestrator, PendingModification mod)
    {
        var pending = new AclManagerPendingChanges
        {
            PendingModifications =
            {
                [(mod.Entry.Path, mod.WasIsDeny)] = mod
            }
        };

        orchestrator.Initialize(pending, TestSid, isContainer: false);

        await RunApplyAsync(orchestrator);
    }

    [Fact]
    public async Task AllowOwnToDeny_ShouldResetOwner()
    {
        // Arrange
        var (orchestrator, pathGrantService) = CreateOrchestrator();

        var entry = new GrantedPathEntry
        {
            Path = @"C:\TestFolder",
            IsDeny = false,
            SavedRights = new SavedRightsState(Execute: true, Write: false, Read: true, Special: false, Own: true)
        };
        var mod = new PendingModification(
            Entry: entry, WasIsDeny: false, WasOwn: true, NewIsDeny: true,
            NewRights: SavedRightsState.DefaultForMode(isDeny: true, own: false));

        // Act
        await ApplyModificationAsync(orchestrator, mod);

        // Assert
        pathGrantService.Verify(p => p.ResetOwner(@"C:\TestFolder", false), Times.Once);
    }

    [Fact]
    public async Task AllowOwnToAllowNoOwn_ShouldResetOwner()
    {
        // Arrange
        var (orchestrator, pathGrantService) = CreateOrchestrator();

        var entry = new GrantedPathEntry
        {
            Path = @"C:\TestFolder2",
            IsDeny = false,
            SavedRights = new SavedRightsState(Execute: true, Write: false, Read: true, Special: false, Own: true)
        };
        var mod = new PendingModification(
            Entry: entry, WasIsDeny: false, WasOwn: true, NewIsDeny: false,
            NewRights: new SavedRightsState(Execute: true, Write: false, Read: true, Special: false, Own: false));

        // Act
        await ApplyModificationAsync(orchestrator, mod);

        // Assert
        pathGrantService.Verify(p => p.ResetOwner(@"C:\TestFolder2", false), Times.Once);
    }

    [Fact]
    public async Task Apply_RemovePhase_CallsRemoveGrant()
    {
        // Arrange: a pending remove entry
        var (orchestrator, pathGrantService) = CreateOrchestrator();
        var entry = new GrantedPathEntry { Path = @"C:\ToRemove", IsDeny = false };

        var pending = new AclManagerPendingChanges();
        pending.PendingRemoves[(@"C:\ToRemove", false)] = entry;
        orchestrator.Initialize(pending, TestSid, isContainer: false);

        // Act
        await RunApplyAsync(orchestrator);

        // Assert: RemoveGrant called with updateFileSystem=true
        pathGrantService.Verify(
            p => p.RemoveGrant(TestSid, @"C:\ToRemove", false, updateFileSystem: true),
            Times.Once);
    }

    [Fact]
    public async Task Apply_AddPhase_CallsAddGrant()
    {
        // Arrange: a pending add entry
        var (orchestrator, pathGrantService) = CreateOrchestrator();
        var rights = new SavedRightsState(Execute: true, Write: false, Read: true, Special: false, Own: false);
        var entry = new GrantedPathEntry { Path = @"C:\ToAdd", IsDeny = false, SavedRights = rights };

        var pending = new AclManagerPendingChanges();
        pending.PendingAdds[(@"C:\ToAdd", false)] = entry;
        orchestrator.Initialize(pending, TestSid, isContainer: false);

        // Act
        await RunApplyAsync(orchestrator);

        // Assert: AddGrant called with the correct parameters
        pathGrantService.Verify(
            p => p.AddGrant(TestSid, @"C:\ToAdd", false, rights, null),
            Times.Once);
    }

    [Fact]
    public async Task AllSuccess_ClearsPendingState()
    {
        // Arrange: one pending add that succeeds
        var (orchestrator, _) = CreateOrchestrator();
        var entry = new GrantedPathEntry { Path = @"C:\Cleared", IsDeny = false };

        var pending = new AclManagerPendingChanges();
        pending.PendingAdds[(@"C:\Cleared", false)] = entry;
        orchestrator.Initialize(pending, TestSid, isContainer: false);

        // Act
        await RunApplyAsync(orchestrator);

        // Assert: pending cleared after all-success apply
        Assert.False(pending.HasPendingChanges);
    }

    [Fact]
    public async Task PartialFailure_KeepsFailedEntriesPending()
    {
        // Arrange: two pending removes; the first throws, the second succeeds.
        // On partial failure, pending state must NOT be cleared.
        var pathGrantService = new Mock<IPathGrantService>();

        pathGrantService
            .Setup(p => p.RemoveGrant(TestSid, @"C:\Fail", false, updateFileSystem: true))
            .Throws(new InvalidOperationException("Simulated failure"));

        pathGrantService
            .Setup(p => p.RemoveGrant(TestSid, @"C:\Ok", false, updateFileSystem: true))
            .Returns(true);

        var orchestrator = CreateOrchestrator(pathGrantService.Object);

        var entryFail = new GrantedPathEntry { Path = @"C:\Fail", IsDeny = false };
        var entryOk = new GrantedPathEntry { Path = @"C:\Ok", IsDeny = false };

        var pending = new AclManagerPendingChanges();
        pending.PendingRemoves[(@"C:\Fail", false)] = entryFail;
        pending.PendingRemoves[(@"C:\Ok", false)] = entryOk;
        orchestrator.Initialize(pending, TestSid, isContainer: false);

        // Act
        await RunApplyAsync(orchestrator);

        // Assert: pending NOT cleared because at least one operation failed
        Assert.True(pending.HasPendingChanges,
            "Pending state must be kept intact when partial failure occurs so the user can retry");
    }

    [Fact]
    public async Task ModeSwitch_AddFails_RestoresOriginalGrant()
    {
        // Arrange: mode switch Allow→Deny; RemoveGrant succeeds but AddGrant (new mode) throws.
        // The orchestrator must: revert SavedRights, call AddGrant with original mode to restore.
        var pathGrantService = new Mock<IPathGrantService>();

        var originalRights = new SavedRightsState(Execute: true, Write: false, Read: true, Special: false, Own: false);
        var newRights = SavedRightsState.DefaultForMode(isDeny: true, own: false);

        // RemoveGrant for original allow mode succeeds
        pathGrantService.Setup(p => p.RemoveGrant(TestSid, @"C:\Switch", false, updateFileSystem: true))
            .Returns(true);

        // AddGrant for new deny mode fails
        pathGrantService.Setup(p => p.AddGrant(TestSid, @"C:\Switch", true, It.IsAny<SavedRightsState?>(), It.IsAny<string?>()))
            .Throws(new InvalidOperationException("Simulated add failure"));

        // Restoration AddGrant (original allow mode) succeeds
        pathGrantService.Setup(p => p.AddGrant(TestSid, @"C:\Switch", false, It.IsAny<SavedRightsState?>(), null))
            .Returns(default(GrantOperationResult));

        var orchestrator = CreateOrchestrator(pathGrantService.Object);

        var entry = new GrantedPathEntry
        {
            Path = @"C:\Switch",
            IsDeny = false,
            SavedRights = originalRights
        };
        var mod = new PendingModification(
            Entry: entry, WasIsDeny: false, WasOwn: false, NewIsDeny: true, NewRights: newRights);

        var pending = new AclManagerPendingChanges();
        pending.PendingModifications[(@"C:\Switch", false)] = mod;
        orchestrator.Initialize(pending, TestSid, isContainer: false);

        // Act
        await RunApplyAsync(orchestrator);

        // Assert: restoration AddGrant was called with the original allow mode (false)
        pathGrantService.Verify(
            p => p.AddGrant(TestSid, @"C:\Switch", false, It.IsAny<SavedRightsState?>(), null),
            Times.Once);

        // SavedRights on the entry should have been restored to the original value
        Assert.Equal(originalRights, entry.SavedRights);

        // Partial failure → pending NOT cleared
        Assert.True(pending.HasPendingChanges);
    }

    // --- F-36: Traverse add/remove/fix phases ---

    [Fact]
    public async Task Apply_TraverseAddPhase_CallsAddTraverse()
    {
        // Arrange
        var (orchestrator, pathGrantService) = CreateOrchestrator();
        var entry = new GrantedPathEntry { Path = @"C:\TraverseDir", IsTraverseOnly = true };

        var pending = new AclManagerPendingChanges();
        pending.PendingTraverseAdds[@"C:\TraverseDir"] = entry;
        orchestrator.Initialize(pending, TestSid, isContainer: false);

        // Act
        await RunApplyAsync(orchestrator);

        // Assert
        pathGrantService.Verify(p => p.AddTraverse(TestSid, @"C:\TraverseDir"), Times.Once);
    }

    [Fact]
    public async Task Apply_GrantModifications_RunBeforeTraverseAdds()
    {
        // Arrange
        var calls = new List<string>();
        var pathGrantService = new Mock<IPathGrantService>();
        pathGrantService.Setup(p => p.AddGrant(
                TestSid,
                @"C:\Grant",
                false,
                It.IsAny<SavedRightsState?>(),
                It.IsAny<string?>()))
            .Callback(() => calls.Add("grant-add"))
            .Returns(default(GrantOperationResult));
        pathGrantService.Setup(p => p.UpdateGrant(
                TestSid,
                @"C:\Modify",
                false,
                It.IsAny<SavedRightsState>(),
                It.IsAny<string?>()))
            .Callback(() => calls.Add("grant-modify"));
        pathGrantService.Setup(p => p.AddTraverse(TestSid, @"C:\Traverse"))
            .Callback(() => calls.Add("traverse-add"))
            .Returns((true, []));
        var orchestrator = CreateOrchestrator(pathGrantService.Object);

        var pending = new AclManagerPendingChanges();
        pending.PendingAdds[(@"C:\Grant", false)] = new GrantedPathEntry
        {
            Path = @"C:\Grant",
            IsDeny = false,
            SavedRights = SavedRightsState.DefaultForMode(isDeny: false)
        };
        var modEntry = new GrantedPathEntry
        {
            Path = @"C:\Modify",
            IsDeny = false,
            SavedRights = SavedRightsState.DefaultForMode(isDeny: false)
        };
        pending.PendingModifications[(@"C:\Modify", false)] = new PendingModification(
            modEntry,
            WasIsDeny: false,
            WasOwn: false,
            NewIsDeny: false,
            NewRights: new SavedRightsState(Execute: true, Write: true, Read: true, Special: false, Own: false));
        pending.PendingTraverseAdds[@"C:\Traverse"] = new GrantedPathEntry
        {
            Path = @"C:\Traverse",
            IsTraverseOnly = true
        };
        orchestrator.Initialize(pending, TestSid, isContainer: false);

        // Act
        await RunApplyAsync(orchestrator);

        // Assert
        Assert.Equal(["grant-add", "grant-modify", "traverse-add"], calls);
    }

    [Fact]
    public async Task Apply_TraverseRemovePhase_CallsRemoveTraverse()
    {
        // Arrange
        var (orchestrator, pathGrantService) = CreateOrchestrator();
        var entry = new GrantedPathEntry { Path = @"C:\TraverseDir", IsTraverseOnly = true };

        var pending = new AclManagerPendingChanges();
        pending.PendingTraverseRemoves[@"C:\TraverseDir"] = entry;
        orchestrator.Initialize(pending, TestSid, isContainer: false);

        // Act
        await RunApplyAsync(orchestrator);

        // Assert: RemoveTraverse called with updateFileSystem=true
        pathGrantService.Verify(
            p => p.RemoveTraverse(TestSid, @"C:\TraverseDir", updateFileSystem: true),
            Times.Once);
    }

    [Fact]
    public async Task Apply_TraverseFixPhase_CallsFixTraverse()
    {
        // Arrange
        var (orchestrator, pathGrantService) = CreateOrchestrator();
        var entry = new GrantedPathEntry { Path = @"C:\TraverseDir", IsTraverseOnly = true };

        var pending = new AclManagerPendingChanges();
        pending.PendingTraverseFixes[@"C:\TraverseDir"] = entry;
        orchestrator.Initialize(pending, TestSid, isContainer: false);

        // Act
        await RunApplyAsync(orchestrator);

        // Assert
        pathGrantService.Verify(p => p.FixTraverse(TestSid, @"C:\TraverseDir"), Times.Once);
    }

    // --- F-36: Untrack phase ---

    [Fact]
    public async Task Apply_UntrackGrantPhase_CallsRemoveGrantWithUpdateFileSystemFalse()
    {
        // Arrange
        var (orchestrator, pathGrantService) = CreateOrchestrator();
        var entry = new GrantedPathEntry { Path = @"C:\Untrack", IsDeny = false };

        var pending = new AclManagerPendingChanges();
        pending.PendingUntrackGrants[(@"C:\Untrack", false)] = entry;
        orchestrator.Initialize(pending, TestSid, isContainer: false);

        // Act
        await RunApplyAsync(orchestrator);

        // Assert: RemoveGrant called with updateFileSystem=false (DB-only untrack)
        pathGrantService.Verify(
            p => p.RemoveGrant(TestSid, @"C:\Untrack", false, updateFileSystem: false),
            Times.Once);
    }

    [Fact]
    public async Task Apply_UntrackTraversePhase_CallsRemoveTraverseWithUpdateFileSystemFalse()
    {
        // Arrange
        var (orchestrator, pathGrantService) = CreateOrchestrator();
        var entry = new GrantedPathEntry { Path = @"C:\TraverseUntrack", IsTraverseOnly = true };

        var pending = new AclManagerPendingChanges();
        pending.PendingUntrackTraverse[@"C:\TraverseUntrack"] = entry;
        orchestrator.Initialize(pending, TestSid, isContainer: false);

        // Act
        await RunApplyAsync(orchestrator);

        // Assert
        pathGrantService.Verify(
            p => p.RemoveTraverse(TestSid, @"C:\TraverseUntrack", updateFileSystem: false),
            Times.Once);
    }

    // --- F-36: Config moves phase ---

    [Fact]
    public async Task Apply_ConfigMovePhase_CallsGrantConfigTrackerAssignGrant()
    {
        // Arrange: a grant entry committed to DB with a pending config move
        var pathGrantService = new Mock<IPathGrantService>();
        var grantConfigTracker = new Mock<IGrantConfigTracker>();
        var db = new AppDatabase();
        db.GetOrCreateAccount(TestSid);

        // Create a grant entry in the DB that the config-move phase will look up
        var dbEntry = new GrantedPathEntry { Path = @"C:\ConfigMove", IsDeny = false };
        db.GetAccount(TestSid)!.Grants.Add(dbEntry);

        var databaseProvider = new Mock<IDatabaseProvider>();
        databaseProvider.Setup(d => d.GetDatabase()).Returns(db);

        var orchestrator = new TestableOrchestrator(
            new Mock<ILoggingService>().Object, pathGrantService.Object,
            grantConfigTracker.Object, databaseProvider.Object,
            new Mock<ISessionSaver>().Object, new Mock<IQuickAccessPinService>().Object);

        var pending = new AclManagerPendingChanges();
        pending.PendingConfigMoves[(@"C:\ConfigMove", false)] = "extra.rfn";
        orchestrator.Initialize(pending, TestSid, isContainer: false);

        // Act
        await RunApplyAsync(orchestrator);

        // Assert: AssignGrant called with the target config path
        grantConfigTracker.Verify(
            t => t.AssignGrant(TestSid, dbEntry, "extra.rfn"),
            Times.Once);
    }

    [Fact]
    public async Task Apply_ContainerSharedTraverseConfigMove_AssignsSharedContainerGrant()
    {
        var pathGrantService = new Mock<IPathGrantService>();
        var grantConfigTracker = new Mock<IGrantConfigTracker>();
        var db = new AppDatabase();
        var sharedEntry = new GrantedPathEntry { Path = @"C:\SharedTraverse", IsTraverseOnly = true };
        db.SharedContainerTraverseGrants.Add(sharedEntry);

        var databaseProvider = new Mock<IDatabaseProvider>();
        databaseProvider.Setup(d => d.GetDatabase()).Returns(db);

        var orchestrator = new TestableOrchestrator(
            new Mock<ILoggingService>().Object, pathGrantService.Object,
            grantConfigTracker.Object, databaseProvider.Object,
            new Mock<ISessionSaver>().Object, new Mock<IQuickAccessPinService>().Object);

        var pending = new AclManagerPendingChanges();
        pending.PendingTraverseConfigMoves[@"C:\SharedTraverse"] = "extra.rfn";
        orchestrator.Initialize(pending, ContainerSid, isContainer: true);

        await RunApplyAsync(orchestrator);

        grantConfigTracker.Verify(
            t => t.AssignGrant(WellKnownSecuritySids.AllApplicationPackagesSid, sharedEntry, "extra.rfn"),
            Times.Once);
    }

    // --- F-36: QuickAccess pin/unpin phases ---

    [Fact]
    public async Task Apply_AddGrantAllowPhase_PinsToQuickAccess()
    {
        // Arrange: a pending allow (non-deny, non-traverse) add — should pin the folder
        var pathGrantService = new Mock<IPathGrantService>();
        var quickAccessPinService = new Mock<IQuickAccessPinService>();
        var db = new AppDatabase();
        db.GetOrCreateAccount(TestSid);

        var databaseProvider = new Mock<IDatabaseProvider>();
        databaseProvider.Setup(d => d.GetDatabase()).Returns(db);

        var orchestrator = new TestableOrchestrator(
            new Mock<ILoggingService>().Object, pathGrantService.Object,
            new Mock<IGrantConfigTracker>().Object, databaseProvider.Object,
            new Mock<ISessionSaver>().Object, quickAccessPinService.Object);

        var rights = new SavedRightsState(Execute: false, Write: false, Read: true, Special: false, Own: false);
        var entry = new GrantedPathEntry { Path = @"C:\PinFolder", IsDeny = false, SavedRights = rights };

        var pending = new AclManagerPendingChanges();
        pending.PendingAdds[(@"C:\PinFolder", false)] = entry;
        orchestrator.Initialize(pending, TestSid, isContainer: false);

        // Act
        await RunApplyAsync(orchestrator);

        // Assert: PinFolders called with the added allow path
        quickAccessPinService.Verify(
            q => q.PinFolders(TestSid, It.Is<IReadOnlyList<string>>(l => l.Contains(@"C:\PinFolder"))),
            Times.Once);
    }

    [Fact]
    public async Task Apply_RemoveGrantAllowPhase_UnpinsFromQuickAccess()
    {
        // Arrange: a pending allow (non-deny, non-traverse) remove — should unpin the folder
        var pathGrantService = new Mock<IPathGrantService>();
        var quickAccessPinService = new Mock<IQuickAccessPinService>();
        var db = new AppDatabase();
        db.GetOrCreateAccount(TestSid);

        var databaseProvider = new Mock<IDatabaseProvider>();
        databaseProvider.Setup(d => d.GetDatabase()).Returns(db);

        var orchestrator = new TestableOrchestrator(
            new Mock<ILoggingService>().Object, pathGrantService.Object,
            new Mock<IGrantConfigTracker>().Object, databaseProvider.Object,
            new Mock<ISessionSaver>().Object, quickAccessPinService.Object);

        var entry = new GrantedPathEntry { Path = @"C:\UnpinFolder", IsDeny = false };

        var pending = new AclManagerPendingChanges();
        pending.PendingRemoves[(@"C:\UnpinFolder", false)] = entry;
        orchestrator.Initialize(pending, TestSid, isContainer: false);

        // Act
        await RunApplyAsync(orchestrator);

        // Assert: UnpinFolders called with the removed allow path
        quickAccessPinService.Verify(
            q => q.UnpinFolders(TestSid, It.Is<IReadOnlyList<string>>(l => l.Contains(@"C:\UnpinFolder"))),
            Times.Once);
    }

    [Fact]
    public async Task Apply_AddGrantDenyPhase_DoesNotPin()
    {
        // Arrange: a deny grant add — deny grants are NOT pinned to Quick Access
        var pathGrantService = new Mock<IPathGrantService>();
        var quickAccessPinService = new Mock<IQuickAccessPinService>();
        var db = new AppDatabase();
        db.GetOrCreateAccount(TestSid);

        var databaseProvider = new Mock<IDatabaseProvider>();
        databaseProvider.Setup(d => d.GetDatabase()).Returns(db);

        var orchestrator = new TestableOrchestrator(
            new Mock<ILoggingService>().Object, pathGrantService.Object,
            new Mock<IGrantConfigTracker>().Object, databaseProvider.Object,
            new Mock<ISessionSaver>().Object, quickAccessPinService.Object);

        var rights = SavedRightsState.DefaultForMode(isDeny: true);
        var entry = new GrantedPathEntry { Path = @"C:\DenyFolder", IsDeny = true, SavedRights = rights };

        var pending = new AclManagerPendingChanges();
        pending.PendingAdds[(@"C:\DenyFolder", true)] = entry;
        orchestrator.Initialize(pending, TestSid, isContainer: false);

        // Act
        await RunApplyAsync(orchestrator);

        // Assert: PinFolders NOT called for deny grants
        quickAccessPinService.Verify(
            q => q.PinFolders(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()),
            Times.Never);
    }

}
