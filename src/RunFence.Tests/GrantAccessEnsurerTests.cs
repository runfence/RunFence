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
        Mock<IAclAccessor> AclAccessor,
        Mock<ITraverseAcl> TraverseAcl) Build(string? interactiveSid = null)
    {
        var log = new Mock<ILoggingService>();
        var aclAccessor = new Mock<IAclAccessor>();
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
        aclAccessor.Setup(a => a.GetSecurity(ExistingDir)).Returns(new DirectorySecurity());

        var db = new AppDatabase();
        var mainGrantStore = new TestGrantIntentStore();
        var storeProvider = new TestGrantIntentStoreProvider(mainGrantStore);
        var repository = new GrantIntentRepository(storeProvider);
        var traverseGrantOwnerResolver = new TraverseGrantOwnerResolver();
        var dbAccessor = new UiThreadDatabaseAccessor(new LambdaDatabaseProvider(() => db), () => SyncInvoker);
        var grantAceService = new GrantAceService(aclAccessor.Object, pathInfo);
        var grantCore = new GrantCoreOperations(grantAceService, ownerMock.Object, dbAccessor, log.Object, pathInfo);
        var ancestorGranter = new AncestorTraverseGranter(log.Object, aclPermission.Object, traverseAcl.Object, pathInfo);
        var traverseCore = new TraverseCoreOperations(traverseAcl.Object, ancestorGranter, aclPermission.Object, dbAccessor, log.Object, pathInfo, traverseGrantOwnerResolver);
        var operations = new GrantFileSystemOperations(grantCore, grantAceService, ownerMock.Object,
            mandatoryLabelMock.Object, dbAccessor);
        var ensurer = new GrantAccessEnsurer(
            aclPermission.Object,
            dbAccessor,
            aclAccessor.Object,
            pathInfo,
            traverseCore,
            operations,
            iuResolver.Object,
            traverseGrantOwnerResolver,
            () => repository,
            () => mainGrantStore,
            new GrantIntentStoreSaveService());
        return (ensurer, operations, db, mainGrantStore, aclPermission, aclAccessor, traverseAcl);
    }

    [Fact]
    public void EnsureAccess_DenyConflictWithoutConfirm_Throws()
    {
        var (ensurer, operations, _, _, _, _, _) = Build();
        operations.AddGrant(UserSid, ExistingDir, isDeny: true, DenyReadExecute);

        Assert.Throws<InvalidOperationException>(() =>
            ensurer.EnsureAccess(UserSid, ExistingDir, ReadOnly, confirm: null));
    }

    [Fact]
    public void EnsureAccess_DenyConflictWithConfirm_PartiallyReducesDeny()
    {
        var (ensurer, operations, db, _, _, _, _) = Build();
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
        var (ensurer, operations, db, mainStore, _, _, _) = Build();
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
    public void EnsureAccess_SpecificContainerWhenSharedAccessAlreadySufficient_DoesNothing()
    {
        var (ensurer, _, db, _, aclPermission, _, _) = Build();
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
        var (ensurer, _, db, mainStore, aclPermission, _, _) = Build();
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
    public void EnsureAccess_SaveFailsForSpecificContainer_DoesNotRemoveManualSharedTraverseEntry()
    {
        var (ensurer, _, db, mainStore, aclPermission, _, _) = Build();
        db.GetOrCreateAccount(AclHelper.AllApplicationPackagesSid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true,
            SourceSids = null
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

        var sharedTraverse = Assert.Single(db.GetAccount(AclHelper.AllApplicationPackagesSid)!.Grants);
        Assert.True(sharedTraverse.IsTraverseOnly);
        Assert.Null(sharedTraverse.SourceSids);
    }

    [Fact]
    public void EnsureAccess_SystemFailure_ThrowsAfterRollback()
    {
        var (ensurer, _, db, mainStore, aclPermission, _, _) = Build();
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
        var (ensurer, _, db, mainStore, aclPermission, aclAccessor, traverseAcl) = Build(InteractiveSid);
        var containerGrantApplied = false;
        var sharedTraverseApplied = false;
        aclAccessor
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
        aclAccessor.Verify(a => a.ApplyExplicitAce(
            ExistingDir,
            ContainerSid,
            AccessControlType.Allow,
            It.IsAny<FileSystemRights>(),
            It.IsAny<Func<FileSystemAccessRule, bool>?>()), Times.Once);
        aclAccessor.Verify(a => a.ApplyExplicitAce(
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
        var (ensurer, _, db, mainStore, aclPermission, aclAccessor, traverseAcl) = Build(InteractiveSid);
        var interactiveGrantApplied = false;
        var interactiveTraverseApplied = false;
        aclAccessor
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
        aclAccessor.Verify(a => a.ApplyExplicitAce(
            ExistingDir,
            ContainerSid,
            AccessControlType.Allow,
            It.IsAny<FileSystemRights>(),
            It.IsAny<Func<FileSystemAccessRule, bool>?>()), Times.Never);
        aclAccessor.Verify(a => a.ApplyExplicitAce(
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
        var (ensurer, _, db, mainStore, aclPermission, aclAccessor, traverseAcl) = Build(InteractiveSid);
        var containerGrantApplied = false;
        var interactiveGrantApplied = false;
        var sharedTraverseApplied = false;
        var interactiveTraverseApplied = false;
        aclAccessor
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
        aclAccessor.Verify(a => a.ApplyExplicitAce(
            ExistingDir,
            ContainerSid,
            AccessControlType.Allow,
            It.IsAny<FileSystemRights>(),
            It.IsAny<Func<FileSystemAccessRule, bool>?>()), Times.Once);
        aclAccessor.Verify(a => a.ApplyExplicitAce(
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
        var (ensurer, _, db, _, aclPermission, aclAccessor, _) = Build();
        bool isFolder = true;
        aclAccessor.Setup(a => a.PathExists(protectedDir, out isFolder)).Returns(true);
        aclPermission.SetupSequence(p => p.NeedsPermissionGrant(
                protectedDir,
                UserSid,
                It.IsAny<FileSystemRights>(),
                It.IsAny<bool>()))
            .Returns(true)
            .Returns(false);

        var result = ensurer.EnsureAccess(UserSid, protectedDir, ReadOnly);

        Assert.True(result.GrantApplied);
        aclAccessor.Verify(a => a.ApplyExplicitAce(
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
        var (ensurer, _, db, _, aclPermission, aclAccessor, _) = Build();
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
        aclAccessor.Verify(a => a.ApplyExplicitAce(
            ExistingDir,
            UserSid,
            AccessControlType.Allow,
            It.IsAny<FileSystemRights>(),
            It.IsAny<Func<FileSystemAccessRule, bool>?>()), Times.Once);
    }

    [Fact]
    public void EnsureTemporaryAccess_FolderExecuteRequest_ThrowsWithoutPersistingTrackedGrantExpansion()
    {
        var (ensurer, operations, db, _, aclPermission, aclAccessor, _) = Build();
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
        aclAccessor.Verify(a => a.ApplyExplicitAce(
            ExistingDir,
            UserSid,
            AccessControlType.Allow,
            It.IsAny<FileSystemRights>(),
            It.IsAny<Func<FileSystemAccessRule, bool>?>()), Times.Once);
    }

    [Fact]
    public void EnsureTemporaryAccess_TrackedGrantNarrowerButAccessAlreadySufficient_RepairsTrackedAclWithoutPersisting()
    {
        var (ensurer, operations, db, _, aclPermission, aclAccessor, _) = Build();
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
        aclAccessor.Verify(a => a.ApplyExplicitAce(
            ExistingDir,
            UserSid,
            AccessControlType.Allow,
            It.IsAny<FileSystemRights>(),
            It.IsAny<Func<FileSystemAccessRule, bool>?>()), Times.Exactly(2));
    }

    [Fact]
    public void EnsureAccess_Unelevated_PropagatesUnelevatedToPermissionChecks()
    {
        var (ensurer, _, db, _, aclPermission, aclAccessor, _) = Build();
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
        aclAccessor.Verify(a => a.ApplyExplicitAce(
            ExistingDir,
            UserSid,
            AccessControlType.Allow,
            It.IsAny<FileSystemRights>(),
            It.IsAny<Func<FileSystemAccessRule, bool>?>()), Times.Once);
    }

    private static GrantedPathEntry? FindGrant(AppDatabase database, string sid, string path, bool isDeny)
        => database.GetAccount(sid)?.Grants.FirstOrDefault(entry =>
            entry is { IsTraverseOnly: false } &&
            entry.IsDeny == isDeny &&
            string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase));
}
