using Moq;
using RunFence.Core.Models;
using RunFence.Core;
using RunFence.Account;
using RunFence.Groups.UI;
using RunFence.Infrastructure;
using RunFence.Tests.Helpers;
using RunFence.UI.Controls;
using Xunit;

namespace RunFence.Tests;

public class GroupGridPopulatorTests
{
    [Fact]
    public void PopulateMembers_WhenGetMembersOfGroupThrows_ClearsRowsAndPropagates()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            var groupSid = "S-1-5-21-1234";
            var localGroupQuery = new Mock<ILocalGroupQueryService>();
            localGroupQuery.Setup(s => s.GetMembersOfGroup(groupSid))
                .Throws(new InvalidOperationException("membership failed"));

            using var groupsGrid = new StyledDataGridView();
            groupsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "GroupName" });
            groupsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "SID" });

            using var membersGrid = new StyledDataGridView();
            membersGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "MemberName" });
            membersGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "MemberSID" });
            membersGrid.Rows.Add("stale-member", "S-1-5-21-99");

            var headerLabel = new Label { Text = "Members:" };

            var populator = new GroupGridPopulator(
                localGroupQuery.Object,
                Mock.Of<ILoggingService>(),
                Mock.Of<ISidNameCacheService>());
            populator.Initialize(groupsGrid, membersGrid, headerLabel);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await populator.PopulateMembers(groupSid));

            Assert.Equal("membership failed", exception.Message);
            Assert.Empty(membersGrid.Rows);
            Assert.Equal($"Members of {groupSid}:", headerLabel.Text);
        });
    }
}
