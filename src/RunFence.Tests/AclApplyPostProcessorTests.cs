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

    [Fact]
    public void Apply_AllSuccess_ClearsPending_PinsUnpins_WithoutRepeatedStoreSave()
    {
        var quickAccess = new Mock<IQuickAccessPinService>();
        var sut = CreateSut(out var _, out var _, out var _,
            quickAccessPinService: quickAccess.Object);

        var pending = new AclManagerPendingChanges();
        pending.PendingAdds[(@"C:\Add", false)] = new GrantedPathEntry
        {
            Path = @"C:\Add",
            IsDeny = false,
            SavedRights = SavedRightsState.DefaultForMode(false)
        };
        pending.PendingRemoves[(@"C:\Remove", false)] = new GrantedPathEntry
        {
            Path = @"C:\Remove",
            IsDeny = false
        };
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
        pending.PendingAdds[(@"C:\Fail", false)] = new GrantedPathEntry { Path = @"C:\Fail", IsDeny = false };
        pending.PendingAdds[(@"C:\Ok", false)] = new GrantedPathEntry { Path = @"C:\Ok", IsDeny = false };
        pending.PendingConfigMoves[(@"C:\Ok", false)] =
            new PendingConfigMove(new GrantedPathEntry { Path = @"C:\Ok", IsDeny = false }, "extra.rfn");
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
        Assert.True(pending.IsPendingAdd(@"C:\Fail", false));
        Assert.False(pending.IsPendingAdd(@"C:\Ok", false));
        Assert.False(pending.PendingConfigMoves.ContainsKey((@"C:\Ok", false)));
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
        pending.PendingRemoves[(@"C:\Warning", false)] = new GrantedPathEntry { Path = @"C:\Warning", IsDeny = false };
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
        pending.PendingAdds[(completedEntry.Path, completedEntry.IsDeny)] = completedEntry;
        pending.PendingAdds[(untouchedEntry.Path, untouchedEntry.IsDeny)] = untouchedEntry;
        pending.PendingConfigMoves[(completedEntry.Path, completedEntry.IsDeny)] =
            new PendingConfigMove(completedEntry, @"C:\Configs\extra.rfn");
        pending.PendingConfigMoves[(pureMoveEntry.Path, pureMoveEntry.IsDeny)] =
            new PendingConfigMove(pureMoveEntry, @"C:\Configs\extra.rfn");
        var plan = new AclApplyPlanBuilder().Build(pending);
        var execution = new AclApplyExecutionResult
        {
            WasCanceled = true
        };
        execution.MarkCompleted(AclPendingOperationKind.GrantAdd, completedEntry.Path, completedEntry.IsDeny);

        var outcome = sut.Apply(plan, execution, pending, TestSid, isContainer: false);

        Assert.False(outcome.Succeeded);
        Assert.False(pending.PendingAdds.ContainsKey((completedEntry.Path, completedEntry.IsDeny)));
        Assert.True(pending.PendingAdds.ContainsKey((untouchedEntry.Path, untouchedEntry.IsDeny)));
        Assert.False(pending.PendingConfigMoves.ContainsKey((completedEntry.Path, completedEntry.IsDeny)));
        Assert.True(pending.PendingConfigMoves.ContainsKey((pureMoveEntry.Path, pureMoveEntry.IsDeny)));
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
        pending.PendingGrantFixes[(grantEntry.Path, grantEntry.IsDeny)] = grantEntry;
        pending.PendingTraverseFixes[traverseEntry.Path] = traverseEntry;
        pending.PendingConfigMoves[(grantEntry.Path, grantEntry.IsDeny)] =
            new PendingConfigMove(grantEntry, @"C:\Configs\extra.rfn");
        pending.PendingTraverseConfigMoves[traverseEntry.Path] =
            new PendingConfigMove(traverseEntry, @"C:\Configs\extra.rfn");
        var plan = new AclApplyPlanBuilder().Build(pending);
        var execution = new AclApplyExecutionResult
        {
            WasCanceled = true
        };
        execution.MarkCompleted(AclPendingOperationKind.GrantFix, grantEntry.Path, grantEntry.IsDeny);
        execution.MarkCompleted(AclPendingOperationKind.TraverseFix, traverseEntry.Path, null);

        var outcome = sut.Apply(plan, execution, pending, TestSid, isContainer: false);

        Assert.False(outcome.Succeeded);
        Assert.False(pending.PendingGrantFixes.ContainsKey((grantEntry.Path, grantEntry.IsDeny)));
        Assert.False(pending.PendingTraverseFixes.ContainsKey(traverseEntry.Path));
        Assert.True(pending.PendingConfigMoves.ContainsKey((grantEntry.Path, grantEntry.IsDeny)));
        Assert.True(pending.PendingTraverseConfigMoves.ContainsKey(traverseEntry.Path));
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
        pending.PendingModifications[(entry.Path, entry.IsDeny)] = new PendingModification(
            entry,
            WasIsDeny: false,
            WasOwn: false,
            NewIsDeny: false,
            NewRights: entry.SavedRights,
            WasRights: entry.SavedRights);
        pending.PendingConfigMoves[(entry.Path, entry.IsDeny)] =
            new PendingConfigMove(entry, @"C:\Configs\extra.rfn");
        var plan = new AclApplyPlanBuilder().Build(pending);
        var execution = new AclApplyExecutionResult
        {
            WasCanceled = true
        };
        execution.MarkCompleted(AclPendingOperationKind.GrantModification, entry.Path, entry.IsDeny);

        var outcome = sut.Apply(plan, execution, pending, TestSid, isContainer: false);

        Assert.False(outcome.Succeeded);
        Assert.False(pending.PendingModifications.ContainsKey((entry.Path, entry.IsDeny)));
        Assert.False(pending.PendingConfigMoves.ContainsKey((entry.Path, entry.IsDeny)));
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
        pending.PendingConfigMoves[(entry.Path, entry.IsDeny)] =
            new PendingConfigMove(entry, additionalStore.ConfigPath);
        pending.PendingModifications[(entry.Path, entry.IsDeny)] = new PendingModification(
            entry,
            WasIsDeny: false,
            WasOwn: false,
            NewIsDeny: false,
            NewRights: entry.SavedRights! with { Execute = true },
            WasRights: entry.SavedRights);
        var plan = new AclApplyPlanBuilder().Build(pending);

        var execution = new AclApplyExecutionResult();
        execution.Errors.Add(new AclApplyError(
            AclPendingOperationKind.GrantModification,
            entry.Path,
            false,
            CreateGrantOperationException(GrantApplyFailureStep.GrantAclApply, entry.Path, "save failed")));

        var outcome = sut.Apply(plan, execution, pending, TestSid, isContainer: false);

        Assert.False(outcome.Succeeded);
        Assert.True(pending.PendingConfigMoves.ContainsKey((entry.Path, entry.IsDeny)));
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
        pending.PendingConfigMoves[(entry.Path, entry.IsDeny)] =
            new PendingConfigMove(entry, additionalStore.ConfigPath);
        pending.PendingModifications[(entry.Path, entry.IsDeny)] = new PendingModification(
            entry,
            WasIsDeny: true,
            WasOwn: false,
            NewIsDeny: true,
            NewRights: SavedRightsState.DefaultForMode(true),
            WasRights: entry.SavedRights);
        var plan = new AclApplyPlanBuilder().Build(pending);

        var execution = new AclApplyExecutionResult();
        execution.Errors.Add(new AclApplyError(
            AclPendingOperationKind.GrantModification,
            entry.Path,
            entry.IsDeny,
            CreateGrantOperationException(GrantApplyFailureStep.GrantAclApply, entry.Path, "acl failed")));

        var outcome = sut.Apply(plan, execution, pending, TestSid, isContainer: false);

        Assert.False(outcome.Succeeded);
        Assert.True(pending.PendingModifications.ContainsKey((entry.Path, entry.IsDeny)));
        Assert.True(pending.PendingConfigMoves.ContainsKey((entry.Path, entry.IsDeny)));
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
        pending.PendingTraverseConfigMoves[entry.Path] =
            new PendingConfigMove(entry, additionalStore.ConfigPath);
        pending.PendingTraverseFixes[entry.Path] = entry;
        var plan = new AclApplyPlanBuilder().Build(pending);

        var execution = new AclApplyExecutionResult();
        execution.Errors.Add(new AclApplyError(
            AclPendingOperationKind.TraverseFix,
            entry.Path,
            null,
            CreateGrantOperationException(GrantApplyFailureStep.FixTraverseAclApply, entry.Path, "fix failed")));

        var outcome = sut.Apply(plan, execution, pending, TestSid, isContainer: false);

        Assert.False(outcome.Succeeded);
        Assert.True(pending.PendingTraverseConfigMoves.ContainsKey(entry.Path));
        Assert.True(pending.PendingTraverseFixes.ContainsKey(entry.Path));
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
        pending.PendingConfigMoves[(entry.Path, entry.IsDeny)] =
            new PendingConfigMove(entry, additionalStore.ConfigPath);
        pending.PendingGrantFixes[(entry.Path, entry.IsDeny)] = entry;
        var plan = new AclApplyPlanBuilder().Build(pending);

        var execution = new AclApplyExecutionResult();
        execution.Errors.Add(new AclApplyError(
            AclPendingOperationKind.GrantFix,
            entry.Path,
            entry.IsDeny,
            CreateGrantOperationException(GrantApplyFailureStep.FixGrantAclApply, entry.Path, "fix failed")));

        var outcome = sut.Apply(plan, execution, pending, TestSid, isContainer: false);

        Assert.False(outcome.Succeeded);
        Assert.True(pending.PendingConfigMoves.ContainsKey((entry.Path, entry.IsDeny)));
        Assert.True(pending.PendingGrantFixes.ContainsKey((entry.Path, entry.IsDeny)));
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
        pending.PendingTraverseConfigMoves[entry.Path] =
            new PendingConfigMove(entry, additionalStore.ConfigPath);
        pending.PendingTraverseFixes[entry.Path] = entry;
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
        pending.PendingConfigMoves[(entry.Path, entry.IsDeny)] =
            new PendingConfigMove(entry, additionalStore.ConfigPath);
        pending.PendingGrantFixes[(entry.Path, entry.IsDeny)] = entry;
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
        pending.PendingConfigMoves[(entry.Path, entry.IsDeny)] =
            new PendingConfigMove(entry, additionalStore.ConfigPath);
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
        pending.PendingConfigMoves[(targetEntry.Path, targetEntry.IsDeny)] =
            new PendingConfigMove(targetEntry, additionalStore.ConfigPath);
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
        pending.PendingTraverseConfigMoves[entry.Path] =
            new PendingConfigMove(entry, additionalStore.ConfigPath);
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
        pending.PendingTraverseConfigMoves[currentEntry.Path] =
            new PendingConfigMove(currentEntry, additionalStore.ConfigPath);
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
        pending.PendingConfigMoves[(entry.Path, entry.IsDeny)] =
            new PendingConfigMove(entry, additionalStore.ConfigPath);
        var plan = new AclApplyPlanBuilder().Build(pending);

        var outcome = sut.Apply(plan, new AclApplyExecutionResult(), pending, TestSid, isContainer: false);

        var error = Assert.Single(outcome.Errors);
        Assert.False(outcome.Succeeded);
        Assert.True(pending.PendingConfigMoves.ContainsKey((entry.Path, entry.IsDeny)));
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
        pending.PendingAdds[(@"C:\Cancel", false)] = new GrantedPathEntry { Path = @"C:\Cancel", IsDeny = false };
        var plan = new AclApplyPlanBuilder().Build(pending);
        var execution = new AclApplyExecutionResult { WasCanceled = true };

        var outcome = sut.Apply(plan, execution, pending, TestSid, isContainer: false);

        Assert.False(outcome.Succeeded);
        Assert.True(pending.IsPendingAdd(@"C:\Cancel", false));
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
        pending.PendingAdds[(completedEntry.Path, completedEntry.IsDeny)] = completedEntry;
        pending.PendingAdds[(fatalEntry.Path, fatalEntry.IsDeny)] = fatalEntry;
        pending.PendingConfigMoves[(completedEntry.Path, completedEntry.IsDeny)] =
            new PendingConfigMove(completedEntry, additionalStore.ConfigPath);
        pending.PendingConfigMoves[(fatalEntry.Path, fatalEntry.IsDeny)] =
            new PendingConfigMove(fatalEntry, additionalStore.ConfigPath);
        pending.PendingConfigMoves[(pureMoveEntry.Path, pureMoveEntry.IsDeny)] =
            new PendingConfigMove(pureMoveEntry, additionalStore.ConfigPath);
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
        Assert.False(pending.PendingAdds.ContainsKey((completedEntry.Path, completedEntry.IsDeny)));
        Assert.True(pending.PendingAdds.ContainsKey((fatalEntry.Path, fatalEntry.IsDeny)));
        Assert.False(pending.PendingConfigMoves.ContainsKey((completedEntry.Path, completedEntry.IsDeny)));
        Assert.True(pending.PendingConfigMoves.ContainsKey((fatalEntry.Path, fatalEntry.IsDeny)));
        Assert.True(pending.PendingConfigMoves.ContainsKey((pureMoveEntry.Path, pureMoveEntry.IsDeny)));
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
        return new AclApplyPostProcessor(
            log.Object,
            new GrantIntentRepository(stores),
            stores,
            quickAccessPinService ?? new Mock<IQuickAccessPinService>().Object,
            new TraverseGrantOwnerResolver());
    }

    private static GrantOperationException CreateGrantOperationException(
        GrantApplyFailureStep step,
        string path,
        string message)
        => new(step, path, configPath: null, new InvalidOperationException(message));

    private static AclApplyExecutionResult CreateExecutionResultWithCompletedPlanOperations(AclApplyPlan plan)
    {
        var result = new AclApplyExecutionResult();

        foreach (var entry in plan.PendingAdds)
            result.MarkCompleted(AclPendingOperationKind.GrantAdd, entry.Path, entry.IsDeny);

        foreach (var entry in plan.PendingRemoves)
            result.MarkCompleted(AclPendingOperationKind.GrantRemove, entry.Path, entry.IsDeny);

        foreach (var modification in plan.PendingModifications)
            result.MarkCompleted(AclPendingOperationKind.GrantModification, modification.Entry.Path, modification.Entry.IsDeny);

        foreach (var entry in plan.PendingGrantFixes)
            result.MarkCompleted(AclPendingOperationKind.GrantFix, entry.Path, entry.IsDeny);

        foreach (var entry in plan.PendingTraverseAdds)
            result.MarkCompleted(AclPendingOperationKind.TraverseAdd, entry.Path, null);

        foreach (var entry in plan.PendingTraverseRemoves)
            result.MarkCompleted(AclPendingOperationKind.TraverseRemove, entry.Path, null);

        foreach (var entry in plan.PendingTraverseFixes)
            result.MarkCompleted(AclPendingOperationKind.TraverseFix, entry.Path, null);

        foreach (var entry in plan.PendingUntrackGrants)
            result.MarkCompleted(AclPendingOperationKind.GrantUntrack, entry.Path, entry.IsDeny);

        foreach (var entry in plan.PendingUntrackTraverse)
            result.MarkCompleted(AclPendingOperationKind.TraverseUntrack, entry.Path, null);

        return result;
    }
}
