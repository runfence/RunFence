using Moq;
using RunFence.Account.Lifecycle;
using RunFence.Acl;
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
        _containerService.Setup(s => s.RevertTraverseAccess(It.IsAny<AppContainerEntry>(), It.IsAny<AppDatabase>()))
            .Returns(new GrantApplyResult(DurableSaveCompleted: true));
        _containerService.Setup(s => s.DeleteProfile(It.IsAny<string>(), It.IsAny<bool>())).Returns(Task.CompletedTask);
        _sidCleanup.Setup(s => s.CleanupContainerFromAppData(
            It.IsAny<string>(), It.IsAny<string?>()));
    }

    private ContainerDeletionService CreateService(AppDatabase database)
        => new(_containerService.Object, _sidCleanup.Object,
            _log.Object, _environmentSetup.Object,
            new LambdaDatabaseProvider(() => database));

    private static AppContainerEntry MakeEntry() => new() { Name = "rfn_test" };

    [Fact]
    public async Task DeleteContainer_CallsRevertTraverseAccess()
    {
        var db = new AppDatabase();

        var result = await CreateService(db).DeleteContainer(MakeEntry(), "S-1-15-2-99-1-2-3-4-5-6");

        _containerService.Verify(s => s.RevertTraverseAccess(It.IsAny<AppContainerEntry>(), db), Times.Once);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task DeleteContainer_PassesSameEntryInstanceToRevertTraverseAccess()
    {
        var db = new AppDatabase();
        var entry = new AppContainerEntry
        {
            Name = "rfn_test",
            Sid = "S-1-15-2-99-1-2-3-4-5-6"
        };
        AppContainerEntry? captured = null;
        _containerService.Setup(s => s.RevertTraverseAccess(It.IsAny<AppContainerEntry>(), It.IsAny<AppDatabase>()))
            .Callback<AppContainerEntry, AppDatabase>((e, _) => captured = e)
            .Returns(new GrantApplyResult(DurableSaveCompleted: true));

        var result = await CreateService(db).DeleteContainer(entry, entry.Sid);

        Assert.Same(entry, captured);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task DeleteContainer_NullContainerSid_SkipsVirtualStoreRevoke()
    {
        var db = new AppDatabase();

        var result = await CreateService(db).DeleteContainer(MakeEntry(), containerSid: null);

        _environmentSetup.Verify(s => s.TryRevokeVirtualStoreAccess(
            It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task DeleteContainer_WithContainerSid_RevokesVirtualStoreAccess()
    {
        const string containerSid = "S-1-15-2-99-1-2-3-4-5-6";
        var db = new AppDatabase();

        var result = await CreateService(db).DeleteContainer(MakeEntry(), containerSid);

        _environmentSetup.Verify(s => s.TryRevokeVirtualStoreAccess(containerSid, It.IsAny<string>()), Times.Once);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task DeleteContainer_PipelineOrdering_DeleteProfileThenRevertTraverseThenCleanup()
    {
        var db = new AppDatabase();
        var callOrder = new List<string>();

        _containerService.Setup(s => s.RevertTraverseAccess(It.IsAny<AppContainerEntry>(), It.IsAny<AppDatabase>()))
            .Callback(() => callOrder.Add("RevertTraverseAccess"))
            .Returns(new GrantApplyResult(DurableSaveCompleted: true));
        _containerService.Setup(s => s.DeleteProfile(It.IsAny<string>(), It.IsAny<bool>()))
            .Callback(() => callOrder.Add("DeleteProfile"))
            .Returns(Task.CompletedTask);
        _sidCleanup.Setup(s => s.CleanupContainerFromAppData(
                It.IsAny<string>(), It.IsAny<string?>()))
            .Callback(() => callOrder.Add("CleanupContainerFromAppData"));

        var result = await CreateService(db).DeleteContainer(MakeEntry(), "S-1-15-2-99-1-2-3-4-5-6");

        Assert.Equal(3, callOrder.Count);
        Assert.Equal("DeleteProfile", callOrder[0]);
        Assert.Equal("RevertTraverseAccess", callOrder[1]);
        Assert.Equal("CleanupContainerFromAppData", callOrder[2]);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task DeleteContainer_RevertTraverseThrows_ReturnsFailedResult()
    {
        var db = new AppDatabase();
        _containerService.Setup(s => s.RevertTraverseAccess(It.IsAny<AppContainerEntry>(), It.IsAny<AppDatabase>()))
            .Throws(new InvalidOperationException("ACL revert failed"));

        var result = await CreateService(db).DeleteContainer(MakeEntry(), "S-1-15-2-99-1-2-3-4-5-6");

        _log.Verify(l => l.Warn(It.Is<string>(s => s.Contains("rfn_test"))), Times.Once);
        _sidCleanup.Verify(s => s.CleanupContainerFromAppData(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
        Assert.False(result.Succeeded);
        Assert.Equal("ACL revert failed", result.ErrorMessage);
    }

    [Fact]
    public async Task DeleteContainer_DeleteProfileFails_ReturnsFailedResult()
    {
        var db = new AppDatabase();
        _containerService.Setup(s => s.DeleteProfile(It.IsAny<string>(), It.IsAny<bool>()))
            .ThrowsAsync(new InvalidOperationException("Profile deletion failed"));

        var result = await CreateService(db).DeleteContainer(MakeEntry(), "S-1-15-2-99-1-2-3-4-5-6");

        Assert.False(result.Succeeded);
        Assert.Equal("Profile deletion failed", result.ErrorMessage);
        _containerService.Verify(s => s.RevertTraverseAccess(It.IsAny<AppContainerEntry>(), It.IsAny<AppDatabase>()), Times.Never);
        _environmentSetup.Verify(s => s.TryRevokeVirtualStoreAccess(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _sidCleanup.Verify(s => s.CleanupContainerFromAppData(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task DeleteContainer_RevertTraverseReturnsWarnings_ReturnsFormattedWarnings()
    {
        var db = new AppDatabase();
        var warning = new GrantApplyWarning(
            GrantApplyFailureStep.PostRemoveAllSave,
            @"C:\ContainerRoot",
            null,
            new InvalidOperationException("save failed"));
        _containerService.Setup(s => s.RevertTraverseAccess(It.IsAny<AppContainerEntry>(), It.IsAny<AppDatabase>()))
            .Returns(new GrantApplyResult(
                DatabaseModified: true,
                DurableSaveCompleted: false,
                Warnings: [warning]));

        var result = await CreateService(db).DeleteContainer(MakeEntry(), "S-1-15-2-99-1-2-3-4-5-6");

        Assert.True(result.Succeeded);
        Assert.Equal([GrantApplyFailureFormatter.Format(warning)], result.Warnings);
        _sidCleanup.Verify(s => s.CleanupContainerFromAppData(It.IsAny<string>(), It.IsAny<string?>()), Times.Once);
    }
}
