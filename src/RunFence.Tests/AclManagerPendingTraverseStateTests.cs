using Moq;
using RunFence.Acl.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public class AclManagerPendingTraverseStateTests
{
    private readonly AclManagerPendingChanges _pending = new();

    [Fact]
    public void GetEffectiveConfigPath_TraverseNoPendingMove_FallsBackToTracker()
    {
        var entry = AclManagerPendingChangesTestData.MakeTraverseEntry(@"C:\Foo");
        const string sid = "S-1-5-21-1";
        var store = new Mock<IGrantIntentStore>();
        store.SetupGet(current => current.ConfigPath).Returns((string?)null);
        var repository = new Mock<IGrantIntentRepository>();
        repository.Setup(current => current.FindTraverse(sid, entry))
            .Returns(new GrantIntentLocation(entry.Clone(), store.Object));
        var provider = new Mock<IGrantIntentStoreProvider>();
        provider.Setup(current => current.ResolveStore((string?)null))
            .Returns(store.Object);

        var result = _pending.Traverse.GetEffectiveConfigPath(entry, repository.Object, provider.Object, sid);

        Assert.Null(result);
    }

    [Fact]
    public void GetEffectiveConfigPath_SpecificContainer_UsesSharedTraverseOwnerSid()
    {
        const string containerSid = "S-1-15-2-99-1-2-3-4-5-6";
        var entry = new GrantedPathEntry
        {
            Path = @"C:\Foo",
            IsTraverseOnly = true,
            SourceSids = [containerSid]
        };
        var repository = new Mock<IGrantIntentRepository>();
        var sharedStore = new TestGrantIntentStore(@"C:\Configs\shared.rfn");
        repository.Setup(current => current.FindTraverse(WellKnownSecuritySids.AllApplicationPackagesSid, entry))
            .Returns(new GrantIntentLocation(entry.Clone(), sharedStore));
        var provider = new TestGrantIntentStoreProvider(new TestGrantIntentStore());
        provider.AddLoadedStore(sharedStore);

        var result = _pending.Traverse.GetEffectiveConfigPath(entry, repository.Object, provider, containerSid);

        Assert.Equal(sharedStore.ConfigPath, result);
        repository.Verify(current => current.FindTraverse(WellKnownSecuritySids.AllApplicationPackagesSid, entry), Times.Once);
    }

    [Fact]
    public void ExistsTraverseInDbOrPending_NonTraverseEntryInDb_ReturnsFalse()
    {
        const string sid = "S-1-5-21-1";
        var db = AclManagerPendingChangesTestData.MakeDbWithGrant(sid, @"C:\Foo", isDeny: false, isTraverseOnly: false);

        Assert.False(_pending.Traverse.ExistsTraverseInDbOrPending(db, sid, @"C:\Foo"));
    }

    [Fact]
    public void ExistsTraverseInDbOrPending_PendingUntrackWithCheckUntrackTrue_ReturnsFalse()
    {
        const string sid = "S-1-5-21-1";
        var db = AclManagerPendingChangesTestData.MakeDbWithTraverse(sid, @"C:\Foo");
        _pending.Traverse.UntrackTraverse(AclManagerPendingChangesTestData.MakeTraverseEntry(@"C:\Foo"));

        Assert.False(_pending.Traverse.ExistsTraverseInDbOrPending(db, sid, @"C:\Foo", checkUntrack: true));
    }

    [Fact]
    public void ExistsTraverseInDbOrPending_PendingUntrackWithCheckUntrackFalse_ReturnsTrue()
    {
        const string sid = "S-1-5-21-1";
        var db = AclManagerPendingChangesTestData.MakeDbWithTraverse(sid, @"C:\Foo");
        _pending.Traverse.UntrackTraverse(AclManagerPendingChangesTestData.MakeTraverseEntry(@"C:\Foo"));

        Assert.True(_pending.Traverse.ExistsTraverseInDbOrPending(db, sid, @"C:\Foo", checkUntrack: false));
    }

    [Fact]
    public void ExistsTraverseInDbOrPending_TraverseInDbNotPending_ReturnsTrue()
    {
        const string sid = "S-1-5-21-1";
        var db = AclManagerPendingChangesTestData.MakeDbWithTraverse(sid, @"C:\Foo");

        Assert.True(_pending.Traverse.ExistsTraverseInDbOrPending(db, sid, @"C:\Foo"));
    }

    [Fact]
    public void ExistsTraverseInDbOrPending_SpecificContainerSharedTraverse_ReturnsTrue()
    {
        const string containerSid = "S-1-15-2-99-1-2-3-4-5-6";
        var db = new AppDatabase();
        db.GetOrCreateAccount(WellKnownSecuritySids.AllApplicationPackagesSid).Grants.Add(
            AclManagerPendingChangesTestData.MakeTraverseEntry(@"C:\Foo"));

        Assert.True(_pending.Traverse.ExistsTraverseInDbOrPending(db, containerSid, @"C:\Foo"));
    }

    [Fact]
    public void ExistsTraverseInDbOrPending_SpecificContainerOtherTrackedSharedTraverse_ReturnsFalse()
    {
        const string containerSid = "S-1-15-2-99-1-2-3-4-5-6";
        const string otherContainerSid = "S-1-15-2-99-1-2-3-4-5-7";
        var db = new AppDatabase();
        db.GetOrCreateAccount(WellKnownSecuritySids.AllApplicationPackagesSid).Grants.Add(
            new GrantedPathEntry
            {
                Path = @"C:\Foo",
                IsTraverseOnly = true,
                SourceSids = [otherContainerSid]
            });

        Assert.False(_pending.Traverse.ExistsTraverseInDbOrPending(db, containerSid, @"C:\Foo"));
    }

    [Fact]
    public void ExistsTraverseInDbOrPending_SpecificContainerSharedTraversePendingUntrack_ReturnsFalse()
    {
        const string containerSid = "S-1-15-2-99-1-2-3-4-5-6";
        var db = new AppDatabase();
        db.GetOrCreateAccount(WellKnownSecuritySids.AllApplicationPackagesSid).Grants.Add(
            AclManagerPendingChangesTestData.MakeTraverseEntry(@"C:\Foo"));
        _pending.Traverse.UntrackTraverse(AclManagerPendingChangesTestData.MakeTraverseEntry(@"C:\Foo"));

        Assert.False(_pending.Traverse.ExistsTraverseInDbOrPending(db, containerSid, @"C:\Foo"));
    }

    [Fact]
    public void TraverseSnapshot_IsDetachedFromLivePendingState()
    {
        var entry = AclManagerPendingChangesTestData.MakeTraverseEntry(@"C:\Traverse");
        _pending.Traverse.AddTraverse(entry);
        _pending.Traverse.MoveTraverseConfig(entry, "extra.rfn");

        var snapshot = _pending.Traverse.GetSnapshot();
        snapshot.PendingAdds.Single().Value.Path = @"C:\Mutated";
        snapshot.PendingConfigMoves.Single().Value.Entry.Path = @"C:\MutatedMove";

        Assert.True(_pending.Traverse.IsPendingTraverseAdd(@"C:\Traverse"));
        Assert.True(_pending.Traverse.TryGetPendingTraverseConfigMove(@"C:\Traverse", out var move));
        Assert.Equal(@"C:\Traverse", move!.Entry.Path);
    }

    [Fact]
    public void RestoreFromSnapshot_RestoresTraverseWrapperStateWithoutTouchingGrantState()
    {
        var grantEntry = AclManagerPendingChangesTestData.MakeEntry(@"C:\Grant");
        var traverseEntry = AclManagerPendingChangesTestData.MakeTraverseEntry(@"C:\Traverse");
        _pending.Grants.AddGrant(grantEntry);
        _pending.Traverse.AddTraverse(traverseEntry);
        var snapshot = _pending.Traverse.GetSnapshot();

        _pending.Traverse.RemoveTraverse(traverseEntry.Path);
        _pending.Traverse.AddTraverse(AclManagerPendingChangesTestData.MakeTraverseEntry(@"C:\Other"));
        _pending.Traverse.RestoreFromSnapshot(snapshot);

        Assert.True(_pending.Traverse.IsPendingTraverseAdd(@"C:\Traverse"));
        Assert.False(_pending.Traverse.IsPendingTraverseAdd(@"C:\Other"));
        Assert.True(_pending.Grants.IsPendingAdd(@"C:\Grant", false));
    }
}
