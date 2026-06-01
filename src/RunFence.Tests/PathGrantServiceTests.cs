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

/// <summary>
/// Tests for <see cref="PathGrantService"/> covering grant operations, traverse management,
/// container interactive-user sync, bulk operations, and utility methods.
/// NTFS reads/writes are prevented by mocking <see cref="IPathSecurityDescriptorAccessor"/>
/// and <see cref="IExplicitAceAccessor"/> (wrapped in a real <see cref="GrantAceService"/>),
/// <see cref="ITraverseAcl"/>, and <see cref="IAclPermissionService"/>.
/// <see cref="AncestorTraverseGranter"/> is used directly (not mocked) with a no-op
/// <see cref="ITraverseAcl"/> mock so no ACEs are written.
/// No real filesystem access is required — all paths used in tests are synthetic constants that
/// never exist on disk. For tests against real NTFS ACLs, see
/// <see cref="PathGrantServiceIntegrationTests"/>.
/// </summary>
public class PathGrantServiceTests
{
    private const string UserSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
    private const string ContainerSid = "S-1-15-2-99-1-2-3-4-5-6";
    private const string OtherContainerSid = "S-1-15-2-99-1-2-3-4-5-7";
    private const string InteractiveSid = "S-1-5-21-1234567890-1234567890-1234567890-1002";
    private const string TestPath = @"C:\TestFolder\SubDir";
    private const string ExistingDir = @"C:\Existing\TestDir";
    private static readonly string BuiltinAdministratorsSid =
        new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value;

    private readonly Mock<IPathSecurityDescriptorAccessor> _fileSecurityAccessor = new();
    private readonly Mock<IExplicitAceAccessor> _explicitAceAccessor = new();
    private readonly Mock<ITraverseAcl> _traverseAcl = new();
    private readonly Mock<IAclPermissionService> _aclPermission = new();
    private readonly Mock<IInteractiveUserResolver> _iuResolver = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly TestFileSystemPathInfo _pathInfo = new();

    private readonly AppDatabase _database = new();
    private readonly GrantServiceTestBundle _service;
    private readonly AncestorTraverseGranter _ancestorGranter;
    private readonly IGrantAceService _grantAceService;
    private readonly IFileOwnerService _fileOwnerService;
    private readonly IMandatoryLabelService _mandatoryLabelService;

    private static readonly IUiThreadInvoker SyncInvoker =
        new LambdaUiThreadInvoker(a => a(), a => a());

    private static Mock<IAclDenyModeService> CreateEmptyDenyModeService()
    {
        var mock = new Mock<IAclDenyModeService>();
        mock.Setup(service => service.GetDeniedRightsPerSid(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<AppEntry>>(),
                It.IsAny<bool>()))
            .Returns(new Dictionary<string, DeniedRights>(StringComparer.OrdinalIgnoreCase));
        return mock;
    }

    public PathGrantServiceTests()
    {
        _pathInfo.AddDirectory(Path.GetPathRoot(TestPath)!);
        _pathInfo.AddDirectory(ExistingDir);

        // HasExplicitTraverseAce=true is used by PromoteNearestAncestor (to detect existing ACEs on
        // ancestor paths) and by the fallback path in AncestorTraverseGranter (when groupSids is null).
        _traverseAcl.Setup(t => t.HasExplicitTraverseAce(It.IsAny<string>(),
                It.IsAny<SecurityIdentifier>()))
            .Returns(true);
        _traverseAcl.Setup(t => t.HasExplicitTraverseAceOrThrow(It.IsAny<string>(),
                It.IsAny<SecurityIdentifier>()))
            .Returns(true);

        // No group SIDs; NeedsPermissionGrant=true means grant is needed by default.
        _aclPermission.Setup(p => p.ResolveAccountGroupSids(It.IsAny<string>()))
            .Returns([]);
        _aclPermission.Setup(p => p.NeedsPermissionGrant(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<FileSystemRights>(), It.IsAny<bool>()))
            .Returns(true);
        // HasEffectiveRights=true signals "already covered" to AncestorTraverseGranter, so no
        // traverse ACEs are written to disk (AncestorTraverseGranter uses HasEffectiveRights when
        // groupSids is non-null, which is always the case since ResolveAccountGroupSids returns []).
        _aclPermission.Setup(p => p.HasEffectiveRights(
                It.IsAny<FileSystemSecurity>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<FileSystemRights>()))
            .Returns(true);

        _iuResolver.Setup(r => r.GetInteractiveUserSid()).Returns((string?)null);
        _fileSecurityAccessor.Setup(a => a.GetSecurity(It.IsAny<string>()))
            .Returns(() => CreateSecurityWithOwner(BuiltinAdministratorsSid));

        _grantAceService = new GrantAceService(_fileSecurityAccessor.Object, _explicitAceAccessor.Object, _pathInfo);
        _fileOwnerService = new FileOwnerService(_log.Object, _pathInfo, _fileSecurityAccessor.Object);
        _mandatoryLabelService = new MandatoryLabelService(_log.Object, _pathInfo);
        _ancestorGranter = new AncestorTraverseGranter(_log.Object, _aclPermission.Object,
            _traverseAcl.Object, _pathInfo);

        _service = BuildService(_database);
    }

    private static GrantIntentStoreMutationService CreateGrantIntentStoreMutationService(
        TraverseGrantStateService traverseGrantStateService,
        Func<IGrantIntentStoreProvider> storeProvider,
        Func<IGrantIntentRepository> repository,
        Func<IGrantIntentStore> mainGrantStore)
        => new(traverseGrantStateService, storeProvider, repository, mainGrantStore);

    private static GrantRuntimeMutationService CreateGrantRuntimeMutationService(
        ITraverseCoreOperations traverseCore,
        UiThreadDatabaseAccessor dbAccessor,
        ContainerInteractiveUserSync containerIuSync,
        LowIntegrityGrantSync lowIlSync,
        IMandatoryLabelService mandatoryLabelService,
        GrantFileSystemOperations fsOps,
        IGrantAceService grantAceService,
        IFileSystemPathInfo pathInfo,
        ITraverseGrantOwnerResolver traverseGrantOwnerResolver,
        TraverseGrantStateService traverseGrantStateService)
        => new(
            traverseCore,
            dbAccessor,
            containerIuSync,
            lowIlSync,
            mandatoryLabelService,
            fsOps,
            grantAceService,
            pathInfo,
            traverseGrantStateService);

    private PersistedGrantMutationWorkflow CreatePersistedGrantMutationWorkflow(
        ITraverseCoreOperations traverseCore,
        UiThreadDatabaseAccessor dbAccessor,
        IMandatoryLabelService mandatoryLabelService,
        GrantFileSystemOperations fsOps,
        IGrantAceService grantAceService,
        IPathSecurityDescriptorAccessor aclAccessor,
        ITraverseIntentStoreCoordinator traverseIntentStoreCoordinator,
        ITraverseGrantOwnerResolver traverseGrantOwnerResolver,
        TraverseGrantStateService traverseGrantStateService,
        Func<IGrantIntentStoreProvider> storeProvider,
        GrantIntentStoreMutationService grantIntentStoreMutationService,
        GrantRuntimeMutationService grantRuntimeMutationService,
        IGrantIntentStoreSaveService grantIntentStoreSaveService)
    {
        var grantRuntimeSnapshotService = new GrantRuntimeSnapshotService(dbAccessor, traverseGrantOwnerResolver);
        var grantIntentMutationStateRestorer = new GrantIntentMutationStateRestorer(grantIntentStoreSaveService);
        var grantAclRollbackService = new GrantAclRollbackService(
            traverseCore,
            fsOps,
            aclAccessor,
            grantRuntimeMutationService,
            grantRuntimeSnapshotService,
            traverseGrantStateService);
        var additiveGrantCompensationService = new AdditiveGrantCompensationService(
            aclAccessor,
            _pathInfo,
            storeProvider,
            grantIntentStoreMutationService,
            grantRuntimeSnapshotService,
            grantIntentStoreSaveService,
            grantAclRollbackService);

        return new PersistedGrantMutationWorkflow(
            traverseCore,
            mandatoryLabelService,
            fsOps,
            grantAceService,
            _pathInfo,
            traverseIntentStoreCoordinator,
            traverseGrantStateService,
            storeProvider,
            new GrantMutationOrderResolver(),
            grantIntentMutationStateRestorer,
            grantAclRollbackService,
            additiveGrantCompensationService,
            grantIntentStoreMutationService,
            grantRuntimeMutationService,
            grantRuntimeSnapshotService,
            grantIntentStoreSaveService);
    }

    private GrantServiceTestBundle BuildService(AppDatabase db, Mock<IAclDenyModeService>? denyModeService = null)
    {
        var ownershipProjection = new GrantIntentOwnershipProjectionService();
        var mainGrantStore = new RuntimeDatabaseGrantIntentStore(() => db, ownershipProjection);
        var storeProvider = new TestGrantIntentStoreProvider(mainGrantStore, ownershipProjection);
        var repository = new GrantIntentRepository(storeProvider);
        var traverseGrantOwnerResolver = new TraverseGrantOwnerResolver();
        var traverseIntentStoreCoordinator = new TraverseIntentStoreCoordinator(() => repository, traverseGrantOwnerResolver);
        var dbAccessor = new UiThreadDatabaseAccessor(new LambdaDatabaseProvider(() => db), () => SyncInvoker);
        var grantCore = new GrantCoreOperations(_grantAceService, _fileOwnerService,
            dbAccessor, _log.Object, _pathInfo);
        var traverseCore = new TraverseCoreOperations(_traverseAcl.Object,
            _ancestorGranter, _aclPermission.Object, dbAccessor, _log.Object, _pathInfo, traverseGrantOwnerResolver);
        var containerIuSync = new ContainerInteractiveUserSync(grantCore, traverseCore,
            traverseGrantOwnerResolver, _iuResolver.Object, _aclPermission.Object, dbAccessor, _log.Object, _pathInfo);
        var traverseGrantStateService = new TraverseGrantStateService(dbAccessor, _pathInfo, traverseIntentStoreCoordinator);
        var lowIlSync = new LowIntegrityGrantSync(grantCore, traverseCore,
            _mandatoryLabelService, dbAccessor);
        denyModeService ??= CreateEmptyDenyModeService();
        var syncService = new PathGrantSyncService(
            dbAccessor,
            _grantAceService,
            () => storeProvider,
            () => repository,
            _log.Object,
            _pathInfo,
            traverseGrantOwnerResolver,
            new LambdaDatabaseProvider(() => db),
            new AppEntryManagedAclScanFilter(
                new AppEntryAllowAclRuleProvider(new AppEntryAclTargetResolver()),
                denyModeService.Object));
        var fsOps = new GrantFileSystemOperations(grantCore, _grantAceService,
            _fileOwnerService, _mandatoryLabelService, dbAccessor);
        var grantIntentStoreSaveService = new GrantIntentStoreSaveService();
        var grantIntentStoreMutationService = CreateGrantIntentStoreMutationService(
            traverseGrantStateService,
            () => storeProvider,
            () => repository,
            () => mainGrantStore);
        var grantRuntimeMutationService = CreateGrantRuntimeMutationService(
            traverseCore,
            dbAccessor,
            containerIuSync,
            lowIlSync,
            _mandatoryLabelService,
            fsOps,
            _grantAceService,
            _pathInfo,
            traverseGrantOwnerResolver,
            traverseGrantStateService);
        var grantRuntimeSnapshotService = new GrantRuntimeSnapshotService(dbAccessor, traverseGrantOwnerResolver);
        var grantIntentMutationStateRestorer = new GrantIntentMutationStateRestorer(grantIntentStoreSaveService);
        var accessEnsurer = new GrantAccessEnsurer(
            _aclPermission.Object,
            dbAccessor,
            _fileSecurityAccessor.Object,
            _pathInfo,
            traverseCore,
            fsOps,
            _iuResolver.Object,
            traverseGrantOwnerResolver,
            () => repository,
            () => mainGrantStore,
            grantIntentStoreSaveService,
            grantIntentMutationStateRestorer,
            grantRuntimeSnapshotService);
        var persistedGrantMutationWorkflow = CreatePersistedGrantMutationWorkflow(
            traverseCore,
            dbAccessor,
            _mandatoryLabelService,
            fsOps,
            _grantAceService,
            _fileSecurityAccessor.Object,
            traverseIntentStoreCoordinator,
            traverseGrantOwnerResolver,
            traverseGrantStateService,
            () => storeProvider,
            grantIntentStoreMutationService,
            grantRuntimeMutationService,
            grantIntentStoreSaveService);
        var traverseRestoreWorkflow = new TraverseRestoreWorkflow(
            traverseCore,
            containerIuSync,
            traverseIntentStoreCoordinator,
            traverseGrantStateService,
            new GrantMutationOrderResolver(),
            new TraverseRestoreStateRestorer(
                grantRuntimeSnapshotService,
                traverseGrantStateService,
                grantIntentStoreSaveService),
            new TraverseRestoreAclRollbackService(
                _pathInfo,
                _traverseAcl.Object,
                traverseCore,
                traverseIntentStoreCoordinator),
            () => storeProvider,
            grantIntentStoreSaveService);
        var traverseIntentStoreMutationService = new TraverseIntentStoreMutationService(
            traverseCore,
            containerIuSync,
            traverseIntentStoreCoordinator,
            traverseGrantStateService,
            grantIntentStoreSaveService);
        var persistedTraverseMutationWorkflow = new PersistedTraverseMutationWorkflow(
            traverseCore, containerIuSync, traverseIntentStoreCoordinator, traverseGrantStateService,
            grantRuntimeSnapshotService, () => mainGrantStore,
            traverseIntentStoreMutationService, grantIntentStoreSaveService);
        var grantMutatorService = new GrantMutatorService(accessEnsurer, fsOps, persistedGrantMutationWorkflow);
        var traverseService = new TraverseService(traverseCore, traverseRestoreWorkflow, persistedTraverseMutationWorkflow);
        var grantIntentSnapshotService = new GrantIntentSnapshotService(
            grantRuntimeSnapshotService,
            traverseIntentStoreCoordinator,
            () => repository);
        var grantAccountCleanupService = new GrantAccountCleanupService(
            persistedGrantMutationWorkflow,
            persistedTraverseMutationWorkflow,
            grantIntentStoreSaveService);
        return new GrantServiceTestBundle(
            grantMutatorService,
            traverseService,
            _grantAceService,
            grantIntentSnapshotService,
            syncService,
            grantAccountCleanupService,
            fsOps);
    }

    private GrantServiceTestBundle BuildServiceWithIuResolver(AppDatabase db, string iuSid)
    {
        var ownershipProjection = new GrantIntentOwnershipProjectionService();
        var mainGrantStore = new RuntimeDatabaseGrantIntentStore(() => db, ownershipProjection);
        var storeProvider = new TestGrantIntentStoreProvider(mainGrantStore, ownershipProjection);
        var repository = new GrantIntentRepository(storeProvider);
        var traverseGrantOwnerResolver = new TraverseGrantOwnerResolver();
        var traverseIntentStoreCoordinator = new TraverseIntentStoreCoordinator(() => repository, traverseGrantOwnerResolver);
        var iuResolver = new Mock<IInteractiveUserResolver>();
        iuResolver.Setup(r => r.GetInteractiveUserSid()).Returns(iuSid);

        var dbAccessor = new UiThreadDatabaseAccessor(new LambdaDatabaseProvider(() => db), () => SyncInvoker);
        var grantCore = new GrantCoreOperations(_grantAceService, _fileOwnerService,
            dbAccessor, _log.Object, _pathInfo);
        var traverseCore = new TraverseCoreOperations(_traverseAcl.Object,
            _ancestorGranter, _aclPermission.Object, dbAccessor, _log.Object, _pathInfo, traverseGrantOwnerResolver);
        var containerIuSync = new ContainerInteractiveUserSync(grantCore, traverseCore,
            traverseGrantOwnerResolver, iuResolver.Object, _aclPermission.Object, dbAccessor, _log.Object, _pathInfo);
        var traverseGrantStateService = new TraverseGrantStateService(dbAccessor, _pathInfo, traverseIntentStoreCoordinator);
        var lowIlSync = new LowIntegrityGrantSync(grantCore, traverseCore,
            _mandatoryLabelService, dbAccessor);
        var denyModeService = CreateEmptyDenyModeService();
        var syncService = new PathGrantSyncService(
            dbAccessor,
            _grantAceService,
            () => storeProvider,
            () => repository,
            _log.Object,
            _pathInfo,
            traverseGrantOwnerResolver,
            new LambdaDatabaseProvider(() => db),
            new AppEntryManagedAclScanFilter(
                new AppEntryAllowAclRuleProvider(new AppEntryAclTargetResolver()),
                denyModeService.Object));
        var fsOps = new GrantFileSystemOperations(grantCore, _grantAceService,
            _fileOwnerService, _mandatoryLabelService, dbAccessor);
        var grantIntentStoreSaveService = new GrantIntentStoreSaveService();
        var grantIntentStoreMutationService = CreateGrantIntentStoreMutationService(
            traverseGrantStateService,
            () => storeProvider,
            () => repository,
            () => mainGrantStore);
        var grantRuntimeMutationService = CreateGrantRuntimeMutationService(
            traverseCore,
            dbAccessor,
            containerIuSync,
            lowIlSync,
            _mandatoryLabelService,
            fsOps,
            _grantAceService,
            _pathInfo,
            traverseGrantOwnerResolver,
            traverseGrantStateService);
        var grantRuntimeSnapshotService = new GrantRuntimeSnapshotService(dbAccessor, traverseGrantOwnerResolver);
        var grantIntentMutationStateRestorer = new GrantIntentMutationStateRestorer(grantIntentStoreSaveService);
        var accessEnsurer = new GrantAccessEnsurer(
            _aclPermission.Object,
            dbAccessor,
            _fileSecurityAccessor.Object,
            _pathInfo,
            traverseCore,
            fsOps,
            iuResolver.Object,
            traverseGrantOwnerResolver,
            () => repository,
            () => mainGrantStore,
            grantIntentStoreSaveService,
            grantIntentMutationStateRestorer,
            grantRuntimeSnapshotService);
        var persistedGrantMutationWorkflow = CreatePersistedGrantMutationWorkflow(
            traverseCore,
            dbAccessor,
            _mandatoryLabelService,
            fsOps,
            _grantAceService,
            _fileSecurityAccessor.Object,
            traverseIntentStoreCoordinator,
            traverseGrantOwnerResolver,
            traverseGrantStateService,
            () => storeProvider,
            grantIntentStoreMutationService,
            grantRuntimeMutationService,
            grantIntentStoreSaveService);
        var traverseRestoreWorkflow = new TraverseRestoreWorkflow(
            traverseCore,
            containerIuSync,
            traverseIntentStoreCoordinator,
            traverseGrantStateService,
            new GrantMutationOrderResolver(),
            new TraverseRestoreStateRestorer(
                grantRuntimeSnapshotService,
                traverseGrantStateService,
                grantIntentStoreSaveService),
            new TraverseRestoreAclRollbackService(
                _pathInfo,
                _traverseAcl.Object,
                traverseCore,
                traverseIntentStoreCoordinator),
            () => storeProvider,
            grantIntentStoreSaveService);
        var traverseIntentStoreMutationService = new TraverseIntentStoreMutationService(
            traverseCore,
            containerIuSync,
            traverseIntentStoreCoordinator,
            traverseGrantStateService,
            grantIntentStoreSaveService);
        var persistedTraverseMutationWorkflow = new PersistedTraverseMutationWorkflow(
            traverseCore, containerIuSync, traverseIntentStoreCoordinator, traverseGrantStateService,
            grantRuntimeSnapshotService, () => mainGrantStore,
            traverseIntentStoreMutationService, grantIntentStoreSaveService);
        var grantMutatorService = new GrantMutatorService(accessEnsurer, fsOps, persistedGrantMutationWorkflow);
        var traverseService = new TraverseService(traverseCore, traverseRestoreWorkflow, persistedTraverseMutationWorkflow);
        var grantIntentSnapshotService = new GrantIntentSnapshotService(
            grantRuntimeSnapshotService,
            traverseIntentStoreCoordinator,
            () => repository);
        var grantAccountCleanupService = new GrantAccountCleanupService(
            persistedGrantMutationWorkflow,
            persistedTraverseMutationWorkflow,
            grantIntentStoreSaveService);
        return new GrantServiceTestBundle(
            grantMutatorService,
            traverseService,
            _grantAceService,
            grantIntentSnapshotService,
            syncService,
            grantAccountCleanupService,
            fsOps);
    }

    /// <summary>
    /// Builds a <see cref="PathGrantService"/> backed by mocked NTFS services
    /// so ACE and owner calls can be verified without real NTFS I/O.
    /// </summary>
    private GrantServiceTestBundle BuildServiceWithMockedNtfs(
        out Mock<IGrantAceService> grantAceMock,
        out Mock<IFileOwnerService> ownerMock,
        out Mock<IMandatoryLabelService> mandatoryLabelMock,
        out Mock<IPathSecurityDescriptorAccessor> aclAccessorMock,
        out AppDatabase db)
    {
        grantAceMock = new Mock<IGrantAceService>();
        ownerMock = new Mock<IFileOwnerService>();
        aclAccessorMock = new Mock<IPathSecurityDescriptorAccessor>();
        aclAccessorMock.Setup(a => a.GetSecurity(It.IsAny<string>()))
            .Returns(() => CreateSecurityWithOwner(BuiltinAdministratorsSid));
        mandatoryLabelMock = new Mock<IMandatoryLabelService>();
        var localDb = new AppDatabase();
        db = localDb;
        var ownershipProjection = new GrantIntentOwnershipProjectionService();
        var mainGrantStore = new RuntimeDatabaseGrantIntentStore(() => localDb, ownershipProjection);
        var storeProvider = new TestGrantIntentStoreProvider(mainGrantStore, ownershipProjection);
        var repository = new GrantIntentRepository(storeProvider);
        var traverseGrantOwnerResolver = new TraverseGrantOwnerResolver();
        var traverseIntentStoreCoordinator = new TraverseIntentStoreCoordinator(() => repository, traverseGrantOwnerResolver);
        var dbAccessor = new UiThreadDatabaseAccessor(new LambdaDatabaseProvider(() => localDb), () => SyncInvoker);
        var grantCore = new GrantCoreOperations(grantAceMock.Object, ownerMock.Object,
            dbAccessor, _log.Object, _pathInfo);
        var traverseCore = new TraverseCoreOperations(_traverseAcl.Object,
            _ancestorGranter, _aclPermission.Object, dbAccessor, _log.Object, _pathInfo, traverseGrantOwnerResolver);
        var containerIuSync = new ContainerInteractiveUserSync(grantCore, traverseCore,
            traverseGrantOwnerResolver, _iuResolver.Object, _aclPermission.Object, dbAccessor, _log.Object, _pathInfo);
        var traverseGrantStateService = new TraverseGrantStateService(dbAccessor, _pathInfo, traverseIntentStoreCoordinator);
        var lowIlSync = new LowIntegrityGrantSync(grantCore, traverseCore,
            mandatoryLabelMock.Object, dbAccessor);
        var denyModeService = CreateEmptyDenyModeService();
        var syncService = new PathGrantSyncService(
            dbAccessor,
            grantAceMock.Object,
            () => storeProvider,
            () => repository,
            _log.Object,
            _pathInfo,
            traverseGrantOwnerResolver,
            new LambdaDatabaseProvider(() => localDb),
            new AppEntryManagedAclScanFilter(
                new AppEntryAllowAclRuleProvider(new AppEntryAclTargetResolver()),
                denyModeService.Object));
        var fsOps = new GrantFileSystemOperations(grantCore, grantAceMock.Object,
            ownerMock.Object, mandatoryLabelMock.Object, dbAccessor);
        var grantIntentStoreSaveService = new GrantIntentStoreSaveService();
        var grantIntentStoreMutationService = CreateGrantIntentStoreMutationService(
            traverseGrantStateService,
            () => storeProvider,
            () => repository,
            () => mainGrantStore);
        var grantRuntimeMutationService = CreateGrantRuntimeMutationService(
            traverseCore,
            dbAccessor,
            containerIuSync,
            lowIlSync,
            mandatoryLabelMock.Object,
            fsOps,
            grantAceMock.Object,
            _pathInfo,
            traverseGrantOwnerResolver,
            traverseGrantStateService);
        var grantRuntimeSnapshotService = new GrantRuntimeSnapshotService(dbAccessor, traverseGrantOwnerResolver);
        var grantIntentMutationStateRestorer = new GrantIntentMutationStateRestorer(grantIntentStoreSaveService);
        var accessEnsurer = new GrantAccessEnsurer(
            _aclPermission.Object,
            dbAccessor,
            aclAccessorMock.Object,
            _pathInfo,
            traverseCore,
            fsOps,
            _iuResolver.Object,
            traverseGrantOwnerResolver,
            () => repository,
            () => mainGrantStore,
            grantIntentStoreSaveService,
            grantIntentMutationStateRestorer,
            grantRuntimeSnapshotService);
        var persistedGrantMutationWorkflow = CreatePersistedGrantMutationWorkflow(
            traverseCore,
            dbAccessor,
            mandatoryLabelMock.Object,
            fsOps,
            grantAceMock.Object,
            aclAccessorMock.Object,
            traverseIntentStoreCoordinator,
            traverseGrantOwnerResolver,
            traverseGrantStateService,
            () => storeProvider,
            grantIntentStoreMutationService,
            grantRuntimeMutationService,
            grantIntentStoreSaveService);
        var traverseRestoreWorkflow = new TraverseRestoreWorkflow(
            traverseCore,
            containerIuSync,
            traverseIntentStoreCoordinator,
            traverseGrantStateService,
            new GrantMutationOrderResolver(),
            new TraverseRestoreStateRestorer(
                grantRuntimeSnapshotService,
                traverseGrantStateService,
                grantIntentStoreSaveService),
            new TraverseRestoreAclRollbackService(
                _pathInfo,
                _traverseAcl.Object,
                traverseCore,
                traverseIntentStoreCoordinator),
            () => storeProvider,
            grantIntentStoreSaveService);
        var traverseIntentStoreMutationService = new TraverseIntentStoreMutationService(
            traverseCore,
            containerIuSync,
            traverseIntentStoreCoordinator,
            traverseGrantStateService,
            grantIntentStoreSaveService);
        var persistedTraverseMutationWorkflow = new PersistedTraverseMutationWorkflow(
            traverseCore, containerIuSync, traverseIntentStoreCoordinator, traverseGrantStateService,
            grantRuntimeSnapshotService, () => mainGrantStore,
            traverseIntentStoreMutationService, grantIntentStoreSaveService);
        var grantMutatorService = new GrantMutatorService(accessEnsurer, fsOps, persistedGrantMutationWorkflow);
        var traverseService = new TraverseService(traverseCore, traverseRestoreWorkflow, persistedTraverseMutationWorkflow);
        var grantIntentSnapshotService = new GrantIntentSnapshotService(
            grantRuntimeSnapshotService,
            traverseIntentStoreCoordinator,
            () => repository);
        var grantAccountCleanupService = new GrantAccountCleanupService(
            persistedGrantMutationWorkflow,
            persistedTraverseMutationWorkflow,
            grantIntentStoreSaveService);
        return new GrantServiceTestBundle(
            grantMutatorService,
            traverseService,
            grantAceMock.Object,
            grantIntentSnapshotService,
            syncService,
            grantAccountCleanupService,
            fsOps);
    }

    private GrantServiceTestBundle BuildServiceWithMockedAclAccessor(
        out Mock<IPathSecurityDescriptorAccessor> aclAccessorMock,
        out AppDatabase db)
    {
        var grantAceMock = new Mock<IGrantAceService>();
        var ownerMock = new Mock<IFileOwnerService>();
        var mandatoryLabelMock = new Mock<IMandatoryLabelService>();
        aclAccessorMock = new Mock<IPathSecurityDescriptorAccessor>();
        aclAccessorMock.Setup(a => a.GetSecurity(It.IsAny<string>()))
            .Returns(() => CreateSecurityWithOwner(BuiltinAdministratorsSid));
        var localDb = new AppDatabase();
        db = localDb;
        var ownershipProjection = new GrantIntentOwnershipProjectionService();
        var mainGrantStore = new RuntimeDatabaseGrantIntentStore(() => localDb, ownershipProjection);
        var storeProvider = new TestGrantIntentStoreProvider(mainGrantStore, ownershipProjection);
        var repository = new GrantIntentRepository(storeProvider);
        var traverseGrantOwnerResolver = new TraverseGrantOwnerResolver();
        var traverseIntentStoreCoordinator = new TraverseIntentStoreCoordinator(() => repository, traverseGrantOwnerResolver);
        var dbAccessor = new UiThreadDatabaseAccessor(new LambdaDatabaseProvider(() => localDb), () => SyncInvoker);
        var grantCore = new GrantCoreOperations(grantAceMock.Object, ownerMock.Object,
            dbAccessor, _log.Object, _pathInfo);
        var traverseCore = new TraverseCoreOperations(_traverseAcl.Object,
            _ancestorGranter, _aclPermission.Object, dbAccessor, _log.Object, _pathInfo, traverseGrantOwnerResolver);
        var containerIuSync = new ContainerInteractiveUserSync(grantCore, traverseCore,
            traverseGrantOwnerResolver, _iuResolver.Object, _aclPermission.Object, dbAccessor, _log.Object, _pathInfo);
        var traverseGrantStateService = new TraverseGrantStateService(dbAccessor, _pathInfo, traverseIntentStoreCoordinator);
        var lowIlSync = new LowIntegrityGrantSync(grantCore, traverseCore,
            mandatoryLabelMock.Object, dbAccessor);
        var denyModeService = CreateEmptyDenyModeService();
        var syncService = new PathGrantSyncService(
            dbAccessor,
            grantAceMock.Object,
            () => storeProvider,
            () => repository,
            _log.Object,
            _pathInfo,
            traverseGrantOwnerResolver,
            new LambdaDatabaseProvider(() => localDb),
            new AppEntryManagedAclScanFilter(
                new AppEntryAllowAclRuleProvider(new AppEntryAclTargetResolver()),
                denyModeService.Object));
        var fsOps = new GrantFileSystemOperations(grantCore, grantAceMock.Object,
            ownerMock.Object, mandatoryLabelMock.Object, dbAccessor);
        var grantIntentStoreSaveService = new GrantIntentStoreSaveService();
        var grantIntentStoreMutationService = CreateGrantIntentStoreMutationService(
            traverseGrantStateService,
            () => storeProvider,
            () => repository,
            () => mainGrantStore);
        var grantRuntimeMutationService = CreateGrantRuntimeMutationService(
            traverseCore,
            dbAccessor,
            containerIuSync,
            lowIlSync,
            mandatoryLabelMock.Object,
            fsOps,
            grantAceMock.Object,
            _pathInfo,
            traverseGrantOwnerResolver,
            traverseGrantStateService);
        var grantRuntimeSnapshotService = new GrantRuntimeSnapshotService(dbAccessor, traverseGrantOwnerResolver);
        var grantIntentMutationStateRestorer = new GrantIntentMutationStateRestorer(grantIntentStoreSaveService);
        var accessEnsurer = new GrantAccessEnsurer(
            _aclPermission.Object,
            dbAccessor,
            aclAccessorMock.Object,
            _pathInfo,
            traverseCore,
            fsOps,
            _iuResolver.Object,
            traverseGrantOwnerResolver,
            () => repository,
            () => mainGrantStore,
            grantIntentStoreSaveService,
            grantIntentMutationStateRestorer,
            grantRuntimeSnapshotService);
        var persistedGrantMutationWorkflow = CreatePersistedGrantMutationWorkflow(
            traverseCore,
            dbAccessor,
            mandatoryLabelMock.Object,
            fsOps,
            grantAceMock.Object,
            aclAccessorMock.Object,
            traverseIntentStoreCoordinator,
            traverseGrantOwnerResolver,
            traverseGrantStateService,
            () => storeProvider,
            grantIntentStoreMutationService,
            grantRuntimeMutationService,
            grantIntentStoreSaveService);
        var traverseRestoreWorkflow = new TraverseRestoreWorkflow(
            traverseCore,
            containerIuSync,
            traverseIntentStoreCoordinator,
            traverseGrantStateService,
            new GrantMutationOrderResolver(),
            new TraverseRestoreStateRestorer(
                grantRuntimeSnapshotService,
                traverseGrantStateService,
                grantIntentStoreSaveService),
            new TraverseRestoreAclRollbackService(
                _pathInfo,
                _traverseAcl.Object,
                traverseCore,
                traverseIntentStoreCoordinator),
            () => storeProvider,
            grantIntentStoreSaveService);
        var traverseIntentStoreMutationService = new TraverseIntentStoreMutationService(
            traverseCore,
            containerIuSync,
            traverseIntentStoreCoordinator,
            traverseGrantStateService,
            grantIntentStoreSaveService);
        var persistedTraverseMutationWorkflow = new PersistedTraverseMutationWorkflow(
            traverseCore, containerIuSync, traverseIntentStoreCoordinator, traverseGrantStateService,
            grantRuntimeSnapshotService, () => mainGrantStore,
            traverseIntentStoreMutationService, grantIntentStoreSaveService);
        var grantMutatorService = new GrantMutatorService(accessEnsurer, fsOps, persistedGrantMutationWorkflow);
        var traverseService = new TraverseService(traverseCore, traverseRestoreWorkflow, persistedTraverseMutationWorkflow);
        var grantIntentSnapshotService = new GrantIntentSnapshotService(
            grantRuntimeSnapshotService,
            traverseIntentStoreCoordinator,
            () => repository);
        var grantAccountCleanupService = new GrantAccountCleanupService(
            persistedGrantMutationWorkflow,
            persistedTraverseMutationWorkflow,
            grantIntentStoreSaveService);
        return new GrantServiceTestBundle(
            grantMutatorService,
            traverseService,
            grantAceMock.Object,
            grantIntentSnapshotService,
            syncService,
            grantAccountCleanupService,
            fsOps);
    }

    private GrantServiceTestBundle BuildStoreAwareService(
        TestGrantIntentStore mainStore,
        out TestGrantIntentStoreProvider storeProvider,
        out AppDatabase db,
        out Mock<IGrantAceService> grantAceMock,
        out Mock<IFileOwnerService> ownerMock)
    {
        storeProvider = new TestGrantIntentStoreProvider(mainStore);
        var repository = new GrantIntentRepository(storeProvider);
        var traverseGrantOwnerResolver = new TraverseGrantOwnerResolver();
        var traverseIntentStoreCoordinator = new TraverseIntentStoreCoordinator(() => repository, traverseGrantOwnerResolver);
        grantAceMock = new Mock<IGrantAceService>();
        ownerMock = new Mock<IFileOwnerService>();
        var mandatoryLabelMock = new Mock<IMandatoryLabelService>();
        var aclAccessorMock = new Mock<IPathSecurityDescriptorAccessor>();
        aclAccessorMock.Setup(a => a.GetSecurity(It.IsAny<string>()))
            .Returns(() => CreateSecurityWithOwner(BuiltinAdministratorsSid));
        db = new AppDatabase();
        var database = db;
        var dbAccessor = new UiThreadDatabaseAccessor(new LambdaDatabaseProvider(() => database), () => SyncInvoker);
        var grantCore = new GrantCoreOperations(grantAceMock.Object, ownerMock.Object,
            dbAccessor, _log.Object, _pathInfo);
        var traverseCore = new TraverseCoreOperations(_traverseAcl.Object,
            _ancestorGranter, _aclPermission.Object, dbAccessor, _log.Object, _pathInfo, traverseGrantOwnerResolver);
        var containerIuSync = new ContainerInteractiveUserSync(grantCore, traverseCore,
            traverseGrantOwnerResolver, _iuResolver.Object, _aclPermission.Object, dbAccessor, _log.Object, _pathInfo);
        var traverseGrantStateService = new TraverseGrantStateService(dbAccessor, _pathInfo, traverseIntentStoreCoordinator);
        var lowIlSync = new LowIntegrityGrantSync(grantCore, traverseCore,
            mandatoryLabelMock.Object, dbAccessor);
        var provider = storeProvider;
        var denyModeService = CreateEmptyDenyModeService();
        var syncService = new PathGrantSyncService(
            dbAccessor,
            grantAceMock.Object,
            () => provider,
            () => repository,
            _log.Object,
            _pathInfo,
            traverseGrantOwnerResolver,
            new LambdaDatabaseProvider(() => database),
            new AppEntryManagedAclScanFilter(
                new AppEntryAllowAclRuleProvider(new AppEntryAclTargetResolver()),
                denyModeService.Object));
        var fsOps = new GrantFileSystemOperations(grantCore, grantAceMock.Object,
            ownerMock.Object, mandatoryLabelMock.Object, dbAccessor);
        var grantIntentStoreSaveService = new GrantIntentStoreSaveService();
        var grantIntentStoreMutationService = CreateGrantIntentStoreMutationService(
            traverseGrantStateService,
            () => provider,
            () => repository,
            () => mainStore);
        var grantRuntimeMutationService = CreateGrantRuntimeMutationService(
            traverseCore,
            dbAccessor,
            containerIuSync,
            lowIlSync,
            mandatoryLabelMock.Object,
            fsOps,
            grantAceMock.Object,
            _pathInfo,
            traverseGrantOwnerResolver,
            traverseGrantStateService);
        var grantRuntimeSnapshotService = new GrantRuntimeSnapshotService(dbAccessor, traverseGrantOwnerResolver);
        var grantIntentMutationStateRestorer = new GrantIntentMutationStateRestorer(grantIntentStoreSaveService);
        var accessEnsurer = new GrantAccessEnsurer(
            _aclPermission.Object,
            dbAccessor,
            aclAccessorMock.Object,
            _pathInfo,
            traverseCore,
            fsOps,
            _iuResolver.Object,
            traverseGrantOwnerResolver,
            () => repository,
            () => mainStore,
            grantIntentStoreSaveService,
            grantIntentMutationStateRestorer,
            grantRuntimeSnapshotService);
        var persistedGrantMutationWorkflow = CreatePersistedGrantMutationWorkflow(
            traverseCore,
            dbAccessor,
            mandatoryLabelMock.Object,
            fsOps,
            grantAceMock.Object,
            aclAccessorMock.Object,
            traverseIntentStoreCoordinator,
            traverseGrantOwnerResolver,
            traverseGrantStateService,
            () => provider,
            grantIntentStoreMutationService,
            grantRuntimeMutationService,
            grantIntentStoreSaveService);
        var traverseRestoreWorkflow = new TraverseRestoreWorkflow(
            traverseCore,
            containerIuSync,
            traverseIntentStoreCoordinator,
            traverseGrantStateService,
            new GrantMutationOrderResolver(),
            new TraverseRestoreStateRestorer(
                grantRuntimeSnapshotService,
                traverseGrantStateService,
                grantIntentStoreSaveService),
            new TraverseRestoreAclRollbackService(
                _pathInfo,
                _traverseAcl.Object,
                traverseCore,
                traverseIntentStoreCoordinator),
            () => provider,
            grantIntentStoreSaveService);
        var traverseIntentStoreMutationService = new TraverseIntentStoreMutationService(
            traverseCore,
            containerIuSync,
            traverseIntentStoreCoordinator,
            traverseGrantStateService,
            grantIntentStoreSaveService);
        var persistedTraverseMutationWorkflow = new PersistedTraverseMutationWorkflow(
            traverseCore, containerIuSync, traverseIntentStoreCoordinator, traverseGrantStateService,
            grantRuntimeSnapshotService, () => mainStore,
            traverseIntentStoreMutationService, grantIntentStoreSaveService);
        var grantMutatorService = new GrantMutatorService(accessEnsurer, fsOps, persistedGrantMutationWorkflow);
        var traverseService = new TraverseService(traverseCore, traverseRestoreWorkflow, persistedTraverseMutationWorkflow);
        var grantIntentSnapshotService = new GrantIntentSnapshotService(
            grantRuntimeSnapshotService,
            traverseIntentStoreCoordinator,
            () => repository);
        var grantAccountCleanupService = new GrantAccountCleanupService(
            persistedGrantMutationWorkflow,
            persistedTraverseMutationWorkflow,
            grantIntentStoreSaveService);
        return new GrantServiceTestBundle(
            grantMutatorService,
            traverseService,
            grantAceMock.Object,
            grantIntentSnapshotService,
            syncService,
            grantAccountCleanupService,
            fsOps);
    }

    private GrantServiceTestBundle BuildStoreAwareServiceWithIuResolver(
        TestGrantIntentStore mainStore,
        string iuSid,
        out TestGrantIntentStoreProvider storeProvider,
        out AppDatabase db,
        out Mock<IGrantAceService> grantAceMock,
        out Mock<IFileOwnerService> ownerMock)
    {
        storeProvider = new TestGrantIntentStoreProvider(mainStore);
        var repository = new GrantIntentRepository(storeProvider);
        var traverseGrantOwnerResolver = new TraverseGrantOwnerResolver();
        var traverseIntentStoreCoordinator = new TraverseIntentStoreCoordinator(() => repository, traverseGrantOwnerResolver);
        grantAceMock = new Mock<IGrantAceService>();
        ownerMock = new Mock<IFileOwnerService>();
        var mandatoryLabelMock = new Mock<IMandatoryLabelService>();
        var aclAccessorMock = new Mock<IPathSecurityDescriptorAccessor>();
        aclAccessorMock.Setup(a => a.GetSecurity(It.IsAny<string>()))
            .Returns(() => CreateSecurityWithOwner(BuiltinAdministratorsSid));
        var iuResolver = new Mock<IInteractiveUserResolver>();
        iuResolver.Setup(r => r.GetInteractiveUserSid()).Returns(iuSid);
        db = new AppDatabase();
        var database = db;
        var dbAccessor = new UiThreadDatabaseAccessor(new LambdaDatabaseProvider(() => database), () => SyncInvoker);
        var grantCore = new GrantCoreOperations(grantAceMock.Object, ownerMock.Object,
            dbAccessor, _log.Object, _pathInfo);
        var traverseCore = new TraverseCoreOperations(_traverseAcl.Object,
            _ancestorGranter, _aclPermission.Object, dbAccessor, _log.Object, _pathInfo, traverseGrantOwnerResolver);
        var containerIuSync = new ContainerInteractiveUserSync(grantCore, traverseCore,
            traverseGrantOwnerResolver, iuResolver.Object, _aclPermission.Object, dbAccessor, _log.Object, _pathInfo);
        var traverseGrantStateService = new TraverseGrantStateService(dbAccessor, _pathInfo, traverseIntentStoreCoordinator);
        var lowIlSync = new LowIntegrityGrantSync(grantCore, traverseCore,
            mandatoryLabelMock.Object, dbAccessor);
        var provider = storeProvider;
        var denyModeService = CreateEmptyDenyModeService();
        var syncService = new PathGrantSyncService(
            dbAccessor,
            grantAceMock.Object,
            () => provider,
            () => repository,
            _log.Object,
            _pathInfo,
            traverseGrantOwnerResolver,
            new LambdaDatabaseProvider(() => database),
            new AppEntryManagedAclScanFilter(
                new AppEntryAllowAclRuleProvider(new AppEntryAclTargetResolver()),
                denyModeService.Object));
        var fsOps = new GrantFileSystemOperations(grantCore, grantAceMock.Object,
            ownerMock.Object, mandatoryLabelMock.Object, dbAccessor);
        var grantIntentStoreSaveService = new GrantIntentStoreSaveService();
        var grantIntentStoreMutationService = CreateGrantIntentStoreMutationService(
            traverseGrantStateService,
            () => provider,
            () => repository,
            () => mainStore);
        var grantRuntimeMutationService = CreateGrantRuntimeMutationService(
            traverseCore,
            dbAccessor,
            containerIuSync,
            lowIlSync,
            mandatoryLabelMock.Object,
            fsOps,
            grantAceMock.Object,
            _pathInfo,
            traverseGrantOwnerResolver,
            traverseGrantStateService);
        var grantRuntimeSnapshotService = new GrantRuntimeSnapshotService(dbAccessor, traverseGrantOwnerResolver);
        var grantIntentMutationStateRestorer = new GrantIntentMutationStateRestorer(grantIntentStoreSaveService);
        var accessEnsurer = new GrantAccessEnsurer(
            _aclPermission.Object,
            dbAccessor,
            aclAccessorMock.Object,
            _pathInfo,
            traverseCore,
            fsOps,
            iuResolver.Object,
            traverseGrantOwnerResolver,
            () => repository,
            () => mainStore,
            grantIntentStoreSaveService,
            grantIntentMutationStateRestorer,
            grantRuntimeSnapshotService);
        var persistedGrantMutationWorkflow = CreatePersistedGrantMutationWorkflow(
            traverseCore,
            dbAccessor,
            mandatoryLabelMock.Object,
            fsOps,
            grantAceMock.Object,
            aclAccessorMock.Object,
            traverseIntentStoreCoordinator,
            traverseGrantOwnerResolver,
            traverseGrantStateService,
            () => provider,
            grantIntentStoreMutationService,
            grantRuntimeMutationService,
            grantIntentStoreSaveService);
        var traverseRestoreWorkflow = new TraverseRestoreWorkflow(
            traverseCore,
            containerIuSync,
            traverseIntentStoreCoordinator,
            traverseGrantStateService,
            new GrantMutationOrderResolver(),
            new TraverseRestoreStateRestorer(
                grantRuntimeSnapshotService,
                traverseGrantStateService,
                grantIntentStoreSaveService),
            new TraverseRestoreAclRollbackService(
                _pathInfo,
                _traverseAcl.Object,
                traverseCore,
                traverseIntentStoreCoordinator),
            () => provider,
            grantIntentStoreSaveService);
        var traverseIntentStoreMutationService = new TraverseIntentStoreMutationService(
            traverseCore,
            containerIuSync,
            traverseIntentStoreCoordinator,
            traverseGrantStateService,
            grantIntentStoreSaveService);
        var persistedTraverseMutationWorkflow = new PersistedTraverseMutationWorkflow(
            traverseCore, containerIuSync, traverseIntentStoreCoordinator, traverseGrantStateService,
            grantRuntimeSnapshotService, () => mainStore,
            traverseIntentStoreMutationService, grantIntentStoreSaveService);
        var grantMutatorService = new GrantMutatorService(accessEnsurer, fsOps, persistedGrantMutationWorkflow);
        var traverseService = new TraverseService(traverseCore, traverseRestoreWorkflow, persistedTraverseMutationWorkflow);
        var grantIntentSnapshotService = new GrantIntentSnapshotService(
            grantRuntimeSnapshotService,
            traverseIntentStoreCoordinator,
            () => repository);
        var grantAccountCleanupService = new GrantAccountCleanupService(
            persistedGrantMutationWorkflow,
            persistedTraverseMutationWorkflow,
            grantIntentStoreSaveService);
        return new GrantServiceTestBundle(
            grantMutatorService,
            traverseService,
            grantAceMock.Object,
            grantIntentSnapshotService,
            syncService,
            grantAccountCleanupService,
            fsOps);
    }

    private static SavedRightsState ReadOnly =>
        new(Execute: false, Write: false, Read: true, Special: false, Own: false);

    private static SavedRightsState ReadExecute =>
        new(Execute: true, Write: false, Read: true, Special: false, Own: false);

    private static SavedRightsState DefaultDeny =>
        SavedRightsState.DefaultForMode(isDeny: true);

    // --- AddGrant ---

    [Fact]
    public void AddGrant_NewEntry_AddsToDbAndReturnsGrantAdded()
    {
        // Act
        var result = _service.AddGrant(UserSid, TestPath, isDeny: false, ReadOnly);

        // Assert
        Assert.True(result.GrantApplied);
        Assert.True(result.DatabaseModified);
        var grants = _database.GetAccount(UserSid)?.Grants;
        Assert.NotNull(grants);
        Assert.Contains(grants, e => e is { IsTraverseOnly: false, IsDeny: false, Path: TestPath });
    }

    [Fact]
    public void AddGrant_DuplicateSameMode_UpdatesSavedRightsInPlace()
    {
        // Arrange
        _service.AddGrant(UserSid, TestPath, isDeny: false, ReadOnly);

        // Act — add again with different rights (same mode, same path)
        var result = _service.AddGrant(UserSid, TestPath, isDeny: false, ReadExecute);

        // Assert — no new non-traverse entry, existing entry updated
        Assert.True(result.GrantApplied);
        Assert.True(result.DatabaseModified);
        var grants = _database.GetAccount(UserSid)!.Grants
            .Where(e => e is { IsTraverseOnly: false, IsDeny: false }).ToList();
        Assert.Single(grants);
        Assert.True(grants[0].SavedRights!.Execute);
    }

    [Fact]
    public void AddGrant_OppositeModeExists_Throws()
    {
        // Arrange: add allow grant first
        _service.AddGrant(UserSid, TestPath, isDeny: false, ReadOnly);

        // Act + Assert: adding deny for same path throws
        Assert.Throws<InvalidOperationException>(
            () => _service.AddGrant(UserSid, TestPath, isDeny: true, DefaultDeny));
    }

    [Fact]
    public void AddGrant_NullSavedRights_UsesDefaultForMode()
    {
        // Act
        _service.AddGrant(UserSid, TestPath, isDeny: false, savedRights: null);

        // Assert — entry recorded with DefaultForMode rights
        var entry = _database.GetAccount(UserSid)!.Grants
            .First(e => e is { IsTraverseOnly: false, IsDeny: false });
        Assert.NotNull(entry.SavedRights);
        Assert.True(entry.SavedRights.Read);
    }

    [Fact]
    public void AddGrant_AllowGrant_AutoAddsTraverseEntry()
    {
        // Act
        _service.AddGrant(UserSid, TestPath, isDeny: false, ReadOnly);

        // Assert — a traverse entry for the same directory was added
        var traverseEntries = _database.GetAccount(UserSid)?.Grants
            .Where(e => e.IsTraverseOnly).ToList();
        Assert.NotNull(traverseEntries);
        Assert.NotEmpty(traverseEntries);
    }

    [Fact]
    public void AddGrant_ExistingAllowEntry_StillAddsTraverseEntry()
    {
        _database.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
        {
            Path = TestPath,
            IsDeny = false,
            SavedRights = ReadOnly
        });

        var result = _service.AddGrant(UserSid, TestPath, isDeny: false, ReadOnly);

        Assert.True(result.GrantApplied);
        Assert.Contains(_database.GetAccount(UserSid)!.Grants, e => e.IsTraverseOnly);
    }

    [Fact]
    public void AddGrant_ContainerSid_TriggersInteractiveUserSync()
    {
        // Arrange: IU resolver returns a valid SID; IU needs the grant
        var db = new AppDatabase();
        var service = BuildServiceWithIuResolver(db, InteractiveSid);

        // Act
        service.AddGrant(ContainerSid, TestPath, isDeny: false, ReadOnly);

        // Assert — IU also has grant
        var iuGrants = db.GetAccount(InteractiveSid)?.Grants;
        Assert.NotNull(iuGrants);
        var entry = Assert.Single(iuGrants!, e => e is { IsTraverseOnly: false, IsDeny: false, Path: TestPath });
        Assert.Contains(ContainerSid, entry.SourceSids ?? []);
    }

    [Fact]
    public void AddGrant_AllApplicationPackagesSid_TriggersInteractiveUserSync()
    {
        var db = new AppDatabase();
        var service = BuildServiceWithIuResolver(db, InteractiveSid);

        service.AddGrant(AclHelper.AllApplicationPackagesSid, TestPath, isDeny: false, ReadOnly);

        var iuGrants = db.GetAccount(InteractiveSid)?.Grants;
        Assert.NotNull(iuGrants);
        Assert.Contains(iuGrants, e => e is { IsTraverseOnly: false, IsDeny: false, Path: TestPath });
    }

    [Fact]
    public void AddGrant_ContainerSid_IuGrantSkippedWhenRightsAlreadySufficient()
    {
        // Arrange: IU already has sufficient rights (NeedsPermissionGrant = false for IU)
        _aclPermission.Setup(p => p.NeedsPermissionGrant(TestPath, InteractiveSid,
                It.IsAny<FileSystemRights>(), It.IsAny<bool>()))
            .Returns(false);

        var db = new AppDatabase();
        var service = BuildServiceWithIuResolver(db, InteractiveSid);

        // Act
        service.AddGrant(ContainerSid, TestPath, isDeny: false, ReadOnly);

        // Assert — IU has no non-traverse grant for TestPath
        var iuNonTraverse = db.GetAccount(InteractiveSid)?.Grants
            .Where(e => e is { IsTraverseOnly: false, IsDeny: false, Path: TestPath }).ToList();
        Assert.Empty(iuNonTraverse ?? []);
    }

    [Fact]
    public void AddGrant_SecondContainerTracksSourceEvenWhenInteractiveUserAlreadyHasRights()
    {
        var db = new AppDatabase();
        var service = BuildServiceWithIuResolver(db, InteractiveSid);

        service.AddGrant(ContainerSid, TestPath, isDeny: false, ReadOnly);
        _aclPermission.Setup(p => p.NeedsPermissionGrant(TestPath, InteractiveSid,
                It.IsAny<FileSystemRights>(), It.IsAny<bool>()))
            .Returns(false);

        service.AddGrant(OtherContainerSid, TestPath, isDeny: false, ReadOnly);

        var iuEntry = db.GetAccount(InteractiveSid)?.Grants
            .FirstOrDefault(e => e is { IsTraverseOnly: false, IsDeny: false, Path: TestPath });
        Assert.NotNull(iuEntry);
        Assert.Contains(ContainerSid, iuEntry!.SourceSids ?? []);
        Assert.Contains(OtherContainerSid, iuEntry.SourceSids ?? []);
    }

    [Fact]
    public void AddGrant_WithOwnerSid_RecordsGrantAndAppliesAce()
    {
        var ownerRights = ReadOnly with { Own = true };
        var service = BuildServiceWithMockedNtfs(out var grantAceMock, out var ownerMock,
            out _, out _, out var db);

        // Act
        var result = service.AddGrant(UserSid, ExistingDir, isDeny: false, ownerRights);

        // Assert: grant recorded; ACE applied; owner change delegated without real NTFS I/O.
        Assert.True(result.GrantApplied);
        Assert.True(result.DatabaseModified);
        var entry = db.GetAccount(UserSid)?.Grants
            .FirstOrDefault(e => e is { IsTraverseOnly: false, IsDeny: false } &&
                                 string.Equals(e.Path, ExistingDir, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(entry);
        grantAceMock.Verify(n => n.ApplyAce(ExistingDir, UserSid, false, ownerRights, true), Times.Once);
        ownerMock.Verify(n => n.ChangeOwner(ExistingDir, UserSid, recursive: false), Times.Once);
    }

    [Fact]
    public void AddGrant_LowIntegritySid_IgnoresOwnerAndStoresOwnerOff()
    {
        var ownerRights = ReadExecute with { Own = true };
        var service = BuildServiceWithMockedNtfs(out _, out var ownerMock, out _, out _, out var db);

        service.AddGrant(AclHelper.LowIntegritySid, TestPath, isDeny: false, ownerRights);

        var entry = db.GetAccount(AclHelper.LowIntegritySid)!.Grants
            .First(e => e is { IsTraverseOnly: false, IsDeny: false });
        Assert.False(entry.SavedRights!.Own);
        ownerMock.Verify(n => n.ChangeOwner(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void EnsureAccess_PostSaveGrantFailure_ThrowsAndRollsBackAndResaves()
    {
        var mainStore = new TestGrantIntentStore();
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out _,
            out var grantAceMock,
            out _);
        _aclPermission.Setup(p => p.NeedsPermissionGrant(
                ExistingDir,
                UserSid,
                It.IsAny<FileSystemRights>(),
                It.IsAny<bool>()))
            .Returns(true);

        var events = new List<string>();
        grantAceMock
            .Setup(n => n.ApplyAce(ExistingDir, UserSid, false, ReadOnly, true))
            .Callback(() => events.Add("acl"));
        mainStore.SaveAction = () => events.Add("save");

        var ex = Assert.Throws<GrantOperationException>(() => service.EnsureAccess(
            UserSid,
            ExistingDir,
            ReadOnly));

        Assert.Equal(GrantApplyFailureStep.TargetEffectiveAccessValidation, ex.Step);
        Assert.Equal(["save", "acl", "save"], events);
    }

    [Fact]
    public void EnsureAccess_SaveFails_ThrowsWithoutNtfsWrite()
    {
        var mainStore = new TestGrantIntentStore();
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out var db,
            out var grantAceMock,
            out _);
        _aclPermission.Setup(p => p.NeedsPermissionGrant(
                ExistingDir,
                UserSid,
                It.IsAny<FileSystemRights>(),
                It.IsAny<bool>()))
            .Returns(true);
        mainStore.SaveAction = () => throw new InvalidOperationException("save failed");

        var ex = Assert.Throws<GrantOperationException>(() => service.EnsureAccess(
            UserSid,
            ExistingDir,
            ReadOnly));

        Assert.Equal(GrantApplyFailureStep.GrantIntentSave, ex.Step);
        Assert.Equal("save failed", ex.Cause.Message);
        var cleanupFailure = Assert.Single(ex.CleanupFailures);
        Assert.Equal(GrantApplyFailureStep.RevertIntentSave, cleanupFailure.Step);
        Assert.Equal(2, mainStore.SaveCount);
        grantAceMock.Verify(n => n.ApplyAce(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<SavedRightsState>(), It.IsAny<bool>()), Times.Never);
        Assert.Empty(mainStore.GetEntries(UserSid));
        Assert.Null(db.GetAccount(UserSid));
    }

    [Fact]
    public void PersistedAddGrant_MainStoreEntry_SavesBeforeAclApply()
    {
        var mainStore = new TestGrantIntentStore();
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out _,
            out var grantAceMock,
            out _);
        var events = new List<string>();
        mainStore.SaveAction = () => events.Add("save");
        grantAceMock.Setup(mock => mock.ApplyAce(ExistingDir, UserSid, false, ReadOnly, true))
            .Callback(() => events.Add("acl"));

        var result = service.AddGrant(UserSid, ExistingDir, isDeny: false, ReadOnly, confirm: null);

        Assert.Equal(["save", "acl"], events);
        Assert.True(result.GrantApplied);
        Assert.True(result.GrantApplied);
        Assert.Contains(mainStore.GetEntries(UserSid),
            entry => string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase) &&
                     entry is { IsDeny: false, IsTraverseOnly: false });
    }

    [Fact]
    public void PersistedAddGrant_CreatesTraverseEntryInRuntimeDb()
    {
        var mainStore = new TestGrantIntentStore();
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out var db,
            out _,
            out _);

        service.AddGrant(UserSid, ExistingDir, isDeny: false, ReadOnly, confirm: null);

        Assert.Contains(db.GetAccount(UserSid)?.Grants ?? [],
            entry => entry.IsTraverseOnly &&
                     string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PersistedAddGrant_SelectedAdditionalStore_CreatesOnlyInSelectedStore()
    {
        var mainStore = new TestGrantIntentStore();
        var additionalStore = new TestGrantIntentStore(@"C:\Configs\extra-a.rfn");
        var service = BuildStoreAwareService(
            mainStore,
            out var storeProvider,
            out var db,
            out _,
            out _);
        storeProvider.AddLoadedStore(additionalStore);

        service.AddGrant(UserSid, ExistingDir, isDeny: false, ReadOnly, confirm: null, store: additionalStore);

        Assert.Empty(mainStore.GetEntries(UserSid));
        Assert.Contains(additionalStore.GetEntries(UserSid),
            entry => string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase) &&
                     entry is { IsDeny: false, IsTraverseOnly: false });
        Assert.Contains(db.GetAccount(UserSid)?.Grants ?? [],
            entry => string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase) &&
                     entry is { IsDeny: false, IsTraverseOnly: false });
    }

    [Fact]
    public void PersistedUpdateGrant_SelectedStore_MovesExistingEntryAndUpdatesRights()
    {
        var mainStore = new TestGrantIntentStore();
        var additionalStore = new TestGrantIntentStore(@"C:\Configs\extra-b.rfn");
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadOnly
        });
        var service = BuildStoreAwareService(
            mainStore,
            out var storeProvider,
            out _,
            out _,
            out _);
        storeProvider.AddLoadedStore(additionalStore);

        var result = service.UpdateGrant(
            UserSid,
            ExistingDir,
            isDeny: false,
            ReadExecute,
            confirm: null,
            store: additionalStore);

        Assert.True(result.GrantApplied);
        Assert.True(result.DatabaseModified);
        Assert.Empty(mainStore.GetEntries(UserSid));
        var entry = Assert.Single(additionalStore.GetEntries(UserSid));
        Assert.True(entry.SavedRights?.Execute);
    }

    [Fact]
    public void PersistedUpdateGrant_StoreMoveOnly_StillReappliesAcl()
    {
        var mainStore = new TestGrantIntentStore();
        var additionalStore = new TestGrantIntentStore(@"C:\Configs\extra-move-only.rfn");
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadOnly
        });
        var service = BuildStoreAwareService(
            mainStore,
            out var storeProvider,
            out _,
            out var grantAceMock,
            out _);
        storeProvider.AddLoadedStore(additionalStore);

        service.UpdateGrant(
            UserSid,
            ExistingDir,
            isDeny: false,
            ReadOnly,
            confirm: null,
            store: additionalStore);

        grantAceMock.Verify(mock => mock.ApplyAce(ExistingDir, UserSid, false, ReadOnly, true), Times.Once);
        Assert.Empty(mainStore.GetEntries(UserSid));
        Assert.Contains(additionalStore.GetEntries(UserSid),
            entry => string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PersistedWideningUpdate_SavesBeforeAclApply()
    {
        var mainStore = new TestGrantIntentStore();
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadOnly
        });
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out var db,
            out var grantAceMock,
            out _);
        db.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadOnly
        });
        var events = new List<string>();
        mainStore.SaveAction = () => events.Add("save");
        grantAceMock.Setup(mock => mock.ApplyAce(ExistingDir, UserSid, false, ReadExecute, true))
            .Callback(() => events.Add("acl"));

        var result = service.UpdateGrant(UserSid, ExistingDir, isDeny: false, ReadExecute, confirm: null);

        Assert.Equal(["save", "acl"], events);
        Assert.True(result.GrantApplied);
        Assert.True(result.DatabaseModified);
        Assert.True(result.DurableSaveCompleted);
    }

    [Fact]
    public void PersistedWideningUpdate_AclFailure_RestoresSavedIntentState()
    {
        var mainStore = new TestGrantIntentStore();
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadOnly
        });
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out var db,
            out var grantAceMock,
            out _);
        db.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadOnly
        });
        grantAceMock.Setup(mock => mock.ApplyAce(ExistingDir, UserSid, false, ReadExecute, true))
            .Throws(new UnauthorizedAccessException("acl failed"));

        var ex = Assert.Throws<GrantOperationException>(() =>
            service.UpdateGrant(UserSid, ExistingDir, isDeny: false, ReadExecute, confirm: null));

        Assert.Equal(GrantApplyFailureStep.GrantAclApply, ex.Step);
        Assert.Equal(2, mainStore.SaveCount);
        Assert.Equal(ReadOnly, Assert.Single(mainStore.GetEntries(UserSid)).SavedRights);
        Assert.Equal(
            ReadOnly,
            Assert.Single(
                db.GetAccount(UserSid)!.Grants,
                entry =>
                    !entry.IsTraverseOnly &&
                    !entry.IsDeny &&
                    string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase)).SavedRights);
    }

    [Fact]
    public void PersistedWideningUpdate_AclFailure_RestoresRuntimeSnapshot_NotTrackedStoreState()
    {
        var mainStore = new TestGrantIntentStore();
        var widenedRights = ReadExecute with { Write = true };
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadOnly
        });
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out var db,
            out var grantAceMock,
            out _);
        db.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadExecute
        });
        grantAceMock.Setup(mock => mock.ApplyAce(ExistingDir, UserSid, false, widenedRights, true))
            .Throws(new UnauthorizedAccessException("acl failed"));

        Assert.Throws<GrantOperationException>(() =>
            service.UpdateGrant(UserSid, ExistingDir, isDeny: false, widenedRights, confirm: null));

        Assert.Equal(ReadOnly, Assert.Single(mainStore.GetEntries(UserSid)).SavedRights);
        Assert.Equal(
            ReadExecute,
            Assert.Single(
                db.GetAccount(UserSid)!.Grants,
                entry =>
                    !entry.IsTraverseOnly &&
                    !entry.IsDeny &&
                    string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase)).SavedRights);
    }

    [Fact]
    public void PersistedWideningUpdate_AclFailure_RestoresDirectorySnapshotUsingDirectoryPathKind()
    {
        var widenedRights = ReadExecute with { Write = true };
        const string blockedDirectoryPath = @"C:\Blocked\Folder";
        var service = BuildServiceWithMockedNtfs(
            out var grantAceMock,
            out _,
            out _,
            out var aclAccessorMock,
            out var db);
        db.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
        {
            Path = blockedDirectoryPath,
            IsDeny = false,
            SavedRights = ReadOnly
        });

        var directorySecurity = new DirectorySecurity();
        directorySecurity.SetOwner(new SecurityIdentifier(BuiltinAdministratorsSid));
        aclAccessorMock.Setup(mock => mock.GetSecurity(blockedDirectoryPath))
            .Returns(directorySecurity);
        aclAccessorMock.Setup(mock => mock.SetOwnerAndAclWithFallback(
                blockedDirectoryPath,
                It.Is<FileSystemSecurity>(security => security is DirectorySecurity)))
            .Verifiable();
        grantAceMock.Setup(mock => mock.ApplyAce(blockedDirectoryPath, UserSid, false, widenedRights, false))
            .Throws(new UnauthorizedAccessException("acl failed"));

        Assert.Throws<GrantOperationException>(() =>
            service.UpdateGrant(UserSid, blockedDirectoryPath, isDeny: false, widenedRights, confirm: null));

        aclAccessorMock.Verify();
    }

    [Fact]
    public void PersistedAddGrant_RuntimeFailureAfterDatabaseMutation_RestoresRuntimeSnapshot()
    {
        var service = BuildServiceWithMockedNtfs(
            out _,
            out _,
            out var mandatoryLabelMock,
            out _,
            out var db);
        var writeRights = ReadOnly with { Write = true };
        var events = new List<string>();

        mandatoryLabelMock.Setup(mock => mock.ReadMandatoryLabel(ExistingDir))
            .Returns("S:(ML;;NW;;;ME)");
        mandatoryLabelMock.Setup(mock => mock.ApplyLowIntegrityLabel(ExistingDir))
            .Callback(() =>
            {
                events.Add(db.GetAccount(AclHelper.LowIntegritySid) == null ? "runtime-missing" : "runtime-present");
                throw new UnauthorizedAccessException("label failed");
            });

        Assert.Throws<GrantOperationException>(() =>
            service.AddGrant(AclHelper.LowIntegritySid, ExistingDir, isDeny: false, writeRights));

        Assert.Equal(["runtime-present"], events);
        Assert.Null(db.GetAccount(AclHelper.LowIntegritySid));
    }

    [Fact]
    public void PersistedAddGrant_AclFailureAfterDescriptorMutation_RestoresDescriptorAndLowIntegritySideEffects()
    {
        var service = BuildServiceWithMockedNtfs(
            out var grantAceMock,
            out _,
            out _,
            out var aclAccessorMock,
            out var db);

        var originalSecurity = new DirectorySecurity();
        originalSecurity.SetOwner(new SecurityIdentifier(BuiltinAdministratorsSid));
        originalSecurity.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(UserSid),
            GrantRightsMapper.ReadMask,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow));
        originalSecurity.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(InteractiveSid),
            GrantRightsMapper.ReadMask,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow));
        var expectedSddl = GetSecuritySddl(originalSecurity);
        FileSystemSecurity storedSecurity = CloneSecurity(originalSecurity);

        aclAccessorMock.Setup(mock => mock.GetSecurity(ExistingDir))
            .Returns(() => CloneSecurity(storedSecurity));
        aclAccessorMock.Setup(mock => mock.SetOwnerAndAclWithFallback(
                ExistingDir,
                It.IsAny<FileSystemSecurity>()))
            .Callback<string, FileSystemSecurity>((_, security) => storedSecurity = CloneSecurity(security));

        db.GetOrCreateAccount(AclHelper.LowIntegritySid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadOnly,
            SourceSids = []
        });

        grantAceMock.Setup(mock => mock.ApplyAce(ExistingDir, UserSid, false, ReadOnly, true))
            .Callback(() =>
            {
                var mutatedSecurity = new DirectorySecurity();
                mutatedSecurity.SetOwner(new SecurityIdentifier(BuiltinAdministratorsSid));
                mutatedSecurity.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(UserSid),
                    GrantRightsMapper.ReadMask | GrantRightsMapper.ExecuteMask,
                    InheritanceFlags.None,
                    PropagationFlags.None,
                    AccessControlType.Allow));
                storedSecurity = mutatedSecurity;

                db.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
                {
                    Path = ExistingDir,
                    IsDeny = false,
                    SavedRights = ReadOnly
                });
                db.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
                {
                    Path = ExistingDir,
                    IsTraverseOnly = true,
                    AllAppliedPaths = [ExistingDir]
                });

                var lowIntegrityEntry = db.GetAccount(AclHelper.LowIntegritySid)!.Grants
                    .Single(entry => !entry.IsTraverseOnly &&
                                     !entry.IsDeny &&
                                     string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase));
                lowIntegrityEntry.SourceSids = [UserSid];

                throw new UnauthorizedAccessException("acl failed");
            });

        Assert.Throws<GrantOperationException>(() =>
            service.AddGrant(UserSid, ExistingDir, isDeny: false, ReadOnly, confirm: null));

        Assert.Equal(expectedSddl, GetSecuritySddl(storedSecurity));
        Assert.Null(db.GetAccount(UserSid));
        var lowIntegrityGrant = Assert.Single(db.GetAccount(AclHelper.LowIntegritySid)!.Grants);
        Assert.Empty(lowIntegrityGrant.SourceSids ?? []);
    }

    [Fact]
    public void PersistedAddGrant_AclFailureAfterDescriptorMutation_RestoresDescriptorAndContainerInteractiveUserSideEffects()
    {
        _iuResolver.Setup(resolver => resolver.GetInteractiveUserSid()).Returns(InteractiveSid);
        var service = BuildServiceWithMockedNtfs(
            out var grantAceMock,
            out _,
            out _,
            out var aclAccessorMock,
            out var db);

        var originalSecurity = new DirectorySecurity();
        originalSecurity.SetOwner(new SecurityIdentifier(BuiltinAdministratorsSid));
        originalSecurity.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(ContainerSid),
            GrantRightsMapper.ReadMask,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow));
        originalSecurity.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(UserSid),
            GrantRightsMapper.ReadMask,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow));
        var expectedSddl = GetSecuritySddl(originalSecurity);
        FileSystemSecurity storedSecurity = CloneSecurity(originalSecurity);

        aclAccessorMock.Setup(mock => mock.GetSecurity(ExistingDir))
            .Returns(() => CloneSecurity(storedSecurity));
        aclAccessorMock.Setup(mock => mock.SetOwnerAndAclWithFallback(
                ExistingDir,
                It.IsAny<FileSystemSecurity>()))
            .Callback<string, FileSystemSecurity>((_, security) => storedSecurity = CloneSecurity(security));

        grantAceMock.Setup(mock => mock.ApplyAce(ExistingDir, ContainerSid, false, ReadOnly, true))
            .Callback(() =>
            {
                var mutatedSecurity = new DirectorySecurity();
                mutatedSecurity.SetOwner(new SecurityIdentifier(BuiltinAdministratorsSid));
                mutatedSecurity.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(ContainerSid),
                    GrantRightsMapper.ReadMask | GrantRightsMapper.ExecuteMask,
                    InheritanceFlags.None,
                    PropagationFlags.None,
                    AccessControlType.Allow));
                storedSecurity = mutatedSecurity;

                db.GetOrCreateAccount(ContainerSid).Grants.Add(new GrantedPathEntry
                {
                    Path = ExistingDir,
                    IsDeny = false,
                    SavedRights = ReadOnly
                });
                db.GetOrCreateAccount(AclHelper.AllApplicationPackagesSid).Grants.Add(new GrantedPathEntry
                {
                    Path = ExistingDir,
                    IsTraverseOnly = true,
                    AllAppliedPaths = [ExistingDir],
                    SourceSids = [ContainerSid]
                });
                db.GetOrCreateAccount(InteractiveSid).Grants.Add(new GrantedPathEntry
                {
                    Path = ExistingDir,
                    IsDeny = false,
                    SavedRights = ReadOnly,
                    SourceSids = [ContainerSid]
                });
                db.GetOrCreateAccount(InteractiveSid).Grants.Add(new GrantedPathEntry
                {
                    Path = ExistingDir,
                    IsTraverseOnly = true,
                    AllAppliedPaths = [ExistingDir],
                    SourceSids = [ContainerSid]
                });

                throw new UnauthorizedAccessException("acl failed");
            });

        Assert.Throws<GrantOperationException>(() =>
            service.AddGrant(ContainerSid, ExistingDir, isDeny: false, ReadOnly, confirm: null));

        Assert.Equal(expectedSddl, GetSecuritySddl(storedSecurity));
        Assert.Null(db.GetAccount(ContainerSid));
        Assert.Empty(db.GetAccount(AclHelper.AllApplicationPackagesSid)?.Grants ?? []);
        Assert.Null(db.GetAccount(InteractiveSid));
    }

    [Fact]
    public void PersistedAddGrant_WithOwnerSid_WhenSideEffectFailsAndSnapshotRestoreFails_RollbackRestoresOriginalOwnerSid()
    {
        var ownerRights = ReadOnly with { Own = true };
        var service = BuildServiceWithMockedNtfs(
            out var grantAceMock,
            out var ownerMock,
            out _,
            out var aclAccessorMock,
            out var db);

        aclAccessorMock.Setup(mock => mock.GetSecurity(ExistingDir))
            .Returns(CreateSecurityWithOwner(BuiltinAdministratorsSid));
        aclAccessorMock.Setup(mock => mock.SetOwnerAndAclWithFallback(
                ExistingDir,
                It.IsAny<FileSystemSecurity>()))
            .Throws(new InvalidOperationException("snapshot restore failed"));
        ownerMock.Setup(mock => mock.ChangeOwner(ExistingDir, UserSid, false));
        ownerMock.Setup(mock => mock.ChangeOwner(ExistingDir, BuiltinAdministratorsSid, false));
        _aclPermission.Setup(permission => permission.ResolveAccountGroupSids(UserSid))
            .Throws(new InvalidOperationException("side effect failed"));

        var ex = Assert.Throws<GrantOperationException>(() =>
            service.AddGrant(UserSid, ExistingDir, isDeny: false, ownerRights, confirm: null));

        Assert.Equal(GrantApplyFailureStep.GrantAclApply, ex.Step);
        Assert.Equal("side effect failed", ex.Cause.Message);
        var cleanupFailure = Assert.Single(ex.CleanupFailures);
        Assert.Equal(GrantApplyFailureStep.GrantAclRollback, cleanupFailure.Step);
        Assert.Equal("snapshot restore failed", cleanupFailure.Exception.Message);
        ownerMock.Verify(mock => mock.ChangeOwner(ExistingDir, UserSid, false), Times.Once);
        ownerMock.Verify(mock => mock.ChangeOwner(ExistingDir, BuiltinAdministratorsSid, false), Times.Once);
        grantAceMock.Verify(mock => mock.RevertAce(ExistingDir, UserSid, false), Times.Once);
        Assert.Null(db.GetAccount(UserSid));
    }

    [Fact]
    public void PersistedAddGrant_WithOwnerSid_WhenGrantRollbackFails_RollbackStillRestoresOriginalOwnerSid()
    {
        var ownerRights = ReadOnly with { Own = true };
        var service = BuildServiceWithMockedNtfs(
            out var grantAceMock,
            out var ownerMock,
            out _,
            out var aclAccessorMock,
            out _);

        aclAccessorMock.Setup(mock => mock.GetSecurity(ExistingDir))
            .Returns(CreateSecurityWithOwner(BuiltinAdministratorsSid));
        aclAccessorMock.Setup(mock => mock.SetOwnerAndAclWithFallback(
                ExistingDir,
                It.IsAny<FileSystemSecurity>()))
            .Throws(new InvalidOperationException("snapshot restore failed"));
        ownerMock.Setup(mock => mock.ChangeOwner(ExistingDir, UserSid, false));
        ownerMock.Setup(mock => mock.ChangeOwner(ExistingDir, BuiltinAdministratorsSid, false));
        grantAceMock.Setup(mock => mock.RevertAce(ExistingDir, UserSid, false))
            .Throws(new InvalidOperationException("ace rollback failed"));
        _aclPermission.Setup(permission => permission.ResolveAccountGroupSids(UserSid))
            .Throws(new InvalidOperationException("side effect failed"));

        var ex = Assert.Throws<GrantOperationException>(() =>
            service.AddGrant(UserSid, ExistingDir, isDeny: false, ownerRights, confirm: null));

        Assert.Equal(GrantApplyFailureStep.GrantAclApply, ex.Step);
        Assert.Equal("side effect failed", ex.Cause.Message);
        Assert.All(ex.CleanupFailures, failure =>
            Assert.Equal(GrantApplyFailureStep.GrantAclRollback, failure.Step));
        Assert.Equal(
            ["snapshot restore failed", "ace rollback failed"],
            ex.CleanupFailures.Select(failure => failure.Exception.Message).ToArray());
        ownerMock.Verify(mock => mock.ChangeOwner(ExistingDir, UserSid, false), Times.Once);
        ownerMock.Verify(mock => mock.ChangeOwner(ExistingDir, BuiltinAdministratorsSid, false), Times.Once);
    }

    [Fact]
    public void PersistedUpdateGrant_WithOwnerSid_WhenPriorGrantRollbackFails_RollbackStillRestoresOriginalOwnerSid()
    {
        var ownerRights = ReadOnly with { Own = true };
        var service = BuildServiceWithMockedNtfs(
            out var grantAceMock,
            out var ownerMock,
            out _,
            out var aclAccessorMock,
            out var db);
        db.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadOnly
        });

        aclAccessorMock.Setup(mock => mock.GetSecurity(ExistingDir))
            .Returns(CreateSecurityWithOwner(BuiltinAdministratorsSid));
        aclAccessorMock.Setup(mock => mock.SetOwnerAndAclWithFallback(
                ExistingDir,
                It.IsAny<FileSystemSecurity>()))
            .Throws(new InvalidOperationException("snapshot restore failed"));
        grantAceMock.Setup(mock => mock.ApplyAce(ExistingDir, UserSid, false, ownerRights, true));
        grantAceMock.Setup(mock => mock.ApplyAce(ExistingDir, UserSid, false, ReadOnly, true))
            .Throws(new InvalidOperationException("prior acl restore failed"));
        ownerMock.Setup(mock => mock.ChangeOwner(ExistingDir, UserSid, false));
        ownerMock.Setup(mock => mock.ChangeOwner(ExistingDir, BuiltinAdministratorsSid, false));
        _aclPermission.Setup(permission => permission.ResolveAccountGroupSids(UserSid))
            .Throws(new InvalidOperationException("side effect failed"));

        var ex = Assert.Throws<GrantOperationException>(() =>
            service.UpdateGrant(UserSid, ExistingDir, isDeny: false, ownerRights, confirm: null));

        Assert.Equal(GrantApplyFailureStep.GrantAclApply, ex.Step);
        Assert.Equal("side effect failed", ex.Cause.Message);
        Assert.All(ex.CleanupFailures, failure =>
            Assert.Equal(GrantApplyFailureStep.GrantAclRollback, failure.Step));
        Assert.Equal(
            ["snapshot restore failed", "prior acl restore failed"],
            ex.CleanupFailures.Select(failure => failure.Exception.Message).ToArray());
        ownerMock.Verify(mock => mock.ChangeOwner(ExistingDir, UserSid, false), Times.Once);
        ownerMock.Verify(mock => mock.ChangeOwner(ExistingDir, BuiltinAdministratorsSid, false), Times.Once);
    }

    [Fact]
    public void PersistedAddGrant_AclFailureAfterContainerSourceAddedToSharedInteractiveUserEntry_PreservesOtherSource()
    {
        _iuResolver.Setup(resolver => resolver.GetInteractiveUserSid()).Returns(InteractiveSid);
        var service = BuildServiceWithMockedNtfs(
            out var grantAceMock,
            out _,
            out _,
            out var aclAccessorMock,
            out var db);

        db.GetOrCreateAccount(InteractiveSid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadOnly,
            SourceSids = [OtherContainerSid]
        });
        db.GetOrCreateAccount(InteractiveSid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true,
            AllAppliedPaths = [ExistingDir],
            SourceSids = [OtherContainerSid]
        });

        var originalSecurity = new DirectorySecurity();
        originalSecurity.SetOwner(new SecurityIdentifier(BuiltinAdministratorsSid));
        originalSecurity.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(InteractiveSid),
            GrantRightsMapper.ReadMask,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow));
        var expectedSddl = GetSecuritySddl(originalSecurity);
        FileSystemSecurity storedSecurity = CloneSecurity(originalSecurity);

        aclAccessorMock.Setup(mock => mock.GetSecurity(ExistingDir))
            .Returns(() => CloneSecurity(storedSecurity));
        aclAccessorMock.Setup(mock => mock.SetOwnerAndAclWithFallback(
                ExistingDir,
                It.IsAny<FileSystemSecurity>()))
            .Callback<string, FileSystemSecurity>((_, security) => storedSecurity = CloneSecurity(security));

        grantAceMock.Setup(mock => mock.ApplyAce(ExistingDir, ContainerSid, false, ReadOnly, true))
            .Callback(() =>
            {
                var mutatedSecurity = new DirectorySecurity();
                mutatedSecurity.SetOwner(new SecurityIdentifier(BuiltinAdministratorsSid));
                mutatedSecurity.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(ContainerSid),
                    GrantRightsMapper.ReadMask,
                    InheritanceFlags.None,
                    PropagationFlags.None,
                    AccessControlType.Allow));
                storedSecurity = mutatedSecurity;

                db.GetOrCreateAccount(ContainerSid).Grants.Add(new GrantedPathEntry
                {
                    Path = ExistingDir,
                    IsDeny = false,
                    SavedRights = ReadOnly
                });
                db.GetOrCreateAccount(AclHelper.AllApplicationPackagesSid).Grants.Add(new GrantedPathEntry
                {
                    Path = ExistingDir,
                    IsTraverseOnly = true,
                    AllAppliedPaths = [ExistingDir],
                    SourceSids = [ContainerSid]
                });

                foreach (var entry in db.GetAccount(InteractiveSid)!.Grants
                             .Where(entry => string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase)))
                {
                    entry.SourceSids!.Add(ContainerSid);
                }

                throw new UnauthorizedAccessException("acl failed");
            });

        Assert.Throws<GrantOperationException>(() =>
            service.AddGrant(ContainerSid, ExistingDir, isDeny: false, ReadOnly, confirm: null));

        Assert.Equal(expectedSddl, GetSecuritySddl(storedSecurity));
        Assert.Null(db.GetAccount(ContainerSid));
        Assert.Empty(db.GetAccount(AclHelper.AllApplicationPackagesSid)?.Grants ?? []);
        var interactiveEntries = db.GetAccount(InteractiveSid)!.Grants;
        Assert.Equal(2, interactiveEntries.Count);
        Assert.All(interactiveEntries, entry => Assert.Equal([OtherContainerSid], entry.SourceSids));
    }

    [Fact]
    public void PersistedAddDenyGrant_AclFailureAfterDescriptorMutation_RestoresDescriptorWithoutAceLevelRollback()
    {
        var service = BuildServiceWithMockedNtfs(
            out var grantAceMock,
            out _,
            out _,
            out var aclAccessorMock,
            out var db);

        var originalSecurity = new DirectorySecurity();
        originalSecurity.SetOwner(new SecurityIdentifier(BuiltinAdministratorsSid));
        originalSecurity.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(UserSid),
            GrantRightsMapper.ReadMask,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Deny));
        originalSecurity.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(InteractiveSid),
            GrantRightsMapper.ReadMask,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow));
        var expectedSddl = GetSecuritySddl(originalSecurity);
        FileSystemSecurity storedSecurity = CloneSecurity(originalSecurity);

        aclAccessorMock.Setup(mock => mock.GetSecurity(ExistingDir))
            .Returns(() => CloneSecurity(storedSecurity));
        aclAccessorMock.Setup(mock => mock.SetOwnerAndAclWithFallback(
                ExistingDir,
                It.IsAny<FileSystemSecurity>()))
            .Callback<string, FileSystemSecurity>((_, security) => storedSecurity = CloneSecurity(security));

        grantAceMock.Setup(mock => mock.ApplyAce(ExistingDir, UserSid, true, DefaultDeny, true))
            .Callback(() =>
            {
                var mutatedSecurity = new DirectorySecurity();
                mutatedSecurity.SetOwner(new SecurityIdentifier(BuiltinAdministratorsSid));
                mutatedSecurity.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(UserSid),
                    GrantRightsMapper.ReadMask | GrantRightsMapper.ExecuteMask,
                    InheritanceFlags.None,
                    PropagationFlags.None,
                    AccessControlType.Deny));
                storedSecurity = mutatedSecurity;

                db.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
                {
                    Path = ExistingDir,
                    IsDeny = true,
                    SavedRights = DefaultDeny
                });

                throw new UnauthorizedAccessException("acl failed");
            });

        Assert.Throws<GrantOperationException>(() =>
            service.AddGrant(UserSid, ExistingDir, isDeny: true, DefaultDeny, confirm: () => true));

        Assert.Equal(expectedSddl, GetSecuritySddl(storedSecurity));
        Assert.Null(db.GetAccount(UserSid));
    }

    [Fact]
    public void PersistedAddGrant_ContainerInteractiveUserSyncFailure_RollsBackAddedTraverseAces()
    {
        const string targetFilePath = @"C:\Existing\TestDir\App.exe";
        _pathInfo.AddFile(targetFilePath);
        _iuResolver.Setup(resolver => resolver.GetInteractiveUserSid()).Returns(InteractiveSid);

        var service = BuildServiceWithMockedNtfs(
            out var grantAceMock,
            out _,
            out _,
            out var aclAccessorMock,
            out var db);

        var originalSecurity = CreateSecurityWithOwner(BuiltinAdministratorsSid);
        var expectedSddl = GetSecuritySddl(originalSecurity);
        FileSystemSecurity storedSecurity = CloneSecurity(originalSecurity);
        var removedTraversePaths = new List<string>();
        var effectiveTraversePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        aclAccessorMock.Setup(mock => mock.GetSecurity(targetFilePath))
            .Returns(() => CloneSecurity(storedSecurity));
        aclAccessorMock.Setup(mock => mock.SetOwnerAndAclWithFallback(
                targetFilePath,
                It.IsAny<FileSystemSecurity>()))
            .Callback<string, FileSystemSecurity>((_, security) => storedSecurity = CloneSecurity(security));

        _aclPermission.Setup(permission => permission.HasEffectiveRights(
                It.IsAny<FileSystemSecurity>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                TraverseRightsHelper.TraverseRights))
            .Returns<FileSystemSecurity, string, IReadOnlyList<string>, FileSystemRights>(
                (_, _, _, _) => false);

        _traverseAcl.Setup(mock => mock.AddAllowAce(
                It.IsAny<string>(),
                It.IsAny<SecurityIdentifier>()))
            .Callback<string, SecurityIdentifier>((path, sid) =>
            {
                effectiveTraversePaths.Add(path);
                TrackTraverseAceInTestSecurity(path, sid.Value);
            });
        _traverseAcl.Setup(mock => mock.RemoveTraverseOnlyAce(
                It.IsAny<string>(),
                It.IsAny<SecurityIdentifier>()))
            .Callback<string, SecurityIdentifier>((path, sid) =>
            {
                removedTraversePaths.Add(path);
                effectiveTraversePaths.Remove(path);
            });
        _traverseAcl.Setup(mock => mock.HasExplicitTraverseAceOrThrow(
                It.IsAny<string>(),
                It.IsAny<SecurityIdentifier>()))
            .Returns<string, SecurityIdentifier>((path, _) => effectiveTraversePaths.Contains(path));

        var applyAceCallCount = 0;
        grantAceMock.Setup(mock => mock.ApplyAce(It.IsAny<string>(), It.IsAny<string>(), false, ReadOnly, It.IsAny<bool>()))
            .Callback<string, string, bool, SavedRightsState, bool>((path, sid, _, _, _) =>
            {
                applyAceCallCount++;
                if (string.Equals(path, targetFilePath, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(sid, ContainerSid, StringComparison.OrdinalIgnoreCase))
                {
                    var mutatedSecurity = CreateSecurityWithOwner(BuiltinAdministratorsSid);
                    mutatedSecurity.AddAccessRule(new FileSystemAccessRule(
                        new SecurityIdentifier(ContainerSid),
                        GrantRightsMapper.ReadMask,
                        InheritanceFlags.None,
                        PropagationFlags.None,
                        AccessControlType.Allow));
                    storedSecurity = mutatedSecurity;
                    return;
                }

                throw new UnauthorizedAccessException("interactive user sync failed");
            });

        Assert.Throws<GrantOperationException>(() =>
            service.AddGrant(ContainerSid, targetFilePath, isDeny: false, ReadOnly, confirm: null));

        Assert.True(applyAceCallCount >= 2);
        Assert.NotEmpty(removedTraversePaths);
        Assert.Equal(expectedSddl, GetSecuritySddl(storedSecurity));
        Assert.Null(db.GetAccount(ContainerSid));
        Assert.Null(db.GetAccount(InteractiveSid));
    }

    [Fact]
    public void PersistedLooseningUpdate_AppliesBeforeSave()
    {
        var mainStore = new TestGrantIntentStore();
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadExecute
        });
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out var db,
            out var grantAceMock,
            out _);
        db.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadExecute
        });
        var events = new List<string>();
        grantAceMock.Setup(mock => mock.ApplyAce(ExistingDir, UserSid, false, ReadOnly, true))
            .Callback(() => events.Add("acl"));
        mainStore.SaveAction = () => events.Add("save");

        var result = service.UpdateGrant(UserSid, ExistingDir, isDeny: false, ReadOnly, confirm: null);

        Assert.Equal(["acl", "save"], events);
        Assert.True(result.GrantApplied);
        Assert.True(result.DatabaseModified);
        Assert.True(result.DurableSaveCompleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PersistedMixedRightsUpdate_UsesRemoveSaveAddOrdering(bool isDeny)
    {
        var initialRights = isDeny ? DefaultDeny with { Read = true } : ReadExecute;
        var updatedRights = isDeny ? DefaultDeny with { Execute = true } : ReadOnly with { Write = true };
        var mainStore = new TestGrantIntentStore();
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = isDeny,
            SavedRights = initialRights
        });
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out var db,
            out var grantAceMock,
            out _);
        db.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = isDeny,
            SavedRights = initialRights
        });
        var events = new List<string>();
        grantAceMock.Setup(mock => mock.RevertAce(ExistingDir, UserSid, isDeny))
            .Callback(() => events.Add("remove"));
        grantAceMock.Setup(mock => mock.ApplyAce(ExistingDir, UserSid, isDeny, updatedRights, true))
            .Callback(() => events.Add("add"));
        mainStore.SaveAction = () => events.Add("save");

        var result = service.UpdateGrant(
            UserSid,
            ExistingDir,
            isDeny,
            updatedRights,
            confirm: isDeny ? () => true : null);

        Assert.Equal(["remove", "save", "add"], events);
        Assert.True(result.GrantApplied);
        Assert.True(result.DatabaseModified);
        Assert.True(result.DurableSaveCompleted);
    }

    [Fact]
    public void PersistedLooseningUpdate_AclFailure_DoesNotMutateStore()
    {
        var mainStore = new TestGrantIntentStore();
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadExecute
        });
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out var db,
            out var grantAceMock,
            out _);
        db.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadExecute
        });
        grantAceMock.Setup(mock => mock.ApplyAce(ExistingDir, UserSid, false, ReadOnly, true))
            .Throws(new UnauthorizedAccessException("update failed"));

        Assert.Throws<GrantOperationException>(() =>
            service.UpdateGrant(UserSid, ExistingDir, isDeny: false, ReadOnly, confirm: null));

        var entry = Assert.Single(mainStore.GetEntries(UserSid));
        Assert.Equal(ReadExecute, entry.SavedRights);
    }

    [Fact]
    public void PersistedSwitchGrantMode_PreservesOwningStore_WhenStoreIsNotSpecified()
    {
        var mainStore = new TestGrantIntentStore();
        var additionalStore = new TestGrantIntentStore(@"C:\Configs\extra-c.rfn");
        additionalStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadOnly
        });
        var service = BuildStoreAwareService(
            mainStore,
            out var storeProvider,
            out _,
            out _,
            out _);
        storeProvider.AddLoadedStore(additionalStore);

        var result = service.SwitchGrantMode(
            UserSid,
            ExistingDir,
            newIsDeny: true,
            DefaultDeny,
            confirm: () => true);

        Assert.True(result.GrantApplied);
        Assert.Empty(mainStore.GetEntries(UserSid));
        var entry = Assert.Single(additionalStore.GetEntries(UserSid));
        Assert.True(entry.IsDeny);
        Assert.Equal(DefaultDeny, entry.SavedRights);
    }

    [Fact]
    public void PersistedSwitchGrantMode_UsesRemoveSaveAddOrdering()
    {
        var mainStore = new TestGrantIntentStore();
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadOnly
        });
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out var db,
            out var grantAceMock,
            out _);
        db.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadOnly
        });
        var events = new List<string>();
        grantAceMock.Setup(mock => mock.RevertAce(ExistingDir, UserSid, false))
            .Callback(() => events.Add("remove"));
        mainStore.SaveAction = () => events.Add("save");
        grantAceMock.Setup(mock => mock.ApplyAce(ExistingDir, UserSid, true, DefaultDeny, true))
            .Callback(() => events.Add("add"));

        var result = service.SwitchGrantMode(
            UserSid,
            ExistingDir,
            newIsDeny: true,
            DefaultDeny,
            confirm: () => true);

        Assert.Equal(["remove", "save", "add"], events);
        Assert.True(result.GrantApplied);
        Assert.True(result.DatabaseModified);
        Assert.True(result.DurableSaveCompleted);
    }

    [Fact]
    public void PersistedAddGrant_SaveFailure_ThrowsBeforeAclApply_AndRestoresStoreMutation()
    {
        var mainStore = new TestGrantIntentStore
        {
            SaveAction = () => throw new InvalidOperationException("save failed")
        };
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out _,
            out var grantAceMock,
            out _);

        var ex = Assert.Throws<GrantOperationException>(() =>
            service.AddGrant(UserSid, ExistingDir, isDeny: false, ReadOnly, confirm: null));

        Assert.Equal(GrantApplyFailureStep.GrantIntentSave, ex.Step);
        Assert.Equal(ExistingDir, ex.Path);
        Assert.Null(ex.ConfigPath);
        Assert.Equal("save failed", ex.Cause.Message);
        var cleanupFailure = Assert.Single(ex.CleanupFailures);
        Assert.Equal(GrantApplyFailureStep.RevertIntentSave, cleanupFailure.Step);
        Assert.Equal(2, mainStore.SaveCount);
        grantAceMock.Verify(mock => mock.ApplyAce(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<SavedRightsState>(),
            It.IsAny<bool>()), Times.Never);
        Assert.Empty(mainStore.GetEntries(UserSid));
    }

    [Fact]
    public void PersistedAddGrant_DenyWithoutConfirm_AppliesMutation()
    {
        var mainStore = new TestGrantIntentStore();
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out var db,
            out var grantAceMock,
            out _);

        var result =
            service.AddGrant(UserSid, ExistingDir, isDeny: true, DefaultDeny, confirm: null);

        Assert.True(result.GrantApplied);
        Assert.True(result.DatabaseModified);
        var storedEntry = Assert.Single(mainStore.GetEntries(UserSid));
        Assert.True(storedEntry.IsDeny);
        Assert.Equal(DefaultDeny, storedEntry.SavedRights);
        var runtimeEntry = Assert.Single(db.GetAccount(UserSid)!.Grants);
        Assert.True(runtimeEntry.IsDeny);
        Assert.Equal(DefaultDeny, runtimeEntry.SavedRights);
        grantAceMock.Verify(mock => mock.ApplyAce(
            ExistingDir,
            UserSid,
            true,
            DefaultDeny,
            true), Times.Once);
    }

    [Fact]
    public void PersistedAddGrant_SelectedStoreSaveFailure_ReportsConfigPath()
    {
        var mainStore = new TestGrantIntentStore();
        var additionalStore = new TestGrantIntentStore(@"C:\Configs\extra-save-fail.rfn")
        {
            SaveAction = () => throw new InvalidOperationException("additional save failed")
        };
        var service = BuildStoreAwareService(
            mainStore,
            out var storeProvider,
            out _,
            out var grantAceMock,
            out _);
        storeProvider.AddLoadedStore(additionalStore);

        var ex = Assert.Throws<GrantOperationException>(() =>
            service.AddGrant(UserSid, ExistingDir, isDeny: false, ReadOnly, confirm: null, store: additionalStore));

        Assert.Equal(GrantApplyFailureStep.GrantIntentSave, ex.Step);
        Assert.Equal(Path.GetFullPath(@"C:\Configs\extra-save-fail.rfn"), ex.ConfigPath);
        grantAceMock.Verify(mock => mock.ApplyAce(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<SavedRightsState>(),
            It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void PersistedAddGrant_AclFailure_RollsBackStoreAndSavesRevert()
    {
        var mainStore = new TestGrantIntentStore();
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out _,
            out var grantAceMock,
            out _);
        var saveSnapshots = new List<int>();
        mainStore.SaveAction = () => saveSnapshots.Add(mainStore.GetEntries(UserSid).Count);
        grantAceMock.Setup(mock => mock.ApplyAce(ExistingDir, UserSid, false, ReadOnly, true))
            .Throws(new UnauthorizedAccessException("acl failed"));

        var ex = Assert.Throws<GrantOperationException>(() =>
            service.AddGrant(UserSid, ExistingDir, isDeny: false, ReadOnly, confirm: null));

        Assert.Equal(GrantApplyFailureStep.GrantAclApply, ex.Step);
        Assert.Equal(ExistingDir, ex.Path);
        Assert.Empty(mainStore.GetEntries(UserSid));
        Assert.Equal(2, mainStore.SaveCount);
        Assert.Equal([1, 0], saveSnapshots);
        grantAceMock.Verify(mock => mock.RevertAce(ExistingDir, UserSid, false), Times.Never);
    }

    [Fact]
    public void PersistedWideningUpdate_MultiStoreSaveOrder_FailureReportsConfigPath_BeforeAclApply()
    {
        var mainStore = new TestGrantIntentStore();
        var additionalAStore = new TestGrantIntentStore(@"C:\Configs\a-order.rfn");
        var additionalZStore = new TestGrantIntentStore(@"C:\Configs\z-order.rfn");
        foreach (var store in new[] { mainStore, additionalAStore, additionalZStore })
        {
            store.AddEntry(UserSid, new GrantedPathEntry
            {
                Path = ExistingDir,
                IsDeny = false,
                SavedRights = ReadOnly
            });
        }

        var service = BuildStoreAwareService(
            mainStore,
            out var storeProvider,
            out var db,
            out var grantAceMock,
            out _);
        storeProvider.AddLoadedStore(additionalZStore);
        storeProvider.AddLoadedStore(additionalAStore);
        db.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadOnly
        });

        var events = new List<string>();
        mainStore.SaveAction = () => events.Add("main-save");
        additionalAStore.SaveAction = () => events.Add("a-save");
        additionalZStore.SaveAction = () =>
        {
            events.Add("z-save");
            Assert.Empty(mainStore.GetEntries(UserSid));
            var selectedEntry = Assert.Single(additionalAStore.GetEntries(UserSid));
            Assert.Equal(ReadExecute, selectedEntry.SavedRights);
            Assert.Empty(additionalZStore.GetEntries(UserSid));
            throw new InvalidOperationException("z save failed");
        };

        var ex = Assert.Throws<GrantOperationException>(() =>
            service.UpdateGrant(
                UserSid,
                ExistingDir,
                isDeny: false,
                ReadExecute,
                confirm: null,
                store: additionalAStore));

        Assert.Equal(GrantApplyFailureStep.GrantIntentSave, ex.Step);
        Assert.Equal(Path.GetFullPath(@"C:\Configs\z-order.rfn"), ex.ConfigPath);
        var cleanupFailure = Assert.Single(ex.CleanupFailures);
        Assert.Equal(GrantApplyFailureStep.RevertIntentSave, cleanupFailure.Step);
        Assert.Equal(Path.GetFullPath(@"C:\Configs\z-order.rfn"), cleanupFailure.ConfigPath);
        Assert.Equal(["main-save", "a-save", "z-save", "main-save", "a-save", "z-save"], events);
        grantAceMock.Verify(mock => mock.ApplyAce(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<SavedRightsState>(),
            It.IsAny<bool>()), Times.Never);
        Assert.Equal(2, mainStore.SaveCount);
        Assert.Equal(2, additionalAStore.SaveCount);
        Assert.Equal(2, additionalZStore.SaveCount);
        Assert.Equal(ReadOnly, Assert.Single(mainStore.GetEntries(UserSid)).SavedRights);
        Assert.Equal(ReadOnly, Assert.Single(additionalAStore.GetEntries(UserSid)).SavedRights);
        Assert.Equal(ReadOnly, Assert.Single(additionalZStore.GetEntries(UserSid)).SavedRights);
        Assert.Equal(ReadOnly, Assert.Single(
            db.GetAccount(UserSid)!.Grants,
            entry =>
                !entry.IsTraverseOnly &&
                !entry.IsDeny &&
                string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase)).SavedRights);
    }

    [Fact]
    public void PersistedMixedRightsUpdate_SaveFailure_RestoresTraverseSideEffects()
    {
        var mainStore = new TestGrantIntentStore
        {
            SaveAction = () => throw new InvalidOperationException("save failed")
        };
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadExecute
        });
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out var db,
            out _,
            out _);
        db.GetOrCreateAccount(UserSid).Grants.AddRange([
            new GrantedPathEntry
            {
                Path = ExistingDir,
                IsDeny = false,
                SavedRights = ReadExecute
            },
            new GrantedPathEntry
            {
                Path = ExistingDir,
                IsTraverseOnly = true
            }
        ]);

        Assert.Throws<GrantOperationException>(() =>
            service.UpdateGrant(
                UserSid,
                ExistingDir,
                isDeny: false,
                ReadOnly with { Write = true },
                confirm: null));

        Assert.Contains(db.GetAccount(UserSid)?.Grants ?? [],
            entry => !entry.IsTraverseOnly &&
                     !entry.IsDeny &&
                     string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase) &&
                     Equals(entry.SavedRights, ReadExecute));
        Assert.Contains(db.GetAccount(UserSid)?.Grants ?? [],
            entry => entry.IsTraverseOnly &&
                     string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PersistedSwitchGrantMode_SaveFailureBeforeAdd_DoesNotRevertNeverAppliedNewMode()
    {
        var mainStore = new TestGrantIntentStore
        {
            SaveAction = () => throw new InvalidOperationException("save failed")
        };
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadOnly
        });
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out var db,
            out var grantAceMock,
            out _);
        db.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadOnly
        });

        Assert.Throws<GrantOperationException>(() =>
            service.SwitchGrantMode(
                UserSid,
                ExistingDir,
                newIsDeny: true,
                DefaultDeny,
                confirm: () => true));

        grantAceMock.Verify(mock => mock.RevertAce(ExistingDir, UserSid, false), Times.Once);
        grantAceMock.Verify(mock => mock.RevertAce(ExistingDir, UserSid, true), Times.Never);
        Assert.Contains(mainStore.GetEntries(UserSid),
            entry => !entry.IsTraverseOnly &&
                     !entry.IsDeny &&
                     string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(mainStore.GetEntries(UserSid),
            entry => entry.IsDeny &&
                     string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PersistedUpdateGrant_PostApplySaveFailure_ReturnsWarning()
    {
        var mainStore = new TestGrantIntentStore
        {
            SaveAction = () => throw new InvalidOperationException("save failed")
        };
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadExecute
        });
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out var db,
            out var grantAceMock,
            out _);
        db.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadExecute
        });

        var result = service.UpdateGrant(UserSid, ExistingDir, isDeny: false, ReadOnly, confirm: null);

        var warning = Assert.Single(result.Warnings);
        Assert.True(result.GrantApplied);
        Assert.True(result.DatabaseModified);
        Assert.False(result.DurableSaveCompleted);
        Assert.Equal(GrantApplyFailureStep.PostGrantMutationSave, warning.Step);
        Assert.Equal(ExistingDir, warning.Path);
        Assert.Null(warning.ConfigPath);
        Assert.Equal("save failed", warning.Cause.Message);
        grantAceMock.Verify(mock => mock.ApplyAce(ExistingDir, UserSid, false, ReadOnly, true), Times.Once);
        var entry = Assert.Single(mainStore.GetEntries(UserSid));
        Assert.Equal(ReadOnly, entry.SavedRights);
        Assert.Equal(ReadOnly, db.GetAccount(UserSid)!.Grants.Single(existing =>
            !existing.IsTraverseOnly &&
            !existing.IsDeny &&
            string.Equals(existing.Path, ExistingDir, StringComparison.OrdinalIgnoreCase)).SavedRights);
    }

    [Fact]
    public void PersistedSwitchGrantMode_RollbackRestoresDenyOwnerState()
    {
        var denyOwnRights = DefaultDeny with { Own = true };
        var mainStore = new TestGrantIntentStore();
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = true,
            SavedRights = denyOwnRights
        });
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out var db,
            out var grantAceMock,
            out var ownerMock);
        db.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = true,
            SavedRights = denyOwnRights
        });
        grantAceMock.Setup(mock => mock.ApplyAce(ExistingDir, UserSid, false, ReadOnly, true))
            .Throws(new UnauthorizedAccessException("apply failed"));

        Assert.Throws<GrantOperationException>(() =>
            service.SwitchGrantMode(UserSid, ExistingDir, newIsDeny: false, ReadOnly, confirm: null));

        ownerMock.Verify(mock => mock.ResetOwner(ExistingDir, false), Times.Once);
        Assert.Contains(mainStore.GetEntries(UserSid),
            entry => entry.IsDeny &&
                     string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PersistedRemoveGrant_RemovesAclBeforeSaving()
    {
        var mainStore = new TestGrantIntentStore();
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadOnly
        });
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out var db,
            out var grantAceMock,
            out _);
        db.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadOnly
        });
        var events = new List<string>();
        grantAceMock.Setup(mock => mock.RevertAce(ExistingDir, UserSid, false))
            .Callback(() => events.Add("acl"));
        mainStore.SaveAction = () => events.Add("save");

        var result = service.RemoveGrant(UserSid, ExistingDir, isDeny: false);

        Assert.Equal(["acl", "save"], events);
        Assert.True(result.GrantApplied);
        Assert.True(result.DatabaseModified);
        Assert.True(result.DurableSaveCompleted);
        Assert.Empty(mainStore.GetEntries(UserSid));
    }

    [Fact]
    public void PersistedRemoveGrant_PostSaveFailure_ReturnsWarningAndKeepsRemovalState()
    {
        var mainStore = new TestGrantIntentStore
        {
            SaveAction = () => throw new InvalidOperationException("remove save failed")
        };
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadOnly
        });
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out var db,
            out _,
            out _);
        db.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadOnly
        });

        var result = service.RemoveGrant(UserSid, ExistingDir, isDeny: false);

        var warning = Assert.Single(result.Warnings);
        Assert.True(result.GrantApplied);
        Assert.True(result.DatabaseModified);
        Assert.False(result.DurableSaveCompleted);
        Assert.Equal(GrantApplyFailureStep.PostGrantRemoveSave, warning.Step);
        Assert.Equal(ExistingDir, warning.Path);
        Assert.Null(warning.ConfigPath);
        Assert.Equal("remove save failed", warning.Cause.Message);
        Assert.Empty(mainStore.GetEntries(UserSid));
        Assert.DoesNotContain(db.GetAccount(UserSid)?.Grants ?? [],
            entry => string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase) &&
                     entry is { IsDeny: false, IsTraverseOnly: false });
    }

    [Fact]
    public void PersistedRemoveGrant_RuntimeDbMissing_StillRevertsAcl()
    {
        var mainStore = new TestGrantIntentStore();
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadOnly
        });
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out _,
            out var grantAceMock,
            out _);

        service.RemoveGrant(UserSid, ExistingDir, isDeny: false);

        grantAceMock.Verify(mock => mock.RevertAce(ExistingDir, UserSid, false), Times.Once);
        Assert.Empty(mainStore.GetEntries(UserSid));
    }

    [Fact]
    public void UntrackGrant_DoesNotTouchNtfs_AndSavesStore()
    {
        var mainStore = new TestGrantIntentStore();
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadOnly
        });
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out _,
            out var grantAceMock,
            out _);

        var result = service.UntrackGrant(UserSid, ExistingDir, isDeny: false);

        Assert.False(result.GrantApplied);
        Assert.True(result.DatabaseModified);
        Assert.True(result.DurableSaveCompleted);
        Assert.Empty(mainStore.GetEntries(UserSid));
        grantAceMock.Verify(mock => mock.ApplyAce(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<SavedRightsState>(),
            It.IsAny<bool>()), Times.Never);
        grantAceMock.Verify(mock => mock.RevertAce(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void UntrackGrant_SaveFailure_ReturnsWarningAndKeepsDbOnlyCleanupState()
    {
        var mainStore = new TestGrantIntentStore
        {
            SaveAction = () => throw new InvalidOperationException("untrack save failed")
        };
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadOnly
        });
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out var db,
            out var grantAceMock,
            out _);
        db.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadOnly
        });

        var result = service.UntrackGrant(UserSid, ExistingDir, isDeny: false);

        var warning = Assert.Single(result.Warnings);
        Assert.False(result.GrantApplied);
        Assert.True(result.DatabaseModified);
        Assert.False(result.DurableSaveCompleted);
        Assert.Equal(GrantApplyFailureStep.UntrackGrantSave, warning.Step);
        Assert.Equal(ExistingDir, warning.Path);
        Assert.Null(warning.ConfigPath);
        Assert.Equal("untrack save failed", warning.Cause.Message);
        Assert.Empty(mainStore.GetEntries(UserSid));
        Assert.DoesNotContain(db.GetAccount(UserSid)?.Grants ?? [],
            entry => !entry.IsTraverseOnly &&
                     !entry.IsDeny &&
                     string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase));
        grantAceMock.Verify(mock => mock.ApplyAce(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<SavedRightsState>(),
            It.IsAny<bool>()), Times.Never);
        grantAceMock.Verify(mock => mock.RevertAce(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void UntrackGrant_DoesNotRemoveRuntimeTraverseEntry()
    {
        var mainStore = new TestGrantIntentStore();
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadOnly
        });
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out var db,
            out _,
            out _);
        db.GetOrCreateAccount(UserSid).Grants.AddRange([
            new GrantedPathEntry
            {
                Path = ExistingDir,
                IsDeny = false,
                SavedRights = ReadOnly
            },
            new GrantedPathEntry
            {
                Path = ExistingDir,
                IsTraverseOnly = true
            }
        ]);

        service.UntrackGrant(UserSid, ExistingDir, isDeny: false);

        Assert.DoesNotContain(db.GetAccount(UserSid)?.Grants ?? [],
            entry => !entry.IsTraverseOnly &&
                     string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(db.GetAccount(UserSid)?.Grants ?? [],
            entry => entry.IsTraverseOnly &&
                     string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PersistedUpdateGrant_RemovingAllowOwnership_ResetsOwner()
    {
        var mainStore = new TestGrantIntentStore();
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadExecute with { Own = true }
        });
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out _,
            out _,
            out var ownerMock);

        service.UpdateGrant(
            UserSid,
            ExistingDir,
            isDeny: false,
            ReadExecute with { Own = false },
            confirm: null);

        ownerMock.Verify(mock => mock.ResetOwner(ExistingDir, false), Times.Once);
    }

    [Fact]
    public void PersistedUpdateGrant_ResetOwnerFailure_RollbackRestoresOriginalOwnerSid()
    {
        var mainStore = new TestGrantIntentStore();
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadExecute with { Own = true }
        });
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out var db,
            out _,
            out var ownerMock);
        db.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadExecute with { Own = true }
        });
        ownerMock.Setup(mock => mock.ResetOwner(ExistingDir, false))
            .Throws(new UnauthorizedAccessException("owner reset failed"));

        var ex = Assert.Throws<GrantOperationException>(() =>
            service.UpdateGrant(
                UserSid,
                ExistingDir,
                isDeny: false,
                ReadExecute with { Own = false },
                confirm: null));

        Assert.Equal(GrantApplyFailureStep.GrantAclApply, ex.Step);
        ownerMock.Verify(mock => mock.ChangeOwner(ExistingDir, BuiltinAdministratorsSid, false), Times.Once);
        Assert.Contains(mainStore.GetEntries(UserSid),
            entry => !entry.IsDeny &&
                     string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase) &&
                     Equals(entry.SavedRights, ReadExecute with { Own = true }));
    }

    [Fact]
    public void PersistedAddTraverse_AllPathsAlreadyEffective_SavesIntentWithoutAclApply()
    {
        var mainStore = new TestGrantIntentStore();
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out var db,
            out _,
            out _);

        var result = service.AddTraverse(UserSid, ExistingDir, store: null);

        Assert.True(result.DatabaseModified);
        Assert.True(result.DurableSaveCompleted);
        Assert.False(result.TraverseApplied);
        Assert.Single(mainStore.GetEntries(UserSid));
        var entry = Assert.Single(mainStore.GetEntries(UserSid));
        Assert.Equal([ExistingDir, Path.GetPathRoot(ExistingDir)!], entry.AllAppliedPaths);
        Assert.Contains(db.GetAccount(UserSid)?.Grants ?? [],
            grant => grant.IsTraverseOnly &&
                     string.Equals(grant.Path, ExistingDir, StringComparison.OrdinalIgnoreCase));
        _traverseAcl.Verify(mock => mock.AddAllowAce(
            It.IsAny<string>(),
            It.IsAny<SecurityIdentifier>()), Times.Never);
    }

    [Fact]
    public void PersistedAddTraverse_AclFailure_RollsBackOnlyAppliedParentPathsAndRestoresStore()
    {
        UseTraverseAclBackedEffectiveRights();
        var mainStore = new TestGrantIntentStore();
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out _,
            out _,
            out _);
        var addCallCount = 0;
        _traverseAcl.Setup(mock => mock.AddAllowAce(
                It.IsAny<string>(),
                It.IsAny<SecurityIdentifier>()))
            .Callback<string, SecurityIdentifier>((path, sid) =>
            {
                addCallCount++;
                TrackTraverseAceInTestSecurity(path, sid.Value);
                if (addCallCount == 2)
                    throw new UnauthorizedAccessException($"failed on {path}");
            });

        var ex = Assert.Throws<GrantOperationException>(() =>
            service.AddTraverse(UserSid, ExistingDir, store: null));

        Assert.Equal(GrantApplyFailureStep.TraverseAclApply, ex.Step);
        Assert.Empty(mainStore.GetEntries(UserSid));
        _traverseAcl.Verify(mock => mock.RemoveTraverseOnlyAce(
            Path.GetPathRoot(ExistingDir)!,
            It.Is<SecurityIdentifier>(sid => sid.Value == UserSid)), Times.Once);
        _traverseAcl.Verify(mock => mock.RemoveTraverseOnlyAce(
            ExistingDir,
            It.Is<SecurityIdentifier>(sid => sid.Value == UserSid)), Times.Never);
    }

    [Fact]
    public void PersistedAddTraverse_VerificationFailure_AppendsRollbackCleanupFailure()
    {
        _aclPermission.Setup(permission => permission.HasEffectiveRights(
                It.IsAny<FileSystemSecurity>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                TraverseRightsHelper.TraverseRights))
            .Returns(false);
        var mainStore = new TestGrantIntentStore();
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out _,
            out _,
            out _);
        _traverseAcl.Setup(mock => mock.RemoveTraverseOnlyAce(
                ExistingDir,
                It.IsAny<SecurityIdentifier>()))
            .Throws(new InvalidOperationException("rollback failed"));

        var ex = Assert.Throws<GrantOperationException>(() =>
            service.AddTraverse(UserSid, ExistingDir, store: null));

        Assert.Equal(GrantApplyFailureStep.TraverseEffectiveAccessValidation, ex.Step);
        var cleanupFailure = Assert.Single(ex.CleanupFailures);
        Assert.Equal(GrantApplyFailureStep.TraverseAclRollback, cleanupFailure.Step);
        Assert.Equal("rollback failed", cleanupFailure.Exception.Message);
        Assert.Empty(mainStore.GetEntries(UserSid));
    }

    [Fact]
    public void PersistedAddTraverse_SpecificContainer_StoresSharedTraverseAndAppliesAllApplicationPackagesAce()
    {
        UseTraverseAclBackedEffectiveRights();
        var mainStore = new TestGrantIntentStore();
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out _,
            out _,
            out _);
        _traverseAcl.Setup(mock => mock.AddAllowAce(
                It.IsAny<string>(),
                It.IsAny<SecurityIdentifier>()))
            .Callback<string, SecurityIdentifier>((path, sid) =>
                TrackTraverseAceInTestSecurity(path, sid.Value));

        var result = service.AddTraverse(ContainerSid, ExistingDir, store: null);

        Assert.True(result.DatabaseModified);
        var entry = Assert.Single(mainStore.GetEntries(AclHelper.AllApplicationPackagesSid));
        Assert.Contains(ContainerSid, entry.SourceSids ?? []);
        _traverseAcl.Verify(mock => mock.AddAllowAce(
            It.IsAny<string>(),
            It.Is<SecurityIdentifier>(sid => sid.Value == AclHelper.AllApplicationPackagesSid)), Times.AtLeastOnce);
        _traverseAcl.Verify(mock => mock.AddAllowAce(
            It.IsAny<string>(),
            It.Is<SecurityIdentifier>(sid => sid.Value == ContainerSid)), Times.Never);
    }

    [Fact]
    public void PersistedAddTraverse_SpecificContainer_PreservesManualSharedTraverseOwnership()
    {
        var mainStore = new TestGrantIntentStore();
        mainStore.AddEntry(AclHelper.AllApplicationPackagesSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true
        });
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out var db,
            out _,
            out _);
        db.GetOrCreateAccount(AclHelper.AllApplicationPackagesSid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true
        });

        var result = service.AddTraverse(ContainerSid, ExistingDir, store: null);

        Assert.True(result.DatabaseModified);
        var storeEntry = Assert.Single(mainStore.GetEntries(AclHelper.AllApplicationPackagesSid));
        Assert.Null(storeEntry.SourceSids);
        var runtimeEntry = Assert.Single(db.GetAccount(AclHelper.AllApplicationPackagesSid)!.Grants);
        Assert.Null(runtimeEntry.SourceSids);
    }

    [Fact]
    public void PersistedAddTraverse_SpecificContainer_SaveFailure_PreservesManualSharedRuntimeTraverseEntry()
    {
        var additionalStore = new TestGrantIntentStore(@"C:\Configs\traverse-container-save-fail.rfn")
        {
            SaveAction = () => throw new InvalidOperationException("save failed")
        };
        var service = BuildStoreAwareService(
            new TestGrantIntentStore(),
            out var storeProvider,
            out var db,
            out _,
            out _);
        storeProvider.AddLoadedStore(additionalStore);
        db.GetOrCreateAccount(AclHelper.AllApplicationPackagesSid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true,
            SourceSids = null
        });

        var ex = Assert.Throws<GrantOperationException>(() =>
            service.AddTraverse(ContainerSid, ExistingDir, store: additionalStore));

        Assert.Equal(GrantApplyFailureStep.TraverseIntentSave, ex.Step);
        var runtimeEntry = Assert.Single(db.GetAccount(AclHelper.AllApplicationPackagesSid)!.Grants);
        Assert.True(runtimeEntry.IsTraverseOnly);
        Assert.Null(runtimeEntry.SourceSids);
    }

    [Fact]
    public void PersistedAddTraverse_SaveFailure_RestoresStoreAndRuntimeTraverseState()
    {
        var additionalStore = new TestGrantIntentStore(@"C:\Configs\traverse-save-fail.rfn")
        {
            SaveAction = () => throw new InvalidOperationException("save failed")
        };
        var service = BuildStoreAwareService(
            new TestGrantIntentStore(),
            out var storeProvider,
            out var db,
            out _,
            out _);
        storeProvider.AddLoadedStore(additionalStore);

        var ex = Assert.Throws<GrantOperationException>(() =>
            service.AddTraverse(UserSid, ExistingDir, store: additionalStore));

        Assert.Equal(GrantApplyFailureStep.TraverseIntentSave, ex.Step);
        var cleanupFailure = Assert.Single(ex.CleanupFailures);
        Assert.Equal(GrantApplyFailureStep.RevertIntentSave, cleanupFailure.Step);
        Assert.Equal(2, additionalStore.SaveCount);
        Assert.Empty(additionalStore.GetEntries(UserSid));
        Assert.Null(db.GetAccount(UserSid));
    }

    [Fact]
    public void PersistedAddAndRemoveTraverse_PreservesManualInteractiveUserMirrorEntry()
    {
        UseTraverseAclBackedEffectiveRights();
        var db = new AppDatabase();
        var service = BuildServiceWithIuResolver(db, InteractiveSid);
        db.GetOrCreateAccount(InteractiveSid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true
        });
        _traverseAcl.Setup(mock => mock.AddAllowAce(
                It.IsAny<string>(),
                It.IsAny<SecurityIdentifier>()))
            .Callback<string, SecurityIdentifier>((path, sid) =>
                TrackTraverseAceInTestSecurity(path, sid.Value));

        service.AddTraverse(ContainerSid, ExistingDir, store: null);
        service.RemoveTraverse(ContainerSid, ExistingDir);

        var entry = db.GetAccount(InteractiveSid)?.Grants.SingleOrDefault(grant =>
            grant.IsTraverseOnly &&
            string.Equals(grant.Path, ExistingDir, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(entry);
        Assert.Null(entry!.SourceSids);
    }

    [Fact]
    public void PersistedRemoveTraverse_RemovesAclBeforeSaving()
    {
        var mainStore = new TestGrantIntentStore();
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true,
            AllAppliedPaths = [ExistingDir]
        });
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out _,
            out _,
            out _);
        var events = new List<string>();
        _traverseAcl.Setup(mock => mock.RemoveTraverseOnlyAce(
                ExistingDir,
                It.IsAny<SecurityIdentifier>()))
            .Callback(() => events.Add("acl"));
        mainStore.SaveAction = () => events.Add("save");

        var result = service.RemoveTraverse(UserSid, ExistingDir);

        Assert.Equal(["acl", "save"], events);
        Assert.True(result.TraverseApplied);
        Assert.True(result.DatabaseModified);
        Assert.True(result.DurableSaveCompleted);
        Assert.Empty(mainStore.GetEntries(UserSid));
    }

    [Fact]
    public void PersistedRemoveTraverse_PostSaveFailure_ReturnsWarningAndKeepsRemovalState()
    {
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
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out var db,
            out _,
            out _);
        db.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true,
            AllAppliedPaths = [ExistingDir]
        });

        var result = service.RemoveTraverse(UserSid, ExistingDir);

        var warning = Assert.Single(result.Warnings);
        Assert.True(result.TraverseApplied);
        Assert.True(result.DatabaseModified);
        Assert.False(result.DurableSaveCompleted);
        Assert.Equal(GrantApplyFailureStep.PostTraverseRemoveSave, warning.Step);
        Assert.Equal(ExistingDir, warning.Path);
        Assert.Null(warning.ConfigPath);
        Assert.Equal("save failed", warning.Cause.Message);
        Assert.Empty(mainStore.GetEntries(UserSid));
        Assert.DoesNotContain(db.GetAccount(UserSid)?.Grants ?? [],
            entry => entry.IsTraverseOnly &&
                     string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PersistedRemoveTraverse_UsesUnionOfTrackedAppliedPathsAcrossStores()
    {
        var mainStore = new TestGrantIntentStore();
        var additionalStore = new TestGrantIntentStore(@"C:\Configs\traverse-remove-union.rfn");
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true,
            AllAppliedPaths = [ExistingDir]
        });
        additionalStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true,
            AllAppliedPaths = [Path.GetPathRoot(ExistingDir)!]
        });
        var service = BuildStoreAwareService(
            mainStore,
            out var storeProvider,
            out _,
            out _,
            out _);
        storeProvider.AddLoadedStore(additionalStore);

        service.RemoveTraverse(UserSid, ExistingDir);

        _traverseAcl.Verify(mock => mock.RemoveTraverseOnlyAce(
            ExistingDir,
            It.Is<SecurityIdentifier>(sid => sid.Value == UserSid)), Times.Once);
        _traverseAcl.Verify(mock => mock.RemoveTraverseOnlyAce(
            Path.GetPathRoot(ExistingDir)!,
            It.Is<SecurityIdentifier>(sid => sid.Value == UserSid)), Times.Once);
    }

    [Fact]
    public void UntrackTraverse_DoesNotTouchNtfs_AndSavesStore()
    {
        var mainStore = new TestGrantIntentStore();
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true,
            AllAppliedPaths = [ExistingDir]
        });
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out _,
            out _,
            out _);

        var result = service.UntrackTraverse(UserSid, ExistingDir);

        Assert.False(result.TraverseApplied);
        Assert.True(result.DatabaseModified);
        Assert.True(result.DurableSaveCompleted);
        Assert.Empty(mainStore.GetEntries(UserSid));
        _traverseAcl.Verify(mock => mock.AddAllowAce(
            It.IsAny<string>(),
            It.IsAny<SecurityIdentifier>()), Times.Never);
        _traverseAcl.Verify(mock => mock.RemoveTraverseOnlyAce(
            It.IsAny<string>(),
            It.IsAny<SecurityIdentifier>()), Times.Never);
    }

    [Fact]
    public void UntrackTraverse_SaveFailure_ReturnsWarningAndKeepsDbOnlyCleanupState()
    {
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
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out var db,
            out _,
            out _);
        db.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true,
            AllAppliedPaths = [ExistingDir]
        });

        var result = service.UntrackTraverse(UserSid, ExistingDir);

        var warning = Assert.Single(result.Warnings);
        Assert.False(result.TraverseApplied);
        Assert.True(result.DatabaseModified);
        Assert.False(result.DurableSaveCompleted);
        Assert.Equal(GrantApplyFailureStep.UntrackTraverseSave, warning.Step);
        Assert.Equal(ExistingDir, warning.Path);
        Assert.Null(warning.ConfigPath);
        Assert.Equal("save failed", warning.Cause.Message);
        Assert.Empty(mainStore.GetEntries(UserSid));
        Assert.DoesNotContain(db.GetAccount(UserSid)?.Grants ?? [],
            entry => entry.IsTraverseOnly &&
                     string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FixTraverseAcl_ReappliesMissingAceWithoutSavingStore()
    {
        UseTraverseAclBackedEffectiveRights();
        var mainStore = new TestGrantIntentStore();
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true,
            AllAppliedPaths = [ExistingDir]
        });
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out _,
            out _,
            out _);
        _traverseAcl.Setup(mock => mock.AddAllowAce(
                It.IsAny<string>(),
                It.IsAny<SecurityIdentifier>()))
            .Callback<string, SecurityIdentifier>((path, sid) =>
                TrackTraverseAceInTestSecurity(path, sid.Value));

        var result = service.FixTraverseAcl(UserSid, ExistingDir);

        Assert.True(result.TraverseApplied);
        Assert.Equal(0, mainStore.SaveCount);
        _traverseAcl.Verify(mock => mock.AddAllowAce(
            ExistingDir,
            It.Is<SecurityIdentifier>(sid => sid.Value == UserSid)), Times.Once);
    }

    [Fact]
    public void FixTraverseAcl_UsesUnionOfTrackedAppliedPathsAcrossStores()
    {
        UseTraverseAclBackedEffectiveRights();
        var mainStore = new TestGrantIntentStore();
        var additionalStore = new TestGrantIntentStore(@"C:\Configs\traverse-fix-union.rfn");
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true,
            AllAppliedPaths = [ExistingDir]
        });
        additionalStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true,
            AllAppliedPaths = [Path.GetPathRoot(ExistingDir)!]
        });
        var service = BuildStoreAwareService(
            mainStore,
            out var storeProvider,
            out _,
            out _,
            out _);
        storeProvider.AddLoadedStore(additionalStore);
        _traverseAcl.Setup(mock => mock.AddAllowAce(
                It.IsAny<string>(),
                It.IsAny<SecurityIdentifier>()))
            .Callback<string, SecurityIdentifier>((path, sid) =>
                TrackTraverseAceInTestSecurity(path, sid.Value));

        service.FixTraverseAcl(UserSid, ExistingDir);

        _traverseAcl.Verify(mock => mock.AddAllowAce(
            ExistingDir,
            It.Is<SecurityIdentifier>(sid => sid.Value == UserSid)), Times.Once);
        _traverseAcl.Verify(mock => mock.AddAllowAce(
            Path.GetPathRoot(ExistingDir)!,
            It.Is<SecurityIdentifier>(sid => sid.Value == UserSid)), Times.Once);
    }

    // --- UpdateGrant ---

    [Fact]
    public void UpdateGrant_ChangesRightsOnExistingEntry()
    {
        // Arrange: add a grant then update it with different rights
        _service.AddGrant(UserSid, TestPath, isDeny: false, ReadOnly);

        // Act
        _service.UpdateGrant(UserSid, TestPath, isDeny: false, ReadExecute);

        // Assert: DB entry has new SavedRights
        var entry = _database.GetAccount(UserSid)!.Grants
            .First(e => e is { IsTraverseOnly: false, IsDeny: false });
        Assert.NotNull(entry.SavedRights);
        Assert.True(entry.SavedRights!.Execute);
    }

    [Fact]
    public void UpdateGrant_AppliesNtfsAce()
    {
        // Arrange
        _service.AddGrant(UserSid, TestPath, isDeny: false, ReadOnly);

        // Reset the mock call count after AddGrant
        _explicitAceAccessor.Invocations.Clear();

        // Act
        _service.UpdateGrant(UserSid, TestPath, isDeny: false, ReadExecute);

        // Assert: NTFS ACE re-applied exactly once
        _explicitAceAccessor.Verify(a => a.ApplyExplicitAce(
            It.IsAny<string>(), UserSid, AccessControlType.Allow, It.IsAny<FileSystemRights>()),
            Times.Once);
    }

    [Fact]
    public void UpdateGrant_WithOwnerSid_CallsChangeOwnerWithCorrectSid()
    {
        // Arrange: mock owner service directly so ChangeOwner can be verified without real NTFS calls.
        var ownerRights = ReadExecute with { Own = true };
        var service = BuildServiceWithMockedNtfs(out _, out var ownerMock, out _, out _, out var db);

        // Pre-populate DB entry directly so no NTFS calls from AddGrant interfere with verification.
        db.GetOrCreateAccount(UserSid).Grants.Add(
            new GrantedPathEntry { Path = TestPath, IsDeny = false, SavedRights = ReadOnly });

        // Act
        service.UpdateGrant(UserSid, TestPath, isDeny: false, ownerRights);

        // Assert: ChangeOwner called with the account SID when Own is requested.
        ownerMock.Verify(n => n.ChangeOwner(It.IsAny<string>(), UserSid, false), Times.Once);
    }

    [Fact]
    public void UpdateGrant_LowIntegritySid_IgnoresOwnerAndStoresOwnerOff()
    {
        var ownerRights = ReadExecute with { Own = true };
        var service = BuildServiceWithMockedNtfs(out _, out var ownerMock, out _, out _, out var db);
        db.GetOrCreateAccount(AclHelper.LowIntegritySid).Grants.Add(
            new GrantedPathEntry { Path = TestPath, IsDeny = false, SavedRights = ReadOnly });

        service.UpdateGrant(AclHelper.LowIntegritySid, TestPath, isDeny: false, ownerRights);

        var entry = db.GetAccount(AclHelper.LowIntegritySid)!.Grants
            .First(e => e is { IsTraverseOnly: false, IsDeny: false });
        Assert.False(entry.SavedRights!.Own);
        ownerMock.Verify(n => n.ChangeOwner(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void UpdateGrant_ModeUnchanged_NoNewEntryAdded()
    {
        // Arrange: single allow grant
        _service.AddGrant(UserSid, TestPath, isDeny: false, ReadOnly);

        // Act: update (same mode, same path)
        _service.UpdateGrant(UserSid, TestPath, isDeny: false, ReadExecute);

        // Assert: still only one non-traverse allow entry
        var grants = _database.GetAccount(UserSid)!.Grants
            .Where(e => e is { IsTraverseOnly: false, IsDeny: false }).ToList();
        Assert.Single(grants);
    }

    [Fact]
    public void RestoreGrant_LegacySnapshot_RestoresNullSavedRightsExactly()
    {
        var mainStore = new TestGrantIntentStore();
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadExecute
        });
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out var db,
            out _,
            out _);
        db.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadExecute
        });

        var result = service.RestoreGrant(
            UserSid,
            ExistingDir,
            isDeny: false,
            new GrantIntentRestoreSnapshot(
                new GrantedPathEntry
                {
                    Path = ExistingDir,
                    IsDeny = false,
                    SavedRights = null
                },
                [new GrantIntentRestoreLocation(new GrantIntentStoreIdentity(null), new GrantedPathEntry
                {
                    Path = ExistingDir,
                    IsDeny = false,
                    SavedRights = null
                })]));

        var entry = db.GetAccount(UserSid)!.Grants
            .Single(e => e is { IsTraverseOnly: false, IsDeny: false } &&
                string.Equals(e.Path, ExistingDir, StringComparison.OrdinalIgnoreCase));
        var storedEntry = Assert.Single(mainStore.GetEntries(UserSid));
        Assert.Null(entry.SavedRights);
        Assert.Null(storedEntry.SavedRights);
        Assert.True(result.DatabaseModified);
        Assert.True(result.DurableSaveCompleted);
    }

    [Fact]
    public void RestoreGrant_PostApplySaveFailure_ReturnsWarningAndKeepsCompletedState()
    {
        var mainStore = new TestGrantIntentStore
        {
            SaveAction = () => throw new InvalidOperationException("save failed")
        };
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadExecute
        });
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out var db,
            out var grantAceMock,
            out _);
        db.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadExecute
        });

        var result = service.RestoreGrant(
            UserSid,
            ExistingDir,
            isDeny: false,
            new GrantIntentRestoreSnapshot(
                new GrantedPathEntry
                {
                    Path = ExistingDir,
                    IsDeny = false,
                    SavedRights = ReadOnly
                },
                [new GrantIntentRestoreLocation(new GrantIntentStoreIdentity(null), new GrantedPathEntry
                {
                    Path = ExistingDir,
                    IsDeny = false,
                    SavedRights = ReadOnly
                })]));

        var warning = Assert.Single(result.Warnings);
        Assert.True(result.GrantApplied);
        Assert.True(result.DatabaseModified);
        Assert.False(result.DurableSaveCompleted);
        Assert.Equal(GrantApplyFailureStep.PostGrantMutationSave, warning.Step);
        Assert.Equal(ExistingDir, warning.Path);
        Assert.Null(warning.ConfigPath);
        Assert.Equal("save failed", warning.Cause.Message);
        grantAceMock.Verify(mock => mock.ApplyAce(ExistingDir, UserSid, false, ReadOnly, true), Times.Once);
        Assert.Equal(ReadOnly, Assert.Single(mainStore.GetEntries(UserSid)).SavedRights);
        Assert.Equal(ReadOnly, db.GetAccount(UserSid)!.Grants.Single(entry =>
            !entry.IsTraverseOnly &&
            !entry.IsDeny &&
            string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase)).SavedRights);
    }

    [Fact]
    public void RestoreGrant_MixedAllowRights_UsesRemoveSaveAddOrdering()
    {
        var mainStore = new TestGrantIntentStore();
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadExecute
        });
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out var db,
            out var grantAceMock,
            out _);
        db.GetOrCreateAccount(UserSid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadExecute
        });
        var restoredRights = ReadOnly with { Write = true };
        var events = new List<string>();
        grantAceMock.Setup(mock => mock.RevertAce(ExistingDir, UserSid, false))
            .Callback(() => events.Add("remove"));
        mainStore.SaveAction = () => events.Add("save");
        grantAceMock.Setup(mock => mock.ApplyAce(
                ExistingDir,
                UserSid,
                false,
                restoredRights,
                true))
            .Callback(() => events.Add("add"));

        var result = service.RestoreGrant(
            UserSid,
            ExistingDir,
            isDeny: false,
            new GrantIntentRestoreSnapshot(
                new GrantedPathEntry
                {
                    Path = ExistingDir,
                    IsDeny = false,
                    SavedRights = restoredRights
                },
                [new GrantIntentRestoreLocation(new GrantIntentStoreIdentity(null), new GrantedPathEntry
                {
                    Path = ExistingDir,
                    IsDeny = false,
                    SavedRights = restoredRights
                })]));

        Assert.Equal(["remove", "save", "add"], events);
        Assert.True(result.GrantApplied);
        Assert.True(result.DatabaseModified);
        Assert.True(result.DurableSaveCompleted);
    }

    // --- FixGrant ---

    [Fact]
    public void FixGrant_ExistingEntry_ReappliesNtfsAceWithoutDbChange()
    {
        // Arrange: pre-populate the DB entry directly (bypassing NTFS)
        _database.GetOrCreateAccount(UserSid).Grants.Add(
            new GrantedPathEntry { Path = TestPath, IsDeny = false, SavedRights = ReadOnly });

        // Act
        _service.FixGrant(UserSid, TestPath, isDeny: false);

        // Assert: NTFS ACE applied once (no prior AddGrant calls — DB was populated directly)
        _explicitAceAccessor.Verify(a => a.ApplyExplicitAce(
            It.IsAny<string>(), UserSid, AccessControlType.Allow, It.IsAny<FileSystemRights>()),
            Times.Once);

        // DB entry unchanged (still has SavedRights = ReadOnly; FixGrant does not modify SavedRights)
        var entry = _database.GetAccount(UserSid)!.Grants
            .First(e => e is { IsTraverseOnly: false, IsDeny: false });
        Assert.NotNull(entry.SavedRights);
        Assert.False(entry.SavedRights!.Execute);
    }

    [Fact]
    public void FixGrant_NoEntryInDb_DoesNotApplyAce()
    {
        // No DB entry for TestPath → FixGrant is a no-op
        _service.FixGrant(UserSid, TestPath, isDeny: false);

        _explicitAceAccessor.Verify(a => a.ApplyExplicitAce(
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<AccessControlType>(), It.IsAny<FileSystemRights>()),
            Times.Never);
    }

    [Fact]
    public void FixGrant_NullSavedRights_UsesDefaultForMode()
    {
        // Arrange: DB entry with null SavedRights (legacy entry)
        _database.GetOrCreateAccount(UserSid).Grants.Add(
            new GrantedPathEntry { Path = TestPath, IsDeny = false, SavedRights = null });

        // Act: FixGrant should use DefaultForMode (Read=true) rather than throwing
        _service.FixGrant(UserSid, TestPath, isDeny: false);

        // Assert: ACE applied once using default rights
        _explicitAceAccessor.Verify(a => a.ApplyExplicitAce(
            It.IsAny<string>(), UserSid, AccessControlType.Allow, It.IsAny<FileSystemRights>()),
            Times.Once);
    }

    [Fact]
    public void FixGrantAcl_ReappliesTrackedGrantWithoutSavingStore()
    {
        var mainStore = new TestGrantIntentStore();
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadOnly
        });
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out _,
            out var grantAceMock,
            out _);

        var result = service.FixGrantAcl(UserSid, ExistingDir, isDeny: false);

        Assert.True(result.GrantApplied);
        Assert.Equal(0, mainStore.SaveCount);
        grantAceMock.Verify(mock => mock.ApplyAce(ExistingDir, UserSid, false, ReadOnly, true), Times.Once);
    }

    [Fact]
    public void FixGrantAcl_AclFailure_ThrowsGrantOperationException()
    {
        var mainStore = new TestGrantIntentStore();
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadOnly
        });
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out _,
            out var grantAceMock,
            out _);
        grantAceMock.Setup(mock => mock.ApplyAce(ExistingDir, UserSid, false, ReadOnly, true))
            .Throws(new UnauthorizedAccessException("fix failed"));

        var ex = Assert.Throws<GrantOperationException>(() =>
            service.FixGrantAcl(UserSid, ExistingDir, isDeny: false));

        Assert.Equal(GrantApplyFailureStep.FixGrantAclApply, ex.Step);
        Assert.Equal(ExistingDir, ex.Path);
        Assert.Equal(0, mainStore.SaveCount);
    }

    // --- FixTraverse ---

    [Fact]
    public void FixTraverse_ExistingEntry_DoesNotDuplicateDbEntry()
    {
        // Arrange: pre-populate traverse entry for TestPath
        _database.GetOrCreateAccount(UserSid).Grants.Add(
            new GrantedPathEntry { Path = TestPath, IsTraverseOnly = true });

        // Act
        _service.FixTraverse(UserSid, TestPath);

        // Assert: no duplicate traverse entry added (FixTraverse updates AllAppliedPaths on the existing
        // entry but does not insert a new one)
        var traverseEntries = _database.GetAccount(UserSid)!.Grants
            .Where(e => e is { IsTraverseOnly: true, Path: TestPath }).ToList();
        Assert.Single(traverseEntries); // still exactly one
    }

    [Fact]
    public void FixTraverse_ReturnsVisitedPaths()
    {
        // Arrange: use a path that exists so AncestorTraverseGranter visits at least one ancestor
        var tempDir = ExistingDir;
        _database.GetOrCreateAccount(UserSid).Grants.Add(
            new GrantedPathEntry { Path = tempDir, IsTraverseOnly = true });

        // Act
        var visited = _service.FixTraverse(UserSid, tempDir);

        // Assert: at least one ancestor path visited
        Assert.NotEmpty(visited);
    }

    // --- EnsureAccess ---

    [Fact]
    public void EnsureAccess_AlreadySufficientWithDbEntry_DoesNotApplyAce()
    {
        // Arrange: existing DB entry + disk ACE matching DB + effective rights sufficient.
        // A real existing directory is used so pathExists=true and the auto-fix check runs.
        // The security descriptor has an explicit ReadMask ACE matching the DB SavedRights,
        // so needsFix=false. Combined with NeedsPermissionGrant=false, EnsureAccess returns no-op.
        var tempDir = ExistingDir;

        // Blank security (no inherited ACEs) with exactly one explicit allow ACE matching ReadOnly.
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(UserSid), GrantRightsMapper.ReadMask,
            InheritanceFlags.None, PropagationFlags.None, AccessControlType.Allow));
        _fileSecurityAccessor.Setup(a => a.GetSecurity(tempDir)).Returns(security);

        // Pre-populate DB entry with matching SavedRights
        _database.GetOrCreateAccount(UserSid).Grants.Add(
            new GrantedPathEntry { Path = tempDir, IsDeny = false, SavedRights = ReadOnly });

        // Effective rights already sufficient
        _aclPermission.Setup(p => p.NeedsPermissionGrant(tempDir, UserSid,
                It.IsAny<FileSystemRights>(), It.IsAny<bool>()))
            .Returns(false);

        // Act
        var result = _service.EnsureAccess(UserSid, tempDir, ReadOnly);

        // Assert: no ACE applied (access was already sufficient and disk state matches DB)
        _explicitAceAccessor.Verify(a => a.ApplyExplicitAce(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<AccessControlType>(), It.IsAny<FileSystemRights>()), Times.Never);
        Assert.False(result.GrantApplied);
    }

    [Fact]
    public void EnsureAccess_AlreadySufficientNoDbEntry_DoesNotPromptOrGrant()
    {
        // Arrange: no DB entry, path exists, but account already has sufficient rights.
        // This is the bug scenario: toolbar folder browser on user's own profile folder.
        var tempDir = ExistingDir;
        _aclPermission.Setup(p => p.NeedsPermissionGrant(tempDir, UserSid,
                It.IsAny<FileSystemRights>(), It.IsAny<bool>()))
            .Returns(false);

        bool confirmCalled = false;

        // Act
        var result = _service.EnsureAccess(UserSid, tempDir, ReadOnly,
            confirm: (_, _) => { confirmCalled = true; return true; });

        // Assert: no prompt, no grant
        Assert.False(confirmCalled);
        Assert.False(result.GrantApplied);
        Assert.False(result.DatabaseModified);
    }

    [Fact]
    public void EnsureAccess_SpecificContainerSid_AllApplicationPackagesAlreadyEffective_DoesNotAddContainerGrant()
    {
        _aclPermission.Setup(p => p.NeedsPermissionGrant(
                ExistingDir,
                AclHelper.AllApplicationPackagesSid,
                It.IsAny<FileSystemRights>(),
                false))
            .Returns(false);

        var result = _service.EnsureAccess(ContainerSid, ExistingDir, ReadExecute, confirm: null);

        Assert.False(result.GrantApplied);
        Assert.False(result.TraverseApplied);
        Assert.False(result.DatabaseModified);
        Assert.Null(_database.GetAccount(ContainerSid));
        _aclPermission.Verify(p => p.NeedsPermissionGrant(
            ExistingDir,
            ContainerSid,
            It.IsAny<FileSystemRights>(),
            false), Times.Never);
    }

    [Fact]
    public void EnsureAccess_NewGrantNeeded_NullConfirm_ProceedsSilently()
    {
        // Arrange: no existing grant; path exists but account lacks access — grantNeeded=true via NeedsPermissionGrant.
        // Null confirm = silent grant, no prompt. Post-grant verification returns false (access now sufficient).
        var tempDir = ExistingDir;
        _aclPermission.SetupSequence(p => p.NeedsPermissionGrant(tempDir, UserSid,
                It.IsAny<FileSystemRights>(), It.IsAny<bool>()))
            .Returns(true)   // grantNeeded check
            .Returns(false); // post-grant verification

        // Act
        var result = _service.EnsureAccess(UserSid, tempDir, ReadOnly, confirm: null);

        // Assert
        Assert.True(result.GrantApplied);
        var grants = _database.GetAccount(UserSid)?.Grants;
        Assert.Contains(grants!, e => e is { IsTraverseOnly: false, IsDeny: false } && e.Path == tempDir);
    }

    [Fact]
    public void EnsureAccess_NewGrantNeeded_ConfirmApproves_AppliesGrant()
    {
        // Arrange: no existing grant; path exists but account lacks access.
        // confirm is called before applying the grant; post-grant verification returns false (access now sufficient).
        var tempDir = ExistingDir;
        _aclPermission.SetupSequence(p => p.NeedsPermissionGrant(tempDir, UserSid,
                It.IsAny<FileSystemRights>(), It.IsAny<bool>()))
            .Returns(true)   // grantNeeded check
            .Returns(false); // post-grant verification

        bool confirmCalled = false;

        // Act
        var result = _service.EnsureAccess(UserSid, tempDir, ReadOnly,
            confirm: (_, _) => { confirmCalled = true; return true; });

        // Assert
        Assert.True(confirmCalled);
        Assert.True(result.GrantApplied);
    }

    [Fact]
    public void EnsureAccess_ConfirmRejectsGrant_ThrowsGrantAccessDeclinedException()
    {
        // Arrange: no existing grant; path exists but account lacks access.
        var tempDir = ExistingDir;

        Assert.Throws<GrantAccessDeclinedException>(
            () => _service.EnsureAccess(UserSid, tempDir, ReadOnly,
                confirm: (_, _) => false));
    }

    [Fact]
    public void EnsureAccess_NonExistentPath_NoDbEntry_ReturnsNoOp()
    {
        // Arrange: path does not exist, no DB entry — cannot check or modify NTFS permissions.
        // Expected: no prompt, no grant, no-op (silent return regardless of confirm).
        bool confirmCalled = false;

        // Act
        var result = _service.EnsureAccess(UserSid, TestPath, ReadOnly,
            confirm: (_, _) => { confirmCalled = true; return true; });

        // Assert
        Assert.False(confirmCalled);
        Assert.False(result.GrantApplied);
        Assert.False(result.DatabaseModified);
    }

    [Fact]
    public void EnsureAccess_StandardPathInfoMissing_UsesAclAccessorPathExistsFallback()
    {
        const string fallbackPath = @"C:\DeniedButReachable\Target";
        var service = BuildServiceWithMockedAclAccessor(out var aclAccessorMock, out var db);
        bool aclIsFolder = true;
        aclAccessorMock.Setup(a => a.PathExists(fallbackPath, out aclIsFolder))
            .Returns(true);
        _aclPermission.SetupSequence(p => p.NeedsPermissionGrant(fallbackPath, UserSid,
                It.IsAny<FileSystemRights>(), It.IsAny<bool>()))
            .Returns(true)
            .Returns(false);

        var result = service.EnsureAccess(UserSid, fallbackPath, ReadOnly, confirm: null);

        Assert.True(result.GrantApplied);
        aclAccessorMock.Verify(a => a.PathExists(fallbackPath, out aclIsFolder), Times.Once);
        Assert.Contains(db.GetAccount(UserSid)!.Grants,
            e => e is { IsTraverseOnly: false, IsDeny: false } &&
                 string.Equals(e.Path, fallbackPath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EnsureAccess_MergesFlags_NeverReducesExistingAccess()
    {
        // Arrange: existing grant has Execute=true; disk ACE is missing (needsFix=true) so EnsureAccess
        // will re-apply the grant using merged rights (ReadOnly requested but Execute must be preserved).
        var tempDir = ExistingDir;

        // Empty ACL → DirectAllowAceCount=0 → needsFix=true → EnsureAccess applies merged rights
        _fileSecurityAccessor.Setup(a => a.GetSecurity(tempDir)).Returns(EmptySecurity());
        // needsFix short-circuits NeedsPermissionGrant for grantNeeded; one call for post-verification
        _aclPermission.Setup(p => p.NeedsPermissionGrant(tempDir, UserSid,
                It.IsAny<FileSystemRights>(), It.IsAny<bool>()))
            .Returns(false);

        _service.AddGrant(UserSid, tempDir, isDeny: false, ReadExecute);

        // Act: request ReadOnly (no Execute flag)
        _service.EnsureAccess(UserSid, tempDir, ReadOnly, confirm: null);

        // Assert: grant still has Execute=true (merge preserved it, not reduced)
        var entry = _database.GetAccount(UserSid)!.Grants
            .First(e => e is { IsTraverseOnly: false, IsDeny: false } && e.Path == tempDir);
        Assert.True(entry.SavedRights!.Execute);
    }

    [Fact]
    public void EnsureAccess_AutoFix_DiskAceMissing_ReappliesAce()
    {
        // Arrange: DB entry exists but disk has no explicit allow ACE (DirectAllowAceCount == 0).
        // This simulates a state where the ACE was lost but the DB still records the grant.
        var tempDir = ExistingDir;

        // Empty security (no explicit ACEs) so DirectAllowAceCount = 0 → needsFix=true
        var emptyAcl = EmptySecurity();
        _fileSecurityAccessor.Setup(a => a.GetSecurity(tempDir)).Returns(emptyAcl);

        // Pre-populate the DB entry
        _database.GetOrCreateAccount(UserSid).Grants.Add(
            new GrantedPathEntry { Path = tempDir, IsDeny = false, SavedRights = ReadOnly });

        // needsFix=true short-circuits grantNeeded without calling NeedsPermissionGrant, so only one call
        // is made: the post-grant check. It must return false so the check passes.
        _aclPermission.Setup(p => p.NeedsPermissionGrant(tempDir, UserSid,
                It.IsAny<FileSystemRights>(), It.IsAny<bool>()))
            .Returns(false);

        // Act: null confirm — auto-fix applies grant silently
        var result = _service.EnsureAccess(UserSid, tempDir, ReadOnly, confirm: null);

        // Assert: ACE was re-applied (AddGrant called ApplyExplicitAce)
        _explicitAceAccessor.Verify(a => a.ApplyExplicitAce(tempDir, UserSid,
            AccessControlType.Allow, It.IsAny<FileSystemRights>()), Times.AtLeastOnce);
    }

    [Fact]
    public void EnsureAccess_AutoFix_SkippedWhenPathDoesNotExist()
    {
        // Arrange: DB entry exists for a non-existent path.
        // The auto-fix check (which reads disk ACL via acl.GetSecurity) is gated on pathExists
        // and must be skipped entirely for non-existent paths. Since no NTFS check is possible,
        // EnsureAccess returns no-op rather than attempting a grant that would fail.
        const string nonExistentPath = @"C:\DoesNotExistNever\subdir";

        _database.GetOrCreateAccount(UserSid).Grants.Add(
            new GrantedPathEntry { Path = nonExistentPath, IsDeny = false, SavedRights = ReadOnly });

        // Act
        var result = _service.EnsureAccess(UserSid, nonExistentPath, ReadOnly, confirm: null);

        // Assert: disk ACL was NOT read (auto-fix check is gated on pathExists)
        _fileSecurityAccessor.Verify(a => a.GetSecurity(It.IsAny<string>()), Times.Never);
        // No grant was attempted (pathExists=false → NeedsPermissionGrant not called → no-op)
        Assert.False(result.GrantApplied);
        Assert.False(result.DatabaseModified);
    }

    [Fact]
    public void EnsureAccess_DenyConflictNullConfirm_ThrowsInvalidOperationException()
    {
        // Arrange: existing deny entry for same path + sid (deny with Read=true)
        var denyRights = new SavedRightsState(Execute: false, Write: true, Read: true, Special: true, Own: false);
        _service.AddGrant(UserSid, TestPath, isDeny: true, denyRights);

        // Act + Assert
        Assert.Throws<InvalidOperationException>(
            () => _service.EnsureAccess(UserSid, TestPath, ReadOnly, confirm: null));
    }

    [Fact]
    public void EnsureAccess_DenyConflictConfirmApproved_WeakensOrRemovesDenyEntry()
    {
        // Arrange: deny with Read=true
        var denyRights = new SavedRightsState(Execute: false, Write: true, Read: true, Special: true, Own: false);
        _service.AddGrant(UserSid, TestPath, isDeny: true, denyRights);

        // Act: request allow with Read=true — deny Read should be resolved
        _service.EnsureAccess(UserSid, TestPath, ReadOnly,
            confirm: (_, _) => true);

        // Assert: deny Read flag is now cleared (or deny fully removed if no conflicting flags remain).
        // Requesting Read-only allow against a deny with Read=true, Execute=false:
        // newDenyRead = existingDeny.Read && !requestedAllow.Read = true && false = false
        // newDenyExecute = existingDeny.Execute && !requestedAllow.Execute = false && true = false
        // → deny fully removed.
        var denyEntry = _database.GetAccount(UserSid)?.Grants
            .FirstOrDefault(e => e is { IsTraverseOnly: false, IsDeny: true, Path: TestPath });
        Assert.Null(denyEntry); // fully removed: no remaining conflicting deny flags
    }

    [Fact]
    public void EnsureAccess_DenyConflictPartialWeakening_ReducesDenyEntryToRemainingFlags()
    {
        // F-75: partial-weakening — deny has both Execute=true and Read=true, but only
        // Read is requested by EnsureAccess. After conflict resolution:
        //   newDenyRead    = denyState.Read    && !requestedAllow.Read    = true  && false = false
        //   newDenyExecute = denyState.Execute && !requestedAllow.Execute = true  && true  = true
        // Since newDenyExecute is still true, the deny entry must be UPDATED (not removed)
        // with Execute=true and Read=false — a partial weakening.

        // Arrange: deny entry with both Read=true and Execute=true
        var denyRights = new SavedRightsState(Execute: true, Write: true, Read: true, Special: true, Own: false);
        _service.AddGrant(UserSid, TestPath, isDeny: true, denyRights);

        // Act: request allow with Read-only (Execute=false) — only the Read conflict is resolved
        _service.EnsureAccess(UserSid, TestPath, ReadOnly, confirm: (_, _) => true);

        // Assert: deny entry still exists (partial weakening, not full removal) with Execute=true, Read=false
        var denyEntry = _database.GetAccount(UserSid)?.Grants
            .FirstOrDefault(e => e is { IsTraverseOnly: false, IsDeny: true, Path: TestPath });
        Assert.NotNull(denyEntry); // entry remains (Execute-only deny still applies)
        Assert.True(denyEntry!.SavedRights!.Execute, "Execute deny must remain — not requested by EnsureAccess");
        Assert.False(denyEntry.SavedRights.Read, "Read deny must be cleared — it conflicted with the requested allow");
    }

    [Fact]
    public void EnsureAccess_DenyConflictConfirmRejected_ThrowsGrantAccessDeclinedException()
    {
        // Arrange: existing deny with Read=true
        var denyRights = new SavedRightsState(Execute: false, Write: true, Read: true, Special: true, Own: false);
        _service.AddGrant(UserSid, TestPath, isDeny: true, denyRights);

        Assert.Throws<GrantAccessDeclinedException>(
            () => _service.EnsureAccess(UserSid, TestPath, ReadOnly,
                confirm: (_, _) => false));
    }

    [Fact]
    public void EnsureAccess_PostGrantVerificationFails_ThrowsGrantOperationException()
    {
        // Arrange: use a real existing directory so pathExists=true and the post-grant
        // verification runs. NeedsPermissionGrant always returns true (default mock),
        // simulating a parent deny that blocks access even after the grant is applied.
        var tempDir = ExistingDir;

        // Act + Assert
        var ex = Assert.Throws<GrantOperationException>(
            () => _service.EnsureAccess(UserSid, tempDir, ReadOnly, confirm: null));
        Assert.Equal(GrantApplyFailureStep.TargetEffectiveAccessValidation, ex.Step);
    }

    [Fact]
    public void EnsureAccess_FileSystemRightsOverload_DelegatesToSavedRightsOverload()
    {
        // Arrange: path exists, account lacks access → grant applied. Uses SetupSequence so
        // post-grant verification returns false (access now sufficient after the grant).
        var tempDir = ExistingDir;
        _aclPermission.SetupSequence(p => p.NeedsPermissionGrant(tempDir, UserSid,
                It.IsAny<FileSystemRights>(), It.IsAny<bool>()))
            .Returns(true)   // grantNeeded check
            .Returns(false); // post-grant verification

        // Act — use the FileSystemRights overload
        var result = _service.EnsureAccess(UserSid, tempDir,
            FileSystemRights.ReadAndExecute, confirm: null);

        // Assert
        Assert.True(result.GrantApplied);
    }

    [Fact]
    public void EnsureAccess_AutoFix_MissingTraverseAce_ReappliesTraverseOnly()
    {
        // Arrange: DB has a correct grant entry AND a traverse entry for tempDir, but one of the
        // covered ancestor directories is missing its traverse ACE on disk. The direct target grant
        // is correct, so ensure-access must repair traverse coverage without reapplying the target grant.
        var tempDir = ExistingDir;
        var parentDir = Path.GetDirectoryName(tempDir)!;
        _pathInfo.AddDirectory(parentDir);

        // Security with correct allow ACE so needsFix=false (disk ACE matches)
        var security = CreateSecurityWithAllowAce(UserSid, GrantRightsMapper.ReadMask);
        var traverseSecurity = CreateSecurityWithAllowAce(UserSid, GrantRightsMapper.ReadMask);
        traverseSecurity.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(UserSid),
            TraverseRightsHelper.TraverseRights,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow));
        _pathInfo.AddDirectory(tempDir, traverseSecurity);
        _fileSecurityAccessor.Setup(a => a.GetSecurity(tempDir)).Returns(security);

        // Pre-populate grant and traverse entries
        _database.GetOrCreateAccount(UserSid).Grants.Add(
            new GrantedPathEntry { Path = tempDir, IsDeny = false, SavedRights = ReadOnly });
        _database.GetOrCreateAccount(UserSid).Grants.Add(
            new GrantedPathEntry { Path = tempDir, IsTraverseOnly = true });

        // Rights are sufficient — only traverse ACE is missing
        _aclPermission.Setup(p => p.NeedsPermissionGrant(tempDir, UserSid,
                It.IsAny<FileSystemRights>(), It.IsAny<bool>()))
            .Returns(false);

        // ExistingDir already has traverse; parentDir does not. After AddAllowAce runs for parentDir,
        // effective coverage becomes complete.
        UseTraverseAclBackedEffectiveRights();
        _traverseAcl.Setup(mock => mock.AddAllowAce(
                It.IsAny<string>(),
                It.Is<SecurityIdentifier>(sid => string.Equals(sid.Value, UserSid, StringComparison.OrdinalIgnoreCase))))
            .Callback<string, SecurityIdentifier>((path, sid) => TrackTraverseAceInTestSecurity(path, sid.Value));

        // Act
        var result = _service.EnsureAccess(UserSid, tempDir, ReadOnly, confirm: null);

        // Assert: ensure-access repairs missing traverse even when the target grant is already sufficient
        Assert.False(result.GrantApplied);
        Assert.True(result.TraverseApplied);
        _traverseAcl.Verify(mock => mock.AddAllowAce(
            parentDir,
            It.Is<SecurityIdentifier>(sid => string.Equals(sid.Value, UserSid, StringComparison.OrdinalIgnoreCase))), Times.Once);
    }

    // --- RemoveGrant ---

    [Fact]
    public void RemoveGrant_ExistingEntry_RemovesFromDbAndCallsRevert()
    {
        // Arrange
        _service.AddGrant(UserSid, TestPath, isDeny: false, ReadOnly);

        // Act
        var removed = _service.RemoveGrant(UserSid, TestPath, isDeny: false);

        // Assert
        Assert.True(removed.DatabaseModified);
        Assert.True(removed.GrantApplied);
        var grants = _database.GetAccount(UserSid)?.Grants
            .Where(e => e is { IsTraverseOnly: false, IsDeny: false }).ToList();
        Assert.Empty(grants ?? []);
        _explicitAceAccessor.Verify(a => a.RemoveExplicitAces(TestPath, UserSid,
            AccessControlType.Allow, It.IsAny<Func<FileSystemAccessRule, bool>?>()), Times.AtLeastOnce);
    }

    [Fact]
    public void RemoveGrant_UpdateFsFalse_SkipsNtfsRevert()
    {
        // Arrange
        _service.AddGrant(UserSid, TestPath, isDeny: false, ReadOnly);

        // Act
        _service.UntrackGrant(UserSid, TestPath, isDeny: false);

        // Assert — RemoveExplicitAces NOT called
        _explicitAceAccessor.Verify(a => a.RemoveExplicitAces(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<AccessControlType>()), Times.Never);
    }

    [Fact]
    public void RemoveGrant_NonExistentEntry_ReturnsFalse()
    {
        var removed = _service.UntrackGrant(UserSid, TestPath, isDeny: false);

        Assert.False(removed.DatabaseModified);
    }

    [Fact]
    public void RemoveGrant_OrphanedTraverseCleaned_WhenNoOtherGrantsNeedIt()
    {
        // Arrange: add grant for a fake existing directory so the traverse entry's path exists
        // for the service without touching the real filesystem.
        // This ensures pathIsStale=false in RemoveTraverse and PromoteNearestAncestor does not run.
        var tempDir = ExistingDir;
        _service.AddGrant(UserSid, tempDir, isDeny: false, ReadOnly);

        // Verify traverse was auto-added
        var traverseBefore = _database.GetAccount(UserSid)?.Grants
            .Where(e => e.IsTraverseOnly).ToList();
        Assert.NotEmpty(traverseBefore ?? []);

        // Act: remove the grant — traverse for tempDir should be cleaned up
        _service.RemoveGrant(UserSid, tempDir, isDeny: false);

        // Assert: no traverse entries remain for tempDir (the only grant path)
        var traverseForTemp = _database.GetAccount(UserSid)?.Grants
            .Where(e => e.IsTraverseOnly &&
                        string.Equals(e.Path, tempDir, StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Empty(traverseForTemp ?? []);
    }

    [Fact]
    public void RemoveGrant_TraversePreservedWhenOtherGrantNeedsIt()
    {
        // Arrange: add two grants for different paths that share the same parent directory.
        // Both grants auto-add traverse for the same parent dir.
        // Removing one grant must not clean up the traverse because the other still needs it.
        const string path1 = @"C:\TestFolder\File1.exe";
        const string path2 = @"C:\TestFolder\File2.exe";

        _service.AddGrant(UserSid, path1, isDeny: false, ReadOnly);
        _service.AddGrant(UserSid, path2, isDeny: false, ReadOnly);

        var traverseBefore = _database.GetAccount(UserSid)?.Grants
            .Where(e => e.IsTraverseOnly).ToList();
        Assert.NotEmpty(traverseBefore ?? []);

        // Act: remove one grant — the other still needs the traverse
        _service.RemoveGrant(UserSid, path1, isDeny: false);

        // Assert: traverse entry still present (needed by path2 grant)
        var traverseAfter = _database.GetAccount(UserSid)?.Grants
            .Where(e => e.IsTraverseOnly).ToList();
        Assert.NotEmpty(traverseAfter ?? []);
    }

    [Fact]
    public void RemoveGrant_ContainerSid_RevertsMatchingInteractiveUserGrant()
    {
        // Arrange: container and IU both have grant for same path
        var db = new AppDatabase();
        var service = BuildServiceWithIuResolver(db, InteractiveSid);

        service.AddGrant(ContainerSid, TestPath, isDeny: false, ReadOnly);

        // Verify IU has the grant
        Assert.Contains(db.GetAccount(InteractiveSid)?.Grants ?? [],
            e => e is { IsTraverseOnly: false, IsDeny: false, Path: TestPath });

        // Act: remove container grant
        service.RemoveGrant(ContainerSid, TestPath, isDeny: false);

        // Assert: IU grant also removed
        var iuEntry = db.GetAccount(InteractiveSid)?.Grants
            .FirstOrDefault(e => e is { IsTraverseOnly: false, IsDeny: false, Path: TestPath });
        Assert.Null(iuEntry);
    }

    // --- AddTraverse / RemoveTraverse ---

    [Fact]
    public void AddTraverse_NewEntry_RecordsDbEntry()
    {
        // Act
        var result = _service.AddTraverse(UserSid, TestPath);

        // Assert — DB entry recorded. TestPath itself does not exist, so AncestorTraverseGranter
        // skips it and visits the nearest existing ancestor (C:\). anyAceAdded=false because
        // HasEffectiveRights returns true (default mock), but dbEntryIsNew=true so modified=true.
        var traverseEntry = _database.GetAccount(UserSid)?.Grants
            .FirstOrDefault(e => e is { IsTraverseOnly: true, Path: TestPath });
        Assert.NotNull(traverseEntry);
        // Visited ancestor paths returned for cleanup tracking
        Assert.True(result.DatabaseModified);
        Assert.NotNull(traverseEntry?.AllAppliedPaths);
        Assert.NotEmpty(traverseEntry!.AllAppliedPaths!);
    }

    [Fact]
    public void AddTraverse_ContainerSid_AlsoAddsTraverseForInteractiveUser()
    {
        // Arrange
        var db = new AppDatabase();
        var service = BuildServiceWithIuResolver(db, InteractiveSid);

        // Act
        service.AddTraverse(ContainerSid, TestPath);

        // Assert: IU also has a traverse entry
        var iuEntry = db.GetAccount(InteractiveSid)?.Grants
            .FirstOrDefault(e => e is { IsTraverseOnly: true, Path: TestPath });
        Assert.NotNull(iuEntry);
        Assert.Contains(ContainerSid, iuEntry.SourceSids ?? []);
    }

    [Fact]
    public void AddTraverse_SpecificContainerSid_AddsSharedAllApplicationPackagesTraverse()
    {
        // Arrange
        _traverseAcl.Invocations.Clear();
        _aclPermission.Setup(p => p.HasEffectiveRights(
                It.IsAny<FileSystemSecurity>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                TraverseRightsHelper.TraverseRights))
            .Returns(() =>
            {
                var priorCalls = _traverseAcl.Invocations.Count(invocation => invocation.Method.Name == nameof(ITraverseAcl.AddAllowAce));
                return priorCalls > 0;
            });

        // Act
        _service.AddTraverse(ContainerSid, ExistingDir);

        // Assert
        _traverseAcl.Verify(t => t.AddAllowAce(
            It.IsAny<string>(),
            It.Is<SecurityIdentifier>(s => s.Value == ContainerSid)), Times.Never);
        _traverseAcl.Verify(t => t.AddAllowAce(
            It.IsAny<string>(),
            It.Is<SecurityIdentifier>(s => s.Value == AclHelper.AllApplicationPackagesSid)), Times.AtLeastOnce);
        Assert.Contains(_database.GetAccount(AclHelper.AllApplicationPackagesSid)!.Grants,
            e => e.IsTraverseOnly && string.Equals(e.Path, ExistingDir, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(_database.GetAccount(ContainerSid)?.Grants ?? [],
            e => e.IsTraverseOnly && string.Equals(e.Path, ExistingDir, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AddTraverse_SpecificContainerSid_AllApplicationPackagesAlreadyEffective_DoesNotAddContainerAce()
    {
        // Arrange
        _traverseAcl.Invocations.Clear();
        _aclPermission.Setup(p => p.HasEffectiveRights(
                It.IsAny<FileSystemSecurity>(),
                ContainerSid,
                It.IsAny<IReadOnlyList<string>>(),
                TraverseRightsHelper.TraverseRights))
            .Returns(false);
        _aclPermission.Setup(p => p.HasEffectiveRights(
                It.IsAny<FileSystemSecurity>(),
                AclHelper.AllApplicationPackagesSid,
                It.IsAny<IReadOnlyList<string>>(),
                TraverseRightsHelper.TraverseRights))
            .Returns(true);

        // Act
        _service.AddTraverse(ContainerSid, ExistingDir);

        // Assert
        _traverseAcl.Verify(t => t.AddAllowAce(
            It.IsAny<string>(),
            It.Is<SecurityIdentifier>(s => s.Value == ContainerSid)), Times.Never);
    }

    [Fact]
    public void AddTraverse_SpecificContainerSid_SpecificGrantAlreadyEffective_DoesNotAddSharedAceOnSamePath()
    {
        // Arrange
        _traverseAcl.Invocations.Clear();
        _aclPermission.Setup(p => p.HasEffectiveRights(
                It.IsAny<FileSystemSecurity>(),
                ContainerSid,
                It.IsAny<IReadOnlyList<string>>(),
                TraverseRightsHelper.TraverseRights))
            .Returns(true);
        _aclPermission.Setup(p => p.HasEffectiveRights(
                It.IsAny<FileSystemSecurity>(),
                AclHelper.AllApplicationPackagesSid,
                It.IsAny<IReadOnlyList<string>>(),
                TraverseRightsHelper.TraverseRights))
            .Returns(false);

        // Act
        _service.AddTraverse(ContainerSid, ExistingDir);

        // Assert
        _traverseAcl.Verify(t => t.AddAllowAce(
            ExistingDir,
            It.Is<SecurityIdentifier>(s => s.Value == AclHelper.AllApplicationPackagesSid)), Times.Never);
    }

    [Fact]
    public void RemoveTraverse_ExistingEntry_RemovesFromDb()
    {
        // Arrange: add traverse first
        _service.AddTraverse(UserSid, TestPath);

        // Act
        var removed = _service.RemoveTraverse(UserSid, TestPath);

        // Assert
        Assert.True(removed.DatabaseModified);
        Assert.True(removed.TraverseApplied);
        var traverseEntries = _database.GetAccount(UserSid)?.Grants
            .Where(e => e is { IsTraverseOnly: true, Path: TestPath }).ToList();
        Assert.Empty(traverseEntries ?? []);
    }

    [Fact]
    public void RemoveTraverse_NonExistentEntry_ReturnsFalse()
    {
        var removed = _service.UntrackTraverse(UserSid, TestPath);

        Assert.False(removed.DatabaseModified);
    }

    [Fact]
    public void RemoveTraverse_ContainerSid_RevertsInteractiveUserTraverse()
    {
        // Arrange: container and IU both have traverse
        var db = new AppDatabase();
        var service = BuildServiceWithIuResolver(db, InteractiveSid);

        service.AddTraverse(ContainerSid, TestPath);

        // Verify IU has traverse
        Assert.Contains(db.GetAccount(InteractiveSid)?.Grants ?? [],
            e => e is { IsTraverseOnly: true, Path: TestPath });

        // Act
        service.RemoveTraverse(ContainerSid, TestPath);

        // Assert: IU traverse removed
        var iuEntry = db.GetAccount(InteractiveSid)?.Grants
            .FirstOrDefault(e => e is { IsTraverseOnly: true, Path: TestPath });
        Assert.Null(iuEntry);
    }

    [Fact]
    public void RemoveTraverse_ContainerSid_PreservesInteractiveUserTraverseWhenOtherContainerStillNeedsPath()
    {
        var db = new AppDatabase();
        var service = BuildServiceWithIuResolver(db, InteractiveSid);

        service.AddTraverse(ContainerSid, TestPath);
        service.AddTraverse(OtherContainerSid, TestPath);

        service.UntrackTraverse(ContainerSid, TestPath);

        var iuEntry = db.GetAccount(InteractiveSid)?.Grants
            .FirstOrDefault(e => e is { IsTraverseOnly: true, Path: TestPath });
        Assert.NotNull(iuEntry);
        Assert.DoesNotContain(ContainerSid, iuEntry!.SourceSids ?? []);
        Assert.Contains(OtherContainerSid, iuEntry.SourceSids ?? []);
    }

    [Fact]
    public void RemoveTraverse_ContainerSid_DoesNotConvertManualInteractiveUserTraverseIntoManagedEntry()
    {
        var db = new AppDatabase();
        var service = BuildServiceWithIuResolver(db, InteractiveSid);

        db.GetOrCreateAccount(InteractiveSid).Grants.Add(new GrantedPathEntry
        {
            Path = TestPath,
            IsTraverseOnly = true
        });
        service.AddTraverse(ContainerSid, TestPath);

        var manualEntry = db.GetAccount(InteractiveSid)?.Grants
            .FirstOrDefault(e => e is { IsTraverseOnly: true, Path: TestPath });
        Assert.NotNull(manualEntry);
        Assert.Null(manualEntry!.SourceSids);

        service.UntrackTraverse(ContainerSid, TestPath);

        manualEntry = db.GetAccount(InteractiveSid)?.Grants
            .FirstOrDefault(e => e is { IsTraverseOnly: true, Path: TestPath });
        Assert.NotNull(manualEntry);
        Assert.Null(manualEntry!.SourceSids);
    }

    [Fact]
    public void RemoveTraverse_PromotesNearestAncestor_WhenTargetPathIsStale()
    {
        // Arrange: manually insert a traverse entry for a non-existent path with a list of
        // applied ancestor paths. One of those ancestors exists on disk and has an explicit
        // traverse ACE → it should be promoted to a standalone DB entry.
        const string stalePath = @"C:\DoesNotExistNever\GoneDir";
        var tempDir = ExistingDir;

        // The stale entry references tempDir as an applied ancestor
        var staleEntry = new GrantedPathEntry
        {
            Path = stalePath,
            IsTraverseOnly = true,
            AllAppliedPaths = [tempDir]
        };
        _database.GetOrCreateAccount(UserSid).Grants.Add(staleEntry);

        // traverseAcl reports that tempDir has an explicit traverse ACE for the SID
        _traverseAcl.Setup(t => t.HasExplicitTraverseAce(
                tempDir, It.Is<SecurityIdentifier>(s => s.Value == UserSid)))
            .Returns(true);
        _traverseAcl.Setup(t => t.HasExplicitTraverseAceOrThrow(
                tempDir, It.Is<SecurityIdentifier>(s => s.Value == UserSid)))
            .Returns(true);

        // Act: remove the stale traverse entry
        var removed = _service.UntrackTraverse(UserSid, stalePath);

        // Assert: stale entry removed; untrack promotes the nearest surviving tracked coverage path.
        Assert.True(removed.DatabaseModified);
        var grants = _database.GetAccount(UserSid)?.Grants;
        Assert.DoesNotContain(grants ?? [], e => e is { IsTraverseOnly: true, Path: stalePath });
        Assert.Contains(grants ?? [], e => e.IsTraverseOnly &&
            string.Equals(e.Path, tempDir, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RemoveTraverse_PreservesAce_WhenGrantNeedsIt()
    {
        // Arrange: a traverse entry covers C:\SharedDir (its AllAppliedPaths list).
        // A separate allow grant exists for a file inside C:\SharedDir, so GetGrantPaths
        // includes C:\SharedDir. When RemoveTraverse is called with updateFileSystem=true,
        // RevertForPath must NOT remove the traverse ACE from C:\SharedDir.
        const string grantFilePath = @"C:\SharedDir\File.exe";
        const string traversePath = @"C:\SomeOtherDir";
        const string sharedDir = @"C:\SharedDir";

        // Add a grant for the file so grantPaths will contain C:\SharedDir
        _service.AddGrant(UserSid, grantFilePath, isDeny: false, ReadOnly);

        // Insert a traverse entry whose AllAppliedPaths includes the grant-needed dir
        var extraTraverse = new GrantedPathEntry
        {
            Path = traversePath,
            IsTraverseOnly = true,
            AllAppliedPaths = [sharedDir] // this dir is protected by the grant above
        };
        _database.GetOrCreateAccount(UserSid).Grants.Add(extraTraverse);

        // Track whether RemoveTraverseOnlyAce is called for sharedDir
        bool removedAceOnSharedDir = false;
        _traverseAcl.Setup(t => t.RemoveTraverseOnlyAce(
                It.Is<string>(p => string.Equals(p, sharedDir, StringComparison.OrdinalIgnoreCase)),
                It.IsAny<SecurityIdentifier>()))
            .Callback(() => removedAceOnSharedDir = true);

        // Act: remove the extra traverse entry (which references sharedDir in its applied paths)
        _service.RemoveTraverse(UserSid, traversePath);

        // Assert: the ACE for sharedDir was NOT removed because the grant still needs it
        Assert.False(removedAceOnSharedDir);
    }

    // --- ChangeOwner / ResetOwner ---

    // --- RemoveAll ---

    [Fact]
    public void RemoveAll_ClearsAllGrantsAndTraverseEntries()
    {
        // Arrange: add a grant (which also auto-adds traverse)
        _service.AddGrant(UserSid, TestPath, isDeny: false, ReadOnly);
        Assert.NotEmpty(_database.GetAccount(UserSid)?.Grants ?? []);

        // Act
        _service.RemoveAll(UserSid);

        // Assert: the tracked allow grant and its original tracked traverse entry are removed.
        var grants = _database.GetAccount(UserSid)?.Grants;
        Assert.DoesNotContain(grants ?? [],
            entry => !entry.IsTraverseOnly &&
                     !entry.IsDeny &&
                     string.Equals(entry.Path, TestPath, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(grants ?? [],
            entry => entry.IsTraverseOnly &&
                     string.Equals(entry.Path, Path.GetDirectoryName(TestPath), StringComparison.OrdinalIgnoreCase));

        // Verify NTFS revert was called for the grant
        _explicitAceAccessor.Verify(a => a.RemoveExplicitAces(TestPath, UserSid,
            AccessControlType.Allow, It.IsAny<Func<FileSystemAccessRule, bool>?>()), Times.AtLeastOnce);
    }

    [Fact]
    public void RemoveAll_ContainerSid_RevertsIuGrants()
    {
        // Arrange: container and IU both have a grant for the same path
        var db = new AppDatabase();
        var service = BuildServiceWithIuResolver(db, InteractiveSid);

        service.AddGrant(ContainerSid, TestPath, isDeny: false, ReadOnly);

        // Verify IU has the matching grant
        var iuBefore = db.GetAccount(InteractiveSid)?.Grants
            .FirstOrDefault(e => e is { IsTraverseOnly: false, IsDeny: false, Path: TestPath });
        Assert.NotNull(iuBefore);

        // Act
        service.RemoveAll(ContainerSid);

        // Assert: IU grant removed when the last managed source is removed
        var iuEntry = db.GetAccount(InteractiveSid)?.Grants
            .FirstOrDefault(e => e is { IsTraverseOnly: false, IsDeny: false, Path: TestPath });
        Assert.Null(iuEntry);
    }

    [Fact]
    public void RemoveAll_ContainerSid_IuGrantPreservedWhenOtherContainerNeedsPath()
    {
        // Arrange: two containers both need TestPath; IU grant present for both
        var db = new AppDatabase();
        var service = BuildServiceWithIuResolver(db, InteractiveSid);

        service.AddGrant(ContainerSid, TestPath, isDeny: false, ReadOnly);
        service.AddGrant(OtherContainerSid, TestPath, isDeny: false, ReadOnly);

        // Act: remove first container
        service.RemoveAll(ContainerSid);

        // Assert: IU grant preserved because OtherContainerSid still needs the path
        var iuEntry = db.GetAccount(InteractiveSid)?.Grants
            .FirstOrDefault(e => e is { IsTraverseOnly: false, IsDeny: false, Path: TestPath });
        Assert.NotNull(iuEntry);
        Assert.DoesNotContain(ContainerSid, iuEntry!.SourceSids ?? []);
        Assert.Contains(OtherContainerSid, iuEntry.SourceSids ?? []);
    }

    [Fact]
    public void RemoveAll_SpecificContainerSid_SharedTraversePreservedWhenOtherContainerGrantNeedsPath()
    {
        _aclPermission.Setup(p => p.HasEffectiveRights(
                It.IsAny<FileSystemSecurity>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                TraverseRightsHelper.TraverseRights))
            .Returns(false);
        var db = new AppDatabase();
        var service = BuildServiceWithIuResolver(db, InteractiveSid);
        service.AddGrant(ContainerSid, ExistingDir, isDeny: false, ReadOnly);
        service.AddGrant(OtherContainerSid, ExistingDir, isDeny: false, ReadOnly);

        _traverseAcl.Invocations.Clear();

        service.RemoveAll(ContainerSid);

        _traverseAcl.Verify(t => t.RemoveTraverseOnlyAce(
            It.IsAny<string>(),
            It.Is<SecurityIdentifier>(s => s.Value == AclHelper.AllApplicationPackagesSid)), Times.Never);
        Assert.Contains(db.GetAccount(AclHelper.AllApplicationPackagesSid)!.Grants,
            e => e.IsTraverseOnly && string.Equals(e.Path, ExistingDir, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RemoveAll_SpecificContainerSid_SharedTraverseRemovedWhenNoContainerGrantNeedsPath()
    {
        _aclPermission.Setup(p => p.HasEffectiveRights(
                It.IsAny<FileSystemSecurity>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                TraverseRightsHelper.TraverseRights))
            .Returns(false);

        _service.AddGrant(ContainerSid, ExistingDir, isDeny: false, ReadOnly);

        _service.RemoveAll(ContainerSid);

        Assert.DoesNotContain(_database.GetAccount(AclHelper.AllApplicationPackagesSid)?.Grants ?? [],
            e => e.IsTraverseOnly && string.Equals(e.Path, ExistingDir, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RemoveAll_SpecificContainerSid_TrackedTraverseOnlyEntryRemoved()
    {
        var db = new AppDatabase();
        var service = BuildServiceWithIuResolver(db, InteractiveSid);
        service.AddTraverse(ContainerSid, ExistingDir);

        Assert.Contains(db.GetAccount(AclHelper.AllApplicationPackagesSid)!.Grants,
            e => e.IsTraverseOnly &&
                 string.Equals(e.Path, ExistingDir, StringComparison.OrdinalIgnoreCase) &&
                 e.SourceSids?.Contains(ContainerSid, StringComparer.OrdinalIgnoreCase) == true);

        service.UntrackAll(ContainerSid);

        Assert.DoesNotContain(db.GetAccount(AclHelper.AllApplicationPackagesSid)?.Grants ?? [],
            e => e.IsTraverseOnly &&
                 string.Equals(e.Path, ExistingDir, StringComparison.OrdinalIgnoreCase) &&
                 e.SourceSids?.Contains(ContainerSid, StringComparer.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(db.GetAccount(InteractiveSid)?.Grants ?? [],
            e => e.IsTraverseOnly &&
                 string.Equals(e.Path, ExistingDir, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RemoveAll_SpecificContainerSid_TrackedTraverseOnlyEntryPreservedForOtherContainer()
    {
        var db = new AppDatabase();
        var service = BuildServiceWithIuResolver(db, InteractiveSid);
        service.AddTraverse(ContainerSid, ExistingDir);
        service.AddTraverse(OtherContainerSid, ExistingDir);

        service.UntrackAll(ContainerSid);

        var entry = db.GetAccount(AclHelper.AllApplicationPackagesSid)?.Grants.FirstOrDefault(e =>
            e.IsTraverseOnly && string.Equals(e.Path, ExistingDir, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(entry);
        Assert.DoesNotContain(ContainerSid, entry!.SourceSids ?? []);
        Assert.Contains(OtherContainerSid, entry.SourceSids ?? []);

        var iuEntry = db.GetAccount(InteractiveSid)?.Grants.FirstOrDefault(e =>
            e.IsTraverseOnly && string.Equals(e.Path, ExistingDir, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(iuEntry);
        Assert.DoesNotContain(ContainerSid, iuEntry!.SourceSids ?? []);
        Assert.Contains(OtherContainerSid, iuEntry.SourceSids ?? []);
    }

    [Fact]
    public void RemoveAll_SpecificContainerSid_UntrackedSharedTraversePreserved()
    {
        _database.GetOrCreateAccount(AclHelper.AllApplicationPackagesSid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true
        });

        _service.AddTraverse(ContainerSid, ExistingDir);
        _service.UntrackAll(ContainerSid);

        var entry = Assert.Single(_database.GetAccount(AclHelper.AllApplicationPackagesSid)?.Grants ?? []);
        Assert.True(entry.IsTraverseOnly);
        Assert.Equal(ExistingDir, entry.Path);
        Assert.Null(entry.SourceSids);
    }

    [Fact]
    public void RemoveAll_ContainerSid_IuGrantRemovedWhenOwnerMatches_EvenIfSavedRightsDiffer()
    {
        // Arrange: container has ReadOnly rights but IU grant has ReadExecute (different SavedRights)
        var db = new AppDatabase();
        var service = BuildServiceWithIuResolver(db, InteractiveSid);

        service.AddGrant(ContainerSid, TestPath, isDeny: false, ReadOnly);

        // Override the IU's SavedRights to simulate rights differing from container's
        var iuEntry = db.GetOrCreateAccount(InteractiveSid).Grants
            .First(e => e is { IsTraverseOnly: false, IsDeny: false, Path: TestPath });
        iuEntry.SavedRights = ReadExecute;

        // Act
        service.RemoveAll(ContainerSid);

        // Assert: IU grant removed when the last managed source is removed — SavedRights difference is irrelevant
        var removed = db.GetAccount(InteractiveSid)?.Grants
            .FirstOrDefault(e => e is { IsTraverseOnly: false, IsDeny: false, Path: TestPath });
        Assert.Null(removed);
    }

    [Fact]
    public void RemoveAll_UpdateFileSystemFalse_ClearsDbWithoutNtfsRevert()
    {
        // Arrange: add a grant (which also auto-adds traverse)
        _service.AddGrant(UserSid, TestPath, isDeny: false, ReadOnly);
        Assert.NotEmpty(_database.GetAccount(UserSid)?.Grants ?? []);

        // Reset invocation tracking after AddGrant so only RemoveAll calls are observed
        _explicitAceAccessor.Invocations.Clear();

        // Act: DB-only clear — no NTFS revert
        _service.UntrackAll(UserSid);

        // Assert: the tracked allow grant and its original tracked traverse entry are removed.
        var grants = _database.GetAccount(UserSid)?.Grants;
        Assert.DoesNotContain(grants ?? [],
            entry => !entry.IsTraverseOnly &&
                     !entry.IsDeny &&
                     string.Equals(entry.Path, TestPath, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(grants ?? [],
            entry => entry.IsTraverseOnly &&
                     string.Equals(entry.Path, Path.GetDirectoryName(TestPath), StringComparison.OrdinalIgnoreCase));

        // Assert: no NTFS calls made
        _explicitAceAccessor.Verify(a => a.RemoveExplicitAces(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<AccessControlType>()), Times.Never);
    }

    [Fact]
    public void PersistedRemoveTraverse_PreservesAncestorAceWhenRuntimeOnlyTraverseStillNeedsIt()
    {
        var storedPath = Path.Combine(ExistingDir, "PersistedChild");
        var runtimeOnlyPath = Path.Combine(ExistingDir, "RuntimeOnlyChild");
        _pathInfo.AddDirectory(storedPath);
        _pathInfo.AddDirectory(runtimeOnlyPath);

        var mainStore = new TestGrantIntentStore();
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = storedPath,
            IsTraverseOnly = true,
            AllAppliedPaths = [storedPath, ExistingDir]
        });
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out var db,
            out _,
            out _);
        db.GetOrCreateAccount(UserSid).Grants.AddRange([
            new GrantedPathEntry
            {
                Path = storedPath,
                IsTraverseOnly = true,
                AllAppliedPaths = [storedPath, ExistingDir]
            },
            new GrantedPathEntry
            {
                Path = runtimeOnlyPath,
                IsTraverseOnly = true,
                AllAppliedPaths = [runtimeOnlyPath, ExistingDir]
            }
        ]);

        var removedPaths = new List<string>();
        _traverseAcl.Setup(mock => mock.RemoveTraverseOnlyAce(
                It.IsAny<string>(),
                It.Is<SecurityIdentifier>(sid => sid.Value == UserSid)))
            .Callback<string, SecurityIdentifier>((path, _) => removedPaths.Add(path));

        var result = service.RemoveTraverse(UserSid, storedPath);

        Assert.True(result.TraverseApplied);
        Assert.Equal([storedPath], removedPaths);
        Assert.DoesNotContain(db.GetAccount(UserSid)!.Grants,
            entry => entry.IsTraverseOnly &&
                     string.Equals(entry.Path, storedPath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(db.GetAccount(UserSid)!.Grants,
            entry => entry.IsTraverseOnly &&
                     string.Equals(entry.Path, runtimeOnlyPath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PersistedRemoveAll_PreservesAncestorAceWhenRuntimeOnlyTraverseStillNeedsIt()
    {
        var storedPath = Path.Combine(ExistingDir, "PersistedChild");
        var runtimeOnlyPath = Path.Combine(ExistingDir, "RuntimeOnlyChild");
        _pathInfo.AddDirectory(storedPath);
        _pathInfo.AddDirectory(runtimeOnlyPath);

        var mainStore = new TestGrantIntentStore();
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = storedPath,
            IsTraverseOnly = true,
            AllAppliedPaths = [storedPath, ExistingDir]
        });
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out var db,
            out _,
            out _);
        db.GetOrCreateAccount(UserSid).Grants.AddRange([
            new GrantedPathEntry
            {
                Path = storedPath,
                IsTraverseOnly = true,
                AllAppliedPaths = [storedPath, ExistingDir]
            },
            new GrantedPathEntry
            {
                Path = runtimeOnlyPath,
                IsTraverseOnly = true,
                AllAppliedPaths = [runtimeOnlyPath, ExistingDir]
            }
        ]);

        var removedPaths = new List<string>();
        _traverseAcl.Setup(mock => mock.RemoveTraverseOnlyAce(
                It.IsAny<string>(),
                It.Is<SecurityIdentifier>(sid => sid.Value == UserSid)))
            .Callback<string, SecurityIdentifier>((path, _) => removedPaths.Add(path));

        var result = service.RemoveAll(UserSid);

        Assert.True(result.TraverseApplied);
        Assert.Equal([storedPath], removedPaths);
        Assert.DoesNotContain(db.GetAccount(UserSid)!.Grants,
            entry => entry.IsTraverseOnly &&
                     string.Equals(entry.Path, storedPath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(db.GetAccount(UserSid)!.Grants,
            entry => entry.IsTraverseOnly &&
                     string.Equals(entry.Path, runtimeOnlyPath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PersistedRemoveAll_ContainerTraverseOutOfSync_StillRevertsInteractiveUserTraverse()
    {
        var mainStore = new TestGrantIntentStore();
        mainStore.AddEntry(AclHelper.AllApplicationPackagesSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true,
            SourceSids = [ContainerSid]
        });
        var service = BuildStoreAwareServiceWithIuResolver(
            mainStore,
            InteractiveSid,
            out _,
            out var db,
            out _,
            out _);
        db.GetOrCreateAccount(InteractiveSid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true,
            SourceSids = [ContainerSid]
        });

        var result = service.RemoveAll(ContainerSid);

        Assert.True(result.TraverseApplied);
        Assert.Empty(mainStore.GetEntries(AclHelper.AllApplicationPackagesSid));
        Assert.DoesNotContain(db.GetAccount(InteractiveSid)?.Grants ?? [],
            entry => entry.IsTraverseOnly &&
                     string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PersistedRemoveAll_LegacyInteractiveUserGrantOwner_RemovesGrantAfterRuntimeGrantRemoval()
    {
        var mainStore = new TestGrantIntentStore();
        mainStore.AddEntry(ContainerSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadOnly
        });
        var service = BuildStoreAwareServiceWithIuResolver(
            mainStore,
            InteractiveSid,
            out _,
            out var db,
            out _,
            out _);
        db.GetOrCreateAccount(ContainerSid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadOnly
        });
        db.GetOrCreateAccount(InteractiveSid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadOnly,
            OwnerContainerSid = ContainerSid
        });

        var result = service.RemoveAll(ContainerSid);

        Assert.True(result.GrantApplied);
        Assert.Empty(mainStore.GetEntries(ContainerSid));
        Assert.DoesNotContain(db.GetAccount(InteractiveSid)?.Grants ?? [],
            entry => !entry.IsTraverseOnly &&
                     !entry.IsDeny &&
                     string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PersistedRemoveAll_RemovesGrantAndTraverseBeforeSaving()
    {
        var mainStore = new TestGrantIntentStore();
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadOnly
        });
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true,
            AllAppliedPaths = [ExistingDir]
        });
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out var db,
            out var grantAceMock,
            out _);
        db.GetOrCreateAccount(UserSid).Grants.AddRange([
            new GrantedPathEntry
            {
                Path = ExistingDir,
                IsDeny = false,
                SavedRights = ReadOnly
            },
            new GrantedPathEntry
            {
                Path = ExistingDir,
                IsTraverseOnly = true,
                AllAppliedPaths = [ExistingDir]
            }
        ]);
        var events = new List<string>();
        grantAceMock.Setup(mock => mock.RevertAce(ExistingDir, UserSid, false))
            .Callback(() => events.Add("grant"));
        _traverseAcl.Setup(mock => mock.RemoveTraverseOnlyAce(
                ExistingDir,
                It.Is<SecurityIdentifier>(sid => sid.Value == UserSid)))
            .Callback(() => events.Add("traverse"));
        mainStore.SaveAction = () => events.Add("save");

        var result = service.RemoveAll(UserSid);

        Assert.Equal(["grant", "traverse", "save"], events);
        Assert.True(result.GrantApplied);
        Assert.True(result.TraverseApplied);
        Assert.True(result.DatabaseModified);
        Assert.True(result.DurableSaveCompleted);
        Assert.Empty(mainStore.GetEntries(UserSid));
    }

    [Fact]
    public void PersistedRemoveAll_PostSaveFailure_ReturnsWarningAndKeepsRemovalState()
    {
        var mainStore = new TestGrantIntentStore
        {
            SaveAction = () => throw new InvalidOperationException("save failed")
        };
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadOnly
        });
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true,
            AllAppliedPaths = [ExistingDir]
        });
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out var db,
            out _,
            out _);
        db.GetOrCreateAccount(UserSid).Grants.AddRange([
            new GrantedPathEntry
            {
                Path = ExistingDir,
                IsDeny = false,
                SavedRights = ReadOnly
            },
            new GrantedPathEntry
            {
                Path = ExistingDir,
                IsTraverseOnly = true,
                AllAppliedPaths = [ExistingDir]
            }
        ]);

        var result = service.RemoveAll(UserSid);

        var warning = Assert.Single(result.Warnings);
        Assert.True(result.GrantApplied);
        Assert.True(result.TraverseApplied);
        Assert.True(result.DatabaseModified);
        Assert.False(result.DurableSaveCompleted);
        Assert.Equal(GrantApplyFailureStep.PostRemoveAllSave, warning.Step);
        Assert.Equal(ExistingDir, warning.Path);
        Assert.Null(warning.ConfigPath);
        Assert.Equal("save failed", warning.Cause.Message);
        Assert.Empty(mainStore.GetEntries(UserSid));
        Assert.Empty(db.GetAccount(UserSid)?.Grants ?? []);
    }

    [Fact]
    public void UntrackAll_DoesNotTouchNtfs_AndSavesStore()
    {
        var mainStore = new TestGrantIntentStore();
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadOnly
        });
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true,
            AllAppliedPaths = [ExistingDir]
        });
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out var db,
            out var grantAceMock,
            out _);
        db.GetOrCreateAccount(UserSid).Grants.AddRange([
            new GrantedPathEntry
            {
                Path = ExistingDir,
                IsDeny = false,
                SavedRights = ReadOnly
            },
            new GrantedPathEntry
            {
                Path = ExistingDir,
                IsTraverseOnly = true,
                AllAppliedPaths = [ExistingDir]
            }
        ]);

        var result = service.UntrackAll(UserSid);

        Assert.True(result.DatabaseModified);
        Assert.True(result.DurableSaveCompleted);
        Assert.Empty(mainStore.GetEntries(UserSid));
        grantAceMock.Verify(mock => mock.ApplyAce(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<SavedRightsState>(),
            It.IsAny<bool>()), Times.Never);
        grantAceMock.Verify(mock => mock.RevertAce(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<bool>()), Times.Never);
        _traverseAcl.Verify(mock => mock.RemoveTraverseOnlyAce(
            It.IsAny<string>(),
            It.IsAny<SecurityIdentifier>()), Times.Never);
    }

    [Fact]
    public void UntrackAll_SaveFailure_ReturnsWarningAndKeepsDbOnlyCleanupState()
    {
        var mainStore = new TestGrantIntentStore
        {
            SaveAction = () => throw new InvalidOperationException("save failed")
        };
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsDeny = false,
            SavedRights = ReadOnly
        });
        mainStore.AddEntry(UserSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true,
            AllAppliedPaths = [ExistingDir]
        });
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out var db,
            out var grantAceMock,
            out _);
        db.GetOrCreateAccount(UserSid).Grants.AddRange([
            new GrantedPathEntry
            {
                Path = ExistingDir,
                IsDeny = false,
                SavedRights = ReadOnly
            },
            new GrantedPathEntry
            {
                Path = ExistingDir,
                IsTraverseOnly = true,
                AllAppliedPaths = [ExistingDir]
            }
        ]);

        var result = service.UntrackAll(UserSid);

        var warning = Assert.Single(result.Warnings);
        Assert.True(result.DatabaseModified);
        Assert.False(result.DurableSaveCompleted);
        Assert.Equal(GrantApplyFailureStep.UntrackAllSave, warning.Step);
        Assert.Equal(ExistingDir, warning.Path);
        Assert.Null(warning.ConfigPath);
        Assert.Equal("save failed", warning.Cause.Message);
        Assert.Empty(mainStore.GetEntries(UserSid));
        Assert.Empty(db.GetAccount(UserSid)?.Grants ?? []);
        grantAceMock.Verify(mock => mock.ApplyAce(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<SavedRightsState>(),
            It.IsAny<bool>()), Times.Never);
        grantAceMock.Verify(mock => mock.RevertAce(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<bool>()), Times.Never);
        _traverseAcl.Verify(mock => mock.RemoveTraverseOnlyAce(
            It.IsAny<string>(),
            It.IsAny<SecurityIdentifier>()), Times.Never);
    }

    [Fact]
    public void PersistedUntrackAll_ContainerTraverseOutOfSync_StillRevertsInteractiveUserTraverse()
    {
        var mainStore = new TestGrantIntentStore();
        mainStore.AddEntry(AclHelper.AllApplicationPackagesSid, new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true,
            SourceSids = [ContainerSid]
        });
        var service = BuildStoreAwareServiceWithIuResolver(
            mainStore,
            InteractiveSid,
            out _,
            out var db,
            out _,
            out _);
        db.GetOrCreateAccount(InteractiveSid).Grants.Add(new GrantedPathEntry
        {
            Path = ExistingDir,
            IsTraverseOnly = true,
            SourceSids = [ContainerSid]
        });

        var result = service.UntrackAll(ContainerSid);

        Assert.True(result.DatabaseModified);
        Assert.True(result.DurableSaveCompleted);
        Assert.Empty(mainStore.GetEntries(AclHelper.AllApplicationPackagesSid));
        Assert.DoesNotContain(db.GetAccount(InteractiveSid)?.Grants ?? [],
            entry => entry.IsTraverseOnly &&
                     string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase));
    }

    // --- UpdateFromPath ---

    [Fact]
    public void UpdateFromPath_NonExistentPath_ReturnsFalse()
    {
        var result = _service.UpdateFromPath(@"C:\DoesNotExistNever\file.exe", UserSid);

        Assert.False(result);
    }

    [Fact]
    public void UpdateFromPath_DiscoverGrant_CreatesDbEntry()
    {
        // Arrange: path exists; ACL has an allow ACE for UserSid with ReadMask (not traverse-only)
        var tempDir = ExistingDir;
        var security = CreateSecurityWithAllowAce(UserSid, GrantRightsMapper.ReadMask);
        _fileSecurityAccessor.Setup(a => a.GetSecurity(tempDir)).Returns(security);

        // Act
        var modified = _service.UpdateFromPath(tempDir, UserSid);

        // Assert: grant entry created
        Assert.True(modified);
        var entry = _database.GetAccount(UserSid)?.Grants
            .FirstOrDefault(e => e is { IsTraverseOnly: false, IsDeny: false } &&
                                 string.Equals(e.Path, tempDir, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(entry);
    }

    [Fact]
    public void UpdateFromPath_DiscoverGrant_TracksMainStoreWithoutSaving()
    {
        var mainStore = new TestGrantIntentStore();
        var service = BuildStoreAwareService(
            mainStore,
            out _,
            out _,
            out var grantAceMock,
            out _);
        grantAceMock.Setup(mock => mock.GetSecurity(ExistingDir))
            .Returns(CreateSecurityWithAllowAce(UserSid, GrantRightsMapper.ReadMask));

        var modified = service.UpdateFromPath(ExistingDir, UserSid);

        Assert.True(modified);
        Assert.Contains(mainStore.GetEntries(UserSid),
            entry => !entry.IsTraverseOnly &&
                     !entry.IsDeny &&
                     string.Equals(entry.Path, ExistingDir, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, mainStore.SaveCount);
    }

    [Fact]
    public void UpdateFromPath_DiscoverTraverseAce_CreatesTraverseDbEntry()
    {
        // Arrange: path exists; ACL has traverse-only ACE for UserSid
        var tempDir = ExistingDir;
        var security = CreateSecurityWithAllowAce(UserSid, GrantRightsMapper.TraverseOnlyMask);
        _fileSecurityAccessor.Setup(a => a.GetSecurity(tempDir)).Returns(security);

        // Act
        var modified = _service.UpdateFromPath(tempDir, UserSid);

        // Assert: traverse entry created
        Assert.True(modified);
        var entry = _database.GetAccount(UserSid)?.Grants
            .FirstOrDefault(e => e.IsTraverseOnly &&
                string.Equals(e.Path, tempDir, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(entry);
    }

    [Fact]
    public void UpdateFromPath_AllApplicationPackagesTraverseAce_CreatesSharedTraverseDbEntry()
    {
        var tempDir = ExistingDir;
        var security = CreateSecurityWithAllowAce(
            AclHelper.AllApplicationPackagesSid,
            GrantRightsMapper.TraverseOnlyMask);
        _fileSecurityAccessor.Setup(a => a.GetSecurity(tempDir)).Returns(security);

        var modified = _service.UpdateFromPath(tempDir, AclHelper.AllApplicationPackagesSid);

        Assert.True(modified);
        Assert.Contains(_database.GetAccount(AclHelper.AllApplicationPackagesSid)!.Grants,
            e => e.IsTraverseOnly && string.Equals(e.Path, tempDir, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UpdateFromPath_NoNewAces_ReturnsFalse()
    {
        // Arrange: path exists; ACL has an ACE for UserSid that already matches the DB
        var tempDir = ExistingDir;
        var rights = ReadOnly;
        // Pre-populate the DB entry to match what ACL would return
        _database.GetOrCreateAccount(UserSid).Grants.Add(
            new GrantedPathEntry { Path = tempDir, IsDeny = false, SavedRights = rights });

        var security = CreateSecurityWithAllowAce(UserSid, GrantRightsMapper.ReadMask);
        _fileSecurityAccessor.Setup(a => a.GetSecurity(tempDir)).Returns(security);

        // Act
        var modified = _service.UpdateFromPath(tempDir, UserSid);

        // Assert: no DB modification (rights already matched)
        Assert.False(modified);
    }

    [Fact]
    public void UpdateFromPath_NullSid_ProcessesAllSidsFoundInAcl()
    {
        // Arrange: path exists; ACL has ACE for UserSid
        var tempDir = ExistingDir;
        var security = CreateSecurityWithAllowAce(UserSid, GrantRightsMapper.ReadMask);
        _fileSecurityAccessor.Setup(a => a.GetSecurity(tempDir)).Returns(security);

        // Act — null sid means process any SID found in the ACL
        var modified = _service.UpdateFromPath(tempDir, sid: null);

        // Assert: DB updated for UserSid
        Assert.True(modified);
        var entry = _database.GetAccount(UserSid)?.Grants
            .FirstOrDefault(e => e is { IsTraverseOnly: false, IsDeny: false });
        Assert.NotNull(entry);
    }

    [Fact]
    public void UpdateFromPath_ManagedAllowAceOnAppEntryPath_SkipsManagedAceButKeepsManualAce()
    {
        _database.Apps.Add(new AppEntry
        {
            Id = "allow-app",
            Name = "AllowApp",
            ExePath = ExistingDir,
            IsFolder = true,
            AclTarget = AclTarget.Folder,
            RestrictAcl = true,
            AclMode = AclMode.Allow,
            AllowedAclEntries =
            [
                new AllowAclEntry
                {
                    Sid = UserSid,
                    AllowExecute = false,
                    AllowWrite = false
                }
            ]
        });

        var security = EmptySecurity();
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(UserSid),
            FileSystemRights.Read | FileSystemRights.Synchronize,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(InteractiveSid),
            FileSystemRights.WriteData,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow));
        _fileSecurityAccessor.Setup(a => a.GetSecurity(ExistingDir)).Returns(security);

        var modified = _service.UpdateFromPath(ExistingDir, sid: null);

        Assert.True(modified);
        Assert.Null(_database.GetAccount(UserSid));
        var entry = _database.GetAccount(InteractiveSid)?.Grants
            .FirstOrDefault(e => e is { IsTraverseOnly: false, IsDeny: false } &&
                                 string.Equals(e.Path, ExistingDir, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(entry);
    }

    [Fact]
    public void UpdateFromPath_ManagedDenyAceOnAppEntryPath_SkipsManagedAceButKeepsManualAce()
    {
        var database = new AppDatabase();
        var denyModeService = new Mock<IAclDenyModeService>();
        denyModeService
            .Setup(service => service.GetDeniedRightsPerSid(
                It.Is<string>(path => string.Equals(path, ExistingDir, StringComparison.OrdinalIgnoreCase)),
                It.IsAny<IReadOnlyList<AppEntry>>(),
                true))
            .Returns(new Dictionary<string, DeniedRights>(StringComparer.OrdinalIgnoreCase)
            {
                [UserSid] = DeniedRights.Execute
            });
        database.Apps.Add(new AppEntry
        {
            Id = "deny-app",
            Name = "DenyApp",
            ExePath = ExistingDir,
            IsFolder = true,
            AclTarget = AclTarget.Folder,
            RestrictAcl = true,
            AclMode = AclMode.Deny,
            DeniedRights = DeniedRights.Execute
        });

        var service = BuildService(database, denyModeService);
        var security = EmptySecurity();
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(UserSid),
            FileSystemRights.ExecuteFile,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Deny));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(InteractiveSid),
            FileSystemRights.ReadData,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Deny));
        _fileSecurityAccessor.Setup(a => a.GetSecurity(ExistingDir)).Returns(security);

        var modified = service.UpdateFromPath(ExistingDir, sid: null);

        Assert.True(modified);
        Assert.Null(database.GetAccount(UserSid));
        var entry = database.GetAccount(InteractiveSid)?.Grants
            .FirstOrDefault(e => e is { IsTraverseOnly: false, IsDeny: true } &&
                                 string.Equals(e.Path, ExistingDir, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(entry);
    }

    // --- Utility: CheckGrantStatus, ReadGrantState, ValidateGrant ---

    [Fact]
    public void CheckGrantStatus_NonExistentPath_ReturnsUnavailable()
    {
        var status = _service.CheckGrantStatus(@"C:\DoesNotExistNever\file.exe",
            UserSid, isDeny: false);

        Assert.Equal(PathAclStatus.Unavailable, status);
    }

    [Fact]
    public void CheckGrantStatus_PathExistsNoMatchingAce_ReturnsBroken()
    {
        // Arrange: path exists (temp dir) but its ACL has no ACE for UserSid in allow mode
        var tempDir = ExistingDir;
        var security = EmptySecurity();
        _fileSecurityAccessor.Setup(a => a.GetSecurity(tempDir)).Returns(security);

        // Act
        var status = _service.CheckGrantStatus(tempDir, UserSid, isDeny: false);

        Assert.Equal(PathAclStatus.Broken, status);
    }

    [Fact]
    public void CheckGrantStatus_PathExistsWithMatchingAce_ReturnsAvailable()
    {
        // Arrange: path exists and ACL has allow ACE for UserSid
        var tempDir = ExistingDir;
        var security = CreateSecurityWithAllowAce(UserSid, GrantRightsMapper.ReadMask);
        _fileSecurityAccessor.Setup(a => a.GetSecurity(tempDir)).Returns(security);

        // Act
        var status = _service.CheckGrantStatus(tempDir, UserSid, isDeny: false);

        Assert.Equal(PathAclStatus.Available, status);
    }

    [Fact]
    public void CheckGrantStatus_TraverseOnlyAce_DoesNotCountAsFullGrant()
    {
        var tempDir = ExistingDir;
        var security = CreateSecurityWithAllowAce(UserSid, GrantRightsMapper.TraverseOnlyMask);
        _fileSecurityAccessor.Setup(a => a.GetSecurity(tempDir)).Returns(security);

        var status = _service.CheckGrantStatus(tempDir, UserSid, isDeny: false);

        Assert.Equal(PathAclStatus.Broken, status);
    }

    [Fact]
    public void ReadGrantState_EmptyAcl_ReturnsAllUncheckedWithZeroAceCounts()
    {
        // Arrange
        var tempDir = ExistingDir;
        _fileSecurityAccessor.Setup(a => a.GetSecurity(tempDir)).Returns(EmptySecurity());

        // Act
        var state = _service.ReadGrantState(tempDir, UserSid, []);

        // Assert: all unchecked, no direct ACEs
        Assert.Equal(RightCheckState.Unchecked, state.AllowExecute);
        Assert.Equal(RightCheckState.Unchecked, state.AllowWrite);
        Assert.Equal(RightCheckState.Unchecked, state.TraverseOnlyAllow);
        Assert.Equal(RightCheckState.Unchecked, state.TraverseOnlyDeny);
        Assert.Equal(0, state.DirectAllowAceCount);
        Assert.Equal(0, state.DirectDenyAceCount);
    }

    [Fact]
    public void ReadGrantState_TraverseOnlyAce_ReportsTraverseOnlyWithoutFullGrant()
    {
        var tempDir = ExistingDir;
        var security = CreateSecurityWithAllowAce(UserSid, GrantRightsMapper.TraverseOnlyMask);
        _fileSecurityAccessor.Setup(a => a.GetSecurity(tempDir)).Returns(security);

        var state = _service.ReadGrantState(tempDir, UserSid, []);

        Assert.Equal(RightCheckState.Checked, state.TraverseOnlyAllow);
        Assert.Equal(RightCheckState.Unchecked, state.AllowExecute);
        Assert.Equal(0, state.DirectAllowAceCount);
    }

    [Fact]
    public void ValidateGrant_NoExistingEntries_DoesNotThrow()
    {
        var exception = Record.Exception(
            () => _service.ValidateGrant(UserSid, TestPath, isDeny: false));

        Assert.Null(exception);
    }

    [Fact]
    public void ValidateGrant_SameModeDuplicate_ThrowsInvalidOperationException()
    {
        // Arrange: add allow grant
        _service.AddGrant(UserSid, TestPath, isDeny: false, ReadOnly);

        // Act + Assert: validate another allow for same path throws
        Assert.Throws<InvalidOperationException>(
            () => _service.ValidateGrant(UserSid, TestPath, isDeny: false));
    }

    [Fact]
    public void ValidateGrant_OppositeModeExists_ThrowsInvalidOperationException()
    {
        // Arrange: add allow grant
        _service.AddGrant(UserSid, TestPath, isDeny: false, ReadOnly);

        // Act + Assert: validate deny for same path throws
        Assert.Throws<InvalidOperationException>(
            () => _service.ValidateGrant(UserSid, TestPath, isDeny: true));
    }

    // --- Helpers ---

    private static FileSystemSecurity EmptySecurity()
    {
        // Create a truly empty security descriptor with no ACEs.
        // Do NOT use DirectoryInfo.GetAccessControl() here — that returns the real DACL from the
        // filesystem which may contain ACEs for the current test user (whose SID could equal UserSid
        // on this machine), causing false positives in tests that expect no matching ACE.
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        return security;
    }

    private static FileSystemSecurity CreateSecurityWithAllowAce(string sid, FileSystemRights rights)
    {
        var security = EmptySecurity();
        var identity = new SecurityIdentifier(sid);
        security.AddAccessRule(new FileSystemAccessRule(
            identity, rights, InheritanceFlags.None, PropagationFlags.None,
            AccessControlType.Allow));
        return security;
    }

    private static FileSystemSecurity CreateSecurityWithOwner(string ownerSid)
    {
        var security = EmptySecurity();
        security.SetOwner(new SecurityIdentifier(ownerSid));
        return security;
    }

    private static FileSystemSecurity CloneSecurity(FileSystemSecurity security)
    {
        var clone = new DirectorySecurity();
        clone.SetSecurityDescriptorSddlForm(
            security.GetSecurityDescriptorSddlForm(AccessControlSections.All),
            AccessControlSections.All);
        return clone;
    }

    private static string GetSecuritySddl(FileSystemSecurity security)
        => security.GetSecurityDescriptorSddlForm(AccessControlSections.All);

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

    private static bool HasExplicitAllowRights(FileSystemSecurity security, string sid, FileSystemRights rights)
        => security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .OfType<FileSystemAccessRule>()
            .Any(rule =>
                rule.AccessControlType == AccessControlType.Allow &&
                string.Equals(rule.IdentityReference.Value, sid, StringComparison.OrdinalIgnoreCase) &&
                (rule.FileSystemRights & rights) == rights);
}
