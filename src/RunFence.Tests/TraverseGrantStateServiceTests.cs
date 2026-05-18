using RunFence.Acl.Traverse;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public class TraverseGrantStateServiceTests
{
    private const string UserSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
    private const string ContainerSid = "S-1-15-2-99-1-2-3-4-5-6";
    private const string OtherContainerSid = "S-1-15-2-99-1-2-3-4-5-7";
    private const string TargetPath = @"C:\Apps\Target";
    private const string OtherPath = @"C:\Apps\Other";

    private static readonly IUiThreadInvoker SyncInvoker =
        new LambdaUiThreadInvoker(a => a(), a => a());

    [Fact]
    public void GetRemainingTraverseEntriesForCleanup_SpecificContainerPreservesOtherSourcesOnly()
    {
        var (_, _, db, service) = CreateService();
        var sharedSid = RunFence.Acl.AclHelper.AllApplicationPackagesSid;
        db.GetOrCreateAccount(sharedSid).Grants.AddRange(
        [
            new GrantedPathEntry
            {
                Path = TargetPath,
                IsTraverseOnly = true,
                SourceSids = [ContainerSid, OtherContainerSid]
            },
            new GrantedPathEntry
            {
                Path = OtherPath,
                IsTraverseOnly = true,
                SourceSids = [ContainerSid]
            },
            new GrantedPathEntry
            {
                Path = @"C:\Manual\Shared",
                IsTraverseOnly = true
            }
        ]);

        var remaining = service.GetRemainingTraverseEntriesForCleanup(
            ContainerSid,
            [
                new GrantIntentLocation(
                    new GrantedPathEntry { Path = TargetPath, IsTraverseOnly = true, SourceSids = [ContainerSid] },
                    new TestGrantIntentStore()),
                new GrantIntentLocation(
                    new GrantedPathEntry { Path = OtherPath, IsTraverseOnly = true, SourceSids = [ContainerSid] },
                    new TestGrantIntentStore())
            ]);

        var trimmedEntry = Assert.Single(
            remaining,
            entry => string.Equals(entry.Path, TargetPath, StringComparison.OrdinalIgnoreCase));
        Assert.Equal([OtherContainerSid], trimmedEntry.SourceSids);
        Assert.DoesNotContain(remaining, entry =>
            string.Equals(entry.Path, OtherPath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(remaining, entry =>
            string.Equals(entry.Path, @"C:\Manual\Shared", StringComparison.OrdinalIgnoreCase) &&
            entry.SourceSids == null);
    }

    [Fact]
    public void GetTraverseGrantPathsForCleanup_ExcludesRemovingSourceGrantAndNormalizesFilesToDirectories()
    {
        var (pathInfo, _, db, service) = CreateService();
        pathInfo.AddDirectory(@"C:\Apps");
        pathInfo.AddDirectory(@"C:\Shared");
        db.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
        {
            Path = @"C:\Apps\App.exe",
            IsTraverseOnly = false,
            IsDeny = false
        });
        db.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
        {
            Path = @"C:\Shared\Shared.exe",
            IsTraverseOnly = false,
            IsDeny = false
        });

        var paths = service.GetTraverseGrantPathsForCleanup(
            UserSid,
            [
                new GrantIntentLocation(
                    new GrantedPathEntry
                    {
                        Path = @"C:\Apps\App.exe",
                        IsTraverseOnly = false,
                        IsDeny = false
                    },
                    new TestGrantIntentStore())
            ]);

        Assert.DoesNotContain(Path.GetFullPath(@"C:\Apps"), paths);
        Assert.Contains(Path.GetFullPath(@"C:\Shared"), paths);
    }

    [Fact]
    public void CollectStoredTraversePaths_UsesAllAppliedPathsAndDeduplicatesAggregateOrder()
    {
        var (_, _, _, service) = CreateService();
        var withStoredPaths = new GrantedPathEntry
        {
            Path = TargetPath,
            IsTraverseOnly = true,
            AllAppliedPaths = [@"C:\Apps\Target", @"C:\Apps", @"C:\"]
        };
        var secondEntry = new GrantedPathEntry
        {
            Path = @"C:\Apps\Nested",
            IsTraverseOnly = true,
            AllAppliedPaths = [@"C:\Apps", @"C:\Apps\Nested", @"C:\"]
        };

        Assert.Equal(
            [Path.GetFullPath(@"C:\Apps\Target"), Path.GetFullPath(@"C:\Apps"), Path.GetFullPath(@"C:\")],
            service.CollectStoredTraversePaths(withStoredPaths));
        Assert.Equal(
            [
                Path.GetFullPath(@"C:\Apps\Target"),
                Path.GetFullPath(@"C:\Apps"),
                Path.GetFullPath(@"C:\"),
                Path.GetFullPath(@"C:\Apps\Nested")
            ],
            service.CollectStoredTraversePaths([withStoredPaths, secondEntry]));
    }

    [Fact]
    public void EntriesEquivalent_IsCaseInsensitiveForStoredPathsAndSources_ButDetectsBehavioralDifferences()
    {
        var (_, _, _, service) = CreateService();
        var left = new GrantedPathEntry
        {
            Path = @"C:\Apps\Target",
            IsTraverseOnly = true,
            AllAppliedPaths = [@"C:\Apps\Target", @"C:\Apps"],
            SourceSids = [ContainerSid],
            PreviousSaclLabel = "S:(ML;;NW;;;ME)"
        };
        var same = new GrantedPathEntry
        {
            Path = @"c:\apps\target",
            IsTraverseOnly = true,
            AllAppliedPaths = [@"c:\apps\target", @"c:\apps"],
            SourceSids = [ContainerSid.ToLowerInvariant()],
            PreviousSaclLabel = "S:(ML;;NW;;;ME)"
        };
        var different = new GrantedPathEntry
        {
            Path = @"C:\Apps\Target",
            IsTraverseOnly = true,
            AllAppliedPaths = [@"C:\Apps\Target"],
            SourceSids = [ContainerSid],
            PreviousSaclLabel = "S:(ML;;NW;;;ME)"
        };

        Assert.True(service.EntriesEquivalent(left, same));
        Assert.False(service.EntriesEquivalent(left, different));
    }

    [Fact]
    public void RestoreStoreSnapshots_RestoresCapturedTraverseEntriesAndLeavesUnrelatedEntries()
    {
        var (_, _, _, service) = CreateService();
        var ownerSid = RunFence.Acl.AclHelper.AllApplicationPackagesSid;
        var mainStore = new TestGrantIntentStore();
        var additionalStore = new TestGrantIntentStore(@"C:\Configs\extra.rfn");
        mainStore.AddEntry(ownerSid, new GrantedPathEntry
        {
            Path = TargetPath,
            IsTraverseOnly = true,
            AllAppliedPaths = [TargetPath, @"C:\Apps"]
        });
        additionalStore.AddEntry(ownerSid, new GrantedPathEntry
        {
            Path = TargetPath,
            IsTraverseOnly = true,
            SourceSids = [ContainerSid],
            AllAppliedPaths = [TargetPath]
        });
        additionalStore.AddEntry(ownerSid, new GrantedPathEntry
        {
            Path = OtherPath,
            IsTraverseOnly = true,
            AllAppliedPaths = [OtherPath]
        });

        var snapshots = service.CaptureStoreSnapshots(ownerSid, Path.GetFullPath(TargetPath), [mainStore, additionalStore]);
        mainStore.ReplaceEntry(
            ownerSid,
            mainStore.GetEntries(ownerSid).Single(entry => string.Equals(entry.Path, TargetPath, StringComparison.OrdinalIgnoreCase)),
            new GrantedPathEntry
            {
                Path = TargetPath,
                IsTraverseOnly = true,
                AllAppliedPaths = [@"C:\Mutated"]
            });
        additionalStore.RemoveEntry(
            ownerSid,
            additionalStore.GetEntries(ownerSid).Single(entry =>
                string.Equals(entry.Path, TargetPath, StringComparison.OrdinalIgnoreCase)));

        service.RestoreStoreSnapshots(ownerSid, Path.GetFullPath(TargetPath), snapshots);

        Assert.Equal(
            [Path.GetFullPath(TargetPath), Path.GetFullPath(@"C:\Apps")],
            Assert.Single(mainStore.GetEntries(ownerSid)).AllAppliedPaths);
        var restoredAdditional = Assert.Single(
            additionalStore.GetEntries(ownerSid),
            entry => string.Equals(entry.Path, TargetPath, StringComparison.OrdinalIgnoreCase));
        Assert.Equal([ContainerSid], restoredAdditional.SourceSids);
        Assert.Contains(additionalStore.GetEntries(ownerSid), entry =>
            string.Equals(entry.Path, OtherPath, StringComparison.OrdinalIgnoreCase));
    }

    private static (TestFileSystemPathInfo PathInfo, TraverseIntentStoreCoordinator Coordinator, AppDatabase Database, TraverseGrantStateService Service)
        CreateService()
    {
        var database = new AppDatabase();
        var pathInfo = new TestFileSystemPathInfo();
        var repository = new GrantIntentRepository(new TestGrantIntentStoreProvider(new TestGrantIntentStore()));
        var coordinator = new TraverseIntentStoreCoordinator(() => repository, new TraverseGrantOwnerResolver());
        var dbAccessor = new UiThreadDatabaseAccessor(new LambdaDatabaseProvider(() => database), () => SyncInvoker);
        var service = new TraverseGrantStateService(dbAccessor, pathInfo, coordinator);
        return (pathInfo, coordinator, database, service);
    }
}
