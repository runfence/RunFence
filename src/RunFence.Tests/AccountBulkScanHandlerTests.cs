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
/// Tests for <see cref="AccountBulkScanHandler.FilterManagedPaths"/>.
/// </summary>
public class AccountBulkScanHandlerTests
{
    private const string Sid1 = "S-1-5-21-1234567890-1234567890-1234567890-1001";
    private const string Sid2 = "S-1-5-21-1234567890-1234567890-1234567890-1002";

    private static AccountBulkScanHandler CreateHandler() => new(
        new Mock<IAccountAclBulkScanService>().Object,
        new Mock<IAclService>().Object,
        new Mock<ILoggingService>().Object,
        new Mock<ISidNameCacheService>().Object,
        new Mock<IDatabaseProvider>().Object);

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
}
