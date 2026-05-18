using System.Security.AccessControl;
using System.Security.Principal;
using Moq;
using RunFence.Acl;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.SidMigration;
using Xunit;

namespace RunFence.Tests;

public class SidAclScanServiceTests
{
    private const string OldSidToMigrate = "S-1-5-21-1000000001-1000000002-1000000003-1001";
    private const string OldSidToDelete = "S-1-5-21-1000000001-1000000002-1000000003-1002";
    private const string NewSid = "S-1-5-21-2000000001-2000000002-2000000003-1001";

    [Fact]
    public async Task ScanAsync_MixedMigrateAndDelete_IncludesDeleteSidAceMatches()
    {
        var security = new DirectorySecurity();
        security.AddAccessRule(CreateRule(OldSidToMigrate, FileSystemRights.Read));
        security.AddAccessRule(CreateRule(OldSidToDelete, FileSystemRights.Write));

        var traverser = new Mock<IFileSystemAclTraverser>();
        traverser
            .Setup(t => t.Traverse(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<IProgress<long>>(),
                It.IsAny<CancellationToken>()))
            .Returns([new AclTraversalEntry(@"C:\scan-root", true, security)]);
        var service = CreateService(traverser.Object);

        var matches = await service.ScanAsync(
            [@"C:\scan-root"],
            [new SidMigrationMapping(OldSidToMigrate, NewSid, "User")],
            [OldSidToDelete],
            new Progress<(long scanned, long found)>(),
            CancellationToken.None);

        var match = Assert.Single(matches);
        Assert.Equal(1, match.AceCountByOldSid[OldSidToMigrate]);
        Assert.Equal(1, match.AceCountByOldSid[OldSidToDelete]);
    }

    [Fact]
    public async Task ScanAsync_DeleteSidOwnerMatch_PreservesOwnerOldSid()
    {
        var security = new DirectorySecurity();
        security.SetOwner(new SecurityIdentifier(OldSidToDelete));

        var traverser = new Mock<IFileSystemAclTraverser>();
        traverser
            .Setup(t => t.Traverse(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<IProgress<long>>(),
                It.IsAny<CancellationToken>()))
            .Returns([new AclTraversalEntry(@"C:\scan-root", true, security)]);
        var service = CreateService(traverser.Object);

        var matches = await service.ScanAsync(
            [@"C:\scan-root"],
            [],
            [OldSidToDelete],
            new Progress<(long scanned, long found)>(),
            CancellationToken.None);

        var match = Assert.Single(matches);
        Assert.True(match.MatchType.HasFlag(SidMigrationMatchType.Owner));
        Assert.Equal(OldSidToDelete, match.OwnerOldSid);
    }

    [Fact]
    public async Task DiscoverOrphanedSidsAsync_ResolveReturnsNull_MarksConfirmedOrphaned()
    {
        var security = new DirectorySecurity();
        security.AddAccessRule(CreateRule(OldSidToDelete, FileSystemRights.Read));
        var traverser = new Mock<IFileSystemAclTraverser>();
        traverser
            .Setup(t => t.Traverse(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<IProgress<long>>(),
                It.IsAny<CancellationToken>()))
            .Returns([new AclTraversalEntry(@"C:\scan-root", true, security)]);
        var sidResolver = new Mock<ISidResolver>();
        sidResolver.Setup(r => r.TryResolveName(OldSidToDelete)).Returns((string?)null);
        var service = new SidAclScanService(Mock.Of<ILoggingService>(), sidResolver.Object, traverser.Object, Mock.Of<IAclAccessor>());

        var results = await service.DiscoverOrphanedSidsAsync(
            [@"C:\scan-root"],
            new Progress<(long scanned, long sidsFound)>(),
            CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(OrphanedSidClassification.ConfirmedOrphaned, result.Classification);
    }

    [Fact]
    public async Task DiscoverOrphanedSidsAsync_ResolveThrows_MarksUnresolved()
    {
        var security = new DirectorySecurity();
        security.AddAccessRule(CreateRule(OldSidToDelete, FileSystemRights.Read));
        var traverser = new Mock<IFileSystemAclTraverser>();
        traverser
            .Setup(t => t.Traverse(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<IProgress<long>>(),
                It.IsAny<CancellationToken>()))
            .Returns([new AclTraversalEntry(@"C:\scan-root", true, security)]);
        var sidResolver = new Mock<ISidResolver>();
        sidResolver.Setup(r => r.TryResolveName(OldSidToDelete)).Throws(new TimeoutException("dc timeout"));
        var service = new SidAclScanService(Mock.Of<ILoggingService>(), sidResolver.Object, traverser.Object, Mock.Of<IAclAccessor>());

        var results = await service.DiscoverOrphanedSidsAsync(
            [@"C:\scan-root"],
            new Progress<(long scanned, long sidsFound)>(),
            CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(OrphanedSidClassification.Unresolved, result.Classification);
    }

    [Fact]
    public async Task ApplyAsync_MixedMigrateAndDelete_ReplacesMigratedAcesAndRemovesDeletedAces()
    {
        using var tempDir = new TempDirectory("RunFence_SidAclScan");
        var filePath = Path.Combine(tempDir.Path, "target.txt");
        await File.WriteAllTextAsync(filePath, "");

        var fileInfo = new FileInfo(filePath);
        var security = fileInfo.GetAccessControl(AccessControlSections.Access);
        security.AddAccessRule(CreateRule(OldSidToMigrate, FileSystemRights.Read));
        security.AddAccessRule(CreateRule(OldSidToDelete, FileSystemRights.Write));
        fileInfo.SetAccessControl(security);

        var service = CreateService(Mock.Of<IFileSystemAclTraverser>(), AclAccessorFactory.Create());
        var hit = new SidMigrationMatch
        {
            Path = filePath,
            IsDirectory = false,
            MatchType = SidMigrationMatchType.Ace,
            AceCountByOldSid = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                [OldSidToMigrate] = 1,
                [OldSidToDelete] = 1
            }
        };

        await service.ApplyAsync(
            [hit],
            [new SidMigrationMapping(OldSidToMigrate, NewSid, "User")],
            [OldSidToDelete],
            new Progress<MigrationProgress>(),
            CancellationToken.None);

        var resultingSids = fileInfo
            .GetAccessControl(AccessControlSections.Access)
            .GetAccessRules(true, false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Select(rule => ((SecurityIdentifier)rule.IdentityReference).Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains(NewSid, resultingSids);
        Assert.DoesNotContain(OldSidToMigrate, resultingSids);
        Assert.DoesNotContain(OldSidToDelete, resultingSids);
    }

    [Fact]
    public async Task ApplyAsync_UsesAclAccessorOwnerAndAclFallbackMutation()
    {
        var aclAccessor = new Mock<IAclAccessor>();
        var security = new DirectorySecurity();
        security.AddAccessRule(CreateRule(OldSidToMigrate, FileSystemRights.Read));
        security.SetOwner(new SecurityIdentifier(OldSidToMigrate));

        aclAccessor
            .Setup(a => a.ModifyOwnerAndAclWithFallback(
                @"C:\scan-root",
                true,
                It.IsAny<Func<FileSystemSecurity, bool>>()))
            .Returns<string, bool, Func<FileSystemSecurity, bool>>((_, _, modify) => modify(security));

        var service = CreateService(Mock.Of<IFileSystemAclTraverser>(), aclAccessor.Object);
        var hit = new SidMigrationMatch
        {
            Path = @"C:\scan-root",
            IsDirectory = true,
            MatchType = SidMigrationMatchType.Ace | SidMigrationMatchType.Owner,
            AceCountByOldSid = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                [OldSidToMigrate] = 1
            },
            OwnerOldSid = OldSidToMigrate
        };

        var result = await service.ApplyAsync(
            [hit],
            [new SidMigrationMapping(OldSidToMigrate, NewSid, "User")],
            [],
            new Progress<MigrationProgress>(),
            CancellationToken.None);

        Assert.Equal(1, result.applied);
        Assert.Equal(0, result.errors);
        aclAccessor.Verify(a => a.ModifyOwnerAndAclWithFallback(
            @"C:\scan-root",
            true,
            It.IsAny<Func<FileSystemSecurity, bool>>()), Times.Once);

        var resultingSids = security
            .GetAccessRules(true, false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Select(rule => ((SecurityIdentifier)rule.IdentityReference).Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains(NewSid, resultingSids);
        Assert.DoesNotContain(OldSidToMigrate, resultingSids);
        var ownerSid = Assert.IsType<SecurityIdentifier>(security.GetOwner(typeof(SecurityIdentifier)));
        Assert.Equal(NewSid, ownerSid.Value);
    }

    private static SidAclScanService CreateService(IFileSystemAclTraverser traverser, IAclAccessor? aclAccessor = null)
        => new(Mock.Of<ILoggingService>(), Mock.Of<ISidResolver>(), traverser, aclAccessor ?? AclAccessorFactory.Create());

    private static FileSystemAccessRule CreateRule(string sid, FileSystemRights rights)
        => new(new SecurityIdentifier(sid), rights, AccessControlType.Allow);

    [Fact]
    public async Task ScanAsync_InaccessibleRoot_ThrowsAndDoesNotReportSuccess()
    {
        var root = Path.GetPathRoot(Environment.SystemDirectory)!;
        var traverser = new Mock<IFileSystemAclTraverser>();
        traverser.Setup(t => t.Traverse(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<IProgress<long>>(),
                It.IsAny<CancellationToken>()))
            .Returns([]);

        var aclAccessor = new Mock<IAclAccessor>();
        bool isFolder = true;
        aclAccessor.Setup(a => a.PathExists(root, out isFolder)).Returns(true);
        aclAccessor.Setup(a => a.GetSecurity(root)).Throws(new UnauthorizedAccessException("denied"));
        var service = new SidAclScanService(
            Mock.Of<ILoggingService>(),
            Mock.Of<ISidResolver>(),
            traverser.Object,
            aclAccessor.Object);

        await Assert.ThrowsAsync<IOException>(() => service.ScanAsync(
            [root],
            [],
            [],
            new Progress<(long scanned, long found)>(),
            CancellationToken.None));
    }
}

