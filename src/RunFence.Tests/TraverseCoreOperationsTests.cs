using System.Security.Principal;
using Moq;
using RunFence.Acl;
using RunFence.Acl.Permissions;
using RunFence.Acl.Traverse;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public class TraverseCoreOperationsTests
{
    private const string UserSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
    private const string ContainerSid = "S-1-15-2-1234567890-1234567890-1234567890-1234567890";
    private const string TestPath = @"C:\Existing\TestDir";

    private static readonly IUiThreadInvoker SyncInvoker =
        new LambdaUiThreadInvoker(a => a(), a => a());

    [Fact]
    public void AddTraverse_WhenDbWriteFails_RollsBackAppliedAces()
    {
        var db = new AppDatabase();
        var traverseAcl = new Mock<ITraverseAcl>();
        traverseAcl.Setup(mock => mock.HasExplicitTraverseAce(It.IsAny<string>(), It.IsAny<SecurityIdentifier>()))
            .Returns(true);
        traverseAcl.Setup(mock => mock.HasExplicitTraverseAceOrThrow(It.IsAny<string>(), It.IsAny<SecurityIdentifier>()))
            .Returns(true);
        var aclPermission = new Mock<IAclPermissionService>();
        aclPermission.Setup(mock => mock.ResolveAccountGroupSids(UserSid)).Returns([]);
        aclPermission.Setup(mock => mock.HasEffectiveRights(
                It.IsAny<System.Security.AccessControl.FileSystemSecurity>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                TraverseRightsHelper.TraverseRights))
            .Returns(false);
        var pathInfo = new TestFileSystemPathInfo()
            .AddDirectory(TestPath)
            .AddDirectory(Path.GetPathRoot(TestPath)!);
        var log = new Mock<ILoggingService>();
        var ancestorGranter = new AncestorTraverseGranter(log.Object, aclPermission.Object, traverseAcl.Object, pathInfo);
        var accessor = new UiThreadDatabaseAccessor(new ThrowOnCallDatabaseProvider(db, failureCall: 1), () => SyncInvoker);
        var service = new TraverseCoreOperations(
            traverseAcl.Object,
            ancestorGranter,
            aclPermission.Object,
            accessor,
            log.Object,
            pathInfo,
            new TraverseGrantOwnerResolver());

        Assert.Throws<InvalidOperationException>(() => service.AddTraverse(UserSid, TestPath));

        traverseAcl.Verify(mock => mock.RemoveTraverseOnlyAce(
            TestPath,
            It.Is<SecurityIdentifier>(sid => sid.Value == UserSid)), Times.Once);
        traverseAcl.Verify(mock => mock.RemoveTraverseOnlyAce(
            Path.GetPathRoot(TestPath)!,
            It.Is<SecurityIdentifier>(sid => sid.Value == UserSid)), Times.Once);
        Assert.DoesNotContain(db.GetAccount(UserSid)?.Grants ?? [],
            entry => entry.IsTraverseOnly &&
                     string.Equals(entry.Path, TestPath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RemoveTraverse_WhenTraverseAclReadFails_ThrowsAndKeepsTrackedState()
    {
        var db = new AppDatabase();
        db.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
        {
            Path = TestPath,
            IsTraverseOnly = true,
            AllAppliedPaths = [TestPath, Path.GetPathRoot(TestPath)!]
        });

        var traverseAcl = new Mock<ITraverseAcl>();
        traverseAcl.Setup(mock => mock.HasExplicitTraverseAce(It.IsAny<string>(), It.IsAny<SecurityIdentifier>()))
            .Returns(true);
        traverseAcl.SetupSequence(mock => mock.HasExplicitTraverseAceOrThrow(It.IsAny<string>(), It.IsAny<SecurityIdentifier>()))
            .Returns(true)
            .Throws(new UnauthorizedAccessException("remove read failed"));
        var aclPermission = new Mock<IAclPermissionService>();
        aclPermission.Setup(mock => mock.ResolveAccountGroupSids(UserSid)).Returns([]);
        var pathInfo = new TestFileSystemPathInfo()
            .AddDirectory(TestPath)
            .AddDirectory(Path.GetPathRoot(TestPath)!);
        var log = new Mock<ILoggingService>();
        var ancestorGranter = new AncestorTraverseGranter(log.Object, aclPermission.Object, traverseAcl.Object, pathInfo);
        var accessor = new UiThreadDatabaseAccessor(new LambdaDatabaseProvider(() => db), () => SyncInvoker);
        var service = new TraverseCoreOperations(
            traverseAcl.Object,
            ancestorGranter,
            aclPermission.Object,
            accessor,
            log.Object,
            pathInfo,
            new TraverseGrantOwnerResolver());

        var ex = Assert.Throws<UnauthorizedAccessException>(() => service.RemoveTraverse(UserSid, TestPath, updateFileSystem: true));

        Assert.Equal("remove read failed", ex.Message);
        Assert.Equal([TestPath, Path.GetPathRoot(TestPath)!], Assert.Single(db.GetAccount(UserSid)!.Grants).AllAppliedPaths);
        traverseAcl.Verify(mock => mock.RemoveTraverseOnlyAce(
            It.IsAny<string>(),
            It.IsAny<SecurityIdentifier>()), Times.Once);
    }

    [Fact]
    public void TrackTraverse_SpecificContainerWithManualSharedEntry_CreatesSourceTrackedEntry()
    {
        var db = new AppDatabase();
        db.GetOrCreateAccount(AclHelper.AllApplicationPackagesSid).Grants.Add(new GrantedPathEntry
        {
            Path = TestPath,
            IsTraverseOnly = true,
            SourceSids = null
        });
        var service = CreateService(db);

        var modified = service.TrackTraverse(ContainerSid, new GrantedPathEntry
        {
            Path = TestPath,
            IsTraverseOnly = true,
            SourceSids = [ContainerSid],
            AllAppliedPaths = [TestPath]
        });

        Assert.True(modified);
        var entries = db.GetAccount(AclHelper.AllApplicationPackagesSid)!.Grants
            .Where(entry => entry.IsTraverseOnly)
            .ToList();
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, entry => entry.SourceSids == null);
        var managedEntry = Assert.Single(entries, entry => entry.SourceSids != null);
        Assert.Equal([ContainerSid], managedEntry.SourceSids);
        Assert.Equal([TestPath], managedEntry.AllAppliedPaths);
    }

    [Fact]
    public void TrackTraverse_SpecificContainerManualEntry_PreservesManualSharedEntry()
    {
        var db = new AppDatabase();
        db.GetOrCreateAccount(AclHelper.AllApplicationPackagesSid).Grants.Add(new GrantedPathEntry
        {
            Path = TestPath,
            IsTraverseOnly = true,
            SourceSids = null
        });
        var service = CreateService(db);

        var modified = service.TrackTraverse(ContainerSid, new GrantedPathEntry
        {
            Path = TestPath,
            IsTraverseOnly = true,
            SourceSids = null,
            AllAppliedPaths = [TestPath]
        });

        Assert.True(modified);
        var entry = Assert.Single(db.GetAccount(AclHelper.AllApplicationPackagesSid)!.Grants);
        Assert.Null(entry.SourceSids);
        Assert.Equal([TestPath], entry.AllAppliedPaths);
    }


    [Fact]
    public void RemoveTraverse_SpecificContainerWithManualAndManagedSharedEntries_RemovesOnlySourceTrackedEntry()
    {
        var db = new AppDatabase();
        var sharedStore = db.GetOrCreateAccount(AclHelper.AllApplicationPackagesSid).Grants;
        sharedStore.Add(new GrantedPathEntry
        {
            Path = TestPath,
            IsTraverseOnly = true,
            SourceSids = null
        });
        sharedStore.Add(new GrantedPathEntry
        {
            Path = TestPath,
            IsTraverseOnly = true,
            SourceSids = [ContainerSid],
            AllAppliedPaths = [TestPath]
        });
        var service = CreateService(db);

        var removed = service.RemoveTraverse(ContainerSid, TestPath, updateFileSystem: false);

        Assert.True(removed);
        var remaining = Assert.Single(db.GetAccount(AclHelper.AllApplicationPackagesSid)!.Grants);
        Assert.Null(remaining.SourceSids);
        Assert.Equal(TestPath, remaining.Path);
    }

    private sealed class ThrowOnCallDatabaseProvider(AppDatabase db, int failureCall) : IDatabaseProvider
    {
        private int _callCount;

        public AppDatabase GetDatabase()
        {
            _callCount++;
            if (_callCount == failureCall)
                throw new InvalidOperationException("simulated database access failure");

            return db;
        }
    }

    private static TraverseCoreOperations CreateService(AppDatabase db)
    {
        var traverseAcl = new Mock<ITraverseAcl>();
        var aclPermission = new Mock<IAclPermissionService>();
        var pathInfo = new TestFileSystemPathInfo()
            .AddDirectory(TestPath)
            .AddDirectory(Path.GetPathRoot(TestPath)!);
        var log = new Mock<ILoggingService>();
        var ancestorGranter = new AncestorTraverseGranter(log.Object, aclPermission.Object, traverseAcl.Object, pathInfo);
        var accessor = new UiThreadDatabaseAccessor(new LambdaDatabaseProvider(() => db), () => SyncInvoker);
        return new TraverseCoreOperations(
            traverseAcl.Object,
            ancestorGranter,
            aclPermission.Object,
            accessor,
            log.Object,
            pathInfo,
            new TraverseGrantOwnerResolver());
    }
}
