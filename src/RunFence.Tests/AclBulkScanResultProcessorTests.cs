using Moq;
using RunFence.Acl;
using RunFence.Acl.UI;
using RunFence.Core.Models;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public class AclBulkScanResultProcessorTests
{
    private const string Sid1 = "S-1-5-21-1234567890-1234567890-1234567890-1001";
    private const string Sid2 = "S-1-5-21-1234567890-1234567890-1234567890-1002";

    private static AclBulkScanResultProcessor CreateProcessor(IDatabaseProvider? databaseProvider = null)
        => new(databaseProvider ?? new Mock<IDatabaseProvider>().Object);

    private static AclBulkScanResultProcessor CreateProcessorWithDatabase(AppDatabase database)
        => CreateProcessor(new LambdaDatabaseProvider(() => database));

    private static DiscoveredGrant MakeAllow(string path, bool execute = false, bool write = false,
        bool read = true, bool special = false, bool isOwner = false)
        => new(path, IsDeny: false, Execute: execute, Write: write, Read: read, Special: special, IsOwner: isOwner);

    private static DiscoveredGrant MakeDeny(string path, bool execute = false, bool read = false)
        => new(path, IsDeny: true, Execute: execute, Write: false, Read: read, Special: false, IsOwner: false);

    [Fact]
    public void ApplyScanResults_AddsGrantToDatabase()
    {
        var database = new AppDatabase();
        var selected = new Dictionary<string, AccountScanResult>
        {
            [Sid1] = new([MakeAllow(@"C:\data\shared")], [])
        };
        bool saveInvoked = false;

        CreateProcessorWithDatabase(database).ApplyScanResults(selected, () => saveInvoked = true);

        Assert.True(saveInvoked);
        var grants = database.GetAccount(Sid1)?.Grants;
        Assert.NotNull(grants);
        Assert.Single(grants);
        Assert.Equal(AclHelper.NormalizePath(@"C:\data\shared"), grants[0].Path);
        Assert.False(grants[0].IsDeny);
        Assert.False(grants[0].IsTraverseOnly);
    }

    [Fact]
    public void ApplyScanResults_AddsDenyGrantToDatabase()
    {
        var database = new AppDatabase();
        var selected = new Dictionary<string, AccountScanResult>
        {
            [Sid1] = new([MakeDeny(@"C:\restricted")], [])
        };

        CreateProcessorWithDatabase(database).ApplyScanResults(selected, () => { });

        var grants = database.GetAccount(Sid1)?.Grants;
        Assert.NotNull(grants);
        Assert.Single(grants);
        Assert.True(grants[0].IsDeny);
        Assert.False(grants[0].IsTraverseOnly);
    }

    [Fact]
    public void ApplyScanResults_DeduplicatesGrant_WhenSamePathAndSameIsDeny()
    {
        var database = new AppDatabase();
        var normalizedPath = AclHelper.NormalizePath(@"C:\data\shared");
        database.GetOrCreateAccount(Sid1).Grants.Add(new GrantedPathEntry
        {
            Path = normalizedPath,
            IsDeny = false,
            IsTraverseOnly = false,
            SavedRights = SavedRightsState.DefaultForMode(false)
        });

        var selected = new Dictionary<string, AccountScanResult>
        {
            [Sid1] = new([MakeAllow(@"C:\data\shared", execute: true, write: true, read: true, special: true)], [])
        };

        var summary = CreateProcessorWithDatabase(database).ApplyScanResults(selected, () => { });

        var entry = Assert.Single(database.GetAccount(Sid1)!.Grants);
        Assert.Equal(0, summary.ImportedCount);
        Assert.Equal(1, summary.UpdatedCount);
        Assert.Empty(summary.SkippedOppositeModeConflictPaths);
        Assert.NotNull(entry.SavedRights);
        Assert.True(entry.SavedRights!.Execute);
        Assert.True(entry.SavedRights.Write);
        Assert.True(entry.SavedRights.Special);
    }

    [Fact]
    public void ApplyScanResults_ExistingSameModeGrantWithSameRights_DoesNotReportUpdateOrSave()
    {
        var database = new AppDatabase();
        var normalizedPath = AclHelper.NormalizePath(@"C:\data\shared");
        var existingRights = SavedRightsState.DefaultForMode(false) with
        {
            Execute = true,
            Write = true,
            Special = true
        };
        database.GetOrCreateAccount(Sid1).Grants.Add(new GrantedPathEntry
        {
            Path = normalizedPath,
            IsDeny = false,
            IsTraverseOnly = false,
            SavedRights = existingRights
        });

        var selected = new Dictionary<string, AccountScanResult>
        {
            [Sid1] = new([MakeAllow(@"C:\data\shared", execute: true, write: true, read: true, special: true)], [])
        };
        var saveCalled = false;

        var summary = CreateProcessorWithDatabase(database).ApplyScanResults(selected, () => saveCalled = true);

        var entry = Assert.Single(database.GetAccount(Sid1)!.Grants);
        Assert.Equal(existingRights, entry.SavedRights);
        Assert.Equal(0, summary.ImportedCount);
        Assert.Equal(0, summary.UpdatedCount);
        Assert.False(summary.HasChanges);
        Assert.False(saveCalled);
        Assert.Empty(summary.SkippedOppositeModeConflictPaths);
    }

    [Fact]
    public void ApplyScanResults_ScanContainsBothAllowAndDenyForSamePath_ImportsBoth()
    {
        var database = new AppDatabase();
        var normalizedPath = AclHelper.NormalizePath(@"C:\data\shared");
        var selected = new Dictionary<string, AccountScanResult>
        {
            [Sid1] = new([MakeAllow(@"C:\data\shared", execute: true), MakeDeny(@"C:\data\shared", read: true)], [])
        };

        var summary = CreateProcessorWithDatabase(database).ApplyScanResults(selected, () => { });

        var grants = database.GetAccount(Sid1)!.Grants.OrderBy(grant => grant.IsDeny).ToList();
        Assert.Equal(2, grants.Count);
        Assert.Equal(normalizedPath, grants[0].Path);
        Assert.False(grants[0].IsDeny);
        Assert.True(grants[0].SavedRights!.Execute);
        Assert.Equal(normalizedPath, grants[1].Path);
        Assert.True(grants[1].IsDeny);
        Assert.True(grants[1].SavedRights!.Read);
        Assert.Equal(2, summary.ImportedCount);
        Assert.Equal(0, summary.UpdatedCount);
        Assert.Empty(summary.SkippedOppositeModeConflictPaths);
    }

    [Fact]
    public void ApplyScanResults_ExistingOppositeModeGrantAndBothModesScanned_TracksBothModes()
    {
        var database = new AppDatabase();
        var normalizedPath = AclHelper.NormalizePath(@"C:\data\shared");
        database.GetOrCreateAccount(Sid1).Grants.Add(new GrantedPathEntry
        {
            Path = normalizedPath,
            IsDeny = false,
            IsTraverseOnly = false,
            SavedRights = SavedRightsState.DefaultForMode(false) with { Execute = true }
        });

        var selected = new Dictionary<string, AccountScanResult>
        {
            [Sid1] = new([MakeAllow(@"C:\data\shared", execute: true), MakeDeny(@"C:\data\shared", execute: true, read: true)], [])
        };

        var summary = CreateProcessorWithDatabase(database).ApplyScanResults(selected, () => { });

        var grants = database.GetAccount(Sid1)!.Grants.OrderBy(grant => grant.IsDeny).ToList();
        Assert.Equal(2, grants.Count);
        Assert.False(grants[0].IsDeny);
        Assert.True(grants[0].SavedRights!.Execute);
        Assert.True(grants[1].IsDeny);
        Assert.True(grants[1].SavedRights!.Execute);
        Assert.True(grants[1].SavedRights!.Read);
        Assert.Equal(1, summary.ImportedCount);
        Assert.Equal(0, summary.UpdatedCount);
        Assert.Empty(summary.SkippedOppositeModeConflictPaths);
    }

    [Fact]
    public void ApplyScanResults_SingleScannedModeWithExistingOppositeMode_SkipsAndDoesNotSave()
    {
        var database = new AppDatabase();
        var normalizedPath = AclHelper.NormalizePath(@"C:\data\shared");
        database.GetOrCreateAccount(Sid1).Grants.Add(new GrantedPathEntry
        {
            Path = normalizedPath,
            IsDeny = false,
            IsTraverseOnly = false,
            SavedRights = SavedRightsState.DefaultForMode(false)
        });

        var selected = new Dictionary<string, AccountScanResult>
        {
            [Sid1] = new([MakeDeny(@"C:\data\shared")], [])
        };
        var saveCalled = false;

        var summary = CreateProcessorWithDatabase(database).ApplyScanResults(selected, () => saveCalled = true);

        Assert.False(summary.HasChanges);
        Assert.False(saveCalled);
        Assert.Equal([normalizedPath], summary.SkippedOppositeModeConflictPaths);
    }

    [Fact]
    public void ApplyScanResults_AddsTraversePathEntry()
    {
        var database = new AppDatabase();
        var selected = new Dictionary<string, AccountScanResult>
        {
            [Sid1] = new([], [@"C:\parent\traverse"])
        };

        CreateProcessorWithDatabase(database).ApplyScanResults(selected, () => { });

        var grants = database.GetAccount(Sid1)?.Grants;
        Assert.NotNull(grants);
        Assert.Single(grants);
        Assert.True(grants[0].IsTraverseOnly);
        Assert.Equal(AclHelper.NormalizePath(@"C:\parent\traverse"), grants[0].Path);
    }

    [Fact]
    public void ApplyScanResults_DeduplicatesTraversePath()
    {
        var database = new AppDatabase();
        var normalizedPath = AclHelper.NormalizePath(@"C:\parent");
        database.GetOrCreateAccount(Sid1).Grants.Add(new GrantedPathEntry
        {
            Path = normalizedPath,
            IsTraverseOnly = true
        });

        var selected = new Dictionary<string, AccountScanResult>
        {
            [Sid1] = new([], [@"C:\parent"])
        };

        CreateProcessorWithDatabase(database).ApplyScanResults(selected, () => { });

        Assert.Single(database.GetAccount(Sid1)!.Grants);
    }

    [Fact]
    public void ApplyScanResults_AllowGrant_RightsMapFromDiscoveredGrant()
    {
        var database = new AppDatabase();
        var grant = MakeAllow(@"C:\tools\util.exe", execute: true, write: true, read: true, isOwner: true);
        var selected = new Dictionary<string, AccountScanResult>
        {
            [Sid1] = new([grant], [])
        };

        CreateProcessorWithDatabase(database).ApplyScanResults(selected, () => { });

        var savedRights = database.GetAccount(Sid1)!.Grants[0].SavedRights;
        Assert.NotNull(savedRights);
        Assert.True(savedRights.Execute);
        Assert.True(savedRights.Write);
        Assert.True(savedRights.Own);
    }

    [Fact]
    public void ApplyScanResults_DenyGrant_RightsMapFromDiscoveredGrant()
    {
        var database = new AppDatabase();
        var grant = MakeDeny(@"C:\restricted", execute: true, read: true);
        var selected = new Dictionary<string, AccountScanResult>
        {
            [Sid1] = new([grant], [])
        };

        CreateProcessorWithDatabase(database).ApplyScanResults(selected, () => { });

        var savedRights = database.GetAccount(Sid1)!.Grants[0].SavedRights;
        Assert.NotNull(savedRights);
        Assert.True(savedRights.Execute);
        Assert.True(savedRights.Read);
        Assert.False(savedRights.Own);
    }

    [Fact]
    public void ApplyScanResults_MultipleSids_EachGetsSeparateGrants()
    {
        var database = new AppDatabase();
        var selected = new Dictionary<string, AccountScanResult>
        {
            [Sid1] = new([MakeAllow(@"C:\data\path1")], []),
            [Sid2] = new([MakeAllow(@"C:\data\path2")], [])
        };

        CreateProcessorWithDatabase(database).ApplyScanResults(selected, () => { });

        Assert.Single(database.GetAccount(Sid1)!.Grants);
        Assert.Equal(AclHelper.NormalizePath(@"C:\data\path1"), database.GetAccount(Sid1)!.Grants[0].Path);
        Assert.Single(database.GetAccount(Sid2)!.Grants);
        Assert.Equal(AclHelper.NormalizePath(@"C:\data\path2"), database.GetAccount(Sid2)!.Grants[0].Path);
    }

    [Fact]
    public void ApplyScanResults_NormalizesPathCaseInsensitive()
    {
        var database = new AppDatabase();
        var normalizedPath = AclHelper.NormalizePath(@"C:\Data\Shared");
        database.GetOrCreateAccount(Sid1).Grants.Add(new GrantedPathEntry
        {
            Path = normalizedPath,
            IsDeny = false,
            IsTraverseOnly = false
        });

        var selected = new Dictionary<string, AccountScanResult>
        {
            [Sid1] = new([MakeAllow(@"C:\data\shared")], [])
        };

        CreateProcessorWithDatabase(database).ApplyScanResults(selected, () => { });

        Assert.Single(database.GetAccount(Sid1)!.Grants);
    }
}
