using Moq;
using RunFence.Acl;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Tests for <see cref="LowIntegrityGrantSync"/> covering all branching paths in
/// <see cref="LowIntegrityGrantSync.RevertSource"/> and <see cref="LowIntegrityGrantSync.RevertAllSources"/>.
/// Mandatory-label calls are mocked via <see cref="IMandatoryLabelService"/>.
/// DB access is performed inline on the calling thread via a synchronous invoker.
/// </summary>
public class LowIntegrityGrantSyncTests
{
    private const string AccountSid = "S-1-5-21-1000-1000-1000-1001";
    private const string OtherAccountSid = "S-1-5-21-1000-1000-1000-1002";
    private const string LowIlSid = AclHelper.LowIntegritySid;
    private const string TestPath = @"C:\TestFolder\SubDir";
    private const string TestPath2 = @"C:\TestFolder\SubDir2";

    private readonly Mock<IMandatoryLabelService> _mandatoryLabelService = new();
    private readonly Mock<IGrantCoreOperations> _grantCore = new();
    private readonly Mock<ITraverseCoreOperations> _traverseCore = new();
    private readonly AppDatabase _database = new();
    private readonly LowIntegrityGrantSync _sync;

    private static readonly IUiThreadInvoker SyncInvoker =
        new LambdaUiThreadInvoker(a => a(), a => a());

    public LowIntegrityGrantSyncTests()
    {
        var dbProvider = new LambdaDatabaseProvider(() => _database);
        var dbAccessor = new UiThreadDatabaseAccessor(dbProvider, SyncInvoker);
        _sync = new LowIntegrityGrantSync(_grantCore.Object, _traverseCore.Object,
            _mandatoryLabelService.Object, dbAccessor);
    }

    private GrantedPathEntry AddLowIlGrant(string path, List<string>? sourceSids,
        bool write = false, string? previousSaclLabel = null)
    {
        var entry = new GrantedPathEntry
        {
            Path = Path.GetFullPath(path),
            IsDeny = false,
            IsTraverseOnly = false,
            SavedRights = new SavedRightsState(Execute: false, Write: write, Read: true, Special: false, Own: false),
            SourceSids = sourceSids,
            PreviousSaclLabel = previousSaclLabel
        };
        _database.GetOrCreateAccount(LowIlSid).Grants.Add(entry);
        return entry;
    }

    // --- RevertSource ---

    [Fact]
    public void RevertSource_NullSourceSids_IsNoOp()
    {
        // Arrange: Low IL grant has null SourceSids (manually added via ACL Manager)
        var normalized = Path.GetFullPath(TestPath);
        AddLowIlGrant(TestPath, sourceSids: null, write: true, previousSaclLabel: "S:(ML;;NW;;;ME)");

        // Act
        _sync.RevertSource(AccountSid, normalized, updateFileSystem: true);

        // Assert: no NTFS removal, no SACL restore, grant entry unchanged
        _grantCore.Verify(g => g.RemoveGrant(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
        _mandatoryLabelService.Verify(n => n.RestoreMandatoryLabel(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);

        var entry = _database.GetAccount(LowIlSid)!.Grants.FirstOrDefault(e => e.Path == normalized);
        Assert.NotNull(entry);
        Assert.Null(entry.SourceSids);
    }

    [Fact]
    public void RevertSource_SourceSidsStillHasOtherSids_RemovesSidOnly_KeepsGrant()
    {
        // Arrange: two source SIDs; remove one → SourceSids has one remaining, grant not removed
        var normalized = Path.GetFullPath(TestPath);
        var entry = AddLowIlGrant(TestPath, sourceSids: [AccountSid, OtherAccountSid]);

        // Act
        _sync.RevertSource(AccountSid, normalized, updateFileSystem: true);

        // Assert: SourceSids has only the remaining SID
        Assert.Single(entry.SourceSids!);
        Assert.Equal(OtherAccountSid, entry.SourceSids![0], StringComparer.OrdinalIgnoreCase);

        // Assert: grant NOT removed
        _grantCore.Verify(g => g.RemoveGrant(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
        _mandatoryLabelService.Verify(n => n.RestoreMandatoryLabel(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void RevertSource_SourceSidsBecomesEmpty_WithWrite_RemovesGrantAndRestoresSacl()
    {
        // Arrange: single source SID, Write grant with stored previous SACL label
        var normalized = Path.GetFullPath(TestPath);
        const string previousLabel = "S:(ML;;NW;;;ME)";
        AddLowIlGrant(TestPath, sourceSids: [AccountSid], write: true, previousSaclLabel: previousLabel);

        // Setup grantCore to indicate a found entry
        _grantCore.Setup(g => g.RemoveGrant(LowIlSid, normalized, false, true))
            .Returns(new GrantRemoveResult(Found: true, SavedRights: null));

        // Act
        _sync.RevertSource(AccountSid, normalized, updateFileSystem: true);

        // Assert: NTFS ACE removed
        _grantCore.Verify(g => g.RemoveGrant(LowIlSid, normalized, false, true), Times.Once);

        // Assert: traverse cleanup called
        _traverseCore.Verify(t => t.CleanupOrphanedTraverse(LowIlSid, normalized), Times.Once);

        // Assert: SACL restored with the stored previous label
        _mandatoryLabelService.Verify(n => n.RestoreMandatoryLabel(normalized, previousLabel), Times.Once);
    }

    [Fact]
    public void RevertSource_UpdateFileSystemFalse_WithWrite_RemovesDbGrantWithoutRestoringSacl()
    {
        var normalized = Path.GetFullPath(TestPath);
        const string previousLabel = "S:(ML;;NW;;;ME)";
        AddLowIlGrant(TestPath, sourceSids: [AccountSid], write: true, previousSaclLabel: previousLabel);

        _grantCore.Setup(g => g.RemoveGrant(LowIlSid, normalized, false, false))
            .Returns(new GrantRemoveResult(Found: true, SavedRights: null));

        _sync.RevertSource(AccountSid, normalized, updateFileSystem: false);

        _grantCore.Verify(g => g.RemoveGrant(LowIlSid, normalized, false, false), Times.Once);
        _mandatoryLabelService.Verify(n => n.RestoreMandatoryLabel(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void RevertSource_SourceSidsBecomesEmpty_ReadOnly_RemovesGrantNoSaclRestore()
    {
        // Arrange: single source SID, Read-only grant (no Write)
        var normalized = Path.GetFullPath(TestPath);
        AddLowIlGrant(TestPath, sourceSids: [AccountSid], write: false);

        // Setup grantCore to indicate a found entry
        _grantCore.Setup(g => g.RemoveGrant(LowIlSid, normalized, false, true))
            .Returns(new GrantRemoveResult(Found: true, SavedRights: null));

        // Act
        _sync.RevertSource(AccountSid, normalized, updateFileSystem: true);

        // Assert: NTFS ACE removed
        _grantCore.Verify(g => g.RemoveGrant(LowIlSid, normalized, false, true), Times.Once);

        // Assert: traverse cleanup called
        _traverseCore.Verify(t => t.CleanupOrphanedTraverse(LowIlSid, normalized), Times.Once);

        // Assert: SACL restore NOT called (no Write)
        _mandatoryLabelService.Verify(n => n.RestoreMandatoryLabel(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    // --- RevertAllSources ---

    [Fact]
    public void RevertAllSources_MultiPath_RevokesAllMatchingPaths()
    {
        // Arrange: account has Low IL grants on two paths, both listing AccountSid as source
        var normalized1 = Path.GetFullPath(TestPath);
        var normalized2 = Path.GetFullPath(TestPath2);
        AddLowIlGrant(TestPath, sourceSids: [AccountSid], write: false);
        AddLowIlGrant(TestPath2, sourceSids: [AccountSid], write: false);

        // Setup grantCore to return found=true for both paths
        _grantCore.Setup(g => g.RemoveGrant(LowIlSid, normalized1, false, true))
            .Returns(new GrantRemoveResult(Found: true, SavedRights: null));
        _grantCore.Setup(g => g.RemoveGrant(LowIlSid, normalized2, false, true))
            .Returns(new GrantRemoveResult(Found: true, SavedRights: null));

        // Act
        _sync.RevertAllSources(AccountSid, updateFileSystem: true);

        // Assert: both grants removed
        _grantCore.Verify(g => g.RemoveGrant(LowIlSid, normalized1, false, true), Times.Once);
        _grantCore.Verify(g => g.RemoveGrant(LowIlSid, normalized2, false, true), Times.Once);
        _traverseCore.Verify(t => t.CleanupOrphanedTraverse(LowIlSid, normalized1), Times.Once);
        _traverseCore.Verify(t => t.CleanupOrphanedTraverse(LowIlSid, normalized2), Times.Once);
    }

    [Fact]
    public void RevertAllSources_UpdateFileSystemFalse_DoesNotRestoreSacl()
    {
        var normalized = Path.GetFullPath(TestPath);
        const string previousLabel = "S:(ML;;NW;;;ME)";
        AddLowIlGrant(TestPath, sourceSids: [AccountSid], write: true, previousSaclLabel: previousLabel);

        _grantCore.Setup(g => g.RemoveGrant(LowIlSid, normalized, false, false))
            .Returns(new GrantRemoveResult(Found: true, SavedRights: null));

        _sync.RevertAllSources(AccountSid, updateFileSystem: false);

        _grantCore.Verify(g => g.RemoveGrant(LowIlSid, normalized, false, false), Times.Once);
        _mandatoryLabelService.Verify(n => n.RestoreMandatoryLabel(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void RevertAllSources_NoMatchingPaths_IsNoOp()
    {
        // Arrange: Low IL account has a grant, but it lists a different source SID
        var normalized = Path.GetFullPath(TestPath);
        AddLowIlGrant(TestPath, sourceSids: [OtherAccountSid], write: false);

        // Act
        _sync.RevertAllSources(AccountSid, updateFileSystem: true);

        // Assert: nothing removed
        _grantCore.Verify(g => g.RemoveGrant(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
        _mandatoryLabelService.Verify(n => n.RestoreMandatoryLabel(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);

        // Assert: grant entry unchanged
        var entry = _database.GetAccount(LowIlSid)!.Grants.FirstOrDefault(e => e.Path == normalized);
        Assert.NotNull(entry);
        Assert.Contains(OtherAccountSid, entry.SourceSids!);
    }
}
