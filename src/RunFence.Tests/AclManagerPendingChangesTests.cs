using RunFence.Acl.UI;
using RunFence.Core.Models;
using Xunit;

namespace RunFence.Tests;

public class AclManagerPendingChangesTests
{
    private readonly AclManagerPendingChanges _pending = new();

    private static GrantedPathEntry MakeEntry(string path, bool isDeny = false) =>
        new() { Path = path, IsDeny = isDeny };

    private static GrantedPathEntry MakeTraverseEntry(string path) =>
        new() { Path = path, IsTraverseOnly = true };

    // --- HasPendingChanges ---

    [Fact]
    public void HasPendingChanges_Empty_ReturnsFalse()
    {
        Assert.False(_pending.HasPendingChanges);
    }

    [Theory]
    [InlineData("adds")]
    [InlineData("removes")]
    [InlineData("modifications")]
    [InlineData("traverseAdds")]
    [InlineData("traverseRemoves")]
    [InlineData("traverseFixes")]
    public void HasPendingChanges_SingleCollectionNonEmpty_ReturnsTrue(string collection)
    {
        var entry = MakeEntry(@"C:\Foo");
        switch (collection)
        {
            case "adds":
                _pending.PendingAdds[(@"C:\Foo", false)] = entry;
                break;
            case "removes":
                _pending.PendingRemoves[(@"C:\Foo", false)] = entry;
                break;
            case "modifications":
                _pending.PendingModifications[(@"C:\Foo", false)] = entry;
                break;
            case "traverseAdds":
                _pending.PendingTraverseAdds[@"C:\Foo"] = entry;
                break;
            case "traverseRemoves":
                _pending.PendingTraverseRemoves[@"C:\Foo"] = entry;
                break;
            case "traverseFixes":
                _pending.PendingTraverseFixes[@"C:\Foo"] = entry;
                break;
        }

        Assert.True(_pending.HasPendingChanges);
    }

    // --- Clear ---

    [Fact]
    public void Clear_EmptiesAllCollections()
    {
        _pending.PendingAdds[(@"C:\Foo", false)] = MakeEntry(@"C:\Foo");
        _pending.PendingRemoves[(@"C:\Bar", true)] = MakeEntry(@"C:\Bar", isDeny: true);
        _pending.PendingModifications[(@"C:\Baz", false)] = MakeEntry(@"C:\Baz");
        _pending.PendingTraverseAdds[@"C:\T1"] = MakeTraverseEntry(@"C:\T1");
        _pending.PendingTraverseRemoves[@"C:\T2"] = MakeTraverseEntry(@"C:\T2");
        _pending.PendingTraverseFixes[@"C:\T3"] = MakeTraverseEntry(@"C:\T3");

        _pending.Clear();

        Assert.False(_pending.HasPendingChanges);
        Assert.Empty(_pending.PendingAdds);
        Assert.Empty(_pending.PendingRemoves);
        Assert.Empty(_pending.PendingModifications);
        Assert.Empty(_pending.PendingTraverseAdds);
        Assert.Empty(_pending.PendingTraverseRemoves);
        Assert.Empty(_pending.PendingTraverseFixes);
    }

    // --- Add then modify stays in PendingAdds only ---

    [Fact]
    public void AddThenModify_StaysInPendingAddsOnly()
    {
        var entry = MakeEntry(@"C:\Foo");
        _pending.PendingAdds[(@"C:\Foo", false)] = entry;

        // Modifying a PendingAdd → update the entry in-place, do NOT add to PendingModifications
        entry.SavedRights = new SavedRightsState(Execute: true, Write: false, Read: true, Special: false, Own: false);
        // (The modification tracking logic is in callers, but the contract is:
        //  IsPendingAdd returns true, IsPendingModification returns false)

        Assert.True(_pending.IsPendingAdd(@"C:\Foo", isDeny: false));
        Assert.False(_pending.IsPendingModification(@"C:\Foo", isDeny: false));
        // The entry was updated in-place
        Assert.True(_pending.FindPendingAdd(@"C:\Foo", isDeny: false)!.SavedRights!.Execute);
    }

    // --- Remove existing DB entry → PendingRemoves ---

    [Fact]
    public void RemoveExistingDbEntry_InPendingRemoves()
    {
        var entry = MakeEntry(@"C:\Foo");
        _pending.PendingRemoves[(@"C:\Foo", false)] = entry;

        Assert.True(_pending.IsPendingRemove(@"C:\Foo", isDeny: false));
        Assert.False(_pending.IsPendingAdd(@"C:\Foo", isDeny: false));
    }

    // --- Modify existing DB entry → PendingModifications ---

    [Fact]
    public void ModifyExistingDbEntry_InPendingModifications()
    {
        var entry = MakeEntry(@"C:\Foo");
        _pending.PendingModifications[(@"C:\Foo", false)] = entry;

        Assert.True(_pending.IsPendingModification(@"C:\Foo", isDeny: false));
        Assert.False(_pending.IsPendingAdd(@"C:\Foo", isDeny: false));
    }

    // --- IsPendingAdd / FindPendingAdd ---

    [Fact]
    public void IsPendingAdd_KnownPath_ReturnsTrue()
    {
        _pending.PendingAdds[(@"C:\Foo", false)] = MakeEntry(@"C:\Foo");

        Assert.True(_pending.IsPendingAdd(@"C:\Foo", isDeny: false));
    }

    [Fact]
    public void IsPendingAdd_UnknownPath_ReturnsFalse()
    {
        Assert.False(_pending.IsPendingAdd(@"C:\Unknown", isDeny: false));
    }

    [Fact]
    public void FindPendingAdd_KnownPath_ReturnsEntry()
    {
        var entry = MakeEntry(@"C:\Foo");
        _pending.PendingAdds[(@"C:\Foo", false)] = entry;

        var found = _pending.FindPendingAdd(@"C:\Foo", isDeny: false);

        Assert.Same(entry, found);
    }

    [Fact]
    public void FindPendingAdd_UnknownPath_ReturnsNull()
    {
        Assert.Null(_pending.FindPendingAdd(@"C:\Unknown", isDeny: false));
    }

    [Fact]
    public void IsPendingAdd_AllowAndDenyAreSeparateKeys()
    {
        _pending.PendingAdds[(@"C:\Foo", false)] = MakeEntry(@"C:\Foo");

        Assert.True(_pending.IsPendingAdd(@"C:\Foo", isDeny: false));
        Assert.False(_pending.IsPendingAdd(@"C:\Foo", isDeny: true));
    }

    // --- Traverse add/remove ---

    [Fact]
    public void TraverseAdd_IsPendingTraverseAdd()
    {
        _pending.PendingTraverseAdds[@"C:\Parent"] = MakeTraverseEntry(@"C:\Parent");

        Assert.True(_pending.IsPendingTraverseAdd(@"C:\Parent"));
        Assert.False(_pending.IsPendingTraverseRemove(@"C:\Parent"));
    }

    [Fact]
    public void TraverseRemove_IsPendingTraverseRemove()
    {
        _pending.PendingTraverseRemoves[@"C:\Parent"] = MakeTraverseEntry(@"C:\Parent");

        Assert.True(_pending.IsPendingTraverseRemove(@"C:\Parent"));
        Assert.False(_pending.IsPendingTraverseAdd(@"C:\Parent"));
    }

    [Fact]
    public void TraverseAddThenRemove_RemovedFromPendingTraverseAdds()
    {
        _pending.PendingTraverseAdds[@"C:\Parent"] = MakeTraverseEntry(@"C:\Parent");
        _pending.PendingTraverseAdds.Remove(@"C:\Parent");

        Assert.False(_pending.IsPendingTraverseAdd(@"C:\Parent"));
    }

    // --- Case-insensitive path keys ---

    [Fact]
    public void PendingAdds_CaseInsensitivePaths_SameEntry()
    {
        _pending.PendingAdds[(@"C:\Foo\Bar", false)] = MakeEntry(@"C:\Foo\Bar");

        // Different casing should resolve to the same key
        Assert.True(_pending.IsPendingAdd(@"c:\foo\bar", isDeny: false));
        Assert.True(_pending.IsPendingAdd(@"C:\FOO\BAR", isDeny: false));
    }

    [Fact]
    public void PendingTraverseAdds_CaseInsensitivePaths_SameEntry()
    {
        _pending.PendingTraverseAdds[@"C:\Foo\Bar"] = MakeTraverseEntry(@"C:\Foo\Bar");

        Assert.True(_pending.IsPendingTraverseAdd(@"c:\foo\bar"));
        Assert.True(_pending.IsPendingTraverseAdd(@"C:\FOO\BAR"));
    }

    [Fact]
    public void PendingAdds_DifferentCasing_OverwritesEntry()
    {
        var entry1 = MakeEntry(@"C:\Foo");
        var entry2 = MakeEntry(@"C:\foo");
        _pending.PendingAdds[(@"C:\Foo", false)] = entry1;
        _pending.PendingAdds[(@"c:\foo", false)] = entry2; // same path, different case

        Assert.Single(_pending.PendingAdds);
        Assert.Same(entry2, _pending.FindPendingAdd(@"C:\Foo", isDeny: false));
    }

    // --- ExistsInDbOrPending ---

    private static AppDatabase MakeDbWithGrant(string sid, string path, bool isDeny, bool isTraverseOnly = false)
    {
        var db = new AppDatabase();
        db.GetOrCreateAccount(sid).Grants.Add(new GrantedPathEntry { Path = path, IsDeny = isDeny, IsTraverseOnly = isTraverseOnly });
        return db;
    }

    [Fact]
    public void ExistsInDbOrPending_InPendingAdds_ReturnsTrue()
    {
        _pending.PendingAdds[(@"C:\Foo", false)] = MakeEntry(@"C:\Foo");
        var db = new AppDatabase();

        Assert.True(_pending.ExistsInDbOrPending(db, "S-1-5-21-1", @"C:\Foo", isDeny: false));
    }

    [Fact]
    public void ExistsInDbOrPending_InDbNotPendingRemove_ReturnsTrue()
    {
        const string sid = "S-1-5-21-1";
        var db = MakeDbWithGrant(sid, @"C:\Foo", isDeny: false);

        Assert.True(_pending.ExistsInDbOrPending(db, sid, @"C:\Foo", isDeny: false));
    }

    [Fact]
    public void ExistsInDbOrPending_InDbButPendingRemove_ReturnsFalse()
    {
        const string sid = "S-1-5-21-1";
        var db = MakeDbWithGrant(sid, @"C:\Foo", isDeny: false);
        _pending.PendingRemoves[(@"C:\Foo", false)] = MakeEntry(@"C:\Foo");

        Assert.False(_pending.ExistsInDbOrPending(db, sid, @"C:\Foo", isDeny: false));
    }

    [Fact]
    public void ExistsInDbOrPending_NotInDbOrPending_ReturnsFalse()
    {
        var db = new AppDatabase();

        Assert.False(_pending.ExistsInDbOrPending(db, "S-1-5-21-1", @"C:\Foo", isDeny: false));
    }

    [Fact]
    public void ExistsInDbOrPending_TraverseOnlyInDb_ReturnsFalse()
    {
        // TraverseOnly entries must NOT be detected as duplicate grants
        const string sid = "S-1-5-21-1";
        var db = MakeDbWithGrant(sid, @"C:\Foo", isDeny: false, isTraverseOnly: true);

        Assert.False(_pending.ExistsInDbOrPending(db, sid, @"C:\Foo", isDeny: false));
    }

    [Fact]
    public void ExistsInDbOrPending_OppositeModeInDb_ReturnsFalse()
    {
        // Deny entry in DB should not match allow check
        const string sid = "S-1-5-21-1";
        var db = MakeDbWithGrant(sid, @"C:\Foo", isDeny: true);

        Assert.False(_pending.ExistsInDbOrPending(db, sid, @"C:\Foo", isDeny: false));
    }

    [Fact]
    public void ExistsInDbOrPending_CaseInsensitivePath_ReturnsTrue()
    {
        const string sid = "S-1-5-21-1";
        var db = MakeDbWithGrant(sid, @"C:\Foo\Bar", isDeny: false);

        Assert.True(_pending.ExistsInDbOrPending(db, sid, @"c:\foo\bar", isDeny: false));
    }
}