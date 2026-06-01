using RunFence.Acl.UI;
using RunFence.Core.Models;
using Xunit;

namespace RunFence.Tests;

public class AclManagerPendingChangesAggregateTests
{
    [Theory]
    [InlineData("adds")]
    [InlineData("removes")]
    [InlineData("modifications")]
    [InlineData("grantFixes")]
    [InlineData("traverseAdds")]
    [InlineData("traverseRemoves")]
    [InlineData("traverseFixes")]
    [InlineData("untrackGrants")]
    [InlineData("untrackTraverse")]
    [InlineData("configMoves")]
    [InlineData("traverseConfigMoves")]
    public void HasPendingChanges_SingleCollectionNonEmpty_ReturnsTrue(string collection)
    {
        var pending = new AclManagerPendingChanges();
        var entry = AclManagerPendingChangesTestData.MakeEntry(@"C:\Foo");
        switch (collection)
        {
            case "adds":
                pending.Grants.AddGrant(entry);
                break;
            case "removes":
                pending.Grants.MarkGrantForRemoval(entry);
                break;
            case "modifications":
                pending.Grants.ModifyGrant(entry, CreateModification(entry, newIsDeny: false));
                break;
            case "grantFixes":
                pending.Grants.AddGrantFix(entry);
                break;
            case "traverseAdds":
                pending.Traverse.AddTraverse(AclManagerPendingChangesTestData.MakeTraverseEntry(@"C:\Foo"));
                break;
            case "traverseRemoves":
                pending.Traverse.MarkTraverseForRemoval(AclManagerPendingChangesTestData.MakeTraverseEntry(@"C:\Foo"));
                break;
            case "traverseFixes":
                pending.Traverse.AddTraverseFix(AclManagerPendingChangesTestData.MakeTraverseEntry(@"C:\Foo"));
                break;
            case "untrackGrants":
                pending.Grants.UntrackGrant(entry);
                break;
            case "untrackTraverse":
                pending.Traverse.UntrackTraverse(AclManagerPendingChangesTestData.MakeTraverseEntry(@"C:\Foo"));
                break;
            case "configMoves":
                pending.Grants.MoveGrantConfig(entry, null);
                break;
            case "traverseConfigMoves":
                pending.Traverse.MoveTraverseConfig(AclManagerPendingChangesTestData.MakeTraverseEntry(@"C:\Foo"), null);
                break;
        }

        Assert.True(pending.HasPendingChanges);
    }

    [Fact]
    public void Clear_EmptiesAllCollections()
    {
        var pending = new AclManagerPendingChanges();
        var grantAdd = AclManagerPendingChangesTestData.MakeEntry(@"C:\Foo");
        var grantRemove = AclManagerPendingChangesTestData.MakeEntry(@"C:\Bar", isDeny: true);
        var grantModify = AclManagerPendingChangesTestData.MakeEntry(@"C:\Baz");
        var grantFix = AclManagerPendingChangesTestData.MakeEntry(@"C:\Fix");
        var grantUntrack = AclManagerPendingChangesTestData.MakeEntry(@"C:\U1");
        var traverseAdd = AclManagerPendingChangesTestData.MakeTraverseEntry(@"C:\T1");
        var traverseRemove = AclManagerPendingChangesTestData.MakeTraverseEntry(@"C:\T2");
        var traverseFix = AclManagerPendingChangesTestData.MakeTraverseEntry(@"C:\T3");
        var traverseUntrack = AclManagerPendingChangesTestData.MakeTraverseEntry(@"C:\U2");

        pending.Grants.AddGrant(grantAdd);
        pending.Grants.MarkGrantForRemoval(grantRemove);
        pending.Grants.ModifyGrant(grantModify, CreateModification(grantModify, newIsDeny: false));
        pending.Grants.AddGrantFix(grantFix);
        pending.Traverse.AddTraverse(traverseAdd);
        pending.Traverse.MarkTraverseForRemoval(traverseRemove);
        pending.Traverse.AddTraverseFix(traverseFix);
        pending.Grants.UntrackGrant(grantUntrack);
        pending.Traverse.UntrackTraverse(traverseUntrack);
        pending.Grants.MoveGrantConfig(AclManagerPendingChangesTestData.MakeEntry(@"C:\C1"), null);
        pending.Traverse.MoveTraverseConfig(AclManagerPendingChangesTestData.MakeTraverseEntry(@"C:\C2"), null);

        pending.Clear();

        Assert.False(pending.HasPendingChanges);
        Assert.Empty(pending.Grants.GetSnapshot().PendingAdds);
        Assert.Empty(pending.Grants.GetSnapshot().PendingRemoves);
        Assert.Empty(pending.Grants.GetSnapshot().PendingModifications);
        Assert.Empty(pending.Grants.GetSnapshot().PendingGrantFixes);
        Assert.Empty(pending.Grants.GetSnapshot().PendingUntrack);
        Assert.Empty(pending.Grants.GetSnapshot().PendingConfigMoves);
        Assert.Empty(pending.Traverse.GetSnapshot().PendingAdds);
        Assert.Empty(pending.Traverse.GetSnapshot().PendingRemoves);
        Assert.Empty(pending.Traverse.GetSnapshot().PendingFixes);
        Assert.Empty(pending.Traverse.GetSnapshot().PendingUntrack);
        Assert.Empty(pending.Traverse.GetSnapshot().PendingConfigMoves);
    }

    [Fact]
    public void CaptureSnapshot_RestoreFromSnapshot_RestoresEveryPendingCollectionExactly()
    {
        var pending = new AclManagerPendingChanges();
        var addEntry = AclManagerPendingChangesTestData.MakeEntry(@"C:\GrantAdd");
        var removeEntry = AclManagerPendingChangesTestData.MakeEntry(@"C:\GrantRemove", isDeny: true);
        var modifiedEntry = AclManagerPendingChangesTestData.MakeEntry(@"C:\GrantModify");
        modifiedEntry.SavedRights = SavedRightsState.DefaultForMode(false);
        var modification = CreateModification(
            modifiedEntry,
            newIsDeny: true,
            newRights: SavedRightsState.DefaultForMode(true),
            wasRights: modifiedEntry.SavedRights);
        var fixEntry = AclManagerPendingChangesTestData.MakeEntry(@"C:\GrantFix");
        var untrackGrantEntry = AclManagerPendingChangesTestData.MakeEntry(@"C:\GrantUntrack", isDeny: true);
        var traverseAddEntry = AclManagerPendingChangesTestData.MakeTraverseEntry(@"C:\TraverseAdd");
        var traverseRemoveEntry = AclManagerPendingChangesTestData.MakeTraverseEntry(@"C:\TraverseRemove");
        var traverseFixEntry = AclManagerPendingChangesTestData.MakeTraverseEntry(@"C:\TraverseFix");
        var untrackTraverseEntry = AclManagerPendingChangesTestData.MakeTraverseEntry(@"C:\TraverseUntrack");

        pending.Grants.AddGrant(addEntry);
        pending.Grants.MarkGrantForRemoval(removeEntry);
        pending.Grants.ModifyGrant(modifiedEntry, modification);
        pending.Grants.AddGrantFix(fixEntry);
        pending.Grants.UntrackGrant(untrackGrantEntry);
        pending.Grants.MoveGrantConfig(addEntry, @"C:\Configs\grant.rfn");
        pending.Traverse.AddTraverse(traverseAddEntry);
        pending.Traverse.MarkTraverseForRemoval(traverseRemoveEntry);
        pending.Traverse.AddTraverseFix(traverseFixEntry);
        pending.Traverse.UntrackTraverse(untrackTraverseEntry);
        pending.Traverse.MoveTraverseConfig(traverseAddEntry, @"C:\Configs\traverse.rfn");

        var snapshot = pending.CaptureSnapshot();

        pending.Clear();
        pending.Grants.AddGrant(AclManagerPendingChangesTestData.MakeEntry(@"C:\OtherGrant"));
        pending.Traverse.AddTraverse(AclManagerPendingChangesTestData.MakeTraverseEntry(@"C:\OtherTraverse"));
        pending.RestoreFromSnapshot(snapshot);

        var grantSnapshot = pending.Grants.GetSnapshot();
        var traverseSnapshot = pending.Traverse.GetSnapshot();

        AclManagerPendingChangesTestData.AssertEntryEquivalent(addEntry, Assert.Single(grantSnapshot.PendingAdds).Value);
        AclManagerPendingChangesTestData.AssertEntryEquivalent(removeEntry, Assert.Single(grantSnapshot.PendingRemoves).Value);
        AclManagerPendingChangesTestData.AssertModificationEquivalent(modification, Assert.Single(grantSnapshot.PendingModifications).Value);
        AclManagerPendingChangesTestData.AssertEntryEquivalent(fixEntry, Assert.Single(grantSnapshot.PendingGrantFixes).Value);
        AclManagerPendingChangesTestData.AssertEntryEquivalent(untrackGrantEntry, Assert.Single(grantSnapshot.PendingUntrack).Value);
        var grantMove = Assert.Single(grantSnapshot.PendingConfigMoves).Value;
        AclManagerPendingChangesTestData.AssertEntryEquivalent(addEntry, grantMove.Entry);
        Assert.Equal(@"C:\Configs\grant.rfn", grantMove.TargetConfigPath);
        AclManagerPendingChangesTestData.AssertEntryEquivalent(traverseAddEntry, Assert.Single(traverseSnapshot.PendingAdds).Value);
        AclManagerPendingChangesTestData.AssertEntryEquivalent(traverseRemoveEntry, Assert.Single(traverseSnapshot.PendingRemoves).Value);
        AclManagerPendingChangesTestData.AssertEntryEquivalent(traverseFixEntry, Assert.Single(traverseSnapshot.PendingFixes).Value);
        AclManagerPendingChangesTestData.AssertEntryEquivalent(untrackTraverseEntry, Assert.Single(traverseSnapshot.PendingUntrack).Value);
        var traverseMove = Assert.Single(traverseSnapshot.PendingConfigMoves).Value;
        AclManagerPendingChangesTestData.AssertEntryEquivalent(traverseAddEntry, traverseMove.Entry);
        Assert.Equal(@"C:\Configs\traverse.rfn", traverseMove.TargetConfigPath);
    }

    [Fact]
    public void RestoreFromSnapshot_PreservesCaseInsensitiveLookups()
    {
        var pending = new AclManagerPendingChanges();
        var addEntry = AclManagerPendingChangesTestData.MakeEntry(@"C:\Mixed\Grant");
        var traverseEntry = AclManagerPendingChangesTestData.MakeTraverseEntry(@"C:\Mixed\Traverse");

        pending.Grants.AddGrant(addEntry);
        pending.Grants.MoveGrantConfig(addEntry, @"C:\Configs\grant.rfn");
        pending.Traverse.AddTraverse(traverseEntry);
        pending.Traverse.MoveTraverseConfig(traverseEntry, @"C:\Configs\traverse.rfn");

        var snapshot = pending.CaptureSnapshot();

        pending.Clear();
        pending.RestoreFromSnapshot(snapshot);

        Assert.True(pending.Grants.IsPendingAdd(@"c:\mixed\grant", false));
        Assert.True(pending.Grants.TryGetPendingConfigMove(@"c:\mixed\grant", false, out var grantMove));
        Assert.Equal(@"C:\Configs\grant.rfn", grantMove!.TargetConfigPath);
        Assert.True(pending.Traverse.IsPendingTraverseAdd(@"c:\mixed\traverse"));
        Assert.True(pending.Traverse.TryGetPendingTraverseConfigMove(@"c:\mixed\traverse", out var traverseMove));
        Assert.Equal(@"C:\Configs\traverse.rfn", traverseMove!.TargetConfigPath);
    }

    [Fact]
    public void CaptureSnapshot_IsImmutableAfterLaterPendingMutations()
    {
        var pending = new AclManagerPendingChanges();
        var originalGrantEntry = AclManagerPendingChangesTestData.MakeEntry(@"C:\OriginalGrant");
        var originalTraverseEntry = AclManagerPendingChangesTestData.MakeTraverseEntry(@"C:\OriginalTraverse");

        pending.Grants.AddGrant(originalGrantEntry);
        pending.Traverse.AddTraverse(originalTraverseEntry);
        var snapshot = pending.CaptureSnapshot();

        pending.Grants.AddGrant(AclManagerPendingChangesTestData.MakeEntry(@"C:\LaterGrant"));
        pending.Traverse.AddTraverse(AclManagerPendingChangesTestData.MakeTraverseEntry(@"C:\LaterTraverse"));
        pending.Grants.MoveGrantConfig(originalGrantEntry, @"C:\Configs\later.rfn");
        pending.Traverse.MoveTraverseConfig(originalTraverseEntry, @"C:\Configs\later-traverse.rfn");

        Assert.Single(snapshot.PendingAdds);
        Assert.DoesNotContain(snapshot.PendingAdds.Keys, key => string.Equals(key.Path, @"C:\LaterGrant", StringComparison.OrdinalIgnoreCase));
        Assert.Single(snapshot.PendingTraverseAdds);
        Assert.DoesNotContain(snapshot.PendingTraverseAdds.Keys, key => string.Equals(key, @"C:\LaterTraverse", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(snapshot.PendingConfigMoves);
        Assert.Empty(snapshot.PendingTraverseConfigMoves);
    }

    [Fact]
    public void SnapshotValues_AreDetachedFromPendingState()
    {
        var pending = new AclManagerPendingChanges();
        var addEntry = AclManagerPendingChangesTestData.MakeEntry(@"C:\OriginalGrant");
        var removeEntry = AclManagerPendingChangesTestData.MakeEntry(@"C:\RemoveGrant", isDeny: true);
        var modifiedEntry = AclManagerPendingChangesTestData.MakeEntry(@"C:\ModifyGrant");
        var modification = CreateModification(
            modifiedEntry,
            newIsDeny: false,
            newRights: SavedRightsState.DefaultForMode(false));
        var traverseEntry = AclManagerPendingChangesTestData.MakeTraverseEntry(@"C:\OriginalTraverse");

        pending.Grants.AddGrant(addEntry);
        pending.Grants.MarkGrantForRemoval(removeEntry);
        pending.Grants.ModifyGrant(modifiedEntry, modification);
        pending.Grants.MoveGrantConfig(addEntry, @"C:\Configs\grant.rfn");
        pending.Traverse.AddTraverse(traverseEntry);
        pending.Traverse.MoveTraverseConfig(traverseEntry, @"C:\Configs\traverse.rfn");

        var grantSnapshot = pending.Grants.GetSnapshot();
        var traverseSnapshot = pending.Traverse.GetSnapshot();

        grantSnapshot.PendingAdds.Single().Value.Path = @"C:\MutatedGrant";
        grantSnapshot.PendingRemoves.Single().Value.Path = @"C:\MutatedRemove";
        grantSnapshot.PendingModifications.Single().Value.Entry.Path = @"C:\MutatedModification";
        grantSnapshot.PendingConfigMoves.Single().Value.Entry.Path = @"C:\MutatedGrantMove";
        traverseSnapshot.PendingAdds.Single().Value.Path = @"C:\MutatedTraverse";
        traverseSnapshot.PendingConfigMoves.Single().Value.Entry.Path = @"C:\MutatedTraverseMove";

        Assert.True(pending.Grants.IsPendingAdd(@"C:\OriginalGrant", false));
        Assert.Equal(@"C:\OriginalGrant", pending.Grants.FindPendingAdd(@"C:\OriginalGrant", false)!.Path);
        Assert.True(pending.Grants.IsPendingRemove(@"C:\RemoveGrant", true));
        Assert.True(pending.Grants.IsPendingModification(@"C:\ModifyGrant", false));
        Assert.True(pending.Grants.TryGetPendingModification(@"C:\ModifyGrant", false, out var pendingModification));
        Assert.Equal(@"C:\ModifyGrant", pendingModification!.Entry.Path);
        pendingModification.Entry.Path = @"C:\MutatedReturnedModification";
        Assert.True(pending.Grants.TryGetPendingModification(@"C:\ModifyGrant", false, out var pendingModificationAgain));
        Assert.Equal(@"C:\ModifyGrant", pendingModificationAgain!.Entry.Path);
        var enumeratedModification = pending.Grants.GetSnapshot().PendingModifications.Single().Value;
        Assert.Equal(@"C:\ModifyGrant", enumeratedModification.Entry.Path);
        Assert.True(pending.Grants.TryGetPendingConfigMove(@"C:\OriginalGrant", false, out var pendingGrantMove));
        Assert.Equal(@"C:\OriginalGrant", pendingGrantMove!.Entry.Path);
        pendingGrantMove.Entry.Path = @"C:\MutatedReturnedGrantMove";
        Assert.True(pending.Grants.TryGetPendingConfigMove(@"C:\OriginalGrant", false, out var pendingGrantMoveAgain));
        Assert.Equal(@"C:\OriginalGrant", pendingGrantMoveAgain!.Entry.Path);
        Assert.True(pending.Traverse.IsPendingTraverseAdd(@"C:\OriginalTraverse"));
        Assert.Equal(@"C:\OriginalTraverse", pending.Traverse.GetSnapshot().PendingAdds.Single().Value.Path);
        Assert.True(pending.Traverse.TryGetPendingTraverseConfigMove(@"C:\OriginalTraverse", out var pendingTraverseMove));
        Assert.Equal(@"C:\OriginalTraverse", pendingTraverseMove!.Entry.Path);
        pendingTraverseMove.Entry.Path = @"C:\MutatedReturnedTraverseMove";
        Assert.True(pending.Traverse.TryGetPendingTraverseConfigMove(@"C:\OriginalTraverse", out var pendingTraverseMoveAgain));
        Assert.Equal(@"C:\OriginalTraverse", pendingTraverseMoveAgain!.Entry.Path);

        pending.Grants.ModifyGrant(
            AclManagerPendingChangesTestData.MakeEntry(@"C:\LeafModifyGrant"),
            modification with { Entry = AclManagerPendingChangesTestData.MakeEntry(@"C:\LeafModifyGrant") });
        var modificationSnapshot = pending.Grants.GetSnapshot();
        modificationSnapshot.PendingModifications.Single(m => string.Equals(m.Value.Entry.Path, @"C:\LeafModifyGrant", StringComparison.OrdinalIgnoreCase))
            .Value.Entry.Path = @"C:\MutatedEnumeratedModification";
        Assert.True(pending.Grants.TryGetPendingModification(@"C:\LeafModifyGrant", false, out var leafModification));
        Assert.Equal(@"C:\LeafModifyGrant", leafModification!.Entry.Path);
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
