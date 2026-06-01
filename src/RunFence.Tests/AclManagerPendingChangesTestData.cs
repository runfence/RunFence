using RunFence.Acl.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

internal static class AclManagerPendingChangesTestData
{
    public static GrantedPathEntry MakeEntry(string path, bool isDeny = false) =>
        new() { Path = path, IsDeny = isDeny };

    public static GrantedPathEntry MakeTraverseEntry(string path) =>
        new() { Path = path, IsTraverseOnly = true };

    public static AppDatabase MakeDbWithGrant(string sid, string path, bool isDeny, bool isTraverseOnly = false)
    {
        var db = new AppDatabase();
        db.GetOrCreateAccount(sid).Grants.Add(new GrantedPathEntry { Path = path, IsDeny = isDeny, IsTraverseOnly = isTraverseOnly });
        return db;
    }

    public static AppDatabase MakeDbWithTraverse(string sid, string path)
    {
        var db = new AppDatabase();
        db.GetOrCreateAccount(sid).Grants.Add(new GrantedPathEntry { Path = path, IsTraverseOnly = true });
        return db;
    }

    public static void AssertEntryEquivalent(GrantedPathEntry expected, GrantedPathEntry actual)
    {
        Assert.Equal(expected.Path, actual.Path);
        Assert.Equal(expected.IsDeny, actual.IsDeny);
        Assert.Equal(expected.IsTraverseOnly, actual.IsTraverseOnly);
        Assert.Equal(expected.SavedRights, actual.SavedRights);
        Assert.Equal(expected.AllAppliedPaths ?? [], actual.AllAppliedPaths ?? []);
        Assert.Equal(expected.OwnerContainerSid, actual.OwnerContainerSid);
        Assert.Equal(expected.SourceSids ?? [], actual.SourceSids ?? []);
        Assert.Equal(expected.PreviousSaclLabel, actual.PreviousSaclLabel);
    }

    public static void AssertModificationEquivalent(PendingModification expected, PendingModification actual)
    {
        AssertEntryEquivalent(expected.Entry, actual.Entry);
        Assert.Equal(expected.WasIsDeny, actual.WasIsDeny);
        Assert.Equal(expected.WasOwn, actual.WasOwn);
        Assert.Equal(expected.NewIsDeny, actual.NewIsDeny);
        Assert.Equal(expected.NewRights, actual.NewRights);
        Assert.Equal(expected.WasRights, actual.WasRights);
        Assert.Equal(expected.WasPreviousSaclLabel, actual.WasPreviousSaclLabel);
    }
}
