using System.Security.AccessControl;
using RunFence.Acl;
using RunFence.Core.Models;
using Xunit;

namespace RunFence.Tests;

public class GrantLookupTests
{
    [Fact]
    public void GrantEntryLookup_FindNonTraverseGrantConflict_IgnoresTraverseOnlyEntries()
    {
        var normalizedPath = Path.GetFullPath(@"C:\Data\Test");
        var entries = new List<GrantedPathEntry>
        {
            new() { Path = normalizedPath, IsTraverseOnly = true },
            new() { Path = normalizedPath, IsDeny = false, SavedRights = SavedRightsState.DefaultForMode(false) },
            new() { Path = normalizedPath, IsDeny = true, SavedRights = SavedRightsState.DefaultForMode(true) }
        };

        var conflict = GrantEntryLookup.FindNonTraverseGrantConflict(entries, normalizedPath, isDeny: false);

        Assert.True(conflict.HasSameModeEntry);
        Assert.NotNull(conflict.OppositeModeEntry);
        Assert.False(conflict.OppositeModeEntry!.IsTraverseOnly);
    }

    [Fact]
    public void GrantEntryLookup_FindGrantEntryInList_MatchesNormalizedPath()
    {
        var normalizedPath = Path.GetFullPath(@"C:\Data\..\Data\Test");
        var entries = new List<GrantedPathEntry>
        {
            new() { Path = Path.GetFullPath(@"C:\Data\Test"), IsDeny = false }
        };

        var entry = GrantEntryLookup.FindGrantEntryInList(entries, normalizedPath, isDeny: false);

        Assert.NotNull(entry);
        Assert.Equal(Path.GetFullPath(@"C:\Data\Test"), entry!.Path);
    }

    [Fact]
    public void GrantEntryLookup_FindGrantEntryInDb_MatchesNormalizedPath()
    {
        var database = new AppDatabase();
        var normalizedPath = Path.GetFullPath(@"C:\Data\..\Data\Test");
        database.GetOrCreateAccount("S-1-5-21-1").Grants.Add(
            new GrantedPathEntry { Path = Path.GetFullPath(@"C:\Data\Test"), IsDeny = true });

        var entry = GrantEntryLookup.FindGrantEntryInDb(database, "S-1-5-21-1", normalizedPath, isDeny: true);

        Assert.NotNull(entry);
        Assert.Equal(Path.GetFullPath(@"C:\Data\Test"), entry!.Path);
    }

    [Fact]
    public void TraverseEntryLookup_FindTraverseEntryInDb_SpecificContainerPrefersSourceTrackedEntry()
    {
        var database = new AppDatabase();
        var normalizedPath = Path.GetFullPath(@"C:\Data\Test");
        var containerSid = "S-1-15-2-99-1-2-3-4-5-6";
        var sharedOwnerSid = AclHelper.AllApplicationPackagesSid;

        database.GetOrCreateAccount(sharedOwnerSid).Grants.AddRange(
        [
            new GrantedPathEntry { Path = normalizedPath, IsTraverseOnly = true },
            new GrantedPathEntry
            {
                Path = normalizedPath,
                IsTraverseOnly = true,
                SourceSids = [containerSid]
            }
        ]);

        var trackedEntry = TraverseEntryLookup.FindTraverseEntryInDb(
            database,
            containerSid,
            normalizedPath,
            includeManualSharedEntries: true);

        Assert.NotNull(trackedEntry);
        var trackedSources = Assert.Single(trackedEntry!.SourceSids!);
        Assert.Equal(containerSid, trackedSources);
    }

    [Fact]
    public void TraverseEntryLookup_FindTraverseEntryInDb_CanReturnManualSharedEntryWhenRequested()
    {
        var database = new AppDatabase();
        var normalizedPath = Path.GetFullPath(@"C:\Data\Test");
        var containerSid = "S-1-15-2-99-1-2-3-4-5-6";

        database.GetOrCreateAccount(AclHelper.AllApplicationPackagesSid).Grants.Add(
            new GrantedPathEntry { Path = normalizedPath, IsTraverseOnly = true });

        var withoutManual = TraverseEntryLookup.FindTraverseEntryInDb(
            database,
            containerSid,
            normalizedPath,
            includeManualSharedEntries: false);
        var withManual = TraverseEntryLookup.FindTraverseEntryInDb(
            database,
            containerSid,
            normalizedPath,
            includeManualSharedEntries: true);

        Assert.Null(withoutManual);
        Assert.NotNull(withManual);
        Assert.Null(withManual!.SourceSids);
    }

    [Fact]
    public void TraverseEntryLookup_ResolveStorageOwnerSid_SpecificContainerUsesSharedOwner()
    {
        var containerSid = "S-1-15-2-99-1-2-3-4-5-6";

        var ownerSid = TraverseEntryLookup.ResolveStorageOwnerSid(containerSid);
        var aclSid = TraverseEntryLookup.ResolveAclSid(containerSid);

        Assert.Equal(AclHelper.AllApplicationPackagesSid, ownerSid);
        Assert.Equal(AclHelper.AllApplicationPackagesSid, aclSid);
    }

    [Fact]
    public void TraverseEntryLookup_FindTraverseEntryForMutation_CanSelectManualOrSourceTrackedSharedEntry()
    {
        var containerSid = "S-1-15-2-99-1-2-3-4-5-6";
        var normalizedPath = Path.GetFullPath(@"C:\Data\Test");
        var entries = new List<GrantedPathEntry>
        {
            new() { Path = normalizedPath, IsTraverseOnly = true },
            new()
            {
                Path = normalizedPath,
                IsTraverseOnly = true,
                SourceSids = [containerSid]
            }
        };

        var manualEntry = TraverseEntryLookup.FindTraverseEntryForMutation(
            entries,
            containerSid,
            normalizedPath,
            sourceTrackedEntry: false);
        var sourceTrackedEntry = TraverseEntryLookup.FindTraverseEntryForMutation(
            entries,
            containerSid,
            normalizedPath,
            sourceTrackedEntry: true);

        Assert.NotNull(manualEntry);
        Assert.Null(manualEntry!.SourceSids);
        Assert.NotNull(sourceTrackedEntry);
        Assert.Equal(containerSid, Assert.Single(sourceTrackedEntry!.SourceSids!));
    }

    [Fact]
    public void GrantIntentRestoreSnapshot_ClonesSecurityAndTraversePaths()
    {
        var security = new DirectorySecurity();
        security.SetOwner(new System.Security.Principal.SecurityIdentifier(
            System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid,
            null));
        var location = new GrantIntentRestoreLocation(
            new GrantIntentStoreIdentity(@"C:\Configs\test.rfn"),
            new GrantedPathEntry { Path = @"C:\Data\Test", IsTraverseOnly = true });
        var snapshot = new GrantIntentRestoreSnapshot(
            runtimeEntry: null,
            locations: [location],
            previousTargetSecurity: security,
            touchedTraversePaths: [@"C:\Data", @"C:\Data\Test"]);

        security.SetOwner(new System.Security.Principal.SecurityIdentifier(
            System.Security.Principal.WellKnownSidType.LocalSystemSid,
            null));

        Assert.NotNull(snapshot.PreviousTargetSecurity);
        Assert.Equal(Path.GetFullPath(@"C:\Configs\test.rfn"), snapshot.Locations[0].StoreIdentity.ConfigPath);
        Assert.Equal([@"C:\Data", @"C:\Data\Test"], snapshot.TouchedTraversePaths);
        Assert.NotSame(security, snapshot.PreviousTargetSecurity);
    }
}
