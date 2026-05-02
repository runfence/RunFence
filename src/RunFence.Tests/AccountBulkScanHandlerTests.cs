using Moq;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Acl;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Tests for <see cref="AccountBulkScanHandler.FilterManagedPaths"/> and
/// <see cref="AccountBulkScanHandler.ApplyScanResults"/>.
/// </summary>
public class AccountBulkScanHandlerTests
{
    private const string Sid1 = "S-1-5-21-1234567890-1234567890-1234567890-1001";
    private const string Sid2 = "S-1-5-21-1234567890-1234567890-1234567890-1002";

    private static AccountBulkScanHandler CreateHandler(IDatabaseProvider? databaseProvider = null) => new(
        new Mock<IAccountAclBulkScanService>().Object,
        new Mock<IAclService>().Object,
        new Mock<ILoggingService>().Object,
        new Mock<ISidNameCacheService>().Object,
        databaseProvider ?? new Mock<IDatabaseProvider>().Object);

    private static AccountBulkScanHandler CreateHandlerWithDatabase(AppDatabase database) =>
        CreateHandler(new LambdaDatabaseProvider(() => database));

    private static DiscoveredGrant MakeGrant(string path, bool isDeny = false) =>
        new(Path: path, IsDeny: isDeny, Execute: false, Write: false, Read: true, Special: false, IsOwner: false);

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

        var filtered = CreateHandler().FilterManagedPaths(results, [], new Mock<IAclService>().Object);

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

        var filtered = CreateHandler().FilterManagedPaths(results, [app], aclMock.Object);

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

        var filtered = CreateHandler().FilterManagedPaths(results, [app], aclMock.Object);

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

        var filtered = CreateHandler().FilterManagedPaths(results, [app], aclMock.Object);

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

        var filtered = CreateHandler().FilterManagedPaths(results, [app], aclMock.Object);

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

        var filtered = CreateHandler().FilterManagedPaths(results, [app], aclMock.Object);

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

        var filtered = CreateHandler().FilterManagedPaths(results, [app], aclMock.Object);

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

        var filtered = CreateHandler().FilterManagedPaths(results, [app], aclMock.Object);

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

        var filtered = CreateHandler().FilterManagedPaths(results, [app], aclMock.Object);

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

        var filtered = CreateHandler().FilterManagedPaths(results, [app], aclMock.Object);

        Assert.Single(filtered[Sid1].Grants);
        Assert.Equal(@"C:\data\shared", filtered[Sid1].Grants[0].Path);
        Assert.Single(filtered[Sid1].TraversePaths);
        Assert.Equal(@"C:\other\traverse", filtered[Sid1].TraversePaths[0]);
    }

    // ── ApplyScanResults ─────────────────────────────────────────────────────

    private static DiscoveredGrant MakeAllow(string path, bool execute = false, bool write = false,
        bool read = true, bool special = false, bool isOwner = false)
        => new(path, IsDeny: false, Execute: execute, Write: write, Read: read, Special: special, IsOwner: isOwner);

    private static DiscoveredGrant MakeDeny(string path, bool execute = false, bool read = false)
        => new(path, IsDeny: true, Execute: execute, Write: false, Read: read, Special: false, IsOwner: false);

    [Fact]
    public void ApplyScanResults_AddsGrantToDatabase()
    {
        // Arrange
        var database = new AppDatabase();
        var selected = new Dictionary<string, AccountScanResult>
        {
            [Sid1] = new([MakeAllow(@"C:\data\shared")], [])
        };
        bool saveInvoked = false;

        // Act
        CreateHandlerWithDatabase(database).ApplyScanResults(selected, () => saveInvoked = true);

        // Assert
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
        // Arrange
        var database = new AppDatabase();
        var selected = new Dictionary<string, AccountScanResult>
        {
            [Sid1] = new([MakeDeny(@"C:\restricted")], [])
        };

        // Act
        CreateHandlerWithDatabase(database).ApplyScanResults(selected, () => { });

        // Assert
        var grants = database.GetAccount(Sid1)?.Grants;
        Assert.NotNull(grants);
        Assert.Single(grants);
        Assert.True(grants[0].IsDeny);
        Assert.False(grants[0].IsTraverseOnly);
    }

    [Fact]
    public void ApplyScanResults_DeduplicatesGrant_WhenSamePathAndSameIsDeny()
    {
        // Arrange: an Allow grant for the same path already exists — the scan must NOT add a duplicate.
        var database = new AppDatabase();
        var normalizedPath = AclHelper.NormalizePath(@"C:\data\shared");
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

        // Act
        CreateHandlerWithDatabase(database).ApplyScanResults(selected, () => { });

        // Assert: still only one grant — no duplicate
        Assert.Single(database.GetAccount(Sid1)!.Grants);
    }

    [Fact]
    public void ApplyScanResults_AllowAndDenyForSamePath_BothAdded_DifferentIsDeny()
    {
        // Arrange: existing Allow grant; scanning discovers a Deny for the same path.
        // These are distinct entries (different IsDeny) so both should be present.
        var database = new AppDatabase();
        var normalizedPath = AclHelper.NormalizePath(@"C:\data\shared");
        database.GetOrCreateAccount(Sid1).Grants.Add(new GrantedPathEntry
        {
            Path = normalizedPath,
            IsDeny = false,
            IsTraverseOnly = false
        });

        var selected = new Dictionary<string, AccountScanResult>
        {
            [Sid1] = new([MakeDeny(@"C:\data\shared")], [])
        };

        // Act
        CreateHandlerWithDatabase(database).ApplyScanResults(selected, () => { });

        // Assert: two grants (one allow, one deny)
        Assert.Equal(2, database.GetAccount(Sid1)!.Grants.Count);
    }

    [Fact]
    public void ApplyScanResults_AddsTraversePathEntry()
    {
        // Arrange
        var database = new AppDatabase();
        var selected = new Dictionary<string, AccountScanResult>
        {
            [Sid1] = new([], [@"C:\parent\traverse"])
        };

        // Act
        CreateHandlerWithDatabase(database).ApplyScanResults(selected, () => { });

        // Assert: traverse-only entry added
        var grants = database.GetAccount(Sid1)?.Grants;
        Assert.NotNull(grants);
        Assert.Single(grants);
        Assert.True(grants[0].IsTraverseOnly);
        Assert.Equal(AclHelper.NormalizePath(@"C:\parent\traverse"), grants[0].Path);
    }

    [Fact]
    public void ApplyScanResults_DeduplicatesTraversePath()
    {
        // Arrange: traverse path already exists in database
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

        // Act
        CreateHandlerWithDatabase(database).ApplyScanResults(selected, () => { });

        // Assert: still only one entry
        Assert.Single(database.GetAccount(Sid1)!.Grants);
    }

    [Fact]
    public void ApplyScanResults_AllowGrant_RightsMapFromDiscoveredGrant()
    {
        // Arrange: discovered grant with Execute=true, Write=true, IsOwner=true
        var database = new AppDatabase();
        var grant = MakeAllow(@"C:\tools\util.exe", execute: true, write: true, read: true, isOwner: true);
        var selected = new Dictionary<string, AccountScanResult>
        {
            [Sid1] = new([grant], [])
        };

        // Act
        CreateHandlerWithDatabase(database).ApplyScanResults(selected, () => { });

        // Assert: SavedRights reflects discovered rights; Own=true because IsOwner=true
        var savedRights = database.GetAccount(Sid1)!.Grants[0].SavedRights;
        Assert.NotNull(savedRights);
        Assert.True(savedRights.Execute);
        Assert.True(savedRights.Write);
        Assert.True(savedRights.Own);
    }

    [Fact]
    public void ApplyScanResults_DenyGrant_RightsMapFromDiscoveredGrant()
    {
        // Arrange: discovered deny grant with Execute=true, Read=true
        var database = new AppDatabase();
        var grant = MakeDeny(@"C:\restricted", execute: true, read: true);
        var selected = new Dictionary<string, AccountScanResult>
        {
            [Sid1] = new([grant], [])
        };

        // Act
        CreateHandlerWithDatabase(database).ApplyScanResults(selected, () => { });

        // Assert: SavedRights has Execute and Read from scan; Own=false for deny by convention
        var savedRights = database.GetAccount(Sid1)!.Grants[0].SavedRights;
        Assert.NotNull(savedRights);
        Assert.True(savedRights.Execute);
        Assert.True(savedRights.Read);
        Assert.False(savedRights.Own);
    }

    [Fact]
    public void ApplyScanResults_MultipleSids_EachGetsSeparateGrants()
    {
        // Arrange
        var database = new AppDatabase();
        var selected = new Dictionary<string, AccountScanResult>
        {
            [Sid1] = new([MakeAllow(@"C:\data\path1")], []),
            [Sid2] = new([MakeAllow(@"C:\data\path2")], [])
        };

        // Act
        CreateHandlerWithDatabase(database).ApplyScanResults(selected, () => { });

        // Assert: each SID has its own grant
        Assert.Single(database.GetAccount(Sid1)!.Grants);
        Assert.Equal(AclHelper.NormalizePath(@"C:\data\path1"), database.GetAccount(Sid1)!.Grants[0].Path);
        Assert.Single(database.GetAccount(Sid2)!.Grants);
        Assert.Equal(AclHelper.NormalizePath(@"C:\data\path2"), database.GetAccount(Sid2)!.Grants[0].Path);
    }

    [Fact]
    public void ApplyScanResults_NormalizesPathCaseInsensitive()
    {
        // Arrange: path already in database with different casing — must be treated as duplicate
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

        // Act
        CreateHandlerWithDatabase(database).ApplyScanResults(selected, () => { });

        // Assert: case-insensitive dedup — still only one entry
        Assert.Single(database.GetAccount(Sid1)!.Grants);
    }
}
