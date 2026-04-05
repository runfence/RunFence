using Moq;
using RunFence.Account.Lifecycle;
using RunFence.Acl;
using RunFence.Acl.Permissions;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launch.Container;
using RunFence.Persistence;
using RunFence.SidMigration;
using Xunit;

namespace RunFence.Tests;

public class ContainerDeletionServiceTests
{
    private const string ContainerSid = "S-1-15-2-99-1-2-3-4-5-6";
    private const string InteractiveSid = "S-1-5-21-1234567890-1234567890-1234567890-1002";

    private readonly Mock<IAppContainerService> _containerService = new();
    private readonly Mock<IGrantedPathAclService> _grantedPathAcl = new();
    private readonly Mock<ISidCleanupHelper> _sidCleanup = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<IAppContainerEnvironmentSetup> _environmentSetup = new();
    private readonly Mock<IInteractiveUserResolver> _iuResolver = new();
    private AppDatabase _testDatabase = new();

    public ContainerDeletionServiceTests()
    {
        _iuResolver.Setup(r => r.GetInteractiveUserSid()).Returns(InteractiveSid);
        _containerService.Setup(s => s.RevertTraverseAccess(It.IsAny<AppContainerEntry>(), It.IsAny<AppDatabase>()));
        _containerService.Setup(s => s.DeleteProfile(It.IsAny<string>(), It.IsAny<bool>()));
        _sidCleanup.Setup(s => s.CleanupContainerFromAppData(
            It.IsAny<string>(), It.IsAny<string?>()));
    }

    private ContainerDeletionService CreateService()
        => new(_containerService.Object, _grantedPathAcl.Object, _sidCleanup.Object,
            _log.Object, _environmentSetup.Object, _iuResolver.Object,
            new LambdaDatabaseProvider(() => _testDatabase));

    private AppDatabase MakeDatabase(
        List<GrantedPathEntry> containerGrants,
        List<GrantedPathEntry> iuGrants)
    {
        var db = new AppDatabase();
        db.GetOrCreateAccount(ContainerSid).Grants.AddRange(containerGrants);
        db.GetOrCreateAccount(InteractiveSid).Grants.AddRange(iuGrants);
        return db;
    }

    private static AppContainerEntry MakeEntry() => new() { Name = "rfn_test" };

    // ── RevertInteractiveUserGrants ────────────────────────────────────────

    [Fact]
    public void DeleteContainer_IuGrantMatchesContainerGrant_IuGrantReverted()
    {
        // Arrange
        var rights = new SavedRightsState(Execute: false, Write: false, Read: true, Special: false, Own: false);
        var containerGrants = new List<GrantedPathEntry>
        {
            new() { Path = @"C:\Apps\MyApp", SavedRights = rights }
        };
        var iuGrants = new List<GrantedPathEntry>
        {
            new() { Path = @"C:\Apps\MyApp", SavedRights = rights }
        };
        var db = MakeDatabase(containerGrants, iuGrants);

        // Act
        _testDatabase = db;
        CreateService().DeleteContainer(MakeEntry(), ContainerSid);

        // Assert: IU grant reverted and removed from DB
        _grantedPathAcl.Verify(a => a.RevertAllGrantsBatch(
            It.Is<IEnumerable<GrantedPathEntry>>(g => g.Any(e => e.Path == @"C:\Apps\MyApp")),
            InteractiveSid), Times.Once);
        Assert.False(db.GetAccount(InteractiveSid)?.Grants.Any() ?? false);
    }

    [Fact]
    public void DeleteContainer_IuGrantHasDifferentSavedRights_IuGrantNotReverted()
    {
        // Arrange: container has Read-only, IU has Read+Execute (from another source)
        var containerRights = new SavedRightsState(Execute: false, Write: false, Read: true, Special: false, Own: false);
        var iuRights = new SavedRightsState(Execute: true, Write: false, Read: true, Special: false, Own: false);
        var containerGrants = new List<GrantedPathEntry>
        {
            new() { Path = @"C:\Apps\MyApp", SavedRights = containerRights }
        };
        var iuGrants = new List<GrantedPathEntry>
        {
            new() { Path = @"C:\Apps\MyApp", SavedRights = iuRights }
        };
        var db = MakeDatabase(containerGrants, iuGrants);

        // Act
        _testDatabase = db;
        CreateService().DeleteContainer(MakeEntry(), ContainerSid);

        // Assert: IU grant not reverted (rights differ)
        _grantedPathAcl.Verify(a => a.RevertAllGrantsBatch(
            It.IsAny<IEnumerable<GrantedPathEntry>>(), InteractiveSid), Times.Never);
        Assert.NotEmpty(db.GetAccount(InteractiveSid)?.Grants ?? new List<GrantedPathEntry>());
        Assert.Single(db.GetAccount(InteractiveSid)!.Grants);
    }

    [Fact]
    public void DeleteContainer_BothGrantsHaveNullSavedRights_IuGrantReverted()
    {
        // Arrange: legacy entries with null SavedRights (null == null → equal → revert)
        var containerGrants = new List<GrantedPathEntry>
        {
            new() { Path = @"C:\Apps\MyApp", SavedRights = null }
        };
        var iuGrants = new List<GrantedPathEntry>
        {
            new() { Path = @"C:\Apps\MyApp", SavedRights = null }
        };
        var db = MakeDatabase(containerGrants, iuGrants);

        // Act
        _testDatabase = db;
        CreateService().DeleteContainer(MakeEntry(), ContainerSid);

        // Assert: IU grant reverted
        _grantedPathAcl.Verify(a => a.RevertAllGrantsBatch(
            It.Is<IEnumerable<GrantedPathEntry>>(g => g.Any(e => e.Path == @"C:\Apps\MyApp")),
            InteractiveSid), Times.Once);
        Assert.False(db.GetAccount(InteractiveSid)?.Grants.Any() ?? false);
    }

    [Fact]
    public void DeleteContainer_IuGrantPathNotInContainerGrants_IuGrantNotReverted()
    {
        // Arrange: IU has grant for a path the container never had
        var rights = new SavedRightsState(Execute: false, Write: false, Read: true, Special: false, Own: false);
        var containerGrants = new List<GrantedPathEntry>
        {
            new() { Path = @"C:\Apps\MyApp", SavedRights = rights }
        };
        var iuGrants = new List<GrantedPathEntry>
        {
            new() { Path = @"C:\Other\Path", SavedRights = rights }
        };
        var db = MakeDatabase(containerGrants, iuGrants);

        // Act
        _testDatabase = db;
        CreateService().DeleteContainer(MakeEntry(), ContainerSid);

        // Assert: unrelated IU grant preserved
        _grantedPathAcl.Verify(a => a.RevertAllGrantsBatch(
            It.IsAny<IEnumerable<GrantedPathEntry>>(), InteractiveSid), Times.Never);
        Assert.NotEmpty(db.GetAccount(InteractiveSid)?.Grants ?? new List<GrantedPathEntry>());
    }

    [Fact]
    public void DeleteContainer_OtherContainerSharesPath_IuGrantNotReverted()
    {
        // Arrange: another container also has a grant for the same path with same rights.
        // The IU grant must not be removed because the other container still needs it.
        const string otherContainerSid = "S-1-15-2-88-1-2-3-4-5-6";
        const string otherContainerName = "rfn_other";

        var rights = new SavedRightsState(Execute: false, Write: false, Read: true, Special: false, Own: false);

        var db = MakeDatabase(
            containerGrants: [new() { Path = @"C:\Apps\Shared", SavedRights = rights }],
            iuGrants: [new() { Path = @"C:\Apps\Shared", SavedRights = rights }]);

        // Add both the deleted and the other container to AppContainers (realistic state before cleanup).
        db.AppContainers.Add(new AppContainerEntry { Name = "rfn_test" });
        db.AppContainers.Add(new AppContainerEntry { Name = otherContainerName });
        db.GetOrCreateAccount(otherContainerSid).Grants.Add(
            new GrantedPathEntry { Path = @"C:\Apps\Shared", SavedRights = rights });

        _containerService.Setup(s => s.GetSid(otherContainerName)).Returns(otherContainerSid);

        // Act
        _testDatabase = db;
        CreateService().DeleteContainer(MakeEntry(), ContainerSid);

        // Assert: IU grant preserved because another container still uses the path
        _grantedPathAcl.Verify(a => a.RevertAllGrantsBatch(
            It.IsAny<IEnumerable<GrantedPathEntry>>(), InteractiveSid), Times.Never);
        Assert.NotEmpty(db.GetAccount(InteractiveSid)?.Grants ?? []);
    }

    // ── Pipeline ordering ──────────────────────────────────────────────────

    [Fact]
    public void DeleteContainer_PipelineOrdering_RevertTraverseThenDeleteProfileThenCleanup()
    {
        // Arrange: no grants — only the core pipeline calls matter here.
        var db = MakeDatabase([], []);
        var callOrder = new List<string>();

        _containerService.Setup(s => s.RevertTraverseAccess(It.IsAny<AppContainerEntry>(), It.IsAny<AppDatabase>()))
            .Callback(() => callOrder.Add("RevertTraverseAccess"));
        _containerService.Setup(s => s.DeleteProfile(It.IsAny<string>(), It.IsAny<bool>()))
            .Callback(() => callOrder.Add("DeleteProfile"));
        _sidCleanup.Setup(s => s.CleanupContainerFromAppData(
                It.IsAny<string>(), It.IsAny<string?>()))
            .Callback(() => callOrder.Add("CleanupContainerFromAppData"));

        // Act
        _testDatabase = db;
        CreateService().DeleteContainer(MakeEntry(), ContainerSid);

        // Assert: each step called exactly once, in the required order
        Assert.Equal(3, callOrder.Count);
        Assert.Equal("RevertTraverseAccess", callOrder[0]);
        Assert.Equal("DeleteProfile", callOrder[1]);
        Assert.Equal("CleanupContainerFromAppData", callOrder[2]);
    }

    [Fact]
    public void DeleteContainer_MixedMatches_OnlyMatchingIuGrantsReverted()
    {
        // Arrange: two paths; one matches (same rights), one doesn't (different rights)
        var matchingRights = new SavedRightsState(Execute: false, Write: false, Read: true, Special: false, Own: false);
        var differentRights = new SavedRightsState(Execute: true, Write: false, Read: true, Special: false, Own: false);
        var containerGrants = new List<GrantedPathEntry>
        {
            new() { Path = @"C:\Apps\AppA", SavedRights = matchingRights },
            new() { Path = @"C:\Apps\AppB", SavedRights = matchingRights },
        };
        var iuGrants = new List<GrantedPathEntry>
        {
            new() { Path = @"C:\Apps\AppA", SavedRights = matchingRights }, // matches → revert
            new() { Path = @"C:\Apps\AppB", SavedRights = differentRights }, // differs → keep
        };
        var db = MakeDatabase(containerGrants, iuGrants);

        // Act
        _testDatabase = db;
        CreateService().DeleteContainer(MakeEntry(), ContainerSid);

        // Assert: only AppA reverted; AppB preserved
        _grantedPathAcl.Verify(a => a.RevertAllGrantsBatch(
            It.Is<IEnumerable<GrantedPathEntry>>(g =>
                g.Count() == 1 && g.Single().Path == @"C:\Apps\AppA"),
            InteractiveSid), Times.Once);
        Assert.NotEmpty(db.GetAccount(InteractiveSid)?.Grants ?? new List<GrantedPathEntry>());
        Assert.Single(db.GetAccount(InteractiveSid)!.Grants);
        Assert.Equal(@"C:\Apps\AppB", db.GetAccount(InteractiveSid)!.Grants[0].Path);
    }
}