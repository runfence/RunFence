using System.Security.AccessControl;
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

public class ContainerInteractiveUserSyncTests
{
    private const string ContainerSid = "S-1-15-2-99-1-2-3-4-5-6";
    private const string InteractiveSid = "S-1-5-21-1234567890-1234567890-1234567890-1002";
    private const string TestPath = @"C:\Existing\TestDir";

    private static readonly IUiThreadInvoker SyncInvoker =
        new LambdaUiThreadInvoker(a => a(), a => a());

    [Fact]
    public void SyncTraverseToInteractiveUser_WhenTrackingWriteFails_RollsBackCreatedTraverse()
    {
        var db = new AppDatabase();
        var pathInfo = new TestFileSystemPathInfo().AddDirectory(TestPath);
        var traverseCore = new Mock<ITraverseCoreOperations>();
        traverseCore.Setup(mock => mock.AddTraverse(InteractiveSid, TestPath))
            .Callback(() => db.GetOrCreateAccount(InteractiveSid).Grants.Add(new GrantedPathEntry
            {
                Path = TestPath,
                IsTraverseOnly = true,
                AllAppliedPaths = [TestPath]
            }))
            .Returns((true, [TestPath]));
        traverseCore.Setup(mock => mock.RemoveTraverse(InteractiveSid, TestPath, true))
            .Callback(() => db.GetAccount(InteractiveSid)?.Grants.RemoveAll(entry =>
                entry.IsTraverseOnly &&
                string.Equals(entry.Path, TestPath, StringComparison.OrdinalIgnoreCase)))
            .Returns(true);
        var sync = BuildSync(db, pathInfo, new ThrowOnCallDatabaseProvider(db, failureCall: 2), traverseCore.Object);

        Assert.Throws<InvalidOperationException>(() => sync.SyncTraverseToInteractiveUser(ContainerSid, TestPath));

        Assert.DoesNotContain(db.GetAccount(InteractiveSid)?.Grants ?? [],
            entry => entry.IsTraverseOnly &&
                     string.Equals(entry.Path, TestPath, StringComparison.OrdinalIgnoreCase));
        traverseCore.Verify(mock => mock.RemoveTraverse(InteractiveSid, TestPath, true), Times.Once);
    }

    [Fact]
    public void SyncTraverseToInteractiveUser_WhenTrackingWriteFailsAfterExistingChange_RestoresSnapshot()
    {
        var db = new AppDatabase();
        db.GetOrCreateAccount(InteractiveSid).Grants.Add(new GrantedPathEntry
        {
            Path = TestPath,
            IsTraverseOnly = true,
            AllAppliedPaths = [TestPath],
            SourceSids = [ContainerSid]
        });
        var pathInfo = new TestFileSystemPathInfo().AddDirectory(TestPath).AddDirectory(Path.GetPathRoot(TestPath)!);
        var traverseCore = new Mock<ITraverseCoreOperations>();
        traverseCore.Setup(mock => mock.AddTraverse(InteractiveSid, TestPath))
            .Callback(() =>
            {
                var entry = db.GetAccount(InteractiveSid)!.Grants.Single(grant =>
                    grant.IsTraverseOnly &&
                    string.Equals(grant.Path, TestPath, StringComparison.OrdinalIgnoreCase));
                entry.AllAppliedPaths = [TestPath, Path.GetPathRoot(TestPath)!];
            })
            .Returns((true, [TestPath, Path.GetPathRoot(TestPath)!]));
        traverseCore.Setup(mock => mock.RemoveTraverse(InteractiveSid, TestPath, true))
            .Callback(() => db.GetAccount(InteractiveSid)?.Grants.RemoveAll(entry =>
                entry.IsTraverseOnly &&
                string.Equals(entry.Path, TestPath, StringComparison.OrdinalIgnoreCase)))
            .Returns(true);
        traverseCore.Setup(mock => mock.TrackTraverse(InteractiveSid, It.IsAny<GrantedPathEntry>()))
            .Callback<string, GrantedPathEntry>((_, entry) => db.GetOrCreateAccount(InteractiveSid).Grants.Add(entry.Clone()))
            .Returns(true);
        traverseCore.Setup(mock => mock.ApplyTraverseAces(InteractiveSid, It.IsAny<IReadOnlyList<string>>()))
            .Returns<string, IReadOnlyList<string>>((_, paths) => paths.ToList());
        var sync = BuildSync(db, pathInfo, new ThrowOnCallDatabaseProvider(db, failureCall: 2), traverseCore.Object);

        Assert.Throws<InvalidOperationException>(() => sync.SyncTraverseToInteractiveUser(ContainerSid, TestPath));

        var restored = Assert.Single(db.GetAccount(InteractiveSid)!.Grants);
        Assert.Equal([TestPath], restored.AllAppliedPaths);
        Assert.Equal([ContainerSid], restored.SourceSids);
        traverseCore.Verify(mock => mock.TrackTraverse(InteractiveSid, It.IsAny<GrantedPathEntry>()), Times.Once);
        traverseCore.Verify(mock => mock.ApplyTraverseAces(InteractiveSid, It.Is<IReadOnlyList<string>>(paths =>
            paths.Count == 1 && string.Equals(paths[0], TestPath, StringComparison.OrdinalIgnoreCase))), Times.Once);
    }

    [Fact]
    public void SyncAllowGrantToInteractiveUser_WhenTrackingWriteFails_RollsBackCreatedGrant()
    {
        var db = new AppDatabase();
        var grantCore = new Mock<IGrantCoreOperations>();
        grantCore.Setup(mock => mock.AddGrant(InteractiveSid, TestPath, false, It.IsAny<SavedRightsState?>(), null, null))
            .Callback<string, string, bool, SavedRightsState?, string?, bool?>((sid, path, _, rights, _, _) =>
                db.GetOrCreateAccount(sid).Grants.Add(new GrantedPathEntry
                {
                    Path = Path.GetFullPath(path),
                    IsDeny = false,
                    SavedRights = rights
                }))
            .Returns(new GrantAddResult(AlreadyExisted: false, DatabaseModified: true));
        grantCore.Setup(mock => mock.RemoveGrant(InteractiveSid, TestPath, false, true))
            .Callback(() => db.GetAccount(InteractiveSid)?.Grants.RemoveAll(entry =>
                !entry.IsTraverseOnly &&
                !entry.IsDeny &&
                string.Equals(entry.Path, TestPath, StringComparison.OrdinalIgnoreCase)))
            .Returns(new GrantRemoveResult(Found: true, SavedRights: null));
        var traverseCore = new Mock<ITraverseCoreOperations>();
        var aclPermission = new Mock<IAclPermissionService>();
        aclPermission.Setup(mock => mock.NeedsPermissionGrant(TestPath, InteractiveSid, It.IsAny<FileSystemRights>()))
            .Returns(true);
        var sync = BuildSync(
            db,
            new TestFileSystemPathInfo().AddDirectory(TestPath),
            new ThrowOnCallDatabaseProvider(db, failureCall: 3),
            traverseCore.Object,
            grantCore.Object,
            aclPermission.Object);

        Assert.Throws<InvalidOperationException>(() =>
            sync.SyncAllowGrantToInteractiveUser(ContainerSid, TestPath, new SavedRightsState(true, false, true, false, false)));

        Assert.DoesNotContain(db.GetAccount(InteractiveSid)?.Grants ?? [],
            entry => !entry.IsTraverseOnly &&
                     !entry.IsDeny &&
                     string.Equals(entry.Path, TestPath, StringComparison.OrdinalIgnoreCase));
        grantCore.Verify(mock => mock.RemoveGrant(InteractiveSid, TestPath, false, true), Times.Once);
        traverseCore.Verify(mock => mock.CleanupOrphanedTraverse(InteractiveSid, TestPath), Times.Once);
    }

    [Fact]
    public void SyncAllowGrantToInteractiveUser_WhenTrackingWriteFailsAfterExistingUpdate_RestoresSnapshot()
    {
        var db = new AppDatabase();
        var originalRights = new SavedRightsState(false, false, true, false, false);
        db.GetOrCreateAccount(InteractiveSid).Grants.Add(new GrantedPathEntry
        {
            Path = TestPath,
            IsDeny = false,
            SavedRights = originalRights,
            SourceSids = [ContainerSid]
        });
        var grantCore = new Mock<IGrantCoreOperations>();
        grantCore.Setup(mock => mock.AddGrant(InteractiveSid, TestPath, false, It.IsAny<SavedRightsState?>(), null, null))
            .Callback<string, string, bool, SavedRightsState?, string?, bool?>((sid, path, _, rights, _, _) =>
            {
                var normalized = Path.GetFullPath(path);
                var grants = db.GetOrCreateAccount(sid).Grants;
                var entry = grants.SingleOrDefault(grant =>
                    !grant.IsTraverseOnly &&
                    !grant.IsDeny &&
                    string.Equals(grant.Path, normalized, StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                {
                    grants.Add(new GrantedPathEntry
                    {
                        Path = normalized,
                        IsDeny = false,
                        SavedRights = rights
                    });
                    return;
                }

                entry.SavedRights = rights;
                entry.SourceSids = null;
            })
            .Returns(new GrantAddResult(AlreadyExisted: true, DatabaseModified: true));
        grantCore.Setup(mock => mock.RemoveGrant(InteractiveSid, TestPath, false, true))
            .Callback(() => db.GetAccount(InteractiveSid)?.Grants.RemoveAll(entry =>
                !entry.IsTraverseOnly &&
                !entry.IsDeny &&
                string.Equals(entry.Path, TestPath, StringComparison.OrdinalIgnoreCase)))
            .Returns(new GrantRemoveResult(Found: true, SavedRights: originalRights));
        var traverseCore = new Mock<ITraverseCoreOperations>();
        var aclPermission = new Mock<IAclPermissionService>();
        aclPermission.Setup(mock => mock.NeedsPermissionGrant(TestPath, InteractiveSid, It.IsAny<FileSystemRights>()))
            .Returns(true);
        var sync = BuildSync(
            db,
            new TestFileSystemPathInfo().AddDirectory(TestPath),
            new ThrowOnCallDatabaseProvider(db, failureCall: 3),
            traverseCore.Object,
            grantCore.Object,
            aclPermission.Object);

        Assert.Throws<InvalidOperationException>(() =>
            sync.SyncAllowGrantToInteractiveUser(ContainerSid, TestPath, new SavedRightsState(true, false, true, false, false)));

        var restored = Assert.Single(db.GetAccount(InteractiveSid)!.Grants);
        Assert.Equal(originalRights, restored.SavedRights);
        Assert.Equal([ContainerSid], restored.SourceSids);
        grantCore.Verify(mock => mock.AddGrant(InteractiveSid, TestPath, false, originalRights, null, null), Times.Once);
    }

    [Fact]
    public void SyncAllowGrantToInteractiveUser_WhenTraverseAddFails_RollsBackGrantAndTraverse()
    {
        var db = new AppDatabase();
        var grantCore = new Mock<IGrantCoreOperations>();
        grantCore.Setup(mock => mock.AddGrant(InteractiveSid, TestPath, false, It.IsAny<SavedRightsState?>(), null, null))
            .Callback<string, string, bool, SavedRightsState?, string?, bool?>((sid, path, _, rights, _, _) =>
                db.GetOrCreateAccount(sid).Grants.Add(new GrantedPathEntry
                {
                    Path = Path.GetFullPath(path),
                    IsDeny = false,
                    SavedRights = rights
                }))
            .Returns(new GrantAddResult(AlreadyExisted: false, DatabaseModified: true));
        grantCore.Setup(mock => mock.RemoveGrant(InteractiveSid, TestPath, false, true))
            .Callback(() => db.GetAccount(InteractiveSid)?.Grants.RemoveAll(entry =>
                !entry.IsTraverseOnly &&
                !entry.IsDeny &&
                string.Equals(entry.Path, TestPath, StringComparison.OrdinalIgnoreCase)))
            .Returns(new GrantRemoveResult(Found: true, SavedRights: null));
        var traverseCore = new Mock<ITraverseCoreOperations>();
        traverseCore.Setup(mock => mock.AddTraverse(InteractiveSid, TestPath))
            .Throws(new InvalidOperationException("simulated traverse failure"));
        var aclPermission = new Mock<IAclPermissionService>();
        aclPermission.Setup(mock => mock.NeedsPermissionGrant(TestPath, InteractiveSid, It.IsAny<FileSystemRights>()))
            .Returns(true);
        var sync = BuildSync(
            db,
            new TestFileSystemPathInfo().AddDirectory(TestPath),
            new LambdaDatabaseProvider(() => db),
            traverseCore.Object,
            grantCore.Object,
            aclPermission.Object);

        Assert.Throws<InvalidOperationException>(() =>
            sync.SyncAllowGrantToInteractiveUser(ContainerSid, TestPath, new SavedRightsState(true, false, true, false, false)));

        Assert.Empty(db.GetAccount(InteractiveSid)?.Grants ?? []);
        grantCore.Verify(mock => mock.RemoveGrant(InteractiveSid, TestPath, false, true), Times.Once);
    }

    private static ContainerInteractiveUserSync BuildSync(
        AppDatabase db,
        IFileSystemPathInfo pathInfo,
        IDatabaseProvider provider,
        ITraverseCoreOperations traverseCore,
        IGrantCoreOperations? grantCore = null,
        IAclPermissionService? aclPermission = null)
    {
        var resolver = new Mock<IInteractiveUserResolver>();
        resolver.Setup(mock => mock.GetInteractiveUserSid()).Returns(InteractiveSid);
        var dbAccessor = new UiThreadDatabaseAccessor(provider, () => SyncInvoker);
        return new ContainerInteractiveUserSync(
            grantCore ?? new Mock<IGrantCoreOperations>().Object,
            traverseCore,
            new TraverseGrantOwnerResolver(),
            resolver.Object,
            aclPermission ?? new Mock<IAclPermissionService>().Object,
            dbAccessor,
            new Mock<ILoggingService>().Object,
            pathInfo);
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
}
