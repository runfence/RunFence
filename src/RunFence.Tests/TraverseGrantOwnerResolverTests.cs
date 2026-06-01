using System.Linq;
using RunFence.Acl;
using RunFence.Acl.Traverse;
using RunFence.Core.Models;
using Xunit;

namespace RunFence.Tests;

public class TraverseGrantOwnerResolverTests
{
    private const string UserSid = "S-1-5-21-100-200-300-1001";
    private const string LowIntegritySid = AclHelper.LowIntegritySid;
    private const string SharedSid = AclHelper.AllApplicationPackagesSid;
    private const string ContainerSid = "S-1-15-2-99-1-2-3-4-5-6";
    private const string OtherContainerSid = "S-1-15-2-99-1-2-3-4-5-7";

    private readonly TraverseGrantOwnerResolver _resolver = new();

    [Theory]
    [InlineData(UserSid, false, UserSid, UserSid)]
    [InlineData(LowIntegritySid, false, LowIntegritySid, LowIntegritySid)]
    [InlineData(SharedSid, false, SharedSid, SharedSid)]
    [InlineData(ContainerSid, true, SharedSid, SharedSid)]
    public void ResolveBehavior_ReturnsExpectedPolicy(
        string sid,
        bool usesShared,
        string storageOwnerSid,
        string aclSid)
    {
        Assert.Equal(usesShared, _resolver.UsesSharedContainerTraverse(sid));
        Assert.Equal(storageOwnerSid, _resolver.ResolveStorageOwnerSid(sid));
        Assert.Equal(aclSid, _resolver.ResolveAclSid(sid));
    }

    [Fact]
    public void EntryAppliesToSid_SourceLessSharedEntry_DependsOnFlag()
    {
        var entry = new GrantedPathEntry { Path = @"C:\Apps", IsTraverseOnly = true, SourceSids = null };

        Assert.True(_resolver.EntryAppliesToSid(entry, ContainerSid, includeManualSharedEntries: true));
        Assert.False(_resolver.EntryAppliesToSid(entry, ContainerSid, includeManualSharedEntries: false));
    }

    [Fact]
    public void TraverseStoreAndCleanupOwners_UseSharedAccountForSpecificContainers()
    {
        var database = new AppDatabase();
        database.GetOrCreateAccount(ContainerSid);
        database.GetOrCreateAccount("S-1-15-2-99-1-2-3-4-5-7");

        var sharedStore = _resolver.GetOrCreateTraverseStore(database, ContainerSid);
        sharedStore.Add(new GrantedPathEntry { Path = @"C:\Apps", IsTraverseOnly = true });

        Assert.Same(sharedStore, _resolver.GetTraverseStoreOrEmpty(database, ContainerSid));
        Assert.Equal(
            [ContainerSid, "S-1-15-2-99-1-2-3-4-5-7"],
            _resolver.GetGrantOwnersForTraverseCleanup(database, ContainerSid).Select(account => account.Sid).OrderBy(sid => sid).ToArray());
    }

    [Fact]
    public void FindTraverseEntry_SharedContainer_UsesSourceSidFilteringByDefault()
    {
        var database = new AppDatabase();
        var sharedStore = _resolver.GetOrCreateTraverseStore(database, ContainerSid);
        sharedStore.Add(new GrantedPathEntry
        {
            Path = @"C:\Apps",
            IsTraverseOnly = true,
            SourceSids = null
        });
        sharedStore.Add(new GrantedPathEntry
        {
            Path = @"C:\Apps",
            IsTraverseOnly = true,
            SourceSids = [ContainerSid]
        });

        var entry = _resolver.FindTraverseEntry(database, ContainerSid, @"C:\Apps");

        Assert.NotNull(entry);
        Assert.Equal([ContainerSid], entry!.SourceSids);
    }

    [Fact]
    public void FindTraverseEntry_SharedContainer_CanIncludeManualSharedEntries()
    {
        var database = new AppDatabase();
        _resolver.GetOrCreateTraverseStore(database, ContainerSid).Add(new GrantedPathEntry
        {
            Path = @"C:\Apps",
            IsTraverseOnly = true,
            SourceSids = null
        });

        var entry = _resolver.FindTraverseEntry(
            database,
            ContainerSid,
            @"C:\Apps",
            includeManualSharedEntries: true);

        Assert.NotNull(entry);
        Assert.Null(entry!.SourceSids);
    }

    [Fact]
    public void FindTraverseEntry_SharedContainer_InclusiveLookupPrefersSourceTrackedEntryOverManualEntry()
    {
        var database = new AppDatabase();
        var sharedStore = _resolver.GetOrCreateTraverseStore(database, ContainerSid);
        sharedStore.Add(new GrantedPathEntry
        {
            Path = @"C:\Apps",
            IsTraverseOnly = true,
            SourceSids = null
        });
        sharedStore.Add(new GrantedPathEntry
        {
            Path = @"C:\Apps",
            IsTraverseOnly = true,
            SourceSids = [ContainerSid]
        });

        var entry = _resolver.FindTraverseEntry(
            database,
            ContainerSid,
            @"C:\Apps",
            includeManualSharedEntries: true);

        Assert.NotNull(entry);
        Assert.Equal([ContainerSid], entry!.SourceSids);
    }

    [Fact]
    public void RestoreTraverseEntry_SharedContainerManualSnapshot_PreservesUnrelatedSourceTrackedEntry()
    {
        var database = new AppDatabase();
        var sharedStore = _resolver.GetOrCreateTraverseStore(database, ContainerSid);
        var manualEntry = new GrantedPathEntry
        {
            Path = @"C:\Apps",
            IsTraverseOnly = true,
            SourceSids = null
        };
        sharedStore.Add(manualEntry);
        sharedStore.Add(new GrantedPathEntry
        {
            Path = @"C:\Apps",
            IsTraverseOnly = true,
            SourceSids = [OtherContainerSid]
        });

        _resolver.RestoreTraverseEntry(database, ContainerSid, @"C:\Apps", manualEntry);

        var entries = database.GetAccount(SharedSid)!.Grants;
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, entry => entry.SourceSids == null);
        Assert.Contains(entries, entry => entry.SourceSids?.SequenceEqual([OtherContainerSid]) == true);
    }

    [Fact]
    public void RestoreTraverseEntry_SharedContainerNullSnapshot_RemovesOnlySourceTrackedEntryForSid()
    {
        var database = new AppDatabase();
        var sharedStore = _resolver.GetOrCreateTraverseStore(database, ContainerSid);
        sharedStore.Add(new GrantedPathEntry
        {
            Path = @"C:\Apps",
            IsTraverseOnly = true,
            SourceSids = null
        });
        sharedStore.Add(new GrantedPathEntry
        {
            Path = @"C:\Apps",
            IsTraverseOnly = true,
            SourceSids = [ContainerSid]
        });
        sharedStore.Add(new GrantedPathEntry
        {
            Path = @"C:\Apps",
            IsTraverseOnly = true,
            SourceSids = [OtherContainerSid]
        });

        _resolver.RestoreTraverseEntry(database, ContainerSid, @"C:\Apps", snapshot: null);

        var entries = database.GetAccount(SharedSid)!.Grants;
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, entry => entry.SourceSids == null);
        Assert.Contains(entries, entry => entry.SourceSids?.SequenceEqual([OtherContainerSid]) == true);
        Assert.DoesNotContain(entries, entry => entry.SourceSids?.SequenceEqual([ContainerSid]) == true);
    }
}
