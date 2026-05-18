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

    private static DiscoveredGrant MakeGrant(string path, bool isDeny = false)
        => new(Path: path, IsDeny: isDeny, Execute: false, Write: false, Read: true, Special: false, IsOwner: false);

    private static Mock<IAclService> MockAcl(params (AppEntry App, string ResolvedPath)[] mappings)
    {
        var mock = new Mock<IAclService>();
        foreach (var (app, resolvedPath) in mappings)
            mock.Setup(a => a.ResolveAclTargetPath(app)).Returns(resolvedPath);
        return mock;
    }

    [Fact]
    public void NoManagedApps_ResultsUnchanged()
    {
        var grant = MakeGrant(@"C:\apps\foo.exe");
        var results = new Dictionary<string, AccountScanResult>(StringComparer.OrdinalIgnoreCase)
        {
            [Sid1] = new([grant], [])
        };

        var filtered = CreateProcessor().FilterManagedPaths(results, [], new Mock<IAclService>().Object);

        Assert.Single(filtered[Sid1].Grants);
    }

    [Fact]
    public void GrantAtManagedFilePath_Filtered()
    {
        var app = new AppEntry { ExePath = @"C:\apps\foo.exe", AclTarget = AclTarget.File, RestrictAcl = true };
        var aclMock = MockAcl((app, @"C:\apps\foo.exe"));
        var grant = MakeGrant(@"C:\apps\foo.exe");
        var results = new Dictionary<string, AccountScanResult>(StringComparer.OrdinalIgnoreCase)
        {
            [Sid1] = new([grant], [])
        };

        var filtered = CreateProcessor().FilterManagedPaths(results, [app], aclMock.Object);

        Assert.Empty(filtered);
    }

    [Fact]
    public void GrantUnderManagedFolderPath_Filtered()
    {
        var app = new AppEntry { ExePath = @"C:\apps\myapp\myapp.exe", AclTarget = AclTarget.Folder, RestrictAcl = true };
        var aclMock = MockAcl((app, @"C:\apps\myapp"));
        var grant = MakeGrant(@"C:\apps\myapp\myapp.exe");
        var results = new Dictionary<string, AccountScanResult>(StringComparer.OrdinalIgnoreCase)
        {
            [Sid1] = new([grant], [])
        };

        var filtered = CreateProcessor().FilterManagedPaths(results, [app], aclMock.Object);

        Assert.Empty(filtered);
    }

    [Fact]
    public void GrantAtUnmanagedPath_PassesThrough()
    {
        var app = new AppEntry { ExePath = @"C:\apps\myapp\myapp.exe", AclTarget = AclTarget.Folder, RestrictAcl = true };
        var aclMock = MockAcl((app, @"C:\apps\myapp"));
        var grant = MakeGrant(@"C:\other\data.txt");
        var results = new Dictionary<string, AccountScanResult>(StringComparer.OrdinalIgnoreCase)
        {
            [Sid1] = new([grant], [])
        };

        var filtered = CreateProcessor().FilterManagedPaths(results, [app], aclMock.Object);

        Assert.Single(filtered[Sid1].Grants);
    }

    [Fact]
    public void TraversePathUnderManagedFolder_Filtered()
    {
        var app = new AppEntry { ExePath = @"C:\apps\myapp\myapp.exe", AclTarget = AclTarget.Folder, RestrictAcl = true };
        var aclMock = MockAcl((app, @"C:\apps\myapp"));
        var results = new Dictionary<string, AccountScanResult>(StringComparer.OrdinalIgnoreCase)
        {
            [Sid1] = new([], [@"C:\apps\myapp"])
        };

        var filtered = CreateProcessor().FilterManagedPaths(results, [app], aclMock.Object);

        Assert.Empty(filtered);
    }

    [Fact]
    public void SidWithAllPathsFiltered_RemovedFromResults()
    {
        var app = new AppEntry { ExePath = @"C:\apps\myapp\myapp.exe", AclTarget = AclTarget.Folder, RestrictAcl = true };
        var aclMock = MockAcl((app, @"C:\apps\myapp"));
        var results = new Dictionary<string, AccountScanResult>(StringComparer.OrdinalIgnoreCase)
        {
            [Sid1] = new([MakeGrant(@"C:\apps\myapp\myapp.exe")], []),
            [Sid2] = new([MakeGrant(@"C:\other\file.txt")], [])
        };

        var filtered = CreateProcessor().FilterManagedPaths(results, [app], aclMock.Object);

        Assert.False(filtered.ContainsKey(Sid1));
        Assert.True(filtered.ContainsKey(Sid2));
    }

    [Fact]
    public void AppWithRestrictAclFalse_PathNotInManagedSet()
    {
        var app = new AppEntry { ExePath = @"C:\apps\foo.exe", AclTarget = AclTarget.File, RestrictAcl = false };
        var aclMock = new Mock<IAclService>();
        var grant = MakeGrant(@"C:\apps\foo.exe");
        var results = new Dictionary<string, AccountScanResult>(StringComparer.OrdinalIgnoreCase)
        {
            [Sid1] = new([grant], [])
        };

        var filtered = CreateProcessor().FilterManagedPaths(results, [app], aclMock.Object);

        Assert.Single(filtered[Sid1].Grants);
        aclMock.Verify(a => a.ResolveAclTargetPath(It.IsAny<AppEntry>()), Times.Never);
    }

    [Fact]
    public void AppWithIsUrlScheme_PathNotInManagedSet()
    {
        var app = new AppEntry { ExePath = "myapp://", IsUrlScheme = true, RestrictAcl = true };
        var aclMock = new Mock<IAclService>();
        var grant = MakeGrant(@"C:\apps\foo.exe");
        var results = new Dictionary<string, AccountScanResult>(StringComparer.OrdinalIgnoreCase)
        {
            [Sid1] = new([grant], [])
        };

        var filtered = CreateProcessor().FilterManagedPaths(results, [app], aclMock.Object);

        Assert.Single(filtered[Sid1].Grants);
        aclMock.Verify(a => a.ResolveAclTargetPath(It.IsAny<AppEntry>()), Times.Never);
    }

    [Fact]
    public void PathMatchingIsCaseInsensitive()
    {
        var app = new AppEntry { ExePath = @"C:\Apps\MyApp\MyApp.exe", AclTarget = AclTarget.Folder, RestrictAcl = true };
        var aclMock = MockAcl((app, @"C:\Apps\MyApp"));
        var grant = MakeGrant(@"C:\apps\myapp\myapp.exe");
        var results = new Dictionary<string, AccountScanResult>(StringComparer.OrdinalIgnoreCase)
        {
            [Sid1] = new([grant], [])
        };

        var filtered = CreateProcessor().FilterManagedPaths(results, [app], aclMock.Object);

        Assert.Empty(filtered);
    }

    [Fact]
    public void MixedManagedAndUnmanagedGrants_OnlyUnmanagedRetained()
    {
        var app = new AppEntry { ExePath = @"C:\apps\myapp\myapp.exe", AclTarget = AclTarget.Folder, RestrictAcl = true };
        var aclMock = MockAcl((app, @"C:\apps\myapp"));
        var results = new Dictionary<string, AccountScanResult>(StringComparer.OrdinalIgnoreCase)
        {
            [Sid1] = new(
                [MakeGrant(@"C:\apps\myapp\myapp.exe"), MakeGrant(@"C:\data\shared")],
                [@"C:\apps\myapp", @"C:\other\traverse"])
        };

        var filtered = CreateProcessor().FilterManagedPaths(results, [app], aclMock.Object);

        Assert.Single(filtered[Sid1].Grants);
        Assert.Equal(@"C:\data\shared", filtered[Sid1].Grants[0].Path);
        Assert.Single(filtered[Sid1].TraversePaths);
        Assert.Equal(@"C:\other\traverse", filtered[Sid1].TraversePaths[0]);
    }

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
