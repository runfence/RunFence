using Moq;
using RunFence.Account.Lifecycle;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launch.Container;
using RunFence.Persistence;
using RunFence.SidMigration;
using Xunit;

namespace RunFence.Tests;

public class ContainerDeletionServiceTests
{
    private readonly Mock<IAppContainerService> _containerService = new();
    private readonly Mock<ISidCleanupHelper> _sidCleanup = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<IAppContainerEnvironmentSetup> _environmentSetup = new();

    public ContainerDeletionServiceTests()
    {
        _containerService.Setup(s => s.RevertTraverseAccess(It.IsAny<AppContainerEntry>(), It.IsAny<AppDatabase>()));
        _containerService.Setup(s => s.DeleteProfile(It.IsAny<string>(), It.IsAny<bool>()));
        _sidCleanup.Setup(s => s.CleanupContainerFromAppData(
            It.IsAny<string>(), It.IsAny<string?>()));
    }

    private ContainerDeletionService CreateService(AppDatabase database)
        => new(_containerService.Object, _sidCleanup.Object,
            _log.Object, _environmentSetup.Object,
            new LambdaDatabaseProvider(() => database));

    private static AppContainerEntry MakeEntry() => new() { Name = "rfn_test" };

    // ── Grant cleanup ───────────────────────────────────────────────────────

    [Fact]
    public void DeleteContainer_CallsRevertTraverseAccess()
    {
        // RevertTraverseAccess delegates to pathGrantService.RemoveAll internally via AppContainerService.
        var db = new AppDatabase();

        CreateService(db).DeleteContainer(MakeEntry(), "S-1-15-2-99-1-2-3-4-5-6");

        _containerService.Verify(s => s.RevertTraverseAccess(It.IsAny<AppContainerEntry>(), db), Times.Once);
    }

    [Fact]
    public void DeleteContainer_NullContainerSid_SkipsVirtualStoreRevoke()
    {
        var db = new AppDatabase();

        CreateService(db).DeleteContainer(MakeEntry(), containerSid: null);

        _environmentSetup.Verify(s => s.TryRevokeVirtualStoreAccess(
            It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void DeleteContainer_WithContainerSid_RevokesVirtualStoreAccess()
    {
        const string containerSid = "S-1-15-2-99-1-2-3-4-5-6";
        var db = new AppDatabase();

        CreateService(db).DeleteContainer(MakeEntry(), containerSid);

        _environmentSetup.Verify(s => s.TryRevokeVirtualStoreAccess(containerSid, It.IsAny<string>()), Times.Once);
    }

    // ── Pipeline ordering ──────────────────────────────────────────────────

    [Fact]
    public void DeleteContainer_PipelineOrdering_RevertTraverseThenDeleteProfileThenCleanup()
    {
        var db = new AppDatabase();
        var callOrder = new List<string>();

        _containerService.Setup(s => s.RevertTraverseAccess(It.IsAny<AppContainerEntry>(), It.IsAny<AppDatabase>()))
            .Callback(() => callOrder.Add("RevertTraverseAccess"));
        _containerService.Setup(s => s.DeleteProfile(It.IsAny<string>(), It.IsAny<bool>()))
            .Callback(() => callOrder.Add("DeleteProfile"));
        _sidCleanup.Setup(s => s.CleanupContainerFromAppData(
                It.IsAny<string>(), It.IsAny<string?>()))
            .Callback(() => callOrder.Add("CleanupContainerFromAppData"));

        CreateService(db).DeleteContainer(MakeEntry(), "S-1-15-2-99-1-2-3-4-5-6");

        Assert.Equal(3, callOrder.Count);
        Assert.Equal("RevertTraverseAccess", callOrder[0]);
        Assert.Equal("DeleteProfile", callOrder[1]);
        Assert.Equal("CleanupContainerFromAppData", callOrder[2]);
    }

    [Fact]
    public void DeleteContainer_RevertTraverseThrows_LogsWarningAndContinues()
    {
        var db = new AppDatabase();
        _containerService.Setup(s => s.RevertTraverseAccess(It.IsAny<AppContainerEntry>(), It.IsAny<AppDatabase>()))
            .Throws(new InvalidOperationException("ACL revert failed"));

        var result = CreateService(db).DeleteContainer(MakeEntry(), "S-1-15-2-99-1-2-3-4-5-6");

        _log.Verify(l => l.Warn(It.Is<string>(s => s.Contains("rfn_test"))), Times.Once);
        _sidCleanup.Verify(s => s.CleanupContainerFromAppData(It.IsAny<string>(), It.IsAny<string?>()), Times.Once);
        Assert.True(result);
    }

    [Fact]
    public void DeleteContainer_DeleteProfileFails_ReturnsFalse()
    {
        var db = new AppDatabase();
        _containerService.Setup(s => s.DeleteProfile(It.IsAny<string>(), It.IsAny<bool>()))
            .Throws(new InvalidOperationException("Profile deletion failed"));

        var result = CreateService(db).DeleteContainer(MakeEntry(), "S-1-15-2-99-1-2-3-4-5-6");

        Assert.False(result);
        _sidCleanup.Verify(s => s.CleanupContainerFromAppData(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }
}
