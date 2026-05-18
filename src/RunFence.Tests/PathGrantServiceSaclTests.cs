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
/// Tests for <see cref="PathGrantService"/> covering Low Integrity SACL label management
/// and <see cref="GrantedPathEntry.SourceSids"/> auto-population/back-tracking.
/// All tests use mocked focused NTFS services so no real NTFS or SACL I/O occurs.
/// </summary>
public class PathGrantServiceSaclTests
{
    private const string UserSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
    private const string OtherUserSid = "S-1-5-21-1234567890-1234567890-1234567890-1002";
    private const string ContainerSid = "S-1-15-2-99-1-2-3-4-5-6";
    private const string LowIlSid = AclHelper.LowIntegritySid;
    private const string TestPath = @"C:\TestFolder\SubDir";
    private const string MediumLabel = "S:(ML;;NW;;;ME)";
    private const string HighLabel = "S:(ML;;NW;;;HI)";

    private readonly Mock<ITraverseAcl> _traverseAcl = new();
    private readonly Mock<IAclPermissionService> _aclPermission = new();
    private readonly Mock<IInteractiveUserResolver> _iuResolver = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly TestFileSystemPathInfo _pathInfo = new();

    private static readonly IUiThreadInvoker SyncInvoker =
        new LambdaUiThreadInvoker(a => a(), a => a());

    private static readonly SavedRightsState ReadOnly =
        new(Execute: false, Write: false, Read: true, Special: false, Own: false);

    private static readonly SavedRightsState WriteRights =
        new(Execute: false, Write: true, Read: true, Special: false, Own: false);

    public PathGrantServiceSaclTests()
    {
        _traverseAcl.Setup(t => t.HasExplicitTraverseAce(It.IsAny<string>(),
                It.IsAny<SecurityIdentifier>()))
            .Returns(true);
        _traverseAcl.Setup(t => t.HasExplicitTraverseAceOrThrow(It.IsAny<string>(),
                It.IsAny<SecurityIdentifier>()))
            .Returns(true);
        _aclPermission.Setup(p => p.ResolveAccountGroupSids(It.IsAny<string>()))
            .Returns([]);
        _aclPermission.Setup(p => p.NeedsPermissionGrant(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<FileSystemRights>(), It.IsAny<bool>()))
            .Returns(true);
        _aclPermission.Setup(p => p.HasEffectiveRights(
                It.IsAny<FileSystemSecurity>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<FileSystemRights>()))
            .Returns(true);
        _iuResolver.Setup(r => r.GetInteractiveUserSid()).Returns((string?)null);
    }

    private (PathGrantService service, Mock<IMandatoryLabelService> mandatoryLabelMock, AppDatabase db) BuildService()
    {
        var grantAceMock = new Mock<IGrantAceService>();
        var ownerMock = new Mock<IFileOwnerService>();
        var aclAccessorMock = new Mock<IAclAccessor>();
        var mandatoryLabelMock = new Mock<IMandatoryLabelService>();
        var db = new AppDatabase();
        var ownershipProjection = new GrantIntentOwnershipProjectionService();
        var mainGrantStore = new RuntimeDatabaseGrantIntentStore(() => db, ownershipProjection);
        var storeProvider = new TestGrantIntentStoreProvider(mainGrantStore, ownershipProjection);
        var repository = new GrantIntentRepository(storeProvider);
        var traverseGrantOwnerResolver = new TraverseGrantOwnerResolver();
        var traverseIntentStoreCoordinator = new TraverseIntentStoreCoordinator(() => repository, traverseGrantOwnerResolver);
        var dbAccessor = new UiThreadDatabaseAccessor(new LambdaDatabaseProvider(() => db), () => SyncInvoker);
        var ancestorGranter = new AncestorTraverseGranter(_log.Object, _aclPermission.Object, _traverseAcl.Object,
            _pathInfo);
        var grantCore = new GrantCoreOperations(grantAceMock.Object, ownerMock.Object,
            dbAccessor, _log.Object, _pathInfo);
        var traverseCore = new TraverseCoreOperations(_traverseAcl.Object,
            ancestorGranter, _aclPermission.Object, dbAccessor, _log.Object, _pathInfo, traverseGrantOwnerResolver);
        var containerIuSync = new ContainerInteractiveUserSync(grantCore, traverseCore,
            traverseGrantOwnerResolver, _iuResolver.Object, _aclPermission.Object, dbAccessor, _log.Object, _pathInfo);
        var traverseGrantStateService = new TraverseGrantStateService(dbAccessor, _pathInfo, traverseIntentStoreCoordinator);
        var lowIlSync = new LowIntegrityGrantSync(grantCore, traverseCore,
            mandatoryLabelMock.Object, dbAccessor);
        var syncService = new PathGrantSyncService(
            dbAccessor,
            grantAceMock.Object,
            () => storeProvider,
            () => repository,
            _log.Object,
            _pathInfo,
            traverseGrantOwnerResolver);
        var fsOps = new GrantFileSystemOperations(grantCore, grantAceMock.Object,
            ownerMock.Object, mandatoryLabelMock.Object, dbAccessor);
        var accessEnsurer = new GrantAccessEnsurer(_aclPermission.Object, dbAccessor,
            aclAccessorMock.Object, _pathInfo, traverseCore, fsOps, _iuResolver.Object, traverseGrantOwnerResolver, () => repository, () => mainGrantStore, new GrantIntentStoreSaveService());
        var grantIntentStoreSaveService = new GrantIntentStoreSaveService();
        var traverseRestoreWorkflow = new TraverseRestoreWorkflow(
            traverseCore,
            dbAccessor,
            containerIuSync,
            _pathInfo,
            _traverseAcl.Object,
            traverseGrantOwnerResolver,
            traverseIntentStoreCoordinator,
            traverseGrantStateService,
            () => storeProvider,
            grantIntentStoreSaveService);
        var service = new PathGrantService(grantCore, traverseCore, dbAccessor,
            containerIuSync, lowIlSync, syncService, mandatoryLabelMock.Object, fsOps, accessEnsurer, grantAceMock.Object, _pathInfo,
            aclAccessorMock.Object, traverseGrantOwnerResolver, traverseIntentStoreCoordinator, traverseGrantStateService, () => storeProvider, () => repository, () => mainGrantStore,
            grantIntentStoreSaveService, traverseRestoreWorkflow);
        return (service, mandatoryLabelMock, db);
    }

    // --- AddGrant: SACL label management ---

    [Fact]
    public void AddGrant_LowIntegritySid_WriteRights_StoresPreviousLabelAndAppliesLowLabel()
    {
        // Arrange
        var (service, mandatoryLabelMock, db) = BuildService();
        mandatoryLabelMock.Setup(n => n.ReadMandatoryLabel(TestPath)).Returns(MediumLabel);

        // Act
        service.AddGrant(LowIlSid, TestPath, isDeny: false, WriteRights);

        // Assert: PreviousSaclLabel stored, low label applied
        var entry = db.GetAccount(LowIlSid)!.Grants
            .First(e => e is { IsTraverseOnly: false, IsDeny: false });
        Assert.Equal(MediumLabel, entry.PreviousSaclLabel);
        mandatoryLabelMock.Verify(n => n.ApplyLowIntegrityLabel(TestPath), Times.Once);
    }

    [Fact]
    public void AddGrant_LowIntegritySid_ReadRightsOnly_NoSaclCalls()
    {
        // Arrange
        var (service, mandatoryLabelMock, _) = BuildService();

        // Act
        service.AddGrant(LowIlSid, TestPath, isDeny: false, ReadOnly);

        // Assert: no SACL operations
        mandatoryLabelMock.Verify(n => n.ReadMandatoryLabel(It.IsAny<string>()), Times.Never);
        mandatoryLabelMock.Verify(n => n.ApplyLowIntegrityLabel(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void AddGrant_LowIntegritySid_WriteRights_AlreadyExistingEntry_PreviousLabelNotOverwritten()
    {
        // Arrange: create an existing Low IL write grant whose stored previous label is already High.
        var (service, mandatoryLabelMock, db) = BuildService();
        mandatoryLabelMock.Setup(n => n.ReadMandatoryLabel(TestPath)).Returns(HighLabel);
        service.AddGrant(LowIlSid, TestPath, isDeny: false, WriteRights);
        mandatoryLabelMock.Invocations.Clear();
        mandatoryLabelMock.Setup(n => n.ReadMandatoryLabel(TestPath)).Returns(MediumLabel);

        // Act: re-grant with Write (entry already existed — AlreadyExisted=true)
        service.AddGrant(LowIlSid, TestPath, isDeny: false, WriteRights);

        // Assert: label NOT overwritten to MediumLabel, still HighLabel
        var entry = db.GetAccount(LowIlSid)!.Grants
            .First(e => e is { IsTraverseOnly: false, IsDeny: false });
        Assert.Equal(HighLabel, entry.PreviousSaclLabel);
        // ApplyLowIntegrityLabel is still called (idempotent)
        mandatoryLabelMock.Verify(n => n.ApplyLowIntegrityLabel(TestPath), Times.Once);
    }

    // --- UpdateGrant: SACL transitions ---

    [Fact]
    public void UpdateGrant_LowIntegritySid_ReadToWrite_StoresLabelAndApplies()
    {
        // Arrange: Low IL entry with Read rights (no Write yet)
        var (service, mandatoryLabelMock, db) = BuildService();
        service.AddGrant(LowIlSid, TestPath, isDeny: false, ReadOnly);
        mandatoryLabelMock.Setup(n => n.ReadMandatoryLabel(TestPath)).Returns(MediumLabel);

        // Act: update to Write rights
        service.UpdateGrant(LowIlSid, TestPath, isDeny: false, WriteRights);

        // Assert: PreviousSaclLabel stored, low label applied
        var entry = db.GetAccount(LowIlSid)!.Grants
            .First(e => e is { IsTraverseOnly: false, IsDeny: false });
        Assert.Equal(MediumLabel, entry.PreviousSaclLabel);
        mandatoryLabelMock.Verify(n => n.ApplyLowIntegrityLabel(TestPath), Times.Once);
        mandatoryLabelMock.Verify(n => n.RestoreMandatoryLabel(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void UpdateGrant_LowIntegritySid_WriteToRead_ClearsLabelAndRestores()
    {
        // Arrange: Low IL entry with Write rights and stored PreviousSaclLabel
        var (service, mandatoryLabelMock, db) = BuildService();
        mandatoryLabelMock.Setup(n => n.ReadMandatoryLabel(TestPath)).Returns(MediumLabel);
        service.AddGrant(LowIlSid, TestPath, isDeny: false, WriteRights);
        mandatoryLabelMock.Invocations.Clear();

        // Act: update to Read-only (remove Write)
        service.UpdateGrant(LowIlSid, TestPath, isDeny: false, ReadOnly);

        // Assert: PreviousSaclLabel cleared, original label restored
        var entry = db.GetAccount(LowIlSid)!.Grants
            .First(e => e is { IsTraverseOnly: false, IsDeny: false });
        Assert.Null(entry.PreviousSaclLabel);
        mandatoryLabelMock.Verify(n => n.RestoreMandatoryLabel(TestPath, MediumLabel), Times.Once);
        mandatoryLabelMock.Verify(n => n.ApplyLowIntegrityLabel(It.IsAny<string>()), Times.Never);
    }

    // --- RemoveGrant: SACL restore ---

    [Fact]
    public void RemoveGrant_LowIntegritySid_WithWrite_RestoresSaclLabel()
    {
        // Arrange: Low IL entry with Write rights and stored PreviousSaclLabel
        var (service, mandatoryLabelMock, db) = BuildService();
        mandatoryLabelMock.Setup(n => n.ReadMandatoryLabel(TestPath)).Returns(MediumLabel);
        service.AddGrant(LowIlSid, TestPath, isDeny: false, WriteRights);
        mandatoryLabelMock.Invocations.Clear();

        // Act
        service.RemoveGrant(LowIlSid, TestPath, isDeny: false);

        // Assert: label restored
        mandatoryLabelMock.Verify(n => n.RestoreMandatoryLabel(TestPath, MediumLabel), Times.Once);
        Assert.Null(db.GetAccount(LowIlSid)?.Grants
            .FirstOrDefault(e => e is { IsTraverseOnly: false, IsDeny: false }));
    }

    [Fact]
    public void RemoveGrant_LowIntegritySid_UpdateFileSystemFalse_WithWrite_DoesNotRestoreSaclLabel()
    {
        // Arrange: DB-only remove of a Low IL write grant must not touch NTFS SACL state.
        var (service, mandatoryLabelMock, db) = BuildService();
        mandatoryLabelMock.Setup(n => n.ReadMandatoryLabel(TestPath)).Returns(MediumLabel);
        service.AddGrant(LowIlSid, TestPath, isDeny: false, WriteRights);
        mandatoryLabelMock.Invocations.Clear();

        // Act
        var removed = service.UntrackGrant(LowIlSid, TestPath, isDeny: false);

        // Assert
        Assert.True(removed.DatabaseModified);
        Assert.False(removed.GrantApplied);
        mandatoryLabelMock.Verify(n => n.RestoreMandatoryLabel(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
        Assert.Null(db.GetAccount(LowIlSid)?.Grants
            .FirstOrDefault(e => e is { IsTraverseOnly: false, IsDeny: false }));
    }

    [Fact]
    public void RemoveGrant_SourceSid_UpdateFileSystemFalse_LastLowIntegritySource_DoesNotRestoreSaclLabel()
    {
        var (service, mandatoryLabelMock, db) = BuildService();
        service.AddGrant(UserSid, TestPath, isDeny: false, ReadOnly);
        mandatoryLabelMock.Setup(n => n.ReadMandatoryLabel(TestPath)).Returns(MediumLabel);
        service.AddGrant(LowIlSid, TestPath, isDeny: false, WriteRights);
        mandatoryLabelMock.Invocations.Clear();

        var removed = service.UntrackGrant(UserSid, TestPath, isDeny: false);

        Assert.True(removed.DatabaseModified);
        Assert.False(removed.GrantApplied);
        mandatoryLabelMock.Verify(n => n.RestoreMandatoryLabel(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
        Assert.Null(db.GetAccount(UserSid)?.Grants
            .FirstOrDefault(e => e is { IsTraverseOnly: false, IsDeny: false }));
        Assert.Null(db.GetAccount(LowIlSid)?.Grants
            .FirstOrDefault(e => e is { IsTraverseOnly: false, IsDeny: false }));
    }

    [Fact]
    public void RemoveGrant_LowIntegritySid_ReadOnly_NoSaclRestore()
    {
        // Arrange: Low IL entry with Read-only rights (no Write, no PreviousSaclLabel)
        var (service, mandatoryLabelMock, db) = BuildService();
        db.GetOrCreateAccount(LowIlSid).Grants.Add(new GrantedPathEntry
        {
            Path = TestPath, IsDeny = false,
            SavedRights = ReadOnly
        });

        // Act
        service.RemoveGrant(LowIlSid, TestPath, isDeny: false);

        // Assert: no SACL restore
        mandatoryLabelMock.Verify(n => n.RestoreMandatoryLabel(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    // --- RemoveAll: SACL restore for Low IL SID ---

    [Fact]
    public void RemoveAll_LowIntegritySid_WithWriteGrants_RestoresAllSaclLabels()
    {
        // Arrange: Low IL account with one Write entry and one Read entry
        var (service, mandatoryLabelMock, _) = BuildService();
        const string writePath = @"C:\TestFolder\WriteDir";
        const string readPath = @"C:\TestFolder\ReadDir";
        mandatoryLabelMock.Setup(n => n.ReadMandatoryLabel(writePath)).Returns(MediumLabel);
        service.AddGrant(LowIlSid, writePath, isDeny: false, WriteRights);
        service.AddGrant(LowIlSid, readPath, isDeny: false, ReadOnly);
        mandatoryLabelMock.Invocations.Clear();

        // Act
        service.RemoveAll(LowIlSid);

        // Assert: RestoreMandatoryLabel called only for the Write entry
        mandatoryLabelMock.Verify(n => n.RestoreMandatoryLabel(writePath, MediumLabel), Times.Once);
        mandatoryLabelMock.Verify(n => n.RestoreMandatoryLabel(readPath, It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void RemoveAll_LowIntegritySid_UpdateFileSystemFalse_NoSaclRestore()
    {
        // Arrange: Low IL account with a Write entry
        var (service, mandatoryLabelMock, db) = BuildService();
        db.GetOrCreateAccount(LowIlSid).Grants.Add(new GrantedPathEntry
        {
            Path = TestPath, IsDeny = false,
            SavedRights = WriteRights,
            PreviousSaclLabel = MediumLabel
        });

        // Act: updateFileSystem=false — no NTFS operations
        service.UntrackAll(LowIlSid);

        // Assert: no SACL restore
        mandatoryLabelMock.Verify(n => n.RestoreMandatoryLabel(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    // --- SourceSids auto-population and back-tracking ---

    [Fact]
    public void AddGrant_LowIntegritySid_ExistingAccountGrants_PopulatesSourceSids()
    {
        // Arrange: two regular accounts already have grants to TestPath
        var (service, _, db) = BuildService();
        service.AddGrant(UserSid, TestPath, isDeny: false, ReadOnly);
        service.AddGrant(OtherUserSid, TestPath, isDeny: false, ReadOnly);

        // Act: add Low IL grant
        service.AddGrant(LowIlSid, TestPath, isDeny: false, ReadOnly);

        // Assert: SourceSids populated with both account SIDs
        var entry = db.GetAccount(LowIlSid)!.Grants
            .First(e => e is { IsTraverseOnly: false, IsDeny: false });
        Assert.NotNull(entry.SourceSids);
        Assert.Contains(UserSid, entry.SourceSids);
        Assert.Contains(OtherUserSid, entry.SourceSids);
    }

    [Fact]
    public void AddGrant_LowIntegritySid_NoExistingAccountGrants_SourceSidsNull()
    {
        // Arrange: no other account grants on TestPath
        var (service, _, db) = BuildService();

        // Act: add Low IL grant (manual via ACL Manager)
        service.AddGrant(LowIlSid, TestPath, isDeny: false, ReadOnly);

        // Assert: SourceSids remains null (manual grant)
        var entry = db.GetAccount(LowIlSid)!.Grants
            .First(e => e is { IsTraverseOnly: false, IsDeny: false });
        Assert.Null(entry.SourceSids);
    }

    [Fact]
    public void AddGrant_NonLowIlSid_PathHasAutoManagedLowIlGrant_AddsSidToSourceSids()
    {
        // Arrange: Low IL grant already exists with SourceSids (auto-managed)
        var (service, _, db) = BuildService();
        service.AddGrant(UserSid, TestPath, isDeny: false, ReadOnly);
        service.AddGrant(LowIlSid, TestPath, isDeny: false, ReadOnly);

        // Act: new regular account grant added — should be back-tracked into Low IL SourceSids
        service.AddGrant(OtherUserSid, TestPath, isDeny: false, ReadOnly);

        // Assert: OtherUserSid added to Low IL grant's SourceSids
        var lowIlEntry = db.GetAccount(LowIlSid)!.Grants
            .First(e => e is { IsTraverseOnly: false, IsDeny: false });
        Assert.Contains(OtherUserSid, lowIlEntry.SourceSids!);
        Assert.Contains(UserSid, lowIlEntry.SourceSids!);
    }

    [Fact]
    public void AddGrant_NonLowIlSid_PathHasManualLowIlGrant_NullSourceSidsPreserved()
    {
        // Arrange: Low IL grant with null SourceSids (manual, not auto-managed)
        var (service, _, db) = BuildService();
        db.GetOrCreateAccount(LowIlSid).Grants.Add(new GrantedPathEntry
        {
            Path = TestPath, IsDeny = false, SavedRights = ReadOnly,
            SourceSids = null
        });

        // Act: new regular account grant added
        service.AddGrant(UserSid, TestPath, isDeny: false, ReadOnly);

        // Assert: SourceSids remains null — manual grant never converted to auto-managed
        var lowIlEntry = db.GetAccount(LowIlSid)!.Grants
            .First(e => e is { IsTraverseOnly: false, IsDeny: false });
        Assert.Null(lowIlEntry.SourceSids);
    }

    [Fact]
    public void AddGrant_ContainerSid_PathHasAutoManagedLowIlGrant_NotAddedToSourceSids()
    {
        // Arrange: Low IL grant with SourceSids (auto-managed)
        var (service, _, db) = BuildService();
        db.GetOrCreateAccount(LowIlSid).Grants.Add(new GrantedPathEntry
        {
            Path = TestPath, IsDeny = false, SavedRights = ReadOnly,
            SourceSids = []
        });

        // Act: container SID grant added (containers are excluded from SourceSids)
        service.AddGrant(ContainerSid, TestPath, isDeny: false, ReadOnly);

        // Assert: container SID NOT added to Low IL SourceSids
        var lowIlEntry = db.GetAccount(LowIlSid)!.Grants
            .First(e => e is { IsTraverseOnly: false, IsDeny: false });
        Assert.DoesNotContain(ContainerSid, lowIlEntry.SourceSids!);
    }
}
