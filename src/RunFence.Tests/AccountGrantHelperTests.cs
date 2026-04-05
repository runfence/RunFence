using RunFence.Acl.Permissions;
using RunFence.Core.Models;
using Xunit;

namespace RunFence.Tests;

public class AccountGrantHelperTests
{
    private const string TestSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";

    // --- AccountEntry creation on first grant ---

    [Fact]
    public void AddGrant_NoExistingAccount_CreatesEntryAndAdds()
    {
        var db = new AppDatabase();

        AccountGrantHelper.AddGrant(db, TestSid, @"C:\Apps\Tool");

        Assert.Single(db.GetAccount(TestSid)!.Grants);
    }

    // --- Path normalization ---

    [Fact]
    public void AddGrant_NormalizesPathBeforeStoring()
    {
        var db = new AppDatabase();

        AccountGrantHelper.AddGrant(db, TestSid, @"C:\Apps\..\Apps\Tool");

        var stored = db.GetAccount(TestSid)!.Grants[0].Path;
        Assert.Equal(@"C:\Apps\Tool", stored);
    }

    // --- Deduplication ---

    [Fact]
    public void AddGrant_SamePath_NotAddedTwice()
    {
        var db = new AppDatabase();

        AccountGrantHelper.AddGrant(db, TestSid, @"C:\Apps\Tool");
        AccountGrantHelper.AddGrant(db, TestSid, @"C:\Apps\Tool");

        Assert.Single(db.GetAccount(TestSid)!.Grants);
    }

    [Fact]
    public void AddGrant_SamePath_CaseInsensitiveDeduplicated()
    {
        var db = new AppDatabase();

        AccountGrantHelper.AddGrant(db, TestSid, @"C:\Apps\Tool");
        AccountGrantHelper.AddGrant(db, TestSid, @"c:\apps\tool");

        Assert.Single(db.GetAccount(TestSid)!.Grants);
    }

    [Fact]
    public void AddGrant_DifferentPaths_BothAdded()
    {
        var db = new AppDatabase();

        AccountGrantHelper.AddGrant(db, TestSid, @"C:\Apps\Tool1");
        AccountGrantHelper.AddGrant(db, TestSid, @"C:\Apps\Tool2");

        Assert.Equal(2, db.GetAccount(TestSid)!.Grants.Count);
    }

    // --- isDeny flag ---

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AddGrant_IsDenyStoredCorrectly(bool isDeny)
    {
        var db = new AppDatabase();

        AccountGrantHelper.AddGrant(db, TestSid, @"C:\Apps\Tool", isDeny: isDeny);

        Assert.Equal(isDeny, db.GetAccount(TestSid)!.Grants[0].IsDeny);
    }

    [Fact]
    public void AddGrant_SamePath_AllowAndDenyAreDistinct()
    {
        // Allow and Deny grants for the same path are different entries
        var db = new AppDatabase();

        AccountGrantHelper.AddGrant(db, TestSid, @"C:\Apps\Tool", isDeny: false);
        AccountGrantHelper.AddGrant(db, TestSid, @"C:\Apps\Tool", isDeny: true);

        var grants = db.GetAccount(TestSid)!.Grants;
        Assert.Equal(2, grants.Count);
        Assert.Contains(grants, e => !e.IsDeny);
        Assert.Contains(grants, e => e.IsDeny);
    }

    [Fact]
    public void AddGrant_SameDenyPath_NotAddedTwice()
    {
        var db = new AppDatabase();

        AccountGrantHelper.AddGrant(db, TestSid, @"C:\Apps\Tool", isDeny: true);
        AccountGrantHelper.AddGrant(db, TestSid, @"C:\Apps\Tool", isDeny: true);

        Assert.Single(db.GetAccount(TestSid)!.Grants);
    }

    // --- TraverseOnly entries not deduplicated against regular grants ---

    [Fact]
    public void AddGrant_SkipsExistingTraverseOnlyEntry_AddsNewNonTraverse()
    {
        // A traverse-only entry for the same path should not prevent adding a regular grant
        var db = new AppDatabase();
        db.GetOrCreateAccount(TestSid).Grants.Add(
            new GrantedPathEntry { Path = @"C:\Apps\Tool", IsTraverseOnly = true });

        AccountGrantHelper.AddGrant(db, TestSid, @"C:\Apps\Tool");

        // The new non-traverse Allow grant was added alongside the traverse-only entry
        var grants = db.GetAccount(TestSid)!.Grants;
        Assert.Equal(2, grants.Count);
        Assert.Contains(grants, e => e is { IsTraverseOnly: false, IsDeny: false });
    }

    // --- Multiple SIDs are independent ---

    [Fact]
    public void AddGrant_MultipleSids_StoredSeparately()
    {
        var db = new AppDatabase();
        var sid2 = "S-1-5-21-1234567890-1234567890-1234567890-1002";

        AccountGrantHelper.AddGrant(db, TestSid, @"C:\Apps\Tool");
        AccountGrantHelper.AddGrant(db, sid2, @"C:\Apps\Other");

        Assert.Single(db.GetAccount(TestSid)!.Grants);
        Assert.Single(db.GetAccount(sid2)!.Grants);
        Assert.Equal(@"C:\Apps\Tool", db.GetAccount(TestSid)!.Grants[0].Path);
        Assert.Equal(@"C:\Apps\Other", db.GetAccount(sid2)!.Grants[0].Path);
    }
}