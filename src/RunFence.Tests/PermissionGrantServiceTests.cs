using System.Security.AccessControl;
using Moq;
using RunFence.Acl.Permissions;
using RunFence.Acl.Traverse;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public class PermissionGrantServiceTests
{
    private const string UserSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
    private const string ContainerSid = "S-1-15-2-99-1-2-3-4-5-6";
    private const string InteractiveSid = "S-1-5-21-1234567890-1234567890-1234567890-1002";

    private readonly Mock<IAclPermissionService> _aclPermission = new();
    private readonly Mock<IUserTraverseService> _traverseService = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly AppDatabase _database = new();
    private readonly PermissionGrantService _service;

    // All invoker strategies run synchronously so DB side-effects are visible in tests.
    private static readonly IUiThreadInvoker SyncInvoker = new LambdaUiThreadInvoker(a => a(), a => a(), a => a());

    public PermissionGrantServiceTests()
    {
        _service = new PermissionGrantService(
            _aclPermission.Object, _traverseService.Object,
            new LambdaDatabaseProvider(() => _database), _log.Object,
            new DefaultInteractiveUserResolver(), SyncInvoker);
    }

    private void SetupEnsureRightsGrants(string path, string sid)
        => _aclPermission.Setup(a => a.EnsureRights(path, sid, It.IsAny<FileSystemRights>(),
                It.IsAny<ILoggingService>(), It.IsAny<Func<string, bool>?>()))
            .Returns(true);

    private void SetupEnsureRightsAlreadySufficient(string path, string sid)
        => _aclPermission.Setup(a => a.EnsureRights(path, sid, It.IsAny<FileSystemRights>(),
                It.IsAny<ILoggingService>(), It.IsAny<Func<string, bool>?>()))
            .Returns(false);

    // --- Core grant flow ---

    [Fact]
    public void EnsureAccess_UserApproves_AceAddedGrantRecordedTraverseAdded()
    {
        // Arrange
        var path = @"C:\Apps\MyApp";
        _aclPermission.Setup(a => a.EnsureRights(path, UserSid, FileSystemRights.ReadAndExecute,
                It.IsAny<ILoggingService>(), It.IsAny<Func<string, bool>?>()))
            .Callback<string, string, FileSystemRights, ILoggingService, Func<string, bool>?>((p, _, _, _, confirm) => confirm!(p))
            .Returns(true);
        _traverseService.Setup(t => t.EnsureTraverseAccess(UserSid, @"C:\Apps")).Returns((true, new List<string>()));

        // Act
        var result = _service.EnsureAccess(path, UserSid, FileSystemRights.ReadAndExecute, (_, _) => true);

        // Assert
        Assert.True(result.GrantAdded);
        var grants = _database.GetAccount(UserSid)?.Grants;
        Assert.NotNull(grants);
        Assert.Single(grants);
        Assert.Equal(path, grants[0].Path);
        _traverseService.Verify(t => t.EnsureTraverseAccess(UserSid, @"C:\Apps"), Times.Once);
    }

    [Fact]
    public void EnsureAccess_AclsAlreadySufficient_NoGrantNoAddGrantTraverseStillAttempted()
    {
        // Arrange
        var path = @"C:\Apps\MyApp";
        SetupEnsureRightsAlreadySufficient(path, UserSid);
        _traverseService.Setup(t => t.EnsureTraverseAccess(UserSid, @"C:\Apps")).Returns((false, new List<string>()));

        int confirmCalls = 0;
        var result = _service.EnsureAccess(path, UserSid, FileSystemRights.ReadAndExecute,
            (_, _) =>
            {
                confirmCalls++;
                return true;
            });

        Assert.False(result.GrantAdded);
        Assert.Equal(0, confirmCalls);
        Assert.Null(_database.GetAccount(UserSid)); // AddGrant not called
        _traverseService.Verify(t => t.EnsureTraverseAccess(UserSid, @"C:\Apps"), Times.Once);
    }

    [Fact]
    public void EnsureAccess_UserDeclined_NoGrantNoAddGrantNoTraverse()
    {
        // Arrange
        var path = @"C:\Apps\MyApp";
        _aclPermission.Setup(a => a.EnsureRights(path, UserSid, FileSystemRights.ReadAndExecute,
                It.IsAny<ILoggingService>(), It.IsAny<Func<string, bool>?>()))
            .Callback<string, string, FileSystemRights, ILoggingService, Func<string, bool>?>((p, _, _, _, confirm) => confirm!(p))
            .Returns(false);

        var result = _service.EnsureAccess(path, UserSid, FileSystemRights.ReadAndExecute, (_, _) => false);

        Assert.False(result.GrantAdded);
        Assert.Null(_database.GetAccount(UserSid)); // AddGrant not called
        _traverseService.Verify(t => t.EnsureTraverseAccess(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void EnsureAccess_NullConfirm_SilentlyGrantsAndAddsTraverse()
    {
        // Arrange
        var path = @"C:\Apps\MyApp";
        SetupEnsureRightsGrants(path, UserSid);
        _traverseService.Setup(t => t.EnsureTraverseAccess(UserSid, @"C:\Apps")).Returns((true, new List<string>()));

        var result = _service.EnsureAccess(path, UserSid, FileSystemRights.ReadAndExecute, confirm: null);

        Assert.True(result.GrantAdded);
        Assert.NotNull(_database.GetAccount(UserSid));
        _traverseService.Verify(t => t.EnsureTraverseAccess(UserSid, @"C:\Apps"), Times.Once);
    }

    [Fact]
    public void EnsureAccess_OceFromConfirm_PropagatesNoTraverse()
    {
        // Arrange
        var path = @"C:\Apps\MyApp";
        _aclPermission.Setup(a => a.EnsureRights(path, UserSid, FileSystemRights.ReadAndExecute,
                It.IsAny<ILoggingService>(), It.IsAny<Func<string, bool>?>()))
            .Callback<string, string, FileSystemRights, ILoggingService, Func<string, bool>?>((p, _, _, _, confirm) => confirm!(p))
            .Returns(false);

        Assert.Throws<OperationCanceledException>(() =>
            _ = _service.EnsureAccess(path, UserSid, FileSystemRights.ReadAndExecute,
                (_, _) => throw new OperationCanceledException()));

        _traverseService.Verify(t => t.EnsureTraverseAccess(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        Assert.Null(_database.GetAccount(UserSid)); // AddGrant not called
    }

    // --- Edge cases ---

    [Fact]
    public void EnsureAccess_RootLevelPath_AceAttemptedTraverseAttemptedOnRoot()
    {
        // C:\ is itself a directory: traverseDir = C:\ (Directory.Exists branch), traverse IS attempted.
        var path = @"C:\";
        SetupEnsureRightsGrants(path, UserSid);
        _traverseService.Setup(t => t.EnsureTraverseAccess(UserSid, @"C:\")).Returns((false, new List<string>()));

        var result = _service.EnsureAccess(path, UserSid, FileSystemRights.ReadAndExecute, confirm: null);

        Assert.True(result.GrantAdded);
        _traverseService.Verify(t => t.EnsureTraverseAccess(UserSid, @"C:\"), Times.Once);
    }

    [Fact]
    public void EnsureAccess_EnsureRightsThrows_Propagates()
    {
        // Arrange
        var path = @"C:\Apps\MyApp";
        _aclPermission.Setup(a => a.EnsureRights(path, UserSid, FileSystemRights.ReadAndExecute,
                It.IsAny<ILoggingService>(), It.IsAny<Func<string, bool>?>()))
            .Throws(new UnauthorizedAccessException("Access denied"));

        Assert.Throws<UnauthorizedAccessException>(() =>
            _ = _service.EnsureAccess(path, UserSid, FileSystemRights.ReadAndExecute, confirm: null));
    }

    [Fact]
    public void EnsureAccess_AclsAlreadySufficient_TraverseAdded_GrantAddedFalseDbModifiedTrue()
    {
        // ACE not added (account already has the rights) but traverse IS newly added.
        // GrantAdded must be false — callers must not add to grantedPaths or trigger pinning
        // for paths the account already had access to. DatabaseModified must be true — traverse
        // was tracked in the database, so callers must save.
        var path = @"C:\Apps\MyApp";
        SetupEnsureRightsAlreadySufficient(path, UserSid);
        _traverseService.Setup(t => t.EnsureTraverseAccess(UserSid, @"C:\Apps")).Returns((true, new List<string>()));

        var result = _service.EnsureAccess(path, UserSid, FileSystemRights.ReadAndExecute, confirm: null);

        Assert.False(result.GrantAdded);     // no ACE added → GrantAdded false
        Assert.True(result.DatabaseModified); // traverse tracked → DatabaseModified true
        Assert.Null(_database.GetAccount(UserSid)); // AddGrant not called
    }

    [Fact]
    public void EnsureAccess_ContainerSidInteractiveGrantOnly_DatabaseModifiedTrue()
    {
        // Container already has access (aceAdded = false); interactive user needs and gets an ACE.
        // GrantAdded must be false (no ACE added for the container SID itself).
        // DatabaseModified must be true — interactive user grant was recorded in DB.
        var aclPermission = new Mock<IAclPermissionService>();
        var traverseService = new Mock<IUserTraverseService>();
        var interactiveResolver = new Mock<IInteractiveUserResolver>();
        interactiveResolver.Setup(r => r.GetInteractiveUserSid()).Returns(InteractiveSid);
        var db = new AppDatabase();
        var service = new PermissionGrantService(aclPermission.Object, traverseService.Object,
            new LambdaDatabaseProvider(() => db), _log.Object, interactiveResolver.Object, SyncInvoker);

        var path = @"C:\Apps\MyApp";
        aclPermission.Setup(a => a.EnsureRights(path, ContainerSid, It.IsAny<FileSystemRights>(),
            It.IsAny<ILoggingService>(), It.IsAny<Func<string, bool>?>())).Returns(false); // container already has access
        aclPermission.Setup(a => a.NeedsPermissionGrant(path, InteractiveSid, It.IsAny<FileSystemRights>()))
            .Returns(true);
        aclPermission.Setup(a => a.EnsureRights(path, InteractiveSid, It.IsAny<FileSystemRights>(),
            It.IsAny<ILoggingService>(), It.IsAny<Func<string, bool>?>())).Returns(true); // interactive user ACE added
        traverseService.Setup(t => t.EnsureTraverseAccess(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((false, new List<string>()));

        var result = service.EnsureAccess(path, ContainerSid, FileSystemRights.ReadAndExecute, confirm: null);

        Assert.False(result.GrantAdded); // no ACE added for the container SID itself
        Assert.True(result.DatabaseModified); // interactive user grant recorded in DB → DatabaseModified signals save needed
    }

    // --- Container auto-grant ---

    [Fact]
    public void EnsureAccess_ContainerSid_ContainerGrantApplied()
    {
        // Verifies the container SID grant path works.
        // Note: the interactive user auto-grant (S-1-15-2-* triggers EnsureAccess for interactiveSid)
        // is structurally gated by IInteractiveUserResolver which returns null by default in tests.
        var path = @"C:\Apps\MyApp";
        SetupEnsureRightsGrants(path, ContainerSid);
        _traverseService.Setup(t => t.EnsureTraverseAccess(It.IsAny<string>(), @"C:\Apps")).Returns((false, new List<string>()));

        var result = _service.EnsureAccess(path, ContainerSid, FileSystemRights.ReadAndExecute, confirm: null);

        Assert.True(result.GrantAdded);
        _aclPermission.Verify(a => a.EnsureRights(path, ContainerSid, FileSystemRights.ReadAndExecute,
            It.IsAny<ILoggingService>(), null), Times.Once);
    }

    [Fact]
    public void EnsureAccess_ContainerSid_AutoGrantsInteractiveUser()
    {
        // Arrange: inject a resolver that returns a known interactive SID
        var aclPermission = new Mock<IAclPermissionService>();
        var traverseService = new Mock<IUserTraverseService>();
        var interactiveResolver = new Mock<IInteractiveUserResolver>();
        interactiveResolver.Setup(r => r.GetInteractiveUserSid()).Returns(InteractiveSid);
        var db = new AppDatabase();
        var service = new PermissionGrantService(aclPermission.Object, traverseService.Object,
            new LambdaDatabaseProvider(() => db), _log.Object, interactiveResolver.Object, SyncInvoker);

        var path = @"C:\Apps\MyApp";
        aclPermission.Setup(a => a.EnsureRights(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<FileSystemRights>(), It.IsAny<ILoggingService>(), It.IsAny<Func<string, bool>?>()))
            .Returns(true);
        aclPermission.Setup(a => a.NeedsPermissionGrant(path, InteractiveSid, It.IsAny<FileSystemRights>()))
            .Returns(true); // interactive user does not yet have the rights
        traverseService.Setup(t => t.EnsureTraverseAccess(It.IsAny<string>(), It.IsAny<string>())).Returns((false, new List<string>()));

        // Act
        service.EnsureAccess(path, ContainerSid, FileSystemRights.ReadAndExecute, confirm: null);

        // Assert: EnsureRights called for container SID and for the interactive user SID
        aclPermission.Verify(a => a.EnsureRights(path, ContainerSid, FileSystemRights.ReadAndExecute,
            It.IsAny<ILoggingService>(), null), Times.Once);
        aclPermission.Verify(a => a.EnsureRights(path, InteractiveSid, FileSystemRights.ReadAndExecute,
            It.IsAny<ILoggingService>(), null), Times.Once);
    }

    [Fact]
    public void EnsureAccess_ContainerSid_InteractiveUserAlreadyHasRights_AutoGrantSkipped()
    {
        // Arrange
        var aclPermission = new Mock<IAclPermissionService>();
        var traverseService = new Mock<IUserTraverseService>();
        var interactiveResolver = new Mock<IInteractiveUserResolver>();
        interactiveResolver.Setup(r => r.GetInteractiveUserSid()).Returns(InteractiveSid);
        var db = new AppDatabase();
        var service = new PermissionGrantService(aclPermission.Object, traverseService.Object,
            new LambdaDatabaseProvider(() => db), _log.Object, interactiveResolver.Object, SyncInvoker);

        var path = @"C:\Apps\MyApp";
        aclPermission.Setup(a => a.EnsureRights(path, ContainerSid, It.IsAny<FileSystemRights>(),
            It.IsAny<ILoggingService>(), It.IsAny<Func<string, bool>?>())).Returns(true);
        aclPermission.Setup(a => a.NeedsPermissionGrant(path, InteractiveSid, It.IsAny<FileSystemRights>()))
            .Returns(false); // interactive user already has effective rights
        traverseService.Setup(t => t.EnsureTraverseAccess(It.IsAny<string>(), It.IsAny<string>())).Returns((false, new List<string>()));

        // Act
        service.EnsureAccess(path, ContainerSid, FileSystemRights.ReadAndExecute, confirm: null);

        // Assert: EnsureRights called only for container SID; interactive user auto-grant skipped
        aclPermission.Verify(a => a.EnsureRights(path, ContainerSid, It.IsAny<FileSystemRights>(),
            It.IsAny<ILoggingService>(), null), Times.Once);
        aclPermission.Verify(a => a.EnsureRights(It.IsAny<string>(), InteractiveSid, It.IsAny<FileSystemRights>(),
            It.IsAny<ILoggingService>(), It.IsAny<Func<string, bool>?>()), Times.Never);
    }

    [Fact]
    public void EnsureAccess_NonContainerSid_NoAutoGrantAttempt()
    {
        // Arrange
        var path = @"C:\Apps\MyApp";
        SetupEnsureRightsGrants(path, UserSid);
        _traverseService.Setup(t => t.EnsureTraverseAccess(UserSid, @"C:\Apps")).Returns((false, new List<string>()));

        _service.EnsureAccess(path, UserSid, FileSystemRights.ReadAndExecute, confirm: null);

        // EnsureRights called once (only for UserSid, no auto-grant for non-container SID)
        _aclPermission.Verify(a => a.EnsureRights(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<FileSystemRights>(), It.IsAny<ILoggingService>(), It.IsAny<Func<string, bool>?>()), Times.Once);
    }

    [Fact]
    public void EnsureAccess_ContainerSidUserDeclined_AutoGrantSkipped()
    {
        // Arrange
        var path = @"C:\Apps\MyApp";
        // Simulate decline: EnsureRights calls confirm which returns false → returns false
        _aclPermission.Setup(a => a.EnsureRights(path, ContainerSid, FileSystemRights.ReadAndExecute,
                It.IsAny<ILoggingService>(), It.IsAny<Func<string, bool>?>()))
            .Callback<string, string, FileSystemRights, ILoggingService, Func<string, bool>?>((p, _, _, _, confirm) => confirm!(p))
            .Returns(false);

        var result = _service.EnsureAccess(path, ContainerSid, FileSystemRights.ReadAndExecute, (_, _) => false);

        Assert.False(result.GrantAdded);
        // No traverse, no auto-grant
        _traverseService.Verify(t => t.EnsureTraverseAccess(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        // EnsureRights called exactly once (for container SID only; interactive auto-grant skipped)
        _aclPermission.Verify(a => a.EnsureRights(path, ContainerSid, FileSystemRights.ReadAndExecute,
            It.IsAny<ILoggingService>(), It.IsAny<Func<string, bool>?>()), Times.Once);
        _aclPermission.Verify(a => a.EnsureRights(It.IsAny<string>(), InteractiveSid, It.IsAny<FileSystemRights>(),
            It.IsAny<ILoggingService>(), It.IsAny<Func<string, bool>?>()), Times.Never);
    }

    // --- AdaptConfirm ---

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AdaptConfirm_CallbackReturnsBool_AdaptedReturnsSameBool(bool callbackResult)
    {
        // When the inner callback returns true/false, the adapted Func must pass it through.
        var adapted = PermissionGrantService.AdaptConfirm(_ => callbackResult);

        bool result = adapted(@"C:\Apps\MyApp", UserSid);

        Assert.Equal(callbackResult, result);
    }

    [Fact]
    public void AdaptConfirm_CallbackReturnsNull_AdaptedThrowsOperationCanceledException()
    {
        // When the inner callback returns null (user cancelled), the adapted Func must throw OCE.
        var adapted = PermissionGrantService.AdaptConfirm(_ => null);

        Assert.Throws<OperationCanceledException>(() => adapted(@"C:\Apps\MyApp", UserSid));
    }
}