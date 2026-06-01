using Moq;
using RunFence.Account;
using RunFence.Acl;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Groups;
using RunFence.Groups.UI;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public class GroupActionOrchestratorTests
{
    [Fact]
    public void DeleteGroup_WhenPromptRejects_DoesNotCallDeletionService()
    {
        var deletePrompt = new Mock<IGroupDeletePrompt>();
        deletePrompt.Setup(p => p.ConfirmDelete("Group")).Returns(false);

        var groupMembership = new Mock<ILocalGroupMutationService>(MockBehavior.Strict);
        var deletionService = CreateDeletionService(groupMembership, new AppDatabase());
        var orchestrator = CreateOrchestrator(deletePrompt.Object, deletionService);

        orchestrator.DeleteGroup("sid", "Group");

        groupMembership.Verify(s => s.DeleteGroup(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void DeleteGroup_WhenOsDeleteFails_ShowsDeleteFailure()
    {
        var deletePrompt = new Mock<IGroupDeletePrompt>();
        deletePrompt.Setup(p => p.ConfirmDelete("Group")).Returns(true);

        var groupMembership = new Mock<ILocalGroupMutationService>();
        groupMembership.Setup(s => s.DeleteGroup("sid")).Throws(new InvalidOperationException("delete failed"));

        var deletionService = CreateDeletionService(groupMembership, new AppDatabase());
        var orchestrator = CreateOrchestrator(deletePrompt.Object, deletionService);

        orchestrator.DeleteGroup("sid", "Group");

        deletePrompt.Verify(p => p.ShowDeleteFailed("Failed to delete group from OS: delete failed"), Times.Once);
    }

    [Fact]
    public void DeleteGroup_WhenSaveFails_RaisesDataChangedAndShowsSaveFailure()
    {
        var deletePrompt = new Mock<IGroupDeletePrompt>();
        deletePrompt.Setup(p => p.ConfirmDelete("Group")).Returns(true);

        var database = new AppDatabase();
        database.SidNames["sid"] = "SavedName";

        var groupMembership = new Mock<ILocalGroupMutationService>();
        groupMembership.Setup(s => s.DeleteGroup("sid"));

        var sessionSaver = new Mock<ISessionSaver>();
        sessionSaver.Setup(s => s.SaveConfig()).Throws(new InvalidOperationException("save failed"));

        var deletionService = CreateDeletionService(groupMembership, database, sessionSaver: sessionSaver.Object);
        var orchestrator = CreateOrchestrator(deletePrompt.Object, deletionService);

        string? changedSid = "initial";
        orchestrator.DataChanged += sid => changedSid = sid;

        orchestrator.DeleteGroup("sid", "Group");

        Assert.Null(changedSid);
        deletePrompt.Verify(p => p.ShowSaveFailed("SavedName", "save failed"), Times.Once);
    }

    private static GroupActionOrchestrator CreateOrchestrator(
        IGroupDeletePrompt deletePrompt,
        GroupDeletionService deletionService,
        GroupBulkScanOrchestrator? bulkScanHandler = null)
    {
        return new GroupActionOrchestrator(
            Mock.Of<IModalCoordinator>(),
            Mock.Of<ILocalGroupMutationService>(),
            deletionService,
            bulkScanHandler,
            null,
            deletePrompt,
            Mock.Of<ISidNameCacheService>(),
            Mock.Of<ILoggingService>());
    }

    private static GroupDeletionService CreateDeletionService(
        Mock<ILocalGroupMutationService> groupMembership,
        AppDatabase database,
        IAclService? aclService = null,
        IGrantMutatorService? grantMutatorService = null,
        ITraverseService? traverseService = null,
        ISessionSaver? sessionSaver = null)
    {
        var sessionProvider = new Mock<ISessionProvider>();
        sessionProvider.Setup(s => s.GetSession()).Returns(new SessionContext
        {
            Database = database,
            CredentialStore = new CredentialStore(),
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.FromBytes([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16])));

        var resolvedGrantMutatorService = grantMutatorService;
        var resolvedTraverseService = traverseService;
        if (resolvedGrantMutatorService == null || resolvedTraverseService == null)
        {
            var grantMutatorMock = new Mock<IGrantMutatorService>();
            grantMutatorMock
                .Setup(s => s.RemoveGrant(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                .Returns(new GrantApplyResult(DatabaseModified: true, DurableSaveCompleted: true));
            var traverseMock = new Mock<ITraverseService>();
            traverseMock
                .Setup(s => s.RemoveTraverse(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(new GrantApplyResult(DatabaseModified: true, DurableSaveCompleted: true));
            resolvedGrantMutatorService ??= grantMutatorMock.Object;
            resolvedTraverseService ??= traverseMock.Object;
        }

        return new GroupDeletionService(
            groupMembership.Object,
            sessionProvider.Object,
            aclService ?? Mock.Of<IAclService>(),
            resolvedGrantMutatorService,
            resolvedTraverseService,
            sessionSaver,
            Mock.Of<ILoggingService>());
    }
}
