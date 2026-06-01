using RunFence.Acl;
using RunFence.Acl.Traverse;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public class GrantRuntimeSnapshotServiceTests
{
    private const string UserSid = "S-1-5-21-100-200-300-1001";
    private const string OtherUserSid = "S-1-5-21-100-200-300-1002";
    private const string ThirdUserSid = "S-1-5-21-100-200-300-1003";
    private const string ContainerSid = "S-1-15-2-99-1-2-3-4-5-6";
    private const string OtherContainerSid = "S-1-15-2-99-1-2-3-4-5-7";
    private const string SharedSid = AclHelper.AllApplicationPackagesSid;
    private const string PathValue = @"C:\Apps\Tool";

    private static readonly IUiThreadInvoker SyncInvoker =
        new LambdaUiThreadInvoker(action => action(), action => action());

    [Fact]
    public void RestoreGrantSnapshot_ReplacesCurrentEntryWithCapturedEntry()
    {
        var database = new AppDatabase();
        var service = CreateService(database);
        var previousRights = new SavedRightsState(Execute: true, Write: false, Read: true, Special: false, Own: false);
        var currentRights = new SavedRightsState(Execute: true, Write: true, Read: true, Special: false, Own: false);
        var previousEntry = new GrantedPathEntry
        {
            Path = PathValue,
            IsDeny = false,
            SavedRights = previousRights,
            SourceSids = [ContainerSid]
        };
        var grants = database.GetOrCreateAccount(UserSid).Grants;
        grants.Add(previousEntry.Clone());
        var snapshot = service.CaptureGrantSnapshot(UserSid, PathValue, isDeny: false);
        grants.Clear();
        grants.Add(new GrantedPathEntry
        {
            Path = PathValue,
            IsDeny = false,
            SavedRights = currentRights,
            SourceSids = [OtherContainerSid]
        });

        service.RestoreGrantSnapshot(snapshot);

        var restored = database.GetAccount(UserSid)!.Grants.Single();
        Assert.Equal(previousRights, restored.SavedRights);
        Assert.Equal([ContainerSid], restored.SourceSids);
    }

    [Fact]
    public void RestoreGrantSnapshot_NullSnapshot_RemovesEntryAndEmptyAccount()
    {
        var database = new AppDatabase();
        var service = CreateService(database);
        database.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
        {
            Path = PathValue,
            IsDeny = false,
            SavedRights = SavedRightsState.DefaultForMode(isDeny: false)
        });
        var snapshot = new GrantRuntimeEntrySnapshot(
            UserSid,
            PathValue,
            isTraverseOnly: false,
            isDeny: false,
            entry: null);

        service.RestoreGrantSnapshot(snapshot);

        Assert.Null(database.GetAccount(UserSid));
    }

    [Fact]
    public void RestoreTraverseSnapshot_NormalAccount_ReplacesCurrentEntryWithCapturedEntry()
    {
        var database = new AppDatabase();
        var service = CreateService(database);
        var previousEntry = new GrantedPathEntry
        {
            Path = PathValue,
            IsTraverseOnly = true,
            AllAppliedPaths = [@"C:\", @"C:\Apps"]
        };
        var grants = database.GetOrCreateAccount(UserSid).Grants;
        grants.Add(previousEntry.Clone());
        var snapshot = service.CaptureTraverseSnapshot(UserSid, PathValue);
        grants.Clear();
        grants.Add(new GrantedPathEntry
        {
            Path = PathValue,
            IsTraverseOnly = true,
            AllAppliedPaths = [@"C:\Changed"]
        });

        service.RestoreTraverseSnapshot(snapshot);

        var restored = database.GetAccount(UserSid)!.Grants.Single();
        Assert.True(restored.IsTraverseOnly);
        Assert.Equal(previousEntry.AllAppliedPaths, restored.AllAppliedPaths);
    }

    [Fact]
    public void RestoreTraverseSnapshot_SpecificContainerNullSnapshot_RemovesOnlySourceTrackedSharedEntry()
    {
        var database = new AppDatabase();
        var service = CreateService(database);
        var sharedEntries = database.GetOrCreateAccount(SharedSid).Grants;
        sharedEntries.Add(new GrantedPathEntry { Path = PathValue, IsTraverseOnly = true, SourceSids = null });
        sharedEntries.Add(new GrantedPathEntry { Path = PathValue, IsTraverseOnly = true, SourceSids = [ContainerSid] });
        sharedEntries.Add(new GrantedPathEntry { Path = PathValue, IsTraverseOnly = true, SourceSids = [OtherContainerSid] });
        var snapshot = new GrantRuntimeEntrySnapshot(
            ContainerSid,
            PathValue,
            isTraverseOnly: true,
            isDeny: false,
            entry: null);

        service.RestoreTraverseSnapshot(snapshot);

        Assert.Equal(2, sharedEntries.Count);
        Assert.Contains(sharedEntries, entry => entry.SourceSids == null);
        Assert.Contains(sharedEntries, entry => entry.SourceSids?.SequenceEqual([OtherContainerSid]) == true);
        Assert.DoesNotContain(sharedEntries, entry => entry.SourceSids?.SequenceEqual([ContainerSid]) == true);
    }

    [Fact]
    public void RestoreTraverseSnapshot_DirectAllApplicationPackagesSid_UsesSharedSidAsOwnAccount()
    {
        var database = new AppDatabase();
        var service = CreateService(database);
        database.GetOrCreateAccount(SharedSid).Grants.Add(new GrantedPathEntry
        {
            Path = PathValue,
            IsTraverseOnly = true
        });
        var snapshot = new GrantRuntimeEntrySnapshot(
            SharedSid,
            PathValue,
            isTraverseOnly: true,
            isDeny: false,
            entry: null);

        service.RestoreTraverseSnapshot(snapshot);

        Assert.Null(database.GetAccount(SharedSid));
    }

    [Fact]
    public void RestoreGrantSnapshot_LowIntegrityEntry_PreservesSavedRightsSourcesAndSaclLabel()
    {
        var database = new AppDatabase();
        var service = CreateService(database);
        var previousEntry = new GrantedPathEntry
        {
            Path = PathValue,
            IsDeny = false,
            SavedRights = new SavedRightsState(Execute: false, Write: true, Read: true, Special: true, Own: false),
            SourceSids = [UserSid],
            PreviousSaclLabel = "S:(ML;;NW;;;ME)"
        };
        database.GetOrCreateAccount(AclHelper.LowIntegritySid).Grants.Add(previousEntry.Clone());
        var snapshot = service.CaptureGrantSnapshot(AclHelper.LowIntegritySid, PathValue, isDeny: false);
        database.GetAccount(AclHelper.LowIntegritySid)!.Grants.Clear();

        service.RestoreGrantSnapshot(snapshot);

        var restored = database.GetAccount(AclHelper.LowIntegritySid)!.Grants.Single();
        Assert.Equal(previousEntry.SavedRights, restored.SavedRights);
        Assert.Equal(previousEntry.SourceSids, restored.SourceSids);
        Assert.Equal(previousEntry.PreviousSaclLabel, restored.PreviousSaclLabel);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RestoreLinkedEntrySnapshots_UsesSourceLinkRules(bool isTraverseOnly)
    {
        var database = new AppDatabase();
        var service = CreateService(database);
        database.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
        {
            Path = PathValue,
            IsTraverseOnly = isTraverseOnly,
            SourceSids = [ContainerSid]
        });
        database.GetOrCreateAccount(OtherUserSid).Grants.Add(new GrantedPathEntry
        {
            Path = PathValue,
            IsTraverseOnly = isTraverseOnly,
            SourceSids = [OtherContainerSid]
        });
        var previousThirdUserEntry = new GrantedPathEntry
        {
            Path = PathValue,
            IsTraverseOnly = isTraverseOnly,
            OwnerContainerSid = ContainerSid
        };
        var snapshots = new[]
        {
            new GrantRuntimeEntrySnapshot(
                ThirdUserSid,
                PathValue,
                isTraverseOnly,
                isDeny: false,
                previousThirdUserEntry)
        };

        service.RestoreLinkedEntrySnapshots(
            PathValue,
            isTraverseOnly,
            ContainerSid,
            snapshots);

        Assert.Null(database.GetAccount(UserSid));
        Assert.NotNull(database.GetAccount(OtherUserSid));
        Assert.Equal([OtherContainerSid], database.GetAccount(OtherUserSid)!.Grants.Single().SourceSids);
        Assert.Equal(ContainerSid, database.GetAccount(ThirdUserSid)!.Grants.Single().OwnerContainerSid);
        Assert.Equal(isTraverseOnly, database.GetAccount(ThirdUserSid)!.Grants.Single().IsTraverseOnly);
    }

    private static GrantRuntimeSnapshotService CreateService(AppDatabase database)
        => new(
            new UiThreadDatabaseAccessor(new LambdaDatabaseProvider(() => database), () => SyncInvoker),
            new TraverseGrantOwnerResolver());
}
