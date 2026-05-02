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

        var service = CreateService(Mock.Of<IFileSystemAclTraverser>());
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

    private static SidAclScanService CreateService(IFileSystemAclTraverser traverser)
        => new(Mock.Of<ILoggingService>(), Mock.Of<ISidResolver>(), traverser);

    private static FileSystemAccessRule CreateRule(string sid, FileSystemRights rights)
        => new(new SecurityIdentifier(sid), rights, AccessControlType.Allow);
}
