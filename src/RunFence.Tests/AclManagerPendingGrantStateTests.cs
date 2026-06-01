using Moq;
using RunFence.Acl.UI;
using RunFence.Core.Models;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public class AclManagerPendingGrantStateTests
{
    private readonly AclManagerPendingChanges _pending = new();

    [Fact]
    public void AddThenModify_StaysInPendingAddsOnly()
    {
        var entry = AclManagerPendingChangesTestData.MakeEntry(@"C:\Foo");
        _pending.Grants.AddGrant(entry);

        entry.SavedRights = new SavedRightsState(Execute: true, Write: false, Read: true, Special: false, Own: false);

        Assert.True(_pending.Grants.IsPendingAdd(@"C:\Foo", isDeny: false));
        Assert.False(_pending.Grants.IsPendingModification(@"C:\Foo", isDeny: false));
        Assert.True(_pending.Grants.FindPendingAdd(@"C:\Foo", isDeny: false)!.SavedRights!.Execute);
    }

    [Fact]
    public void PendingAdds_DifferentCasing_OverwritesEntry()
    {
        var entry1 = AclManagerPendingChangesTestData.MakeEntry(@"C:\Foo");
        var entry2 = AclManagerPendingChangesTestData.MakeEntry(@"C:\foo");
        _pending.Grants.AddGrant(entry1);
        _pending.Grants.AddGrant(entry2);

        Assert.Single(_pending.Grants.GetSnapshot().PendingAdds);
        Assert.Same(entry2, _pending.Grants.FindPendingAdd(@"C:\Foo", isDeny: false));
    }

    [Fact]
    public void ExistsInDbOrPending_InDbNotPendingRemove_ReturnsTrue()
    {
        const string sid = "S-1-5-21-1";
        var db = AclManagerPendingChangesTestData.MakeDbWithGrant(sid, @"C:\Foo", isDeny: false);

        Assert.True(_pending.Grants.ExistsInDbOrPending(db, sid, @"C:\Foo", isDeny: false));
    }

    [Fact]
    public void ExistsInDbOrPending_InDbButPendingRemove_ReturnsFalse()
    {
        const string sid = "S-1-5-21-1";
        var db = AclManagerPendingChangesTestData.MakeDbWithGrant(sid, @"C:\Foo", isDeny: false);
        _pending.Grants.MarkGrantForRemoval(AclManagerPendingChangesTestData.MakeEntry(@"C:\Foo"));

        Assert.False(_pending.Grants.ExistsInDbOrPending(db, sid, @"C:\Foo", isDeny: false));
    }

    [Fact]
    public void ExistsInDbOrPending_TraverseOnlyInDb_ReturnsFalse()
    {
        const string sid = "S-1-5-21-1";
        var db = AclManagerPendingChangesTestData.MakeDbWithGrant(sid, @"C:\Foo", isDeny: false, isTraverseOnly: true);

        Assert.False(_pending.Grants.ExistsInDbOrPending(db, sid, @"C:\Foo", isDeny: false));
    }

    [Fact]
    public void ExistsInDbOrPending_OppositeModeInDb_ReturnsFalse()
    {
        const string sid = "S-1-5-21-1";
        var db = AclManagerPendingChangesTestData.MakeDbWithGrant(sid, @"C:\Foo", isDeny: true);

        Assert.False(_pending.Grants.ExistsInDbOrPending(db, sid, @"C:\Foo", isDeny: false));
    }

    [Fact]
    public void ExistsInDbOrPending_CaseInsensitivePath_ReturnsTrue()
    {
        const string sid = "S-1-5-21-1";
        var db = AclManagerPendingChangesTestData.MakeDbWithGrant(sid, @"C:\Foo\Bar", isDeny: false);

        Assert.True(_pending.Grants.ExistsInDbOrPending(db, sid, @"c:\foo\bar", isDeny: false));
    }

    [Fact]
    public void GetEffectiveIsDeny_PendingModeSwitchToDeny_ReturnsNewIsDeny()
    {
        var entry = AclManagerPendingChangesTestData.MakeEntry(@"C:\Foo");
        _pending.Grants.ModifyGrant(entry, CreateModification(entry, newIsDeny: true));

        Assert.True(_pending.Grants.GetEffectiveIsDeny(entry));
    }

    [Fact]
    public void GetEffectiveIsDeny_CommittedEntryMutatedToNewMode_StillFindsPendingModification()
    {
        var entry = AclManagerPendingChangesTestData.MakeEntry(@"C:\Foo", isDeny: true);
        _pending.Grants.ModifyGrant(
            entry,
            new PendingModification(
                entry,
                WasIsDeny: false,
                WasOwn: false,
                NewIsDeny: true,
                NewRights: SavedRightsState.DefaultForMode(true),
                WasRights: SavedRightsState.DefaultForMode(false)));

        Assert.True(_pending.Grants.GetEffectiveIsDeny(entry));
        Assert.True(_pending.Grants.IsPendingModification(@"C:\Foo", true));
    }

    [Fact]
    public void GetEffectiveRights_PendingModWithNewRights_ReturnsNewRights()
    {
        var entry = AclManagerPendingChangesTestData.MakeEntry(@"C:\Foo");
        entry.SavedRights = new SavedRightsState(Execute: false, Write: false, Read: true, Special: false, Own: false);
        var newRights = new SavedRightsState(Execute: true, Write: true, Read: true, Special: false, Own: false);
        _pending.Grants.ModifyGrant(entry, CreateModification(entry, newIsDeny: false, newRights: newRights));

        Assert.Equal(newRights, _pending.Grants.GetEffectiveRights(entry));
    }

    [Fact]
    public void GetEffectiveRights_PendingModWithNullNewRights_FallsBackToEntrySavedRights()
    {
        var entry = AclManagerPendingChangesTestData.MakeEntry(@"C:\Foo");
        var originalRights = new SavedRightsState(Execute: false, Write: false, Read: true, Special: false, Own: false);
        entry.SavedRights = originalRights;
        _pending.Grants.ModifyGrant(entry, CreateModification(entry, newIsDeny: true));

        Assert.Equal(originalRights, _pending.Grants.GetEffectiveRights(entry));
    }

    [Fact]
    public void IsPendingConfigMove_ReKeyedByModeSwitch_ReturnsTrue()
    {
        var entry = AclManagerPendingChangesTestData.MakeEntry(@"C:\Foo");
        _pending.Grants.ModifyGrant(entry, CreateModification(entry, newIsDeny: true));
        _pending.Grants.MoveGrantConfig(AclManagerPendingChangesTestData.MakeEntry(@"C:\Foo", isDeny: true), "extra.rfn");

        Assert.True(_pending.Grants.IsPendingConfigMove(@"C:\Foo", isDeny: false));
    }

    [Fact]
    public void IsPendingConfigMove_CommittedEntryMutatedToNewMode_StillFindsRekeyedMove()
    {
        var entry = AclManagerPendingChangesTestData.MakeEntry(@"C:\Foo", isDeny: true);
        _pending.Grants.ModifyGrant(
            entry,
            new PendingModification(
                entry,
                WasIsDeny: false,
                WasOwn: false,
                NewIsDeny: true,
                NewRights: SavedRightsState.DefaultForMode(true),
                WasRights: SavedRightsState.DefaultForMode(false)));
        _pending.Grants.MoveGrantConfig(AclManagerPendingChangesTestData.MakeEntry(@"C:\Foo", isDeny: true), "extra.rfn");

        Assert.True(_pending.Grants.IsPendingConfigMove(@"C:\Foo", isDeny: true));
        Assert.True(_pending.Grants.IsPendingConfigMove(@"C:\Foo", isDeny: false));
    }

    [Fact]
    public void AllowDenyAllowRevert_RemovingPendingModification_RestoresCommittedModeAndRights()
    {
        var entry = AclManagerPendingChangesTestData.MakeEntry(@"C:\Foo", isDeny: false);
        var originalRights = SavedRightsState.DefaultForMode(false);
        entry.SavedRights = originalRights;

        _pending.Grants.ModifyGrant(
            entry,
            new PendingModification(
                entry,
                WasIsDeny: false,
                WasOwn: false,
                NewIsDeny: true,
                NewRights: SavedRightsState.DefaultForMode(true),
                WasRights: originalRights));
        _pending.Grants.MoveGrantConfig(AclManagerPendingChangesTestData.MakeEntry(entry.Path, isDeny: true), "extra.rfn");

        _pending.Grants.RemoveGrantModification(entry.Path, false, out _);
        _pending.Grants.RemoveGrantConfigMove(entry.Path, true, out _);
        _pending.Grants.MoveGrantConfig(AclManagerPendingChangesTestData.MakeEntry(entry.Path, isDeny: false), "extra.rfn");

        Assert.False(_pending.Grants.GetEffectiveIsDeny(entry));
        Assert.Equal(originalRights, _pending.Grants.GetEffectiveRights(entry));
        Assert.False(_pending.Grants.IsPendingModification(entry.Path, false));
        Assert.True(_pending.Grants.IsPendingConfigMove(entry.Path, false));
    }

    [Fact]
    public void DenyAllowDenyRevert_RemovingPendingModification_RestoresCommittedModeAndRights()
    {
        var entry = AclManagerPendingChangesTestData.MakeEntry(@"C:\Foo", isDeny: true);
        var originalRights = SavedRightsState.DefaultForMode(true);
        entry.SavedRights = originalRights;

        _pending.Grants.ModifyGrant(
            entry,
            new PendingModification(
                entry,
                WasIsDeny: true,
                WasOwn: false,
                NewIsDeny: false,
                NewRights: SavedRightsState.DefaultForMode(false),
                WasRights: originalRights));
        _pending.Grants.MoveGrantConfig(AclManagerPendingChangesTestData.MakeEntry(entry.Path, isDeny: false), "extra.rfn");

        _pending.Grants.RemoveGrantModification(entry.Path, true, out _);
        _pending.Grants.RemoveGrantConfigMove(entry.Path, false, out _);
        _pending.Grants.MoveGrantConfig(AclManagerPendingChangesTestData.MakeEntry(entry.Path, isDeny: true), "extra.rfn");

        Assert.True(_pending.Grants.GetEffectiveIsDeny(entry));
        Assert.Equal(originalRights, _pending.Grants.GetEffectiveRights(entry));
        Assert.False(_pending.Grants.IsPendingModification(entry.Path, true));
        Assert.True(_pending.Grants.IsPendingConfigMove(entry.Path, true));
    }

    [Fact]
    public void TrackModeChange_DoubleSwitch_RekeysPendingConfigMoveBackToCommittedMode()
    {
        var helper = new AclManagerPendingStateHelper();
        helper.Initialize(_pending);
        var entry = AclManagerPendingChangesTestData.MakeEntry(@"C:\Foo");
        entry.SavedRights = SavedRightsState.DefaultForMode(false);
        _pending.Grants.MoveGrantConfig(entry, "extra.rfn");

        helper.ComputePendingModification(entry, newIsDeny: true, SavedRightsState.DefaultForMode(true));
        helper.TrackModeChange(entry, newIsDeny: true);
        Assert.True(_pending.Grants.TryGetPendingConfigMove(entry.Path, true, out _));

        helper.ComputePendingModification(entry, newIsDeny: false, SavedRightsState.DefaultForMode(false));
        helper.TrackModeChange(entry, newIsDeny: false);

        Assert.True(_pending.Grants.TryGetPendingConfigMove(entry.Path, false, out var move));
        Assert.Equal("extra.rfn", move!.TargetConfigPath);
    }

    [Fact]
    public void PendingModification_KeyIsCaseInsensitive_ForLookupAndRemoval()
    {
        var entry = AclManagerPendingChangesTestData.MakeEntry(@"C:\Mixed\Path");
        var modification = CreateModification(entry, newIsDeny: false);
        _pending.Grants.ModifyGrant(entry, modification);

        Assert.True(_pending.Grants.TryGetPendingModification(@"c:\mixed\path", false, out var found));
        Assert.Equal(entry.Path, found!.Entry.Path);
        Assert.True(_pending.Grants.RemoveGrantModification(@"c:\mixed\path", false, out var removed));
        Assert.Equal(entry.Path, removed!.Entry.Path);
        Assert.False(_pending.Grants.IsPendingModification(entry.Path, false));
    }

    [Fact]
    public void PendingConfigMove_KeyIsCaseInsensitive_ForLookupAndRemoval()
    {
        var entry = AclManagerPendingChangesTestData.MakeEntry(@"C:\Mixed\ConfigMove");
        _pending.Grants.MoveGrantConfig(entry, "extra.rfn");

        Assert.True(_pending.Grants.TryGetPendingConfigMove(@"c:\mixed\configmove", false, out var found));
        Assert.Equal("extra.rfn", found!.TargetConfigPath);
        Assert.True(_pending.Grants.RemoveGrantConfigMove(@"c:\mixed\configmove", false, out var removed));
        Assert.Equal(entry.Path, removed!.Entry.Path);
        Assert.False(_pending.Grants.IsPendingConfigMove(entry.Path, false));
    }

    [Fact]
    public void GrantSnapshot_IsDetachedFromLivePendingState()
    {
        var entry = AclManagerPendingChangesTestData.MakeEntry(@"C:\Grant");
        _pending.Grants.AddGrant(entry);
        _pending.Grants.MoveGrantConfig(entry, "extra.rfn");

        var snapshot = _pending.Grants.GetSnapshot();
        snapshot.PendingAdds.Single().Value.Path = @"C:\Mutated";
        snapshot.PendingConfigMoves.Single().Value.Entry.Path = @"C:\MutatedMove";

        Assert.True(_pending.Grants.IsPendingAdd(@"C:\Grant", false));
        Assert.Equal(@"C:\Grant", _pending.Grants.FindPendingAdd(@"C:\Grant", false)!.Path);
        Assert.True(_pending.Grants.TryGetPendingConfigMove(@"C:\Grant", false, out var move));
        Assert.Equal(@"C:\Grant", move!.Entry.Path);
    }

    [Fact]
    public void RestoreFromSnapshot_RestoresGrantWrapperStateWithoutTouchingTraverseState()
    {
        var grantEntry = AclManagerPendingChangesTestData.MakeEntry(@"C:\Grant");
        var traverseEntry = AclManagerPendingChangesTestData.MakeTraverseEntry(@"C:\Traverse");
        _pending.Grants.AddGrant(grantEntry);
        _pending.Traverse.AddTraverse(traverseEntry);
        var snapshot = _pending.Grants.GetSnapshot();

        _pending.Grants.RemoveGrant(grantEntry.Path, grantEntry.IsDeny);
        _pending.Grants.AddGrant(AclManagerPendingChangesTestData.MakeEntry(@"C:\Other"));
        _pending.Grants.RestoreFromSnapshot(snapshot);

        Assert.True(_pending.Grants.IsPendingAdd(@"C:\Grant", false));
        Assert.False(_pending.Grants.IsPendingAdd(@"C:\Other", false));
        Assert.True(_pending.Traverse.IsPendingTraverseAdd(@"C:\Traverse"));
    }

    [Fact]
    public void GetEffectiveConfigPath_GrantWithModeSwitchAndPendingMove_UsesEffectiveIsDeny()
    {
        var entry = AclManagerPendingChangesTestData.MakeEntry(@"C:\Foo", isDeny: false);
        _pending.Grants.ModifyGrant(entry, CreateModification(entry, newIsDeny: true));
        _pending.Grants.MoveGrantConfig(AclManagerPendingChangesTestData.MakeEntry(@"C:\Foo", isDeny: true), "extra.rfn");

        var result = _pending.Grants.GetEffectiveConfigPath(
            entry,
            new Mock<IGrantIntentRepository>().Object,
            new Mock<IGrantIntentStoreProvider>().Object,
            "S-1-5-21-1");

        Assert.Equal("extra.rfn", result);
    }

    [Fact]
    public void GetEffectiveConfigPath_GrantNoPendingMove_FallsBackToTracker()
    {
        var entry = AclManagerPendingChangesTestData.MakeEntry(@"C:\Foo");
        const string sid = "S-1-5-21-1";
        var store = new Mock<IGrantIntentStore>();
        store.SetupGet(current => current.ConfigPath).Returns("committed.rfn");
        var repository = new Mock<IGrantIntentRepository>();
        repository.Setup(current => current.FindGrant(sid, entry))
            .Returns(new GrantIntentLocation(entry.Clone(), store.Object));
        var provider = new Mock<IGrantIntentStoreProvider>();
        provider.Setup(current => current.ResolveStore("committed.rfn"))
            .Returns(store.Object);

        var result = _pending.Grants.GetEffectiveConfigPath(entry, repository.Object, provider.Object, sid);

        Assert.Equal("committed.rfn", result);
    }

    private static PendingModification CreateModification(
        GrantedPathEntry entry,
        bool newIsDeny,
        SavedRightsState? newRights = null,
        SavedRightsState? wasRights = null) =>
        new(
            entry,
            WasIsDeny: entry.IsDeny,
            WasOwn: entry.SavedRights?.Own == true,
            NewIsDeny: newIsDeny,
            NewRights: newRights,
            WasRights: wasRights ?? entry.SavedRights);
}
