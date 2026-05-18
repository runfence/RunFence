using System.Security.AccessControl;
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

public class TraverseRestoreWorkflowTests
{
    private const string UserSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
    private const string ExistingDir = @"C:\Existing\TestDir";

    private readonly Mock<IAclAccessor> _aclAccessor = new();
    private readonly Mock<ITraverseAcl> _traverseAcl = new();
    private readonly Mock<IAclPermissionService> _aclPermission = new();
    private readonly Mock<IInteractiveUserResolver> _iuResolver = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly TestFileSystemPathInfo _pathInfo = new();
    private readonly GrantAceService _grantAceService;
    private readonly FileOwnerService _fileOwnerService;

    private static readonly IUiThreadInvoker SyncInvoker =
        new LambdaUiThreadInvoker(a => a(), a => a());

    public TraverseRestoreWorkflowTests()
    {
        _pathInfo.AddDirectory(Path.GetPathRoot(ExistingDir)!);
        _pathInfo.AddDirectory(ExistingDir);

        _traverseAcl.Setup(t => t.HasExplicitTraverseAce(It.IsAny<string>(), It.IsAny<SecurityIdentifier>()))
            .Returns(true);
        _traverseAcl.Setup(t => t.HasExplicitTraverseAceOrThrow(It.IsAny<string>(), It.IsAny<SecurityIdentifier>()))
            .Returns(true);

        _aclPermission.Setup(p => p.ResolveAccountGroupSids(It.IsAny<string>()))
            .Returns([]);
        _aclPermission.Setup(p => p.NeedsPermissionGrant(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<FileSystemRights>(),
                It.IsAny<bool>()))
            .Returns(true);
        _aclPermission.Setup(p => p.HasEffectiveRights(
                It.IsAny<FileSystemSecurity>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<FileSystemRights>()))
            .Returns(true);

        _iuResolver.Setup(r => r.GetInteractiveUserSid()).Returns((string?)null);
        _aclAccessor.Setup(a => a.GetSecurity(It.IsAny<string>()))
            .Returns(CreateSecurityWithOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value));

        _grantAceService = new GrantAceService(_aclAccessor.Object, _pathInfo);
        _fileOwnerService = new FileOwnerService(_log.Object, _pathInfo);
    }

    [Fact]
    public void RestoreTraverse_LegacySnapshot_RestoresNullAppliedPathsExactly()
    {
        var mainStore = new TestGrantIntentStore();
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true,
            AllAppliedPaths = [ExistingDir, Path.GetPathRoot(ExistingDir)!]
        });
        var workflow = BuildWorkflow(
            mainStore,
            out _,
            out var db);
        db.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true,
            AllAppliedPaths = [ExistingDir, Path.GetPathRoot(ExistingDir)!]
        });

        var result = workflow.Restore(
            UserSid,
            ExistingDir,
            new GrantIntentRestoreSnapshot(
                new GrantedPathEntry
                {
                    Path = ExistingDir,
                    IsTraverseOnly = true,
                    AllAppliedPaths = null
                },
                [new GrantIntentRestoreLocation(null, new GrantedPathEntry
                {
                    Path = ExistingDir,
                    IsTraverseOnly = true,
                    AllAppliedPaths = null
                })]));

        var entry = db.GetAccount(UserSid)!.Grants
            .Single(e => e.IsTraverseOnly &&
                string.Equals(e.Path, ExistingDir, StringComparison.OrdinalIgnoreCase));
        var storedEntry = Assert.Single(mainStore.GetEntries(UserSid));
        Assert.Null(entry.AllAppliedPaths);
        Assert.Null(storedEntry.AllAppliedPaths);
        Assert.True(result.DatabaseModified);
        Assert.True(result.DurableSaveCompleted);
    }

    [Fact]
    public void RestoreTraverse_RemoveOnly_RemovesAclBeforeSave()
    {
        var rootPath = Path.GetPathRoot(ExistingDir)!;
        var mainStore = new TestGrantIntentStore();
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true,
            AllAppliedPaths = [ExistingDir, rootPath]
        });
        var workflow = BuildWorkflow(
            mainStore,
            out _,
            out var db);
        db.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true,
            AllAppliedPaths = [ExistingDir, rootPath]
        });

        var events = new List<string>();
        _traverseAcl.Setup(mock => mock.RemoveTraverseOnlyAce(
                rootPath,
                It.Is<SecurityIdentifier>(sid => sid.Value == UserSid)))
            .Callback(() => events.Add("remove"));
        mainStore.SaveAction = () => events.Add("save");

        var result = workflow.Restore(
            UserSid,
            ExistingDir,
            new GrantIntentRestoreSnapshot(
                new GrantedPathEntry
                {
                    Path = ExistingDir,
                    IsTraverseOnly = true,
                    AllAppliedPaths = [ExistingDir]
                },
                [new GrantIntentRestoreLocation(null, new GrantedPathEntry
                {
                    Path = ExistingDir,
                    IsTraverseOnly = true,
                    AllAppliedPaths = [ExistingDir]
                })]));

        Assert.Equal(["remove", "save"], events);
        Assert.True(result.TraverseApplied);
        Assert.True(result.DatabaseModified);
        Assert.True(result.DurableSaveCompleted);
        Assert.Equal([ExistingDir], Assert.Single(mainStore.GetEntries(UserSid)).AllAppliedPaths);
        Assert.Equal([ExistingDir], db.GetAccount(UserSid)!.Grants.Single(entry =>
            entry.IsTraverseOnly &&
            string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase)).AllAppliedPaths);
    }

    [Fact]
    public void RestoreTraverse_RemoveOnlyPostSaveFailure_ReturnsWarningAndKeepsCompletedState()
    {
        var rootPath = Path.GetPathRoot(ExistingDir)!;
        var mainStore = new TestGrantIntentStore();
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true,
            AllAppliedPaths = [ExistingDir, rootPath]
        });
        var workflow = BuildWorkflow(
            mainStore,
            out _,
            out var db);
        db.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true,
            AllAppliedPaths = [ExistingDir, rootPath]
        });

        var events = new List<string>();
        _traverseAcl.Setup(mock => mock.RemoveTraverseOnlyAce(
                rootPath,
                It.Is<SecurityIdentifier>(sid => sid.Value == UserSid)))
            .Callback(() => events.Add("remove"));
        mainStore.SaveAction = () =>
        {
            events.Add("save");
            throw new InvalidOperationException("save failed");
        };

        var result = workflow.Restore(
            UserSid,
            ExistingDir,
            new GrantIntentRestoreSnapshot(
                new GrantedPathEntry
                {
                    Path = ExistingDir,
                    IsTraverseOnly = true,
                    AllAppliedPaths = [ExistingDir]
                },
                [new GrantIntentRestoreLocation(null, new GrantedPathEntry
                {
                    Path = ExistingDir,
                    IsTraverseOnly = true,
                    AllAppliedPaths = [ExistingDir]
                })]));

        var warning = Assert.Single(result.Warnings);
        Assert.Equal(["remove", "save"], events);
        Assert.True(result.TraverseApplied);
        Assert.True(result.DatabaseModified);
        Assert.False(result.DurableSaveCompleted);
        Assert.Equal(GrantApplyFailureStep.PostTraverseRemoveSave, warning.Step);
        Assert.Equal(ExistingDir, warning.Path);
        Assert.Null(warning.ConfigPath);
        Assert.Equal("save failed", warning.Cause.Message);
        Assert.Equal(1, mainStore.SaveCount);
        Assert.Equal([ExistingDir], Assert.Single(mainStore.GetEntries(UserSid)).AllAppliedPaths);
        Assert.Equal([ExistingDir], db.GetAccount(UserSid)!.Grants.Single(entry =>
            entry.IsTraverseOnly &&
            string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase)).AllAppliedPaths);
        _traverseAcl.Verify(mock => mock.AddAllowAce(
            It.IsAny<string>(),
            It.IsAny<SecurityIdentifier>()), Times.Never);
    }

    [Fact]
    public void RestoreTraverse_MixedCoverage_UsesRemoveSaveAddOrdering()
    {
        UseTraverseAclBackedEffectiveRights();
        var rootPath = Path.GetPathRoot(ExistingDir)!;
        var mainStore = new TestGrantIntentStore();
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true,
            AllAppliedPaths = [rootPath]
        });
        var workflow = BuildWorkflow(
            mainStore,
            out _,
            out var db);
        db.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true,
            AllAppliedPaths = [rootPath]
        });

        var events = new List<string>();
        _traverseAcl.Setup(mock => mock.RemoveTraverseOnlyAce(
                rootPath,
                It.Is<SecurityIdentifier>(sid => sid.Value == UserSid)))
            .Callback(() => events.Add("remove"));
        mainStore.SaveAction = () => events.Add("save");
        _traverseAcl.Setup(mock => mock.AddAllowAce(
                ExistingDir,
                It.Is<SecurityIdentifier>(sid => sid.Value == UserSid)))
            .Callback<string, SecurityIdentifier>((path, sid) =>
            {
                events.Add("add");
                TrackTraverseAceInTestSecurity(path, sid.Value);
            });

        var result = workflow.Restore(
            UserSid,
            ExistingDir,
            new GrantIntentRestoreSnapshot(
                new GrantedPathEntry
                {
                    Path = ExistingDir,
                    IsTraverseOnly = true,
                    AllAppliedPaths = [ExistingDir]
                },
                [new GrantIntentRestoreLocation(null, new GrantedPathEntry
                {
                    Path = ExistingDir,
                    IsTraverseOnly = true,
                    AllAppliedPaths = [ExistingDir]
                })]));

        Assert.Equal(["remove", "save", "add"], events);
        Assert.True(result.TraverseApplied);
        Assert.True(result.DatabaseModified);
        Assert.True(result.DurableSaveCompleted);
    }

    [Fact]
    public void RestoreTraverse_SharedContainerRestore_MutatesSourceTrackedEntryAndLeavesManualSharedEntryUnchanged()
    {
        var containerSid = "S-1-15-2-99-1-2-3-4-5-6";
        var rootPath = Path.GetPathRoot(ExistingDir)!;
        var sharedSid = AclHelper.AllApplicationPackagesSid;
        var manualEntry = new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true,
            AllAppliedPaths = [rootPath]
        };
        var sourceTrackedEntry = new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true,
            SourceSids = [containerSid],
            AllAppliedPaths = [ExistingDir, rootPath]
        };
        var mainStore = new TestGrantIntentStore();
        mainStore.AddEntry(sharedSid, manualEntry.Clone());
        mainStore.AddEntry(sharedSid, sourceTrackedEntry.Clone());
        var workflow = BuildWorkflow(
            mainStore,
            out _,
            out var db);
        db.GetOrCreateAccount(sharedSid).Grants.Add(manualEntry.Clone());
        db.GetOrCreateAccount(sharedSid).Grants.Add(sourceTrackedEntry.Clone());

        var result = workflow.Restore(
            containerSid,
            ExistingDir,
            new GrantIntentRestoreSnapshot(
                new GrantedPathEntry
                {
                    Path = ExistingDir,
                    IsTraverseOnly = true,
                    SourceSids = [containerSid],
                    AllAppliedPaths = [ExistingDir]
                },
                [new GrantIntentRestoreLocation(null, new GrantedPathEntry
                {
                    Path = ExistingDir,
                    IsTraverseOnly = true,
                    SourceSids = [containerSid],
                    AllAppliedPaths = [ExistingDir]
                })]));

        Assert.True(result.DatabaseModified);
        Assert.Equal([rootPath], mainStore.GetEntries(sharedSid).Single(entry => entry.SourceSids == null).AllAppliedPaths);
        Assert.Equal([ExistingDir], mainStore.GetEntries(sharedSid).Single(entry => entry.SourceSids != null).AllAppliedPaths);
        Assert.Equal([rootPath], db.GetAccount(sharedSid)!.Grants.Single(entry => entry.SourceSids == null).AllAppliedPaths);
        Assert.Equal([ExistingDir], db.GetAccount(sharedSid)!.Grants.Single(entry => entry.SourceSids != null).AllAppliedPaths);
    }

    [Fact]
    public void RestoreTraverse_SaveFailure_RestoresPreviousTrackedTraverseState()
    {
        var rootPath = Path.GetPathRoot(ExistingDir)!;
        var mainStore = new TestGrantIntentStore
        {
            SaveAction = () => throw new InvalidOperationException("save failed")
        };
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true,
            AllAppliedPaths = [ExistingDir]
        });
        var workflow = BuildWorkflow(
            mainStore,
            out _,
            out var db);
        db.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true,
            AllAppliedPaths = [ExistingDir]
        });

        var ex = Assert.Throws<GrantOperationException>(() =>
            workflow.Restore(
                UserSid,
                ExistingDir,
                new GrantIntentRestoreSnapshot(
                    new GrantedPathEntry
                    {
                        Path = ExistingDir,
                        IsTraverseOnly = true,
                        AllAppliedPaths = [ExistingDir, rootPath]
                    },
                    [new GrantIntentRestoreLocation(null, new GrantedPathEntry
                    {
                        Path = ExistingDir,
                        IsTraverseOnly = true,
                        AllAppliedPaths = [ExistingDir, rootPath]
                    })])));

        Assert.Equal(GrantApplyFailureStep.TraverseIntentSave, ex.Step);
        Assert.Equal(2, mainStore.SaveCount);
        Assert.Equal(GrantApplyFailureStep.RevertIntentSave, Assert.Single(ex.CleanupFailures).Step);
        Assert.Equal([ExistingDir], Assert.Single(mainStore.GetEntries(UserSid)).AllAppliedPaths);
        Assert.Equal([ExistingDir], db.GetAccount(UserSid)!.Grants.Single(entry =>
            entry.IsTraverseOnly &&
            string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase)).AllAppliedPaths);
        _traverseAcl.Verify(mock => mock.AddAllowAce(
            It.IsAny<string>(),
            It.IsAny<SecurityIdentifier>()), Times.Never);
    }

    [Fact]
    public void RestoreTraverse_MultiStoreSaveOrder_FailureReportsConfigPath_BeforeAclApply()
    {
        UseTraverseAclBackedEffectiveRights();
        var rootPath = Path.GetPathRoot(ExistingDir)!;
        var mainStore = new TestGrantIntentStore();
        var additionalAStore = new TestGrantIntentStore(@"C:\Configs\a-traverse-order.rfn");
        var additionalZStore = new TestGrantIntentStore(@"C:\Configs\z-traverse-order.rfn");
        foreach (var store in new[] { mainStore, additionalAStore, additionalZStore })
        {
            store.AddEntry(UserSid, new GrantedPathEntry
            {
                Path = ExistingDir,
                IsTraverseOnly = true,
                AllAppliedPaths = [ExistingDir]
            });
        }

        var workflow = BuildWorkflow(
            mainStore,
            out var storeProvider,
            out var db);
        storeProvider.AddLoadedStore(additionalZStore);
        storeProvider.AddLoadedStore(additionalAStore);
        db.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true,
            AllAppliedPaths = [ExistingDir]
        });
        TrackTraverseAceInTestSecurity(ExistingDir, UserSid);

        var events = new List<string>();
        mainStore.SaveAction = () => events.Add("main-save");
        additionalAStore.SaveAction = () => events.Add("a-save");
        additionalZStore.SaveAction = () =>
        {
            events.Add("z-save");
            Assert.Empty(mainStore.GetEntries(UserSid));
            Assert.Equal([ExistingDir, rootPath], Assert.Single(additionalAStore.GetEntries(UserSid)).AllAppliedPaths);
            Assert.Empty(additionalZStore.GetEntries(UserSid));
            Assert.Equal([ExistingDir, rootPath], db.GetAccount(UserSid)!.Grants.Single(entry =>
                entry.IsTraverseOnly &&
                string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase)).AllAppliedPaths);
            throw new InvalidOperationException("z save failed");
        };

        var ex = Assert.Throws<GrantOperationException>(() =>
            workflow.Restore(
                UserSid,
                ExistingDir,
                new GrantIntentRestoreSnapshot(
                    new GrantedPathEntry
                    {
                        Path = ExistingDir,
                        IsTraverseOnly = true,
                        AllAppliedPaths = [ExistingDir, rootPath]
                    },
                    [new GrantIntentRestoreLocation(
                        additionalAStore.ConfigPath,
                        new GrantedPathEntry
                        {
                            Path = ExistingDir,
                            IsTraverseOnly = true,
                            AllAppliedPaths = [ExistingDir, rootPath]
                        })])));

        Assert.Equal(GrantApplyFailureStep.TraverseIntentSave, ex.Step);
        Assert.Equal(Path.GetFullPath(@"C:\Configs\z-traverse-order.rfn"), ex.ConfigPath);
        var cleanupFailure = Assert.Single(ex.CleanupFailures);
        Assert.Equal(GrantApplyFailureStep.RevertIntentSave, cleanupFailure.Step);
        Assert.Equal(Path.GetFullPath(@"C:\Configs\z-traverse-order.rfn"), cleanupFailure.ConfigPath);
        Assert.Equal(["main-save", "a-save", "z-save", "main-save", "a-save", "z-save"], events);
        _traverseAcl.Verify(mock => mock.AddAllowAce(
            It.IsAny<string>(),
            It.IsAny<SecurityIdentifier>()), Times.Never);
        Assert.Equal(2, mainStore.SaveCount);
        Assert.Equal(2, additionalAStore.SaveCount);
        Assert.Equal(2, additionalZStore.SaveCount);
        Assert.Equal([ExistingDir], Assert.Single(mainStore.GetEntries(UserSid)).AllAppliedPaths);
        Assert.Equal([ExistingDir], Assert.Single(additionalAStore.GetEntries(UserSid)).AllAppliedPaths);
        Assert.Equal([ExistingDir], Assert.Single(additionalZStore.GetEntries(UserSid)).AllAppliedPaths);
        Assert.Equal([ExistingDir], db.GetAccount(UserSid)!.Grants.Single(entry =>
            entry.IsTraverseOnly &&
            string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase)).AllAppliedPaths);
    }

    [Fact]
    public void RestoreTraverse_MixedRemoveFailure_DoesNotRecreatePreexistingDriftedTraverseAcl_AndRestoresState()
    {
        UseTraverseAclBackedEffectiveRights();
        var rootPath = Path.GetPathRoot(ExistingDir)!;
        var mainStore = new TestGrantIntentStore();
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true,
            AllAppliedPaths = [rootPath]
        });
        var workflow = BuildWorkflow(
            mainStore,
            out _,
            out var db);
        db.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true,
            AllAppliedPaths = [rootPath]
        });
        _traverseAcl.Setup(mock => mock.RemoveTraverseOnlyAce(
                rootPath,
                It.Is<SecurityIdentifier>(sid => sid.Value == UserSid)))
            .Throws(new UnauthorizedAccessException("remove failed"));
        var reappliedPaths = new List<string>();
        _traverseAcl.Setup(mock => mock.AddAllowAce(
                It.IsAny<string>(),
                It.Is<SecurityIdentifier>(sid => sid.Value == UserSid)))
            .Callback<string, SecurityIdentifier>((path, sid) =>
            {
                reappliedPaths.Add(path);
                TrackTraverseAceInTestSecurity(path, sid.Value);
            });

        var ex = Assert.Throws<GrantOperationException>(() =>
            workflow.Restore(
                UserSid,
                ExistingDir,
                new GrantIntentRestoreSnapshot(
                    new GrantedPathEntry
                    {
                        Path = ExistingDir,
                        IsTraverseOnly = true,
                        AllAppliedPaths = [ExistingDir]
                    },
                    [new GrantIntentRestoreLocation(null, new GrantedPathEntry
                    {
                        Path = ExistingDir,
                        IsTraverseOnly = true,
                        AllAppliedPaths = [ExistingDir]
                    })])));

        Assert.Equal(GrantApplyFailureStep.TraverseAclApply, ex.Step);
        Assert.Empty(reappliedPaths);
        Assert.Equal([rootPath], Assert.Single(mainStore.GetEntries(UserSid)).AllAppliedPaths);
        Assert.Equal([rootPath], db.GetAccount(UserSid)!.Grants.Single(entry =>
            entry.IsTraverseOnly &&
            string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase)).AllAppliedPaths);
    }

    [Fact]
    public void RestoreTraverse_ExplicitAclSnapshotReadFailure_ThrowsBeforeMutation()
    {
        var rootPath = Path.GetPathRoot(ExistingDir)!;
        var mainStore = new TestGrantIntentStore();
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true,
            AllAppliedPaths = [ExistingDir, rootPath]
        });
        var workflow = BuildWorkflow(
            mainStore,
            out _,
            out var db);
        db.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true,
            AllAppliedPaths = [ExistingDir, rootPath]
        });
        _traverseAcl.Setup(mock => mock.HasExplicitTraverseAceOrThrow(
                It.IsAny<string>(),
                It.Is<SecurityIdentifier>(sid => sid.Value == UserSid)))
            .Throws(new UnauthorizedAccessException("acl read failed"));

        var ex = Assert.Throws<GrantOperationException>(() =>
            workflow.Restore(
                UserSid,
                ExistingDir,
                new GrantIntentRestoreSnapshot(
                    new GrantedPathEntry
                    {
                        Path = ExistingDir,
                        IsTraverseOnly = true,
                        AllAppliedPaths = [ExistingDir]
                    },
                    [new GrantIntentRestoreLocation(null, new GrantedPathEntry
                    {
                        Path = ExistingDir,
                        IsTraverseOnly = true,
                        AllAppliedPaths = [ExistingDir]
                    })])));

        Assert.Equal(GrantApplyFailureStep.TraverseAclApply, ex.Step);
        Assert.Equal("acl read failed", ex.Cause.Message);
        Assert.Equal([ExistingDir, rootPath], db.GetAccount(UserSid)!.Grants.Single(entry =>
            entry.IsTraverseOnly &&
            string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase)).AllAppliedPaths);
        _traverseAcl.Verify(mock => mock.HasExplicitTraverseAceOrThrow(
            It.IsAny<string>(),
            It.Is<SecurityIdentifier>(sid => sid.Value == UserSid)), Times.AtLeastOnce);
        _traverseAcl.Verify(mock => mock.HasExplicitTraverseAce(
            It.IsAny<string>(),
            It.Is<SecurityIdentifier>(sid => sid.Value == UserSid)), Times.Never);
        _traverseAcl.Verify(mock => mock.RemoveTraverseOnlyAce(
            It.IsAny<string>(),
            It.IsAny<SecurityIdentifier>()), Times.Never);
    }

    [Fact]
    public void RestoreTraverse_RemoveStepTraverseAclReadFailure_ThrowsBeforeTrackedStateMutation()
    {
        var rootPath = Path.GetPathRoot(ExistingDir)!;
        var mainStore = new TestGrantIntentStore();
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true,
            AllAppliedPaths = [ExistingDir, rootPath]
        });
        var workflow = BuildWorkflow(
            mainStore,
            out _,
            out var db);
        db.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true,
            AllAppliedPaths = [ExistingDir, rootPath]
        });
        _traverseAcl.SetupSequence(mock => mock.HasExplicitTraverseAceOrThrow(
                It.IsAny<string>(),
                It.Is<SecurityIdentifier>(sid => sid.Value == UserSid)))
            .Returns(true)
            .Throws(new UnauthorizedAccessException("remove read failed"));

        var ex = Assert.Throws<GrantOperationException>(() =>
            workflow.Restore(
                UserSid,
                ExistingDir,
                new GrantIntentRestoreSnapshot(
                    new GrantedPathEntry
                    {
                        Path = ExistingDir,
                        IsTraverseOnly = true,
                        AllAppliedPaths = [ExistingDir]
                    },
                    [new GrantIntentRestoreLocation(null, new GrantedPathEntry
                    {
                        Path = ExistingDir,
                        IsTraverseOnly = true,
                        AllAppliedPaths = [ExistingDir]
                    })])));

        Assert.Equal(GrantApplyFailureStep.TraverseAclApply, ex.Step);
        Assert.Equal("remove read failed", ex.Cause.Message);
        Assert.Equal([ExistingDir, rootPath], Assert.Single(mainStore.GetEntries(UserSid)).AllAppliedPaths);
        Assert.Equal([ExistingDir, rootPath], db.GetAccount(UserSid)!.Grants.Single(entry =>
            entry.IsTraverseOnly &&
            string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase)).AllAppliedPaths);
        _traverseAcl.Verify(mock => mock.RemoveTraverseOnlyAce(
            It.IsAny<string>(),
            It.IsAny<SecurityIdentifier>()), Times.Never);
    }

    [Fact]
    public void RestoreTraverse_EffectiveTraverseReadFailure_ThrowsBeforeMutation()
    {
        var rootPath = Path.GetPathRoot(ExistingDir)!;
        var mainStore = new TestGrantIntentStore();
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true,
            AllAppliedPaths = [rootPath]
        });
        var workflow = BuildWorkflow(
            mainStore,
            out _,
            out var db);
        db.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true,
            AllAppliedPaths = [rootPath]
        });
        _aclPermission.Setup(mock => mock.ResolveAccountGroupSids(UserSid))
            .Throws(new UnauthorizedAccessException("effective traverse read failed"));

        var ex = Assert.Throws<GrantOperationException>(() =>
            workflow.Restore(
                UserSid,
                ExistingDir,
                new GrantIntentRestoreSnapshot(
                    new GrantedPathEntry
                    {
                        Path = ExistingDir,
                        IsTraverseOnly = true,
                        AllAppliedPaths = [ExistingDir]
                    },
                    [new GrantIntentRestoreLocation(null, new GrantedPathEntry
                    {
                        Path = ExistingDir,
                        IsTraverseOnly = true,
                        AllAppliedPaths = [ExistingDir]
                    })])));

        Assert.Equal(GrantApplyFailureStep.TraverseAclApply, ex.Step);
        Assert.Equal("effective traverse read failed", ex.Cause.Message);
        Assert.Equal([rootPath], Assert.Single(mainStore.GetEntries(UserSid)).AllAppliedPaths);
        Assert.Equal([rootPath], db.GetAccount(UserSid)!.Grants.Single(entry =>
            entry.IsTraverseOnly &&
            string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase)).AllAppliedPaths);
        _traverseAcl.Verify(mock => mock.RemoveTraverseOnlyAce(
            It.IsAny<string>(),
            It.IsAny<SecurityIdentifier>()), Times.Never);
    }

    private TraverseRestoreWorkflow BuildWorkflow(
        TestGrantIntentStore mainStore,
        out TestGrantIntentStoreProvider storeProvider,
        out AppDatabase db)
    {
        storeProvider = new TestGrantIntentStoreProvider(mainStore);
        var repository = new GrantIntentRepository(storeProvider);
        var traverseGrantOwnerResolver = new TraverseGrantOwnerResolver();
        var traverseIntentStoreCoordinator = new TraverseIntentStoreCoordinator(() => repository, traverseGrantOwnerResolver);
        db = new AppDatabase();
        var database = db;
        var dbAccessor = new UiThreadDatabaseAccessor(new LambdaDatabaseProvider(() => database), () => SyncInvoker);
        var grantCore = new GrantCoreOperations(_grantAceService, _fileOwnerService, dbAccessor, _log.Object, _pathInfo);
        var ancestorGranter = new AncestorTraverseGranter(_log.Object, _aclPermission.Object, _traverseAcl.Object, _pathInfo);
        var traverseCore = new TraverseCoreOperations(
            _traverseAcl.Object,
            ancestorGranter,
            _aclPermission.Object,
            dbAccessor,
            _log.Object,
            _pathInfo,
            traverseGrantOwnerResolver);
        var traverseGrantStateService = new TraverseGrantStateService(dbAccessor, _pathInfo, traverseIntentStoreCoordinator);
        var containerIuSync = new ContainerInteractiveUserSync(
            grantCore,
            traverseCore,
            traverseGrantOwnerResolver,
            _iuResolver.Object,
            _aclPermission.Object,
            dbAccessor,
            _log.Object,
            _pathInfo);
        var saveService = new GrantIntentStoreSaveService();
        var provider = storeProvider;

        return new TraverseRestoreWorkflow(
            traverseCore,
            dbAccessor,
            containerIuSync,
            _pathInfo,
            _traverseAcl.Object,
            traverseGrantOwnerResolver,
            traverseIntentStoreCoordinator,
            traverseGrantStateService,
            () => provider,
            saveService);
    }

    private void UseTraverseAclBackedEffectiveRights()
    {
        _aclPermission.Setup(permission => permission.HasEffectiveRights(
                It.IsAny<FileSystemSecurity>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                TraverseRightsHelper.TraverseRights))
            .Returns<FileSystemSecurity, string, IReadOnlyList<string>, FileSystemRights>(
                (security, sid, _, rights) => HasExplicitAllowRights(security, sid, rights));
    }

    private void TrackTraverseAceInTestSecurity(string path, string sid)
    {
        var security = _pathInfo.GetDirectorySecurity(path);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(sid),
            TraverseRightsHelper.TraverseRights,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow));
        _pathInfo.AddDirectory(path, security);
    }

    private static FileSystemSecurity CreateSecurityWithOwner(string ownerSid)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(new SecurityIdentifier(ownerSid));
        return security;
    }

    private static bool HasExplicitAllowRights(FileSystemSecurity security, string sid, FileSystemRights rights)
        => security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .OfType<FileSystemAccessRule>()
            .Any(rule =>
                rule.AccessControlType == AccessControlType.Allow &&
                string.Equals(rule.IdentityReference.Value, sid, StringComparison.OrdinalIgnoreCase) &&
                (rule.FileSystemRights & rights) == rights);
}
