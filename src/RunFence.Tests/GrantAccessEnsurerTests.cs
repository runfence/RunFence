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

public class GrantAccessEnsurerTests
{
    private const string UserSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
    private const string ContainerSid = "S-1-15-2-99-1-2-3-4-5-6";
    private const string OtherContainerSid = "S-1-15-2-99-1-2-3-4-5-7";
    private const string InteractiveSid = "S-1-5-21-1234567890-1234567890-1234567890-1002";
    private const string ExistingDir = @"C:\Existing\TestDir";

    private static readonly SavedRightsState ReadOnly =
        new(Execute: false, Write: false, Read: true, Special: false, Own: false);

    private static readonly SavedRightsState DenyReadExecute =
        new(Execute: true, Write: true, Read: true, Special: true, Own: false);

    private static readonly IUiThreadInvoker SyncInvoker =
        new LambdaUiThreadInvoker(a => a(), a => a());

    private static (
        GrantAccessEnsurer Ensurer,
        GrantFileSystemOperations Operations,
        AppDatabase Database,
        TestGrantIntentStore MainStore,
        Mock<IAclPermissionService> AclPermission,
        Mock<IPathSecurityDescriptorAccessor> FileSecurityAccessor,
        Mock<IExplicitAceAccessor> ExplicitAceAccessor,
        Mock<ITraverseAcl> TraverseAcl) Build(
        string? interactiveSid = null,
        IGrantIntentStoreSaveService? grantIntentStoreSaveService = null)
    {
        var log = new Mock<ILoggingService>();
        var fileSecurityAccessor = new Mock<IPathSecurityDescriptorAccessor>();
        var explicitAceAccessor = new Mock<IExplicitAceAccessor>();
        var aclPermission = new Mock<IAclPermissionService>();
        var traverseAcl = new Mock<ITraverseAcl>();
        var iuResolver = new Mock<IInteractiveUserResolver>();
        var ownerMock = new Mock<IFileOwnerService>();
        var mandatoryLabelMock = new Mock<IMandatoryLabelService>();
        var pathInfo = new TestFileSystemPathInfo();
        pathInfo.AddDirectory(Path.GetPathRoot(ExistingDir)!);
        pathInfo.AddDirectory(ExistingDir);

        aclPermission.Setup(p => p.ResolveAccountGroupSids(It.IsAny<string>())).Returns([]);
        aclPermission.Setup(p => p.NeedsPermissionGrant(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<FileSystemRights>(), It.IsAny<bool>())).Returns(false);
        aclPermission.Setup(p => p.HasEffectiveRights(It.IsAny<FileSystemSecurity>(), It.IsAny<string>(),
            It.IsAny<IReadOnlyList<string>>(), It.IsAny<FileSystemRights>())).Returns(true);
        traverseAcl.Setup(t => t.HasExplicitTraverseAce(It.IsAny<string>(), It.IsAny<SecurityIdentifier>()))
            .Returns(true);
        traverseAcl.Setup(t => t.HasExplicitTraverseAceOrThrow(It.IsAny<string>(), It.IsAny<SecurityIdentifier>()))
            .Returns(true);
        iuResolver.Setup(r => r.GetInteractiveUserSid()).Returns(interactiveSid);
        fileSecurityAccessor.Setup(a => a.GetSecurity(ExistingDir)).Returns(new DirectorySecurity());

        var db = new AppDatabase();
        var mainGrantStore = new TestGrantIntentStore();
        var storeProvider = new TestGrantIntentStoreProvider(mainGrantStore);
        var repository = new GrantIntentRepository(storeProvider);
        var traverseGrantOwnerResolver = new TraverseGrantOwnerResolver();
        var dbAccessor = new UiThreadDatabaseAccessor(new LambdaDatabaseProvider(() => db), () => SyncInvoker);
        var grantAceService = new GrantAceService(fileSecurityAccessor.Object, explicitAceAccessor.Object, pathInfo);
        var grantCore = new GrantCoreOperations(grantAceService, ownerMock.Object, dbAccessor, log.Object, pathInfo);
        var ancestorGranter = new AncestorTraverseGranter(log.Object, aclPermission.Object, traverseAcl.Object, pathInfo);
        var traverseIntentStoreCoordinator = new TraverseIntentStoreCoordinator(() => repository, traverseGrantOwnerResolver);
        var traverseCore = new TraverseCoreOperations(
            traverseAcl.Object,
            ancestorGranter,
            aclPermission.Object,
            dbAccessor,
            log.Object,
            pathInfo,
            traverseGrantOwnerResolver);
        var traverseGrantStateService = new TraverseGrantStateService(dbAccessor, pathInfo, traverseIntentStoreCoordinator);
        var containerIuSync = new ContainerInteractiveUserSync(
            grantCore,
            traverseCore,
            traverseGrantOwnerResolver,
            iuResolver.Object,
            aclPermission.Object,
            dbAccessor,
            log.Object,
            pathInfo);
        var lowIntegrityGrantSync = new LowIntegrityGrantSync(
            grantCore,
            traverseCore,
            mandatoryLabelMock.Object,
            dbAccessor);
        var operations = new GrantFileSystemOperations(grantCore, grantAceService, ownerMock.Object,
            mandatoryLabelMock.Object, dbAccessor);
        grantIntentStoreSaveService ??= new GrantIntentStoreSaveService();
        var grantRuntimeMutationService = new GrantRuntimeMutationService(
            traverseCore,
            dbAccessor,
            containerIuSync,
            lowIntegrityGrantSync,
            mandatoryLabelMock.Object,
            operations,
            grantAceService,
            pathInfo,
            traverseGrantStateService);
        var grantRuntimeSnapshotService = new GrantRuntimeSnapshotService(dbAccessor, traverseGrantOwnerResolver);
        var grantIntentMutationStateRestorer = new GrantIntentMutationStateRestorer(grantIntentStoreSaveService);
        var ensurer = new GrantAccessEnsurer(
            aclPermission.Object,
            dbAccessor,
            fileSecurityAccessor.Object,
            pathInfo,
            traverseCore,
            operations,
            iuResolver.Object,
            traverseGrantOwnerResolver,
            () => repository,
            () => mainGrantStore,
            grantIntentStoreSaveService,
            grantIntentMutationStateRestorer,
            grantRuntimeSnapshotService);
        return (ensurer, operations, db, mainGrantStore, aclPermission, fileSecurityAccessor, explicitAceAccessor, traverseAcl);
    }

    [Fact]
    public void EnsureAccess_DenyConflictWithoutConfirm_Throws()
    {
        var (ensurer, operations, _, _, _, _, _, _) = Build();
        operations.AddGrant(UserSid, ExistingDir, isDeny: true, DenyReadExecute);

        Assert.Throws<InvalidOperationException>(() =>
            ensurer.EnsureAccess(UserSid, ExistingDir, ReadOnly, confirm: null));
    }

    [Fact]
    public void EnsureAccess_DenyConflictWithConfirm_PartiallyReducesDeny()
    {
        var (ensurer, operations, db, _, _, _, _, _) = Build();
        operations.AddGrant(UserSid, ExistingDir, isDeny: true, DenyReadExecute);

        ensurer.EnsureAccess(UserSid, ExistingDir, ReadOnly, confirm: (_, _) => true);

        var deny = FindGrant(db, UserSid, ExistingDir, isDeny: true);
        Assert.NotNull(deny);
        Assert.True(deny!.SavedRights!.Execute);
        Assert.False(deny.SavedRights.Read);
    }

    [Fact]
    public void EnsureAccess_DenyConflictSaveFailure_RestoresOriginalDeny()
    {
        var (ensurer, operations, db, mainStore, _, _, _, _) = Build();
        operations.AddGrant(UserSid, ExistingDir, isDeny: true, DenyReadExecute);
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = true,
            SavedRights = DenyReadExecute
        });
        var saveAttempts = 0;
        mainStore.SaveAction = () =>
        {
            saveAttempts++;
            if (saveAttempts == 1)
                throw new InvalidOperationException("save failed");
        };

        var ex = Assert.Throws<GrantOperationException>(() =>
            ensurer.EnsureAccess(UserSid, ExistingDir, ReadOnly, confirm: (_, _) => true));

        Assert.Equal(GrantApplyFailureStep.DenyConflictPostUpdateSave, ex.Step);
        Assert.Empty(ex.CleanupFailures);
        Assert.Equal(2, mainStore.SaveCount);
        Assert.Equal(DenyReadExecute, FindGrant(db, UserSid, ExistingDir, isDeny: true)?.SavedRights);
        Assert.Equal(DenyReadExecute, Assert.Single(mainStore.GetEntries(UserSid)).SavedRights);
        Assert.Null(FindGrant(db, UserSid, ExistingDir, isDeny: false));
    }

    [Fact]
    public void EnsureAccess_DenyConflictSaveFailure_AggregatesAllRollbackStoreCleanupFailures()
    {
        var saveService = new InterceptingGrantIntentStoreSaveService(
            onSave: (_, failureStep, normalizedPath) =>
            {
                if (failureStep == GrantApplyFailureStep.DenyConflictPostUpdateSave)
                {
                    throw new GrantOperationException(
                        GrantApplyFailureStep.DenyConflictPostUpdateSave,
                        normalizedPath,
                        "main.rfn",
                        new InvalidOperationException("deny save failed"));
                }

                if (failureStep == GrantApplyFailureStep.RevertIntentSave)
                {
                    throw new GrantOperationException(
                        GrantApplyFailureStep.RevertIntentSave,
                        normalizedPath,
                        "rollback.rfn",
                        new InvalidOperationException("rollback save failed"),
                        [
                            new GrantApplyFailure(
                                GrantApplyFailureStep.TraverseAclRollback,
                                normalizedPath,
                                "traverse-cleanup.rfn",
                                new InvalidOperationException("traverse cleanup failed")),
                            new GrantApplyFailure(
                                GrantApplyFailureStep.GrantAclRollback,
                                normalizedPath,
                                "grant-cleanup.rfn",
                                new InvalidOperationException("grant cleanup failed"))
                        ]);
                }
            });
        var (ensurer, operations, db, mainStore, _, _, _, _) = Build(grantIntentStoreSaveService: saveService);
        operations.AddGrant(UserSid, ExistingDir, isDeny: true, DenyReadExecute);
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = true,
            SavedRights = DenyReadExecute
        });

        var ex = Assert.Throws<GrantOperationException>(() =>
            ensurer.EnsureAccess(UserSid, ExistingDir, ReadOnly, confirm: (_, _) => true));

        Assert.Equal(GrantApplyFailureStep.DenyConflictPostUpdateSave, ex.Step);
        Assert.Equal("deny save failed", ex.Cause.Message);
        Assert.Collection(
            ex.CleanupFailures,
            failure =>
            {
                Assert.Equal(GrantApplyFailureStep.RevertIntentSave, failure.Step);
                Assert.Equal("rollback.rfn", failure.ConfigPath);
                Assert.Equal("rollback save failed", failure.Exception.Message);
            },
            failure =>
            {
                Assert.Equal(GrantApplyFailureStep.TraverseAclRollback, failure.Step);
                Assert.Equal("traverse-cleanup.rfn", failure.ConfigPath);
                Assert.Equal("traverse cleanup failed", failure.Exception.Message);
            },
            failure =>
            {
                Assert.Equal(GrantApplyFailureStep.GrantAclRollback, failure.Step);
                Assert.Equal("grant-cleanup.rfn", failure.ConfigPath);
                Assert.Equal("grant cleanup failed", failure.Exception.Message);
            });
        Assert.Equal(DenyReadExecute, FindGrant(db, UserSid, ExistingDir, isDeny: true)?.SavedRights);
        Assert.Equal(DenyReadExecute, Assert.Single(mainStore.GetEntries(UserSid)).SavedRights);
    }

    [Fact]
    public void EnsureAccess_SpecificContainerWhenSharedAccessAlreadySufficient_DoesNothing()
    {
        var (ensurer, _, db, _, aclPermission, _, _, _) = Build();
        aclPermission.Setup(p => p.NeedsPermissionGrant(
                ExistingDir,
                AclHelper.AllApplicationPackagesSid,
                It.IsAny<FileSystemRights>(),
                It.IsAny<bool>()))
            .Returns(false);

        var result = ensurer.EnsureAccess(ContainerSid, ExistingDir, ReadOnly);

        Assert.False(result.GrantApplied);
        Assert.False(result.TraverseApplied);
        Assert.False(result.DatabaseModified);
        Assert.Null(db.GetAccount(ContainerSid));
    }

    [Fact]
    public void EnsureAccess_SaveFails_ThrowsAndRestoresTrackedIntent()
    {
        var (ensurer, _, db, mainStore, aclPermission, _, _, _) = Build();
        aclPermission.Setup(p => p.NeedsPermissionGrant(
                ExistingDir,
                UserSid,
                It.IsAny<FileSystemRights>(),
                It.IsAny<bool>()))
            .Returns(true);
        mainStore.SaveAction = () => throw new InvalidOperationException("save failed");

        var ex = Assert.Throws<GrantOperationException>(() => ensurer.EnsureAccess(
            UserSid,
            ExistingDir,
            ReadOnly));

        Assert.Equal(GrantApplyFailureStep.GrantIntentSave, ex.Step);
        Assert.Equal("save failed", ex.Cause.Message);
        var cleanupFailure = Assert.Single(ex.CleanupFailures);
        Assert.Equal(GrantApplyFailureStep.RevertIntentSave, cleanupFailure.Step);
        Assert.Equal(2, mainStore.SaveCount);
        Assert.Empty(mainStore.GetEntries(UserSid));
        Assert.Null(db.GetAccount(UserSid));
    }

    [Fact]
    public void EnsureAccess_SaveFails_AggregatesRollbackSaveCleanupFailuresThroughSharedRestorer()
    {
        var saveService = new InterceptingGrantIntentStoreSaveService(
            onSave: (stores, failureStep, normalizedPath) =>
            {
                if (failureStep == GrantApplyFailureStep.GrantIntentSave)
                {
                    throw new GrantOperationException(
                        GrantApplyFailureStep.GrantIntentSave,
                        normalizedPath,
                        "main.rfn",
                        new InvalidOperationException("save failed"));
                }

                if (failureStep == GrantApplyFailureStep.RevertIntentSave)
                {
                    throw new GrantOperationException(
                        GrantApplyFailureStep.RevertIntentSave,
                        normalizedPath,
                        "rollback.rfn",
                        new InvalidOperationException("rollback save failed"),
                        [
                            new GrantApplyFailure(
                                GrantApplyFailureStep.TraverseAclRollback,
                                normalizedPath,
                                "cleanup.rfn",
                                new InvalidOperationException("cleanup failed"))
                        ]);
                }
            });
        var (ensurer, _, db, mainStore, aclPermission, _, _, _) = Build(grantIntentStoreSaveService: saveService);
        aclPermission.Setup(p => p.NeedsPermissionGrant(
                ExistingDir,
                UserSid,
                It.IsAny<FileSystemRights>(),
                It.IsAny<bool>()))
            .Returns(true);

        var ex = Assert.Throws<GrantOperationException>(() => ensurer.EnsureAccess(
            UserSid,
            ExistingDir,
            ReadOnly));

        Assert.Equal(GrantApplyFailureStep.GrantIntentSave, ex.Step);
        Assert.Collection(
            ex.CleanupFailures,
            failure =>
            {
                Assert.Equal(GrantApplyFailureStep.RevertIntentSave, failure.Step);
                Assert.Equal("rollback.rfn", failure.ConfigPath);
                Assert.Equal("rollback save failed", failure.Exception.Message);
            },
            failure =>
            {
                Assert.Equal(GrantApplyFailureStep.TraverseAclRollback, failure.Step);
                Assert.Equal("cleanup.rfn", failure.ConfigPath);
                Assert.Equal("cleanup failed", failure.Exception.Message);
            });
        Assert.Empty(mainStore.GetEntries(UserSid));
        Assert.Null(db.GetAccount(UserSid));
    }

    [Fact]
    public void EnsureAccess_SaveFailsForSpecificContainer_DoesNotRemoveManualSharedTraverseEntry()
    {
        var (ensurer, _, db, mainStore, aclPermission, _, _, _) = Build();
        db.GetOrCreateAccount(AclHelper.AllApplicationPackagesSid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true,
            SourceSids = null
        });
        db.GetOrCreateAccount(AclHelper.AllApplicationPackagesSid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true,
            SourceSids = [OtherContainerSid]
        });
        aclPermission.Setup(p => p.NeedsPermissionGrant(
                ExistingDir,
                AclHelper.AllApplicationPackagesSid,
                It.IsAny<FileSystemRights>(),
                It.IsAny<bool>()))
            .Returns(true);
        aclPermission.Setup(p => p.NeedsPermissionGrant(
                ExistingDir,
                ContainerSid,
                It.IsAny<FileSystemRights>(),
                It.IsAny<bool>()))
            .Returns(true);
        mainStore.SaveAction = () => throw new InvalidOperationException("save failed");

        Assert.Throws<GrantOperationException>(() => ensurer.EnsureAccess(
            ContainerSid,
            ExistingDir,
            ReadOnly));

        var sharedTraverses = db.GetAccount(AclHelper.AllApplicationPackagesSid)!.Grants;
        Assert.Equal(2, sharedTraverses.Count);
        Assert.Contains(sharedTraverses, entry => entry.IsTraverseOnly && entry.SourceSids == null);
        Assert.Contains(sharedTraverses, entry =>
            entry.IsTraverseOnly &&
            entry.SourceSids?.SequenceEqual([OtherContainerSid]) == true);
    }

    [Fact]
    public void EnsureAccess_SystemFailure_ThrowsAfterRollback()
    {
        var (ensurer, _, db, mainStore, aclPermission, _, _, _) = Build();
        var permissionChecks = 0;
        aclPermission.Setup(p => p.NeedsPermissionGrant(
                ExistingDir,
                UserSid,
                It.IsAny<FileSystemRights>(),
                It.IsAny<bool>()))
            .Returns(() =>
            {
                permissionChecks++;
                return true;
            });

        var saveCalled = false;
        mainStore.SaveAction = () => saveCalled = true;
        var ex = Assert.Throws<GrantOperationException>(() => ensurer.EnsureAccess(
            UserSid,
            ExistingDir,
            ReadOnly));

        Assert.True(saveCalled);
        Assert.Equal(GrantApplyFailureStep.TargetEffectiveAccessValidation, ex.Step);
        Assert.Null(FindGrant(db, UserSid, ExistingDir, isDeny: false));
        Assert.True(permissionChecks >= 2);
    }

    [Fact]
    public void EnsureAccess_SpecificContainerWithInteractiveUser_ContainerOnlyMutation_PersistsContainerOwnership()
    {
        var (ensurer, _, db, mainStore, aclPermission, _, explicitAceAccessor, traverseAcl) = Build(InteractiveSid);
        var containerGrantApplied = false;
        var sharedTraverseApplied = false;
        explicitAceAccessor
            .Setup(a => a.ApplyExplicitAce(
                It.IsAny<string>(),
                It.IsAny<string>(),
                AccessControlType.Allow,
                It.IsAny<FileSystemRights>(),
                It.IsAny<Func<FileSystemAccessRule, bool>?>()))
            .Callback<string, string, AccessControlType, FileSystemRights, Func<FileSystemAccessRule, bool>?>(
                (_, sid, _, _, _) =>
                {
                    if (string.Equals(sid, ContainerSid, StringComparison.OrdinalIgnoreCase))
                        containerGrantApplied = true;
                });
        traverseAcl
            .Setup(a => a.AddAllowAce(It.IsAny<string>(), It.IsAny<SecurityIdentifier>()))
            .Callback<string, SecurityIdentifier>((_, sid) =>
            {
                if (string.Equals(sid.Value, AclHelper.AllApplicationPackagesSid, StringComparison.OrdinalIgnoreCase))
                    sharedTraverseApplied = true;
            });
        aclPermission.Setup(p => p.NeedsPermissionGrant(
                ExistingDir,
                AclHelper.AllApplicationPackagesSid,
                It.IsAny<FileSystemRights>(),
                It.IsAny<bool>()))
            .Returns(true);
        aclPermission.Setup(p => p.NeedsPermissionGrant(
                ExistingDir,
                ContainerSid,
                It.IsAny<FileSystemRights>(),
                It.IsAny<bool>()))
            .Returns(() => !containerGrantApplied);
        aclPermission.Setup(p => p.NeedsPermissionGrant(
                ExistingDir,
                InteractiveSid,
                It.IsAny<FileSystemRights>(),
                It.IsAny<bool>()))
            .Returns(false);
        aclPermission.Setup(p => p.HasEffectiveRights(
                It.IsAny<FileSystemSecurity>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                TraverseRightsHelper.TraverseRights))
            .Returns<FileSystemSecurity, string, IReadOnlyList<string>, FileSystemRights>((_, sid, _, _) =>
                sid switch
                {
                    var value when string.Equals(value, ContainerSid, StringComparison.OrdinalIgnoreCase) => false,
                    var value when string.Equals(value, AclHelper.AllApplicationPackagesSid, StringComparison.OrdinalIgnoreCase) => sharedTraverseApplied,
                    _ => true
                });

        var result = ensurer.EnsureAccess(ContainerSid, ExistingDir, ReadOnly);

        Assert.True(result.GrantApplied);
        Assert.True(result.TraverseApplied);
        Assert.True(result.DatabaseModified);
        Assert.True(result.DurableSaveCompleted);
        Assert.Null(FindGrant(db, InteractiveSid, ExistingDir, isDeny: false));
        Assert.Null(mainStore.GetEntries(InteractiveSid).FirstOrDefault(entry => !entry.IsTraverseOnly));
        Assert.Equal(ReadOnly, FindGrant(db, ContainerSid, ExistingDir, isDeny: false)?.SavedRights);
        Assert.Null(FindGrant(db, ContainerSid, ExistingDir, isDeny: false)?.SourceSids);
        var sharedTraverse = Assert.Single(db.GetAccount(AclHelper.AllApplicationPackagesSid)!.Grants);
        Assert.Contains(ContainerSid, sharedTraverse.SourceSids ?? []);
        Assert.Contains(mainStore.GetEntries(ContainerSid),
            entry => !entry.IsTraverseOnly &&
                     string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase) &&
                     entry.SourceSids == null);
        Assert.Contains(mainStore.GetEntries(AclHelper.AllApplicationPackagesSid),
            entry => entry.IsTraverseOnly &&
                     string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase) &&
                     (entry.SourceSids?.Contains(ContainerSid, StringComparer.OrdinalIgnoreCase) ?? false));
        explicitAceAccessor.Verify(a => a.ApplyExplicitAce(
            ExistingDir,
            ContainerSid,
            AccessControlType.Allow,
            It.IsAny<FileSystemRights>(),
            It.IsAny<Func<FileSystemAccessRule, bool>?>()), Times.Once);
        explicitAceAccessor.Verify(a => a.ApplyExplicitAce(
            ExistingDir,
            InteractiveSid,
            AccessControlType.Allow,
            It.IsAny<FileSystemRights>(),
            It.IsAny<Func<FileSystemAccessRule, bool>?>()), Times.Never);
        traverseAcl.Verify(a => a.AddAllowAce(
            It.IsAny<string>(),
            It.Is<SecurityIdentifier>(sid => sid.Value == AclHelper.AllApplicationPackagesSid)), Times.AtLeastOnce);
        traverseAcl.Verify(a => a.AddAllowAce(
            It.IsAny<string>(),
            It.Is<SecurityIdentifier>(sid => sid.Value == InteractiveSid)), Times.Never);
    }

    [Fact]
    public void EnsureAccess_SpecificContainerWithInteractiveUser_InteractiveOnlyMutation_TracksContainerSource()
    {
        var (ensurer, _, db, mainStore, aclPermission, _, explicitAceAccessor, traverseAcl) = Build(InteractiveSid);
        var interactiveGrantApplied = false;
        var interactiveTraverseApplied = false;
        explicitAceAccessor
            .Setup(a => a.ApplyExplicitAce(
                It.IsAny<string>(),
                It.IsAny<string>(),
                AccessControlType.Allow,
                It.IsAny<FileSystemRights>(),
                It.IsAny<Func<FileSystemAccessRule, bool>?>()))
            .Callback<string, string, AccessControlType, FileSystemRights, Func<FileSystemAccessRule, bool>?>(
                (_, sid, _, _, _) =>
                {
                    if (string.Equals(sid, InteractiveSid, StringComparison.OrdinalIgnoreCase))
                        interactiveGrantApplied = true;
                });
        traverseAcl
            .Setup(a => a.AddAllowAce(It.IsAny<string>(), It.IsAny<SecurityIdentifier>()))
            .Callback<string, SecurityIdentifier>((_, sid) =>
            {
                if (string.Equals(sid.Value, InteractiveSid, StringComparison.OrdinalIgnoreCase))
                    interactiveTraverseApplied = true;
            });
        aclPermission.Setup(p => p.NeedsPermissionGrant(
                ExistingDir,
                AclHelper.AllApplicationPackagesSid,
                It.IsAny<FileSystemRights>(),
                It.IsAny<bool>()))
            .Returns(false);
        aclPermission.Setup(p => p.NeedsPermissionGrant(
                ExistingDir,
                InteractiveSid,
                It.IsAny<FileSystemRights>(),
                It.IsAny<bool>()))
            .Returns(() => !interactiveGrantApplied);
        aclPermission.Setup(p => p.HasEffectiveRights(
                It.IsAny<FileSystemSecurity>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                TraverseRightsHelper.TraverseRights))
            .Returns<FileSystemSecurity, string, IReadOnlyList<string>, FileSystemRights>((_, sid, _, _) =>
                string.Equals(sid, InteractiveSid, StringComparison.OrdinalIgnoreCase)
                    ? interactiveTraverseApplied
                    : true);

        var result = ensurer.EnsureAccess(ContainerSid, ExistingDir, ReadOnly);

        Assert.True(result.GrantApplied);
        Assert.True(result.TraverseApplied);
        Assert.True(result.DatabaseModified);
        Assert.True(result.DurableSaveCompleted);
        Assert.Null(FindGrant(db, ContainerSid, ExistingDir, isDeny: false));
        Assert.Empty(db.GetAccount(AclHelper.AllApplicationPackagesSid)?.Grants ?? []);
        Assert.Equal(ReadOnly, FindGrant(db, InteractiveSid, ExistingDir, isDeny: false)?.SavedRights);
        Assert.Contains(ContainerSid, FindGrant(db, InteractiveSid, ExistingDir, isDeny: false)?.SourceSids ?? []);
        Assert.Contains(db.GetAccount(InteractiveSid)?.Grants ?? [],
            entry => entry.IsTraverseOnly &&
                     string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase) &&
                     (entry.SourceSids?.Contains(ContainerSid, StringComparer.OrdinalIgnoreCase) ?? false));
        Assert.Empty(mainStore.GetEntries(ContainerSid));
        Assert.Empty(mainStore.GetEntries(AclHelper.AllApplicationPackagesSid));
        Assert.Contains(mainStore.GetEntries(InteractiveSid),
            entry => !entry.IsTraverseOnly &&
                     string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase) &&
                     (entry.SourceSids?.Contains(ContainerSid, StringComparer.OrdinalIgnoreCase) ?? false));
        Assert.Contains(mainStore.GetEntries(InteractiveSid),
            entry => entry.IsTraverseOnly &&
                     string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase) &&
                     (entry.SourceSids?.Contains(ContainerSid, StringComparer.OrdinalIgnoreCase) ?? false));
        explicitAceAccessor.Verify(a => a.ApplyExplicitAce(
            ExistingDir,
            ContainerSid,
            AccessControlType.Allow,
            It.IsAny<FileSystemRights>(),
            It.IsAny<Func<FileSystemAccessRule, bool>?>()), Times.Never);
        explicitAceAccessor.Verify(a => a.ApplyExplicitAce(
            ExistingDir,
            InteractiveSid,
            AccessControlType.Allow,
            It.IsAny<FileSystemRights>(),
            It.IsAny<Func<FileSystemAccessRule, bool>?>()), Times.Once);
        traverseAcl.Verify(a => a.AddAllowAce(
            It.IsAny<string>(),
            It.Is<SecurityIdentifier>(sid => sid.Value == AclHelper.AllApplicationPackagesSid)), Times.Never);
        traverseAcl.Verify(a => a.AddAllowAce(
            It.IsAny<string>(),
            It.Is<SecurityIdentifier>(sid => sid.Value == InteractiveSid)), Times.AtLeastOnce);
    }

    [Fact]
    public void EnsureAccess_SpecificContainerWithInteractiveUser_CombinedMutation_PreservesPerIdentityIntent()
    {
        var (ensurer, _, db, mainStore, aclPermission, _, explicitAceAccessor, traverseAcl) = Build(InteractiveSid);
        var containerGrantApplied = false;
        var interactiveGrantApplied = false;
        var sharedTraverseApplied = false;
        var interactiveTraverseApplied = false;
        explicitAceAccessor
            .Setup(a => a.ApplyExplicitAce(
                It.IsAny<string>(),
                It.IsAny<string>(),
                AccessControlType.Allow,
                It.IsAny<FileSystemRights>(),
                It.IsAny<Func<FileSystemAccessRule, bool>?>()))
            .Callback<string, string, AccessControlType, FileSystemRights, Func<FileSystemAccessRule, bool>?>(
                (_, sid, _, _, _) =>
                {
                    if (string.Equals(sid, ContainerSid, StringComparison.OrdinalIgnoreCase))
                        containerGrantApplied = true;
                    if (string.Equals(sid, InteractiveSid, StringComparison.OrdinalIgnoreCase))
                        interactiveGrantApplied = true;
                });
        traverseAcl
            .Setup(a => a.AddAllowAce(It.IsAny<string>(), It.IsAny<SecurityIdentifier>()))
            .Callback<string, SecurityIdentifier>((_, sid) =>
            {
                if (string.Equals(sid.Value, AclHelper.AllApplicationPackagesSid, StringComparison.OrdinalIgnoreCase))
                    sharedTraverseApplied = true;
                if (string.Equals(sid.Value, InteractiveSid, StringComparison.OrdinalIgnoreCase))
                    interactiveTraverseApplied = true;
            });
        aclPermission.Setup(p => p.NeedsPermissionGrant(
                ExistingDir,
                AclHelper.AllApplicationPackagesSid,
                It.IsAny<FileSystemRights>(),
                It.IsAny<bool>()))
            .Returns(true);
        aclPermission.Setup(p => p.NeedsPermissionGrant(
                ExistingDir,
                ContainerSid,
                It.IsAny<FileSystemRights>(),
                It.IsAny<bool>()))
            .Returns(() => !containerGrantApplied);
        aclPermission.Setup(p => p.NeedsPermissionGrant(
                ExistingDir,
                InteractiveSid,
                It.IsAny<FileSystemRights>(),
                It.IsAny<bool>()))
            .Returns(() => !interactiveGrantApplied);
        aclPermission.Setup(p => p.HasEffectiveRights(
                It.IsAny<FileSystemSecurity>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                TraverseRightsHelper.TraverseRights))
            .Returns<FileSystemSecurity, string, IReadOnlyList<string>, FileSystemRights>((_, sid, _, _) =>
                sid switch
                {
                    var value when string.Equals(value, ContainerSid, StringComparison.OrdinalIgnoreCase) => false,
                    var value when string.Equals(value, AclHelper.AllApplicationPackagesSid, StringComparison.OrdinalIgnoreCase) => sharedTraverseApplied,
                    var value when string.Equals(value, InteractiveSid, StringComparison.OrdinalIgnoreCase) => interactiveTraverseApplied,
                    _ => true
                });

        var result = ensurer.EnsureAccess(ContainerSid, ExistingDir, ReadOnly);

        Assert.True(result.GrantApplied);
        Assert.True(result.TraverseApplied);
        Assert.True(result.DatabaseModified);
        Assert.True(result.DurableSaveCompleted);
        var containerGrant = FindGrant(db, ContainerSid, ExistingDir, isDeny: false);
        var interactiveGrant = FindGrant(db, InteractiveSid, ExistingDir, isDeny: false);
        Assert.NotNull(containerGrant);
        Assert.NotNull(interactiveGrant);
        Assert.Null(containerGrant!.SourceSids);
        Assert.Contains(ContainerSid, interactiveGrant!.SourceSids ?? []);
        var sharedTraverse = Assert.Single(db.GetAccount(AclHelper.AllApplicationPackagesSid)!.Grants);
        Assert.Contains(ContainerSid, sharedTraverse.SourceSids ?? []);
        Assert.Contains(db.GetAccount(InteractiveSid)?.Grants ?? [],
            entry => entry.IsTraverseOnly &&
                     string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase) &&
                     (entry.SourceSids?.Contains(ContainerSid, StringComparer.OrdinalIgnoreCase) ?? false));
        Assert.Contains(mainStore.GetEntries(ContainerSid),
            entry => !entry.IsTraverseOnly &&
                     string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase) &&
                     entry.SourceSids == null);
        Assert.Contains(mainStore.GetEntries(InteractiveSid),
            entry => !entry.IsTraverseOnly &&
                     string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase) &&
                     (entry.SourceSids?.Contains(ContainerSid, StringComparer.OrdinalIgnoreCase) ?? false));
        Assert.Contains(mainStore.GetEntries(AclHelper.AllApplicationPackagesSid),
            entry => entry.IsTraverseOnly &&
                     string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase) &&
                     (entry.SourceSids?.Contains(ContainerSid, StringComparer.OrdinalIgnoreCase) ?? false));
        Assert.Contains(mainStore.GetEntries(InteractiveSid),
            entry => entry.IsTraverseOnly &&
                     string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase) &&
                     (entry.SourceSids?.Contains(ContainerSid, StringComparer.OrdinalIgnoreCase) ?? false));
        explicitAceAccessor.Verify(a => a.ApplyExplicitAce(
            ExistingDir,
            ContainerSid,
            AccessControlType.Allow,
            It.IsAny<FileSystemRights>(),
            It.IsAny<Func<FileSystemAccessRule, bool>?>()), Times.Once);
        explicitAceAccessor.Verify(a => a.ApplyExplicitAce(
            ExistingDir,
            InteractiveSid,
            AccessControlType.Allow,
            It.IsAny<FileSystemRights>(),
            It.IsAny<Func<FileSystemAccessRule, bool>?>()), Times.Once);
        traverseAcl.Verify(a => a.AddAllowAce(
            It.IsAny<string>(),
            It.Is<SecurityIdentifier>(sid => sid.Value == AclHelper.AllApplicationPackagesSid)), Times.AtLeastOnce);
        traverseAcl.Verify(a => a.AddAllowAce(
            It.IsAny<string>(),
            It.Is<SecurityIdentifier>(sid => sid.Value == InteractiveSid)), Times.AtLeastOnce);
    }

    [Fact]
    public void EnsureAccess_InaccessibleDirectory_UsesAclAccessorFolderStateForAceWrite()
    {
        const string protectedDir = @"C:\DeniedButReachable\Folder";
        var (ensurer, _, db, _, aclPermission, fileSecurityAccessor, explicitAceAccessor, _) = Build();
        bool isFolder = true;
        fileSecurityAccessor.Setup(a => a.PathExists(protectedDir, out isFolder)).Returns(true);
        aclPermission.SetupSequence(p => p.NeedsPermissionGrant(
                protectedDir,
                UserSid,
                It.IsAny<FileSystemRights>(),
                It.IsAny<bool>()))
            .Returns(true)
            .Returns(false);

        var result = ensurer.EnsureAccess(UserSid, protectedDir, ReadOnly);

        Assert.True(result.GrantApplied);
        explicitAceAccessor.Verify(a => a.ApplyExplicitAce(
            protectedDir,
            UserSid,
            AccessControlType.Allow,
            GrantRightsMapper.MapAllowRights(ReadOnly, isFolder: true),
            It.IsAny<Func<FileSystemAccessRule, bool>?>()), Times.Once);
        Assert.Contains(db.GetAccount(UserSid)!.Grants,
            e => e is { IsTraverseOnly: false, IsDeny: false } &&
                 string.Equals(e.Path, protectedDir, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EnsureTemporaryAccess_TargetGrantNeeded_AppliesAceWithoutTrackingTargetIntent()
    {
        var (ensurer, _, db, _, aclPermission, _, explicitAceAccessor, _) = Build();
        aclPermission.SetupSequence(p => p.NeedsPermissionGrant(
                ExistingDir,
                UserSid,
                It.IsAny<FileSystemRights>(),
                It.IsAny<bool>()))
            .Returns(true)
            .Returns(false);

        var result = ensurer.EnsureTemporaryAccess(UserSid, ExistingDir, ReadOnly, confirm: null);

        Assert.True(result.GrantApplied);
        Assert.False(result.DatabaseModified);
        Assert.False(result.DurableSaveCompleted);
        Assert.Null(db.GetAccount(UserSid));
        explicitAceAccessor.Verify(a => a.ApplyExplicitAce(
            ExistingDir,
            UserSid,
            AccessControlType.Allow,
            It.IsAny<FileSystemRights>(),
            It.IsAny<Func<FileSystemAccessRule, bool>?>()), Times.Once);
    }

    [Fact]
    public void EnsureTemporaryAccess_FolderExecuteRequest_ThrowsWithoutPersistingTrackedGrantExpansion()
    {
        var (ensurer, operations, db, _, aclPermission, _, explicitAceAccessor, _) = Build();
        operations.AddGrant(UserSid, ExistingDir, isDeny: false, ReadOnly);
        aclPermission.SetupSequence(p => p.NeedsPermissionGrant(
                ExistingDir,
                UserSid,
                It.IsAny<FileSystemRights>(),
                It.IsAny<bool>()))
            .Returns(true)
            .Returns(false);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ensurer.EnsureTemporaryAccess(
                UserSid,
                ExistingDir,
                new SavedRightsState(Execute: true, Write: false, Read: true, Special: false, Own: false),
                confirm: null));

        Assert.Contains("Use EnsureAccess", exception.Message, StringComparison.Ordinal);
        var grant = db.GetAccount(UserSid)?.Grants.Single(entry =>
            !entry.IsTraverseOnly &&
            !entry.IsDeny &&
            string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(ReadOnly, grant?.SavedRights);
        explicitAceAccessor.Verify(a => a.ApplyExplicitAce(
            ExistingDir,
            UserSid,
            AccessControlType.Allow,
            It.IsAny<FileSystemRights>(),
            It.IsAny<Func<FileSystemAccessRule, bool>?>()), Times.Once);
    }

    [Fact]
    public void EnsureTemporaryAccess_TrackedGrantNarrowerButAccessAlreadySufficient_RepairsTrackedAclWithoutPersisting()
    {
        var (ensurer, operations, db, _, aclPermission, _, explicitAceAccessor, _) = Build();
        operations.AddGrant(UserSid, ExistingDir, isDeny: false, ReadOnly);
        aclPermission.Setup(p => p.NeedsPermissionGrant(
                ExistingDir,
                UserSid,
                It.IsAny<FileSystemRights>(),
                It.IsAny<bool>()))
            .Returns(false);

        var result = ensurer.EnsureTemporaryAccess(
            UserSid,
            ExistingDir,
            new SavedRightsState(Execute: true, Write: false, Read: true, Special: false, Own: false),
            confirm: null);

        Assert.True(result.GrantApplied);
        Assert.False(result.DatabaseModified);
        Assert.Equal(ReadOnly, db.GetAccount(UserSid)?.Grants.Single(entry =>
            !entry.IsTraverseOnly &&
            !entry.IsDeny &&
            string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase)).SavedRights);
        explicitAceAccessor.Verify(a => a.ApplyExplicitAce(
            ExistingDir,
            UserSid,
            AccessControlType.Allow,
            It.IsAny<FileSystemRights>(),
            It.IsAny<Func<FileSystemAccessRule, bool>?>()), Times.Exactly(2));
    }

    [Fact]
    public void EnsureAccess_Unelevated_PropagatesUnelevatedToPermissionChecks()
    {
        var (ensurer, _, db, _, aclPermission, _, explicitAceAccessor, _) = Build();
        aclPermission.SetupSequence(p => p.NeedsPermissionGrant(
                ExistingDir,
                UserSid,
                It.IsAny<FileSystemRights>(),
                true))
            .Returns(true)
            .Returns(false);

        var result = ensurer.EnsureAccess(
            UserSid,
            ExistingDir,
            ReadOnly,
            confirm: null,
            unelevated: true);

        Assert.True(result.GrantApplied);
        Assert.True(result.DatabaseModified);
        Assert.Contains(db.GetAccount(UserSid)!.Grants,
            entry => !entry.IsTraverseOnly &&
                     !entry.IsDeny &&
                     string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase));
        aclPermission.Verify(p => p.NeedsPermissionGrant(
            ExistingDir,
            UserSid,
            It.IsAny<FileSystemRights>(),
            true), Times.AtLeast(2));
        explicitAceAccessor.Verify(a => a.ApplyExplicitAce(
            ExistingDir,
            UserSid,
            AccessControlType.Allow,
            It.IsAny<FileSystemRights>(),
            It.IsAny<Func<FileSystemAccessRule, bool>?>()), Times.Once);
    }

    [Fact]
    public void EnsureAccess_HighestAllowedTraverseCheck_KeepsAdministratorsGroup()
    {
        var (ensurer, _, _, _, aclPermission, _, explicitAceAccessor, _) = Build();
        aclPermission.SetupSequence(p => p.NeedsPermissionGrant(
                ExistingDir,
                UserSid,
                It.IsAny<FileSystemRights>(),
                false))
            .Returns(true)
            .Returns(false);
        aclPermission.Setup(p => p.ResolveAccountGroupSids(UserSid))
            .Returns(["S-1-1-0", "S-1-5-11", AclComputeHelper.AdministratorsSid.Value]);
        aclPermission.Setup(p => p.HasEffectiveRights(
                It.IsAny<FileSystemSecurity>(),
                UserSid,
                It.IsAny<IReadOnlyList<string>>(),
                TraverseRightsHelper.TraverseRights))
            .Returns<FileSystemSecurity, string, IReadOnlyList<string>, FileSystemRights>((_, _, groups, _) =>
                groups.Contains(AclComputeHelper.AdministratorsSid.Value, StringComparer.OrdinalIgnoreCase));

        var result = ensurer.EnsureAccess(
            UserSid,
            ExistingDir,
            ReadOnly,
            confirm: null,
            unelevated: false);

        Assert.True(result.GrantApplied);
        aclPermission.Verify(p => p.HasEffectiveRights(
            It.IsAny<FileSystemSecurity>(),
            UserSid,
            It.Is<IReadOnlyList<string>>(groups => groups.Contains(
                AclComputeHelper.AdministratorsSid.Value,
                StringComparer.OrdinalIgnoreCase)),
            TraverseRightsHelper.TraverseRights), Times.AtLeastOnce);
        explicitAceAccessor.Verify(a => a.ApplyExplicitAce(
            ExistingDir,
            UserSid,
            AccessControlType.Allow,
            It.IsAny<FileSystemRights>(),
            It.IsAny<Func<FileSystemAccessRule, bool>?>()), Times.Once);
    }

    [Fact]
    public void EnsureAccess_TargetGrantRequiredAndTraverseAlreadyEffective_TracksTraverseWithoutApplyingTraverseAce()
    {
        var (ensurer, _, db, mainStore, aclPermission, _, explicitAceAccessor, traverseAcl) = Build();
        aclPermission.SetupSequence(p => p.NeedsPermissionGrant(
                ExistingDir,
                UserSid,
                It.IsAny<FileSystemRights>(),
                true))
            .Returns(true)
            .Returns(false);
        aclPermission.Setup(p => p.HasEffectiveRights(
                It.IsAny<FileSystemSecurity>(),
                UserSid,
                It.IsAny<IReadOnlyList<string>>(),
                TraverseRightsHelper.TraverseRights))
            .Returns(true);

        var result = ensurer.EnsureAccess(
            UserSid,
            ExistingDir,
            ReadOnly,
            confirm: null,
            unelevated: true);

        Assert.True(result.GrantApplied);
        Assert.False(result.TraverseApplied);
        Assert.True(result.DatabaseModified);
        Assert.Contains(db.GetAccount(UserSid)!.Grants,
            entry => entry.IsTraverseOnly &&
                     string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(mainStore.GetEntries(UserSid),
            entry => entry.IsTraverseOnly &&
                     string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase));
        traverseAcl.Verify(a => a.AddAllowAce(It.IsAny<string>(), It.IsAny<SecurityIdentifier>()), Times.Never);
        explicitAceAccessor.Verify(a => a.ApplyExplicitAce(
            ExistingDir,
            UserSid,
            AccessControlType.Allow,
            It.IsAny<FileSystemRights>(),
            It.IsAny<Func<FileSystemAccessRule, bool>?>()), Times.Once);
    }

    [Fact]
    public void EnsureAccess_TraverseMissingWithoutTargetGrant_AddsTraverseAceAndTracksTraverse()
    {
        var (ensurer, _, db, mainStore, aclPermission, _, explicitAceAccessor, traverseAcl) = Build();
        var traverseApplied = false;
        aclPermission.Setup(p => p.NeedsPermissionGrant(
                ExistingDir,
                UserSid,
                It.IsAny<FileSystemRights>(),
                true))
            .Returns(false);
        aclPermission.Setup(p => p.HasEffectiveRights(
                It.IsAny<FileSystemSecurity>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                TraverseRightsHelper.TraverseRights))
            .Returns(() => traverseApplied);
        traverseAcl.Setup(a => a.AddAllowAce(It.IsAny<string>(), It.IsAny<SecurityIdentifier>()))
            .Callback(() => traverseApplied = true);

        var result = ensurer.EnsureAccess(
            UserSid,
            ExistingDir,
            ReadOnly,
            confirm: null,
            unelevated: true);

        Assert.False(result.GrantApplied);
        Assert.True(result.TraverseApplied);
        Assert.True(result.DatabaseModified);
        Assert.Contains(db.GetAccount(UserSid)!.Grants,
            entry => entry.IsTraverseOnly &&
                     string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(mainStore.GetEntries(UserSid),
            entry => entry.IsTraverseOnly &&
                     string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase));
        traverseAcl.Verify(a => a.AddAllowAce(It.IsAny<string>(), It.IsAny<SecurityIdentifier>()), Times.AtLeastOnce);
        explicitAceAccessor.Verify(a => a.ApplyExplicitAce(
            ExistingDir,
            UserSid,
            AccessControlType.Allow,
            It.IsAny<FileSystemRights>(),
            It.IsAny<Func<FileSystemAccessRule, bool>?>()), Times.Never);
    }

    private static GrantedPathEntry? FindGrant(AppDatabase database, string sid, string path, bool isDeny)
        => database.GetAccount(sid)?.Grants.FirstOrDefault(entry =>
            entry is { IsTraverseOnly: false } &&
            entry.IsDeny == isDeny &&
            string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase));

    private sealed class InterceptingGrantIntentStoreSaveService(Action<IEnumerable<IGrantIntentStore>, GrantApplyFailureStep, string> onSave)
        : IGrantIntentStoreSaveService
    {
        private readonly GrantIntentStoreSaveService inner = new();

        public void Save(
            IEnumerable<IGrantIntentStore> stores,
            GrantApplyFailureStep failureStep,
            string normalizedPath)
        {
            onSave(stores, failureStep, normalizedPath);
            inner.Save(stores, failureStep, normalizedPath);
        }

        public IReadOnlyList<GrantApplyWarning> SaveWithWarnings(
            IEnumerable<IGrantIntentStore> stores,
            GrantApplyFailureStep failureStep,
            string normalizedPath)
            => inner.SaveWithWarnings(stores, failureStep, normalizedPath);

        public string? GetPrimaryConfigPath(IEnumerable<IGrantIntentStore> stores)
            => inner.GetPrimaryConfigPath(stores);
    }
}
