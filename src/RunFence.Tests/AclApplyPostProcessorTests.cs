using Moq;
using RunFence.Acl;
using RunFence.Acl.QuickAccess;
using RunFence.Acl.Traverse;
using RunFence.Acl.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public class AclApplyPostProcessorTests
{
    private const string TestSid = "S-1-5-21-111-222-333-1001";
    private const string ContainerSid = "S-1-15-2-99-1-2-3-4-5-6";
    private static readonly AclApplyPhaseCatalog PhaseCatalog = new();

    private static AclGrantPendingChangesSnapshot GetGrantSnapshot(AclManagerPendingChanges pending)
        => pending.Grants.GetSnapshot();

    private static AclTraversePendingChangesSnapshot GetTraverseSnapshot(AclManagerPendingChanges pending)
        => pending.Traverse.GetSnapshot();

    private static void AddPendingGrant(AclManagerPendingChanges pending, GrantedPathEntry entry)
        => pending.Grants.AddGrant(entry);

    private static void AddPendingRemoval(AclManagerPendingChanges pending, GrantedPathEntry entry)
        => pending.Grants.MarkGrantForRemoval(entry);

    private static void AddPendingModification(AclManagerPendingChanges pending, GrantedPathEntry entry, PendingModification modification)
        => pending.Grants.ModifyGrant(entry, modification);

    private static void AddPendingGrantFix(AclManagerPendingChanges pending, GrantedPathEntry entry)
        => pending.Grants.AddGrantFix(entry);

    private static void AddPendingTraverse(AclManagerPendingChanges pending, GrantedPathEntry entry)
        => pending.Traverse.AddTraverse(entry);

    private static void AddPendingTraverseRemoval(AclManagerPendingChanges pending, GrantedPathEntry entry)
        => pending.Traverse.MarkTraverseForRemoval(entry);

    private static void AddPendingTraverseFix(AclManagerPendingChanges pending, GrantedPathEntry entry)
        => pending.Traverse.AddTraverseFix(entry);

    private static void AddPendingUntrackGrant(AclManagerPendingChanges pending, GrantedPathEntry entry)
        => pending.Grants.UntrackGrant(entry);

    private static void AddPendingUntrackTraverse(AclManagerPendingChanges pending, GrantedPathEntry entry)
        => pending.Traverse.UntrackTraverse(entry);

    private static void AddPendingGrantConfigMove(AclManagerPendingChanges pending, GrantedPathEntry entry, string? targetConfigPath)
        => pending.Grants.MoveGrantConfig(entry, targetConfigPath);

    private static void AddPendingTraverseConfigMove(AclManagerPendingChanges pending, GrantedPathEntry entry, string? targetConfigPath)
        => pending.Traverse.MoveTraverseConfig(entry, targetConfigPath);

    [Fact]
    public void Apply_AllSuccess_ClearsPending_PinsUnpins_WithoutRepeatedStoreSave()
    {
        var quickAccess = new Mock<IQuickAccessPinService>();
        var sut = CreateSut(out var _, out var _, out var _,
            quickAccessPinService: quickAccess.Object);

        var pending = new AclManagerPendingChanges();
        AddPendingGrant(pending, new GrantedPathEntry
        {
            Path = @"C:\Add",
            IsDeny = false,
            SavedRights = SavedRightsState.DefaultForMode(false)
        });
        AddPendingRemoval(pending, new GrantedPathEntry
        {
            Path = @"C:\Remove",
            IsDeny = false
        });
        var plan = new AclApplyPlanBuilder().Build(pending);

        var outcome = sut.Apply(plan, CreateExecutionResultWithCompletedPlanOperations(plan), pending, TestSid, isContainer: false);

        Assert.True(outcome.Succeeded);
        Assert.False(pending.HasPendingChanges);
        Assert.Empty(outcome.Errors);
        quickAccess.Verify(x => x.PinFolders(TestSid, It.Is<IReadOnlyList<string>>(l => l.Contains(@"C:\Add"))), Times.Once);
        quickAccess.Verify(x => x.UnpinFolders(TestSid, It.Is<IReadOnlyList<string>>(l => l.Contains(@"C:\Remove"))), Times.Once);
    }

    [Fact]
    public void Apply_Failure_RetainsOnlyFailedPendingEntries()
    {
        var sut = CreateSut(out _, out _, out _);

        var pending = new AclManagerPendingChanges();
        AddPendingGrant(pending, new GrantedPathEntry { Path = @"C:\Fail", IsDeny = false });
        AddPendingGrant(pending, new GrantedPathEntry { Path = @"C:\Ok", IsDeny = false });
        AddPendingGrantConfigMove(pending, new GrantedPathEntry { Path = @"C:\Ok", IsDeny = false }, "extra.rfn");
        var plan = new AclApplyPlanBuilder().Build(pending);

        var execution = new AclApplyExecutionResult();
        execution.Errors.Add(new AclApplyError(
            AclPendingOperationKind.GrantAdd,
            @"C:\Fail",
            false,
            CreateGrantOperationException(GrantApplyFailureStep.GrantAclApply, @"C:\Fail", "boom")));
        execution.MarkCompleted(AclPendingOperationKind.GrantAdd, @"C:\Ok", false);

        var outcome = sut.Apply(plan, execution, pending, TestSid, isContainer: false);

        Assert.False(outcome.Succeeded);
        Assert.True(pending.Grants.IsPendingAdd(@"C:\Fail", false));
        Assert.False(pending.Grants.IsPendingAdd(@"C:\Ok", false));
        Assert.False(pending.Grants.TryGetPendingConfigMove(@"C:\Ok", false, out _));
        Assert.Single(outcome.Errors);
    }

    [Fact]
    public void Apply_WarningOnlyOperation_ClearsPendingStateAndReturnsWarnings()
    {
        var sut = CreateSut(out _, out _, out _);
        var warning = new GrantApplyWarning(
            GrantApplyFailureStep.PostGrantRemoveSave,
            @"C:\Warning",
            null,
            new InvalidOperationException("save warning"));

        var pending = new AclManagerPendingChanges();
        AddPendingRemoval(pending, new GrantedPathEntry { Path = @"C:\Warning", IsDeny = false });
        var plan = new AclApplyPlanBuilder().Build(pending);
        var execution = new AclApplyExecutionResult();
        execution.MarkCompleted(AclPendingOperationKind.GrantRemove, @"C:\Warning", false);
        execution.Warnings.Add(warning);

        var outcome = sut.Apply(plan, execution, pending, TestSid, isContainer: false);

        Assert.True(outcome.Succeeded);
        Assert.False(pending.HasPendingChanges);
        Assert.Empty(outcome.Errors);
        Assert.Equal([warning], outcome.Warnings);
    }

    [Fact]
    public void Apply_CanceledAfterCompletedOperations_RemovesOnlyCompletedPendingEntries()
    {
        var quickAccess = new Mock<IQuickAccessPinService>();
        var sut = CreateSut(out _, out _, out _,
            quickAccessPinService: quickAccess.Object);
        var completedEntry = new GrantedPathEntry
        {
            Path = @"C:\Completed",
            IsDeny = false,
            SavedRights = SavedRightsState.DefaultForMode(false)
        };
        var untouchedEntry = new GrantedPathEntry
        {
            Path = @"C:\Untouched",
            IsDeny = false,
            SavedRights = SavedRightsState.DefaultForMode(false)
        };
        var pureMoveEntry = new GrantedPathEntry
        {
            Path = @"C:\PureMove",
            IsDeny = false,
            SavedRights = SavedRightsState.DefaultForMode(false)
        };

        var pending = new AclManagerPendingChanges();
        AddPendingGrant(pending, completedEntry);
        AddPendingGrant(pending, untouchedEntry);
        AddPendingGrantConfigMove(pending, completedEntry, @"C:\Configs\extra.rfn");
        AddPendingGrantConfigMove(pending, pureMoveEntry, @"C:\Configs\extra.rfn");
        var plan = new AclApplyPlanBuilder().Build(pending);
        var execution = new AclApplyExecutionResult
        {
            WasCanceled = true
        };
        execution.MarkCompleted(AclPendingOperationKind.GrantAdd, completedEntry.Path, completedEntry.IsDeny);

        var outcome = sut.Apply(plan, execution, pending, TestSid, isContainer: false);

        Assert.False(outcome.Succeeded);
        Assert.False(GetGrantSnapshot(pending).PendingAdds.ContainsKey((completedEntry.Path, completedEntry.IsDeny)));
        Assert.True(GetGrantSnapshot(pending).PendingAdds.ContainsKey((untouchedEntry.Path, untouchedEntry.IsDeny)));
        Assert.False(pending.Grants.TryGetPendingConfigMove(completedEntry.Path, completedEntry.IsDeny, out _));
        Assert.True(pending.Grants.TryGetPendingConfigMove(pureMoveEntry.Path, pureMoveEntry.IsDeny, out _));
        quickAccess.Verify(
            x => x.PinFolders(TestSid, It.Is<IReadOnlyList<string>>(paths => paths.Count == 1 && paths.Contains(completedEntry.Path))),
            Times.Once);
    }

    [Fact]
    public void Apply_CanceledAfterCompletedFix_KeepsConfigMovePending()
    {
        var sut = CreateSut(out _, out _, out _);
        var grantEntry = new GrantedPathEntry
        {
            Path = @"C:\GrantFixMove",
            IsDeny = false,
            SavedRights = SavedRightsState.DefaultForMode(false)
        };
        var traverseEntry = new GrantedPathEntry
        {
            Path = @"C:\TraverseFixMove",
            IsTraverseOnly = true
        };

        var pending = new AclManagerPendingChanges();
        AddPendingGrantFix(pending, grantEntry);
        AddPendingTraverseFix(pending, traverseEntry);
        AddPendingGrantConfigMove(pending, grantEntry, @"C:\Configs\extra.rfn");
        AddPendingTraverseConfigMove(pending, traverseEntry, @"C:\Configs\extra.rfn");
        var plan = new AclApplyPlanBuilder().Build(pending);
        var execution = new AclApplyExecutionResult
        {
            WasCanceled = true
        };
        execution.MarkCompleted(AclPendingOperationKind.GrantFix, grantEntry.Path, grantEntry.IsDeny);
        execution.MarkCompleted(AclPendingOperationKind.TraverseFix, traverseEntry.Path, null);

        var outcome = sut.Apply(plan, execution, pending, TestSid, isContainer: false);

        Assert.False(outcome.Succeeded);
        Assert.False(GetGrantSnapshot(pending).PendingGrantFixes.ContainsKey((grantEntry.Path, grantEntry.IsDeny)));
        Assert.False(GetTraverseSnapshot(pending).PendingFixes.ContainsKey(traverseEntry.Path));
        Assert.True(pending.Grants.TryGetPendingConfigMove(grantEntry.Path, grantEntry.IsDeny, out _));
        Assert.True(pending.Traverse.TryGetPendingTraverseConfigMove(traverseEntry.Path, out _));
        Assert.Empty(outcome.Errors);
        Assert.Empty(outcome.Warnings);
    }

    [Fact]
    public void Apply_CanceledAfterCompletedModification_RemovesConfigMovePending()
    {
        var sut = CreateSut(out _, out _, out _);
        var entry = new GrantedPathEntry
        {
            Path = @"C:\ModifiedMove",
            IsDeny = false,
            SavedRights = SavedRightsState.DefaultForMode(false)
        };

        var pending = new AclManagerPendingChanges();
        AddPendingModification(pending, entry, new PendingModification(
            entry,
            WasIsDeny: false,
            WasOwn: false,
            NewIsDeny: false,
            NewRights: entry.SavedRights,
            WasRights: entry.SavedRights));
        AddPendingGrantConfigMove(pending, entry, @"C:\Configs\extra.rfn");
        var plan = new AclApplyPlanBuilder().Build(pending);
        var execution = new AclApplyExecutionResult
        {
            WasCanceled = true
        };
        execution.MarkCompleted(AclPendingOperationKind.GrantModification, entry.Path, entry.IsDeny);

        var outcome = sut.Apply(plan, execution, pending, TestSid, isContainer: false);

        Assert.False(outcome.Succeeded);
        Assert.False(GetGrantSnapshot(pending).PendingModifications.ContainsKey((entry.Path, entry.IsDeny)));
        Assert.False(pending.Grants.TryGetPendingConfigMove(entry.Path, entry.IsDeny, out _));
        Assert.Empty(outcome.Errors);
        Assert.Empty(outcome.Warnings);
    }

    [Fact]
    public void Apply_FailedModification_KeepsConfigMovePendingWithoutRepeatingSelectedStoreSave()
    {
        var sut = CreateSut(out var mainStore, out var stores, out var _);
        var additionalStore = new TestGrantIntentStore(@"C:\Configs\extra.rfn");
        stores.AddLoadedStore(additionalStore);
        var entry = new GrantedPathEntry
        {
            Path = @"C:\Moved",
            IsDeny = false,
            SavedRights = SavedRightsState.DefaultForMode(false)
        };
        mainStore.AddEntry(TestSid, entry);

        var pending = new AclManagerPendingChanges();
        AddPendingGrantConfigMove(pending, entry, additionalStore.ConfigPath);
        AddPendingModification(pending, entry, new PendingModification(
            entry,
            WasIsDeny: false,
            WasOwn: false,
            NewIsDeny: false,
            NewRights: entry.SavedRights! with { Execute = true },
            WasRights: entry.SavedRights));
        var plan = new AclApplyPlanBuilder().Build(pending);

        var execution = new AclApplyExecutionResult();
        execution.Errors.Add(new AclApplyError(
            AclPendingOperationKind.GrantModification,
            entry.Path,
            false,
            CreateGrantOperationException(GrantApplyFailureStep.GrantAclApply, entry.Path, "save failed")));

        var outcome = sut.Apply(plan, execution, pending, TestSid, isContainer: false);

        Assert.False(outcome.Succeeded);
        Assert.True(pending.Grants.TryGetPendingConfigMove(entry.Path, entry.IsDeny, out _));
        Assert.Equal(0, mainStore.SaveCount);
        Assert.Equal(0, additionalStore.SaveCount);
        Assert.Single(mainStore.GetEntries(TestSid));
        Assert.Empty(additionalStore.GetEntries(TestSid));
    }

    [Fact]
    public void Apply_FailedModeSwitchWithDivergedNtfsMode_RetainsPendingByCommittedKey_AndSkipsPureMove()
    {
        var sut = CreateSut(out var mainStore, out var stores, out var _);
        var additionalStore = new TestGrantIntentStore(@"C:\Configs\extra.rfn");
        stores.AddLoadedStore(additionalStore);
        var entry = new GrantedPathEntry
        {
            Path = @"C:\SwitchFallback",
            IsDeny = false,
            SavedRights = SavedRightsState.DefaultForMode(false)
        };
        mainStore.AddEntry(TestSid, entry);

        var pending = new AclManagerPendingChanges();
        AddPendingGrantConfigMove(pending, entry, additionalStore.ConfigPath);
        AddPendingModification(pending, entry, new PendingModification(
            entry,
            WasIsDeny: true,
            WasOwn: false,
            NewIsDeny: true,
            NewRights: SavedRightsState.DefaultForMode(true),
            WasRights: entry.SavedRights));
        var plan = new AclApplyPlanBuilder().Build(pending);

        var execution = new AclApplyExecutionResult();
        execution.Errors.Add(new AclApplyError(
            AclPendingOperationKind.GrantModification,
            entry.Path,
            entry.IsDeny,
            CreateGrantOperationException(GrantApplyFailureStep.GrantAclApply, entry.Path, "acl failed")));

        var outcome = sut.Apply(plan, execution, pending, TestSid, isContainer: false);

        Assert.False(outcome.Succeeded);
        Assert.True(GetGrantSnapshot(pending).PendingModifications.ContainsKey((entry.Path, entry.IsDeny)));
        Assert.True(pending.Grants.TryGetPendingConfigMove(entry.Path, entry.IsDeny, out _));
        Assert.Equal(0, mainStore.SaveCount);
        Assert.Equal(0, additionalStore.SaveCount);
        Assert.Single(mainStore.GetEntries(TestSid));
        Assert.Empty(additionalStore.GetEntries(TestSid));
    }

    [Fact]
    public void Apply_FailedTraverseFix_KeepsCommittedTraverseConfigMovePending()
    {
        var sut = CreateSut(out var mainStore, out var stores, out var _);
        var additionalStore = new TestGrantIntentStore(@"C:\Configs\extra.rfn");
        stores.AddLoadedStore(additionalStore);
        var entry = new GrantedPathEntry
        {
            Path = @"C:\Traverse",
            IsTraverseOnly = true
        };
        mainStore.AddEntry(TestSid, entry);

        var pending = new AclManagerPendingChanges();
        AddPendingTraverseConfigMove(pending, entry, additionalStore.ConfigPath);
        AddPendingTraverseFix(pending, entry);
        var plan = new AclApplyPlanBuilder().Build(pending);

        var execution = new AclApplyExecutionResult();
        execution.Errors.Add(new AclApplyError(
            AclPendingOperationKind.TraverseFix,
            entry.Path,
            null,
            CreateGrantOperationException(GrantApplyFailureStep.FixTraverseAclApply, entry.Path, "fix failed")));

        var outcome = sut.Apply(plan, execution, pending, TestSid, isContainer: false);

        Assert.False(outcome.Succeeded);
        Assert.True(pending.Traverse.TryGetPendingTraverseConfigMove(entry.Path, out _));
        Assert.True(GetTraverseSnapshot(pending).PendingFixes.ContainsKey(entry.Path));
        Assert.Equal(0, mainStore.SaveCount);
        Assert.Equal(0, additionalStore.SaveCount);
        Assert.Single(mainStore.GetEntries(TestSid));
        Assert.Empty(additionalStore.GetEntries(TestSid));
    }

    [Fact]
    public void Apply_FailedGrantFix_KeepsCommittedGrantConfigMovePending()
    {
        var sut = CreateSut(out var mainStore, out var stores, out var _);
        var additionalStore = new TestGrantIntentStore(@"C:\Configs\extra.rfn");
        stores.AddLoadedStore(additionalStore);
        var entry = new GrantedPathEntry
        {
            Path = @"C:\GrantFix",
            IsDeny = false,
            SavedRights = SavedRightsState.DefaultForMode(false)
        };
        mainStore.AddEntry(TestSid, entry);

        var pending = new AclManagerPendingChanges();
        AddPendingGrantConfigMove(pending, entry, additionalStore.ConfigPath);
        AddPendingGrantFix(pending, entry);
        var plan = new AclApplyPlanBuilder().Build(pending);

        var execution = new AclApplyExecutionResult();
        execution.Errors.Add(new AclApplyError(
            AclPendingOperationKind.GrantFix,
            entry.Path,
            entry.IsDeny,
            CreateGrantOperationException(GrantApplyFailureStep.FixGrantAclApply, entry.Path, "fix failed")));

        var outcome = sut.Apply(plan, execution, pending, TestSid, isContainer: false);

        Assert.False(outcome.Succeeded);
        Assert.True(pending.Grants.TryGetPendingConfigMove(entry.Path, entry.IsDeny, out _));
        Assert.True(GetGrantSnapshot(pending).PendingGrantFixes.ContainsKey((entry.Path, entry.IsDeny)));
        Assert.Equal(0, mainStore.SaveCount);
        Assert.Equal(0, additionalStore.SaveCount);
        Assert.Single(mainStore.GetEntries(TestSid));
        Assert.Empty(additionalStore.GetEntries(TestSid));
    }

    [Fact]
    public void Apply_SuccessfulTraverseFix_MovesCommittedTraverseConfigOwnership()
    {
        var sut = CreateSut(out var mainStore, out var stores, out var _);
        var additionalStore = new TestGrantIntentStore(@"C:\Configs\extra.rfn");
        stores.AddLoadedStore(additionalStore);
        var entry = new GrantedPathEntry
        {
            Path = @"C:\TraverseSuccess",
            IsTraverseOnly = true
        };
        mainStore.AddEntry(TestSid, entry);

        var pending = new AclManagerPendingChanges();
        AddPendingTraverseConfigMove(pending, entry, additionalStore.ConfigPath);
        AddPendingTraverseFix(pending, entry);
        var plan = new AclApplyPlanBuilder().Build(pending);

        var outcome = sut.Apply(plan, CreateExecutionResultWithCompletedPlanOperations(plan), pending, TestSid, isContainer: false);

        Assert.True(outcome.Succeeded);
        Assert.False(pending.HasPendingChanges);
        Assert.Equal(1, mainStore.SaveCount);
        Assert.Equal(1, additionalStore.SaveCount);
        Assert.Empty(mainStore.GetEntries(TestSid));
        Assert.Single(additionalStore.GetEntries(TestSid));
        Assert.Empty(outcome.Warnings);
    }

    [Fact]
    public void Apply_SuccessfulGrantFix_MovesCommittedGrantConfigOwnership()
    {
        var sut = CreateSut(out var mainStore, out var stores, out var _);
        var additionalStore = new TestGrantIntentStore(@"C:\Configs\extra.rfn");
        stores.AddLoadedStore(additionalStore);
        var entry = new GrantedPathEntry
        {
            Path = @"C:\GrantFixSuccess",
            IsDeny = false,
            SavedRights = SavedRightsState.DefaultForMode(false)
        };
        mainStore.AddEntry(TestSid, entry);

        var pending = new AclManagerPendingChanges();
        AddPendingGrantConfigMove(pending, entry, additionalStore.ConfigPath);
        AddPendingGrantFix(pending, entry);
        var plan = new AclApplyPlanBuilder().Build(pending);

        var outcome = sut.Apply(plan, CreateExecutionResultWithCompletedPlanOperations(plan), pending, TestSid, isContainer: false);

        Assert.True(outcome.Succeeded);
        Assert.False(pending.HasPendingChanges);
        Assert.Equal(1, mainStore.SaveCount);
        Assert.Equal(1, additionalStore.SaveCount);
        Assert.Empty(mainStore.GetEntries(TestSid));
        Assert.Single(additionalStore.GetEntries(TestSid));
    }

    [Fact]
    public void Apply_PureGrantConfigMove_MovesOwnershipAndSavesAffectedStores()
    {
        var sut = CreateSut(out var mainStore, out var stores, out var _);
        var additionalStore = new TestGrantIntentStore(@"C:\Configs\extra.rfn");
        stores.AddLoadedStore(additionalStore);
        var entry = new GrantedPathEntry
        {
            Path = @"C:\GrantMove",
            IsDeny = false,
            SavedRights = SavedRightsState.DefaultForMode(false)
        };
        mainStore.AddEntry(TestSid, entry);

        var pending = new AclManagerPendingChanges();
        AddPendingGrantConfigMove(pending, entry, additionalStore.ConfigPath);
        var plan = new AclApplyPlanBuilder().Build(pending);

        var outcome = sut.Apply(plan, CreateExecutionResultWithCompletedPlanOperations(plan), pending, TestSid, isContainer: false);

        Assert.True(outcome.Succeeded);
        Assert.False(pending.HasPendingChanges);
        Assert.Equal(1, mainStore.SaveCount);
        Assert.Equal(1, additionalStore.SaveCount);
        Assert.Empty(mainStore.GetEntries(TestSid));
        Assert.Single(additionalStore.GetEntries(TestSid));
    }

    [Fact]
    public void Apply_PureGrantConfigMove_UsesFullEntryIdentityInsteadOfPathOnly()
    {
        var sut = CreateSut(out var mainStore, out var stores, out var _);
        var additionalStore = new TestGrantIntentStore(@"C:\Configs\extra.rfn");
        stores.AddLoadedStore(additionalStore);
        var otherEntry = new GrantedPathEntry
        {
            Path = @"C:\GrantMoveIdentity",
            IsDeny = false,
            SavedRights = SavedRightsState.DefaultForMode(false) with { Execute = true }
        };
        var targetEntry = new GrantedPathEntry
        {
            Path = @"C:\GrantMoveIdentity",
            IsDeny = false,
            SavedRights = SavedRightsState.DefaultForMode(false)
        };
        mainStore.AddEntry(TestSid, otherEntry);
        mainStore.AddEntry(TestSid, targetEntry);

        var pending = new AclManagerPendingChanges();
        AddPendingGrantConfigMove(pending, targetEntry, additionalStore.ConfigPath);
        var plan = new AclApplyPlanBuilder().Build(pending);

        var outcome = sut.Apply(plan, CreateExecutionResultWithCompletedPlanOperations(plan), pending, TestSid, isContainer: false);

        Assert.True(outcome.Succeeded);
        var remainingMain = mainStore.GetEntries(TestSid);
        var movedAdditional = additionalStore.GetEntries(TestSid);
        Assert.Single(remainingMain);
        Assert.Single(movedAdditional);
        Assert.True(remainingMain[0].SavedRights?.Execute);
        Assert.False(movedAdditional[0].SavedRights?.Execute);
    }

    [Fact]
    public void Apply_PureSharedTraverseConfigMove_MovesOwnershipAndSavesAffectedStores()
    {
        var sut = CreateSut(out var mainStore, out var stores, out var _);
        var additionalStore = new TestGrantIntentStore(@"C:\Configs\extra.rfn");
        stores.AddLoadedStore(additionalStore);
        var entry = new GrantedPathEntry
        {
            Path = @"C:\SharedTraverse",
            IsTraverseOnly = true
        };
        mainStore.AddEntry(WellKnownSecuritySids.AllApplicationPackagesSid, entry);

        var pending = new AclManagerPendingChanges();
        AddPendingTraverseConfigMove(pending, entry, additionalStore.ConfigPath);
        var plan = new AclApplyPlanBuilder().Build(pending);

        var outcome = sut.Apply(plan, CreateExecutionResultWithCompletedPlanOperations(plan), pending, ContainerSid, isContainer: true);

        Assert.True(outcome.Succeeded);
        Assert.False(pending.HasPendingChanges);
        Assert.Equal(1, mainStore.SaveCount);
        Assert.Equal(1, additionalStore.SaveCount);
        Assert.Empty(mainStore.GetEntries(WellKnownSecuritySids.AllApplicationPackagesSid));
        Assert.Single(additionalStore.GetEntries(WellKnownSecuritySids.AllApplicationPackagesSid));
    }

    [Fact]
    public void Apply_PureSharedTraverseConfigMove_DoesNotMoveOtherContainerTrackedEntry()
    {
        var sut = CreateSut(out var mainStore, out var stores, out var _);
        var additionalStore = new TestGrantIntentStore(@"C:\Configs\extra.rfn");
        stores.AddLoadedStore(additionalStore);
        var currentEntry = new GrantedPathEntry
        {
            Path = @"C:\SharedTraverse",
            IsTraverseOnly = true,
            SourceSids = [ContainerSid]
        };
        var otherEntry = new GrantedPathEntry
        {
            Path = @"C:\SharedTraverse",
            IsTraverseOnly = true,
            SourceSids = ["S-1-15-2-99-1-2-3-4-5-7"]
        };
        mainStore.AddEntry(WellKnownSecuritySids.AllApplicationPackagesSid, currentEntry);
        mainStore.AddEntry(WellKnownSecuritySids.AllApplicationPackagesSid, otherEntry);

        var pending = new AclManagerPendingChanges();
        AddPendingTraverseConfigMove(pending, currentEntry, additionalStore.ConfigPath);
        var plan = new AclApplyPlanBuilder().Build(pending);

        var outcome = sut.Apply(plan, CreateExecutionResultWithCompletedPlanOperations(plan), pending, ContainerSid, isContainer: true);

        Assert.True(outcome.Succeeded);
        Assert.Contains(mainStore.GetEntries(WellKnownSecuritySids.AllApplicationPackagesSid), entry =>
            entry.SourceSids?.Contains("S-1-15-2-99-1-2-3-4-5-7") == true);
        Assert.DoesNotContain(mainStore.GetEntries(WellKnownSecuritySids.AllApplicationPackagesSid), entry =>
            entry.SourceSids?.Contains(ContainerSid) == true);
        Assert.Contains(additionalStore.GetEntries(WellKnownSecuritySids.AllApplicationPackagesSid), entry =>
            entry.SourceSids?.Contains(ContainerSid) == true);
        Assert.DoesNotContain(additionalStore.GetEntries(WellKnownSecuritySids.AllApplicationPackagesSid), entry =>
            entry.SourceSids?.Contains("S-1-15-2-99-1-2-3-4-5-7") == true);
    }

    [Fact]
    public void Apply_PureGrantConfigMoveSaveFailure_RetainsPendingAndReturnsStructuredError()
    {
        var sut = CreateSut(out var mainStore, out var stores, out var log);
        var additionalStore = new TestGrantIntentStore(@"C:\Configs\extra.rfn")
        {
            SaveAction = () => throw new InvalidOperationException("save failed")
        };
        stores.AddLoadedStore(additionalStore);
        var entry = new GrantedPathEntry
        {
            Path = @"C:\GrantMoveFail",
            IsDeny = false
        };
        mainStore.AddEntry(TestSid, entry);

        var pending = new AclManagerPendingChanges();
        AddPendingGrantConfigMove(pending, entry, additionalStore.ConfigPath);
        var plan = new AclApplyPlanBuilder().Build(pending);

        var outcome = sut.Apply(plan, new AclApplyExecutionResult(), pending, TestSid, isContainer: false);

        var error = Assert.Single(outcome.Errors);
        Assert.False(outcome.Succeeded);
        Assert.True(pending.Grants.TryGetPendingConfigMove(entry.Path, entry.IsDeny, out _));
        Assert.Equal(GrantApplyFailureStep.GrantIntentSave, error.Step);
        Assert.Equal(additionalStore.ConfigPath, error.ConfigPath);
        Assert.Empty(outcome.Warnings);
        log.Verify(x => x.Error(
            GrantApplyFailureFormatter.Format(error.Step, error.Path, error.ConfigPath, error.Cause),
            error), Times.Once);
    }

    [Fact]
    public void Apply_Canceled_RetainsPendingAndSkipsPinning()
    {
        var quickAccess = new Mock<IQuickAccessPinService>();
        var sut = CreateSut(out _, out _, out _,
            quickAccessPinService: quickAccess.Object);

        var pending = new AclManagerPendingChanges();
        AddPendingGrant(pending, new GrantedPathEntry { Path = @"C:\Cancel", IsDeny = false });
        var plan = new AclApplyPlanBuilder().Build(pending);
        var execution = new AclApplyExecutionResult { WasCanceled = true };

        var outcome = sut.Apply(plan, execution, pending, TestSid, isContainer: false);

        Assert.False(outcome.Succeeded);
        Assert.True(pending.Grants.IsPendingAdd(@"C:\Cancel", false));
        quickAccess.Verify(x => x.PinFolders(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()), Times.Never);
        quickAccess.Verify(x => x.UnpinFolders(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()), Times.Never);
    }

    [Fact]
    public void Apply_FatalFailure_PrunesOnlyConfirmedCompletionsAndSkipsPureConfigMoves()
    {
        var sut = CreateSut(out var mainStore, out var stores, out _);
        var additionalStore = new TestGrantIntentStore(@"C:\Configs\extra.rfn");
        stores.AddLoadedStore(additionalStore);
        var completedEntry = new GrantedPathEntry
        {
            Path = @"C:\Completed",
            IsDeny = false,
            SavedRights = SavedRightsState.DefaultForMode(false)
        };
        var fatalEntry = new GrantedPathEntry
        {
            Path = @"C:\Fatal",
            IsDeny = false,
            SavedRights = SavedRightsState.DefaultForMode(false)
        };
        var pureMoveEntry = new GrantedPathEntry
        {
            Path = @"C:\PureMove",
            IsDeny = false,
            SavedRights = SavedRightsState.DefaultForMode(false)
        };
        mainStore.AddEntry(TestSid, pureMoveEntry);

        var pending = new AclManagerPendingChanges();
        AddPendingGrant(pending, completedEntry);
        AddPendingGrant(pending, fatalEntry);
        AddPendingGrantConfigMove(pending, completedEntry, additionalStore.ConfigPath);
        AddPendingGrantConfigMove(pending, fatalEntry, additionalStore.ConfigPath);
        AddPendingGrantConfigMove(pending, pureMoveEntry, additionalStore.ConfigPath);
        var plan = new AclApplyPlanBuilder().Build(pending);
        var execution = new AclApplyExecutionResult();
        execution.MarkCompleted(AclPendingOperationKind.GrantAdd, completedEntry.Path, completedEntry.IsDeny);
        execution.SetFatalFailure(new AclApplyFatalFailure(
            AclPendingOperationKind.GrantAdd,
            fatalEntry.Path,
            fatalEntry.IsDeny,
            CreateGrantOperationException(GrantApplyFailureStep.GrantAclApply, fatalEntry.Path, "fatal")));

        var outcome = sut.Apply(plan, execution, pending, TestSid, isContainer: false);

        var error = Assert.Single(outcome.Errors);
        Assert.False(outcome.Succeeded);
        Assert.Equal(fatalEntry.Path, error.Path);
        Assert.False(GetGrantSnapshot(pending).PendingAdds.ContainsKey((completedEntry.Path, completedEntry.IsDeny)));
        Assert.True(GetGrantSnapshot(pending).PendingAdds.ContainsKey((fatalEntry.Path, fatalEntry.IsDeny)));
        Assert.False(pending.Grants.TryGetPendingConfigMove(completedEntry.Path, completedEntry.IsDeny, out _));
        Assert.True(pending.Grants.TryGetPendingConfigMove(fatalEntry.Path, fatalEntry.IsDeny, out _));
        Assert.True(pending.Grants.TryGetPendingConfigMove(pureMoveEntry.Path, pureMoveEntry.IsDeny, out _));
        Assert.Equal(0, mainStore.SaveCount);
        Assert.Equal(0, additionalStore.SaveCount);
    }

    private static AclApplyPostProcessor CreateSut(
        out TestGrantIntentStore mainStore,
        out TestGrantIntentStoreProvider stores,
        out Mock<ILoggingService> log,
        IQuickAccessPinService? quickAccessPinService = null)
    {
        var ownershipProjection = new GrantIntentOwnershipProjectionService();
        mainStore = new TestGrantIntentStore { OwnershipProjectionService = ownershipProjection };
        stores = new TestGrantIntentStoreProvider(mainStore, ownershipProjection);
        log = new Mock<ILoggingService>();
        var postProcessingPolicy = new AclApplyPostProcessingPolicy(PhaseCatalog);
        return new AclApplyPostProcessor(
            log.Object,
            new GrantIntentRepository(stores),
            stores,
            quickAccessPinService ?? new Mock<IQuickAccessPinService>().Object,
            new TraverseGrantOwnerResolver(),
            PhaseCatalog,
            postProcessingPolicy);
    }

    private static GrantOperationException CreateGrantOperationException(
        GrantApplyFailureStep step,
        string path,
        string message)
        => new(step, path, configPath: null, new InvalidOperationException(message));

    private static AclApplyExecutionResult CreateExecutionResultWithCompletedPlanOperations(AclApplyPlan plan)
    {
        var result = new AclApplyExecutionResult();

        foreach (var descriptor in PhaseCatalog.OrderedPhases)
        {
            switch (descriptor.Phase)
            {
                case AclApplyPhase.GrantAdd:
                    foreach (var entry in plan.PendingAdds)
                        result.MarkCompleted(descriptor.OperationKind, entry.Path, entry.IsDeny);
                    break;
                case AclApplyPhase.GrantRemove:
                    foreach (var entry in plan.PendingRemoves)
                        result.MarkCompleted(descriptor.OperationKind, entry.Path, entry.IsDeny);
                    break;
                case AclApplyPhase.GrantModification:
                    foreach (var modification in plan.PendingModifications)
                        result.MarkCompleted(descriptor.OperationKind, modification.Entry.Path, modification.Entry.IsDeny);
                    break;
                case AclApplyPhase.GrantFix:
                    foreach (var entry in plan.PendingGrantFixes)
                        result.MarkCompleted(descriptor.OperationKind, entry.Path, entry.IsDeny);
                    break;
                case AclApplyPhase.TraverseAdd:
                    foreach (var entry in plan.PendingTraverseAdds)
                        result.MarkCompleted(descriptor.OperationKind, entry.Path, null);
                    break;
                case AclApplyPhase.TraverseRemove:
                    foreach (var entry in plan.PendingTraverseRemoves)
                        result.MarkCompleted(descriptor.OperationKind, entry.Path, null);
                    break;
                case AclApplyPhase.TraverseFix:
                    foreach (var entry in plan.PendingTraverseFixes)
                        result.MarkCompleted(descriptor.OperationKind, entry.Path, null);
                    break;
                case AclApplyPhase.GrantUntrack:
                    foreach (var entry in plan.PendingUntrackGrants)
                        result.MarkCompleted(descriptor.OperationKind, entry.Path, entry.IsDeny);
                    break;
                case AclApplyPhase.TraverseUntrack:
                    foreach (var entry in plan.PendingUntrackTraverse)
                        result.MarkCompleted(descriptor.OperationKind, entry.Path, null);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        return result;
    }
}
