using Moq;
using RunFence.Acl;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Groups;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public class GroupDeletionServiceTests
{
    private const string GroupSid = "S-1-5-21-100-200-300-4444";

    [Fact]
    public void DeleteGroup_OsDeleteFails_LeavesDatabaseUnchanged()
    {
        var db = new AppDatabase();
        db.Apps.Add(new AppEntry { Id = "a1", RestrictAcl = true, AllowedAclEntries = [new AllowAclEntry { Sid = GroupSid }] });
        db.SidNames[GroupSid] = "OldGroup";

        var groupMembership = new Mock<ILocalGroupMutationService>();
        groupMembership.Setup(s => s.DeleteGroup(GroupSid)).Throws(new InvalidOperationException("delete failed"));
        var service = CreateService(db, groupMembership: groupMembership.Object);

        var result = service.DeleteGroup(GroupSid);

        Assert.Equal(GroupDeletionStatus.OsDeleteFailed, result.Status);
        Assert.Equal("OldGroup", result.GroupName);
        Assert.Single(db.Apps[0].AllowedAclEntries!);
        Assert.Equal("OldGroup", db.SidNames[GroupSid]);
    }

    [Fact]
    public void DeleteGroup_Success_RemovesAllowedAclEntriesAndCleansSnapshot()
    {
        var db = new AppDatabase
        {
            AccountGroupSnapshots = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["S-1-5-21-100-200-300-1001"] = [GroupSid]
            }
        };
        db.Apps.Add(new AppEntry { Id = "a1", RestrictAcl = true, AllowedAclEntries = [new AllowAclEntry { Sid = GroupSid }] });
        db.GetOrCreateAccount(GroupSid).Grants.Add(new GrantedPathEntry { Path = @"C:\x", IsTraverseOnly = true });
        db.SidNames[GroupSid] = "Group";

        var service = CreateService(db);
        var result = service.DeleteGroup(GroupSid);

        Assert.Equal(GroupDeletionStatus.Succeeded, result.Status);
        Assert.Equal("Group", result.GroupName);
        Assert.True(result.DataChangedRaised);
        Assert.Empty(db.Apps[0].AllowedAclEntries!);
        Assert.Empty(db.AccountGroupSnapshots!["S-1-5-21-100-200-300-1001"]);
        Assert.Null(db.GetAccount(GroupSid));
        Assert.False(db.SidNames.ContainsKey(GroupSid));
    }

    [Fact]
    public void DeleteGroup_PostOsCleanupFailure_SavesCleanedDatabaseAndLogsWarning()
    {
        var db = new AppDatabase();
        db.Apps.Add(new AppEntry { Id = "a1", RestrictAcl = true, AllowedAclEntries = [new AllowAclEntry { Sid = GroupSid }] });
        db.GetOrCreateAccount(GroupSid).Grants.Add(new GrantedPathEntry { Path = @"C:\grant", IsDeny = false });
        db.SidNames[GroupSid] = "Group";

        var aclService = new Mock<IAclService>();
        aclService
            .Setup(s => s.RevertAcl(It.IsAny<AppEntry>(), It.IsAny<IReadOnlyList<AppEntry>>()))
            .Throws(new InvalidOperationException("acl cleanup failed"));

        var sessionSaver = new Mock<ISessionSaver>();
        var service = CreateService(
            db,
            aclService: aclService.Object,
            sessionSaver: sessionSaver.Object);

        var result = service.DeleteGroup(GroupSid);

        Assert.Equal(GroupDeletionStatus.Succeeded, result.Status);
        Assert.True(result.DataChangedRaised);
        sessionSaver.Verify(s => s.SaveConfig(), Times.Once);
        Assert.NotEmpty(result.Warnings);

        Assert.Empty(db.Apps[0].AllowedAclEntries!);
        Assert.Null(db.GetAccount(GroupSid));
        Assert.False(db.SidNames.ContainsKey(GroupSid));
    }

    [Fact]
    public void DeleteGroup_SaveConfigFails_ReturnsWindowsDeletedSaveFailed()
    {
        var db = new AppDatabase();
        db.Apps.Add(new AppEntry { Id = "a1", RestrictAcl = true, AllowedAclEntries = [new AllowAclEntry { Sid = GroupSid }] });
        db.SidNames[GroupSid] = "Group";

        var sessionSaver = new Mock<ISessionSaver>();
        sessionSaver.Setup(s => s.SaveConfig()).Throws(new InvalidOperationException("save failed"));
        var service = CreateService(db, sessionSaver: sessionSaver.Object);

        var result = service.DeleteGroup(GroupSid);

        Assert.Equal(GroupDeletionStatus.WindowsDeletedSaveFailed, result.Status);
        Assert.True(result.DataChangedRaised);
        Assert.NotEmpty(result.Errors);
        Assert.Equal("save failed", result.SaveErrorMessage);
        Assert.Empty(db.Apps[0].AllowedAclEntries!);
        Assert.False(db.SidNames.ContainsKey(GroupSid));
    }

    [Fact]
    public void DeleteGroup_RemoveTraverseReturnsFalse_DeletesGroupButLogsWarning()
    {
        var db = new AppDatabase();
        db.GetOrCreateAccount(GroupSid).Grants.Add(new GrantedPathEntry { Path = @"C:\x", IsTraverseOnly = true });
        db.SidNames[GroupSid] = "Group";

        var pathGrantService = new Mock<IPathGrantService>();
        pathGrantService
            .Setup(s => s.RemoveTraverse(GroupSid, @"C:\x"))
            .Returns(new GrantApplyResult());

        var service = CreateService(db, pathGrantService: pathGrantService.Object);

        var result = service.DeleteGroup(GroupSid);

        Assert.Equal(GroupDeletionStatus.Succeeded, result.Status);
        Assert.NotEmpty(result.Warnings);
        Assert.Contains("Failed to remove traverse grant 'C:\\x' for deleted group S-1-5-21-100-200-300-4444: Cleanup did not find traverse entry 'C:\\x' for SID 'S-1-5-21-100-200-300-4444'.", result.Warnings);
        Assert.Null(db.GetAccount(GroupSid));
        Assert.False(db.SidNames.ContainsKey(GroupSid));
    }

    [Fact]
    public void DeleteGroup_RemoveGrantReturnsWarnings_AddsWarningsToResult()
    {
        var db = new AppDatabase();
        db.GetOrCreateAccount(GroupSid).Grants.Add(new GrantedPathEntry { Path = @"C:\grant", IsDeny = false });
        db.SidNames[GroupSid] = "Group";

        var warning = new GrantApplyWarning(
            GrantApplyFailureStep.PostGrantRemoveSave,
            @"C:\grant",
            null,
            new InvalidOperationException("save failed"));

        var pathGrantService = new Mock<IPathGrantService>();
        pathGrantService
            .Setup(s => s.RemoveGrant(GroupSid, @"C:\grant", false))
            .Returns(new GrantApplyResult(
                DatabaseModified: true,
                DurableSaveCompleted: false,
                Warnings: [warning]));

        var service = CreateService(db, pathGrantService: pathGrantService.Object);

        var result = service.DeleteGroup(GroupSid);

        Assert.Equal(GroupDeletionStatus.Succeeded, result.Status);
        Assert.Contains(GrantApplyFailureFormatter.Format(warning), result.Warnings);
        Assert.NotEmpty(result.Warnings);
        Assert.Null(db.GetAccount(GroupSid));
        Assert.False(db.SidNames.ContainsKey(GroupSid));
    }

    [Fact]
    public void DeleteGroup_RemoveTraverseReturnsWarnings_DoesNotFailDeletion()
    {
        var db = new AppDatabase();
        db.GetOrCreateAccount(GroupSid).Grants.Add(new GrantedPathEntry { Path = @"C:\x", IsTraverseOnly = true });
        db.SidNames[GroupSid] = "Group";

        var warning = new GrantApplyWarning(
            GrantApplyFailureStep.PostTraverseRemoveSave,
            @"C:\x",
            null,
            new InvalidOperationException("save failed"));

        var pathGrantService = new Mock<IPathGrantService>();
        pathGrantService
            .Setup(s => s.RemoveTraverse(GroupSid, @"C:\x"))
            .Returns(new GrantApplyResult(
                DatabaseModified: true,
                DurableSaveCompleted: false,
                Warnings: [warning]));

        var service = CreateService(db, pathGrantService: pathGrantService.Object);

        var result = service.DeleteGroup(GroupSid);

        Assert.Equal(GroupDeletionStatus.Succeeded, result.Status);
        Assert.Contains(GrantApplyFailureFormatter.Format(warning), result.Warnings);
        Assert.NotEmpty(result.Warnings);
        Assert.Null(db.GetAccount(GroupSid));
        Assert.False(db.SidNames.ContainsKey(GroupSid));
    }

    private static GroupDeletionService CreateService(
        AppDatabase db,
        ILocalGroupMutationService? groupMembership = null,
        IAclService? aclService = null,
        IPathGrantService? pathGrantService = null,
        ISessionSaver? sessionSaver = null)
    {
        var grantService = pathGrantService;
        if (grantService == null)
        {
            var pathGrantMock = new Mock<IPathGrantService>();
            pathGrantMock
                .Setup(s => s.RemoveGrant(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                .Returns(new GrantApplyResult(DatabaseModified: true, DurableSaveCompleted: true));
            pathGrantMock
                .Setup(s => s.RemoveTraverse(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(new GrantApplyResult(DatabaseModified: true, DurableSaveCompleted: true));
            grantService = pathGrantMock.Object;
        }

        var sessionProvider = new Mock<ISessionProvider>();
        sessionProvider.Setup(s => s.GetSession()).Returns(new SessionContext
{
            Database = db,
            CredentialStore = new CredentialStore(),
        }.WithOwnedPinDerivedKey(TestSecretFactory.FromBytes([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16])));
        return new GroupDeletionService(
            groupMembership ?? Mock.Of<ILocalGroupMutationService>(),
            sessionProvider.Object,
            aclService ?? Mock.Of<IAclService>(),
            grantService,
            sessionSaver,
            Mock.Of<ILoggingService>());
    }
}
