using Moq;
using RunFence.Account;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Groups.UI;
using RunFence.Infrastructure;
using RunFence.Tests.Helpers;
using RunFence.UI.Controls;
using Xunit;

namespace RunFence.Tests;

public class GroupRefreshControllerTests
{
    [Fact]
    public async Task RefreshNow_WhenNotInitialized_SetsRetryStateAndClearsRefreshing()
    {
        var populator = new GroupGridPopulator(
            Mock.Of<ILocalGroupQueryService>(),
            Mock.Of<ILoggingService>(),
            Mock.Of<ISidNameCacheService>());
        var controller = new GroupRefreshController(populator, Mock.Of<ILoggingService>());

        await controller.RefreshNow();

        Assert.False(controller.IsRefreshing);
        Assert.NotNull(controller.RetryState);
        Assert.Equal(GroupRefreshRetryOperation.Refresh, controller.RetryState!.Operation);
    }

    [Fact]
    public void RefreshNow_IncludesSelectionBeforeAndAfter_AndMarksMembersRefreshed()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            var groupQuery = new Mock<ILocalGroupQueryService>();
            groupQuery.Setup(g => g.GetLocalGroups()).Returns([
                new LocalUserAccount("Admins", "S-1-5-32-544"),
                new LocalUserAccount("Users", "S-1-5-32-545")
            ]);
            groupQuery.Setup(g => g.GetMembersOfGroup(It.IsAny<string>())).Returns([]);

            using var groupsGrid = new StyledDataGridView();
            groupsGrid.Columns.Add("GroupName", "GroupName");
            groupsGrid.Columns.Add("SID", "SID");
            using var membersGrid = new StyledDataGridView();
            membersGrid.Columns.Add("AccountName", "AccountName");
            membersGrid.Columns.Add("SID", "SID");
            using var label = new Label();

            var populator = new GroupGridPopulator(groupQuery.Object, Mock.Of<ILoggingService>(), Mock.Of<ISidNameCacheService>());
            populator.Initialize(groupsGrid, membersGrid, label);

            var controller = new GroupRefreshController(populator, Mock.Of<ILoggingService>());
            controller.Initialize(() => groupsGrid.SelectedRows.Count > 0 ? groupsGrid.SelectedRows[0].Tag as string : null);

            GroupRefreshCompletedInfo? completion = null;
            controller.RefreshCompleted += info =>
            {
                completion = info;
                return Task.CompletedTask;
            };

            await controller.RefreshNow();

            Assert.NotNull(completion);
            Assert.Null(completion!.SelectedSidBeforeRefresh);
            Assert.Equal(groupsGrid.SelectedRows[0].Tag as string, completion.SelectedSidAfterRefresh);
            Assert.True(completion.MembersWereRefreshed);
        });
    }

    [Fact]
    public void RefreshNow_WhenSelectionChanges_ReportsOldAndNewSids()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            var groupQuery = new Mock<ILocalGroupQueryService>();
            groupQuery.Setup(g => g.GetLocalGroups()).Returns([
                new LocalUserAccount("Admins", "S-1-5-32-544"),
                new LocalUserAccount("Users", "S-1-5-32-545")
            ]);
            groupQuery.Setup(g => g.GetMembersOfGroup(It.IsAny<string>())).Returns([]);

            using var groupsGrid = new StyledDataGridView();
            groupsGrid.Columns.Add("GroupName", "GroupName");
            groupsGrid.Columns.Add("SID", "SID");
            using var membersGrid = new StyledDataGridView();
            membersGrid.Columns.Add("AccountName", "AccountName");
            membersGrid.Columns.Add("SID", "SID");
            using var label = new Label();

            var initialRow = new DataGridViewRow();
            initialRow.CreateCells(groupsGrid, "Old", "S-1-5-32-545");
            initialRow.Tag = "S-1-5-32-545";
            groupsGrid.Rows.Add(initialRow);
            groupsGrid.Rows[0].Selected = true;
            groupsGrid.CurrentCell = groupsGrid.Rows[0].Cells[0];

            var populator = new GroupGridPopulator(groupQuery.Object, Mock.Of<ILoggingService>(), Mock.Of<ISidNameCacheService>());
            populator.Initialize(groupsGrid, membersGrid, label);

            var controller = new GroupRefreshController(populator, Mock.Of<ILoggingService>());
            controller.Initialize(() => groupsGrid.SelectedRows.Count > 0 ? groupsGrid.SelectedRows[0].Tag as string : null);
            populator.SetPreferredSelection("S-1-5-32-544");

            GroupRefreshCompletedInfo? completion = null;
            controller.RefreshCompleted += info =>
            {
                completion = info;
                return Task.CompletedTask;
            };

            await controller.RefreshNow();

            Assert.NotNull(completion);
            Assert.Equal("S-1-5-32-545", completion!.SelectedSidBeforeRefresh);
            Assert.Equal("S-1-5-32-544", completion.SelectedSidAfterRefresh);
        });
    }
}
