using Moq;
using RunFence.Account;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Groups.UI;
using RunFence.Groups.UI.Forms;
using RunFence.Groups;
using RunFence.Infrastructure;
using RunFence.Tests.Helpers;
using RunFence.UI;
using System.Windows.Forms;
using Xunit;

namespace RunFence.Tests;

public class GroupsPanelMemberRefreshFailureTests
{
    private const string TestGroupSid = "S-1-5-32-544";

    [Fact]
    public void SortPathRefreshFailure_ClearsMembersAndResetsLoading()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            var groupQuery = new Mock<ILocalGroupQueryService>(MockBehavior.Strict);
            var groupMutation = new Mock<ILocalGroupMutationService>(MockBehavior.Strict);
            var gridPopulator = new Mock<IGroupGridPopulator>();
            var log = new Mock<ILoggingService>();

            groupQuery.Setup(s => s.GetGroupDescription(It.IsAny<string>())).Returns("test");
            groupQuery.Setup(s => s.GetMembersOfGroup(It.IsAny<string>())).Returns([]);

            var memberPicker = new Mock<IMemberPickerDialog>();
            var prompt = new Mock<IGroupMembershipPrompt>();

            var (host, panel, groupsGrid, membersGrid, membersHeader, membersToolStrip) = CreatePanel(
                groupQuery.Object,
                groupMutation.Object,
                gridPopulator.Object,
                log.Object,
                memberPicker.Object,
                prompt.Object);
            using (host)
            {
                var selectedRow = new DataGridViewRow();
                selectedRow.CreateCells(groupsGrid, "Test Group", TestGroupSid);
                selectedRow.Tag = TestGroupSid;
                groupsGrid.Rows.Add(selectedRow);
                selectedRow.Selected = true;
                groupsGrid.CurrentCell = selectedRow.Cells[0];

                membersGrid.Rows.Add("stale", "S-1-5-21-99");

                var populateCalls = 0;
                var clearCalls = 0;
                gridPopulator.Setup(p => p.PopulateMembers(TestGroupSid)).Returns(() =>
                {
                    populateCalls++;
                    return Task.FromException(new InvalidOperationException("member load failed"));
                });
                gridPopulator.Setup(p => p.ClearMembers()).Callback(() =>
                {
                    clearCalls++;
                    membersGrid.Rows.Clear();
                    membersHeader.Text = "Members:";
                });

                panel.BeginRefreshMembersGrid();

                StaTestHelper.PumpUntil(() => clearCalls > 0);

                Assert.Equal(0, membersGrid.Rows.Count);
                Assert.Equal("Members:", membersHeader.Text);
                Assert.True(membersGrid.Enabled);

                Assert.NotEqual(0, populateCalls);
                Assert.NotEqual(0, clearCalls);
            }
        });
    }

    [Fact]
    public void AddMemberButtonRefreshFailure_ClearsMembersAndResetsLoading()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            var groupQuery = new Mock<ILocalGroupQueryService>();
            var groupMutation = new Mock<ILocalGroupMutationService>();
            var gridPopulator = new Mock<IGroupGridPopulator>();
            var log = new Mock<ILoggingService>();

            groupQuery.Setup(s => s.GetGroupDescription(It.IsAny<string>())).Returns("test");
            groupQuery.Setup(s => s.GetMembersOfGroup(It.IsAny<string>())).Returns([]);
            groupMutation.Setup(s => s.AddUserToGroups(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>>()));

            var memberPicker = new Mock<IMemberPickerDialog>();
            memberPicker.Setup(s => s.ShowPicker(
                "Test Group",
                It.IsAny<HashSet<string>>(),
                It.IsAny<IWin32Window>()))
                .Returns([new LocalUserAccount("new member", "S-1-5-21-999")]);
            var prompt = new Mock<IGroupMembershipPrompt>();

            var (host, panel, groupsGrid, membersGrid, membersHeader, membersToolStrip) = CreatePanel(
                groupQuery.Object,
                groupMutation.Object,
                gridPopulator.Object,
                log.Object,
                memberPicker.Object,
                prompt.Object);
            using (host)
            {
                var selectedRow = new DataGridViewRow();
                selectedRow.CreateCells(groupsGrid, "Test Group", TestGroupSid);
                selectedRow.Tag = TestGroupSid;
                groupsGrid.Rows.Add(selectedRow);
                selectedRow.Selected = true;
                groupsGrid.CurrentCell = selectedRow.Cells[0];

                membersGrid.Rows.Clear();

                var clearCalls = 0;
                gridPopulator.Setup(p => p.PopulateMembers(TestGroupSid)).Returns(() =>
                    Task.FromException(new InvalidOperationException("member load failed")));
                gridPopulator.Setup(p => p.ClearMembers()).Callback(() =>
                {
                    clearCalls++;
                    membersGrid.Rows.Clear();
                    membersHeader.Text = "Members:";
                });

                membersGrid.Rows.Add("existing member", "S-1-5-21-98");

                var addButton = membersToolStrip.Items.OfType<ToolStripButton>()
                    .Single(button => button.ToolTipText == "Add Member");
                addButton.Enabled = true;
                addButton.PerformClick();

                StaTestHelper.PumpUntil(() => clearCalls > 0);

                Assert.Equal(0, membersGrid.Rows.Count);
                Assert.Equal("Members:", membersHeader.Text);
                Assert.True(membersGrid.Enabled);
                gridPopulator.Verify(
                    p => p.PopulateMembers(TestGroupSid),
                    Times.AtLeastOnce);
            }
        });
    }

    [Fact]
    public void RemoveMemberButtonRefreshFailure_ClearsMembersAndResetsLoading()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            var groupQuery = new Mock<ILocalGroupQueryService>();
            var groupMutation = new Mock<ILocalGroupMutationService>();
            var gridPopulator = new Mock<IGroupGridPopulator>();
            var log = new Mock<ILoggingService>();

            groupQuery.Setup(s => s.GetGroupDescription(It.IsAny<string>())).Returns("test");
            groupQuery.Setup(s => s.GetMembersOfGroup(It.IsAny<string>())).Returns([]);
            groupMutation.Setup(s => s.RemoveUserFromGroups(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>>()));

            var memberPicker = new Mock<IMemberPickerDialog>();
            var prompt = new Mock<IGroupMembershipPrompt>();
            prompt.Setup(p => p.ConfirmRemove(It.IsAny<string>())).Returns(true);

            var (host, panel, groupsGrid, membersGrid, membersHeader, membersToolStrip) = CreatePanel(
                groupQuery.Object,
                groupMutation.Object,
                gridPopulator.Object,
                log.Object,
                memberPicker.Object,
                prompt.Object);
            using (host)
            {
                var selectedRow = new DataGridViewRow();
                selectedRow.CreateCells(groupsGrid, "Test Group", TestGroupSid);
                selectedRow.Tag = TestGroupSid;
                groupsGrid.Rows.Add(selectedRow);
                selectedRow.Selected = true;
                groupsGrid.CurrentCell = selectedRow.Cells[0];

                membersGrid.Rows.Add("existing member", "S-1-5-21-98");
                var existingMemberRow = membersGrid.Rows[0];
                existingMemberRow.Tag = "S-1-5-21-98";
                existingMemberRow.Selected = true;

                var clearCalls = 0;
                gridPopulator.Setup(p => p.PopulateMembers(TestGroupSid)).Returns(() =>
                    Task.FromException(new InvalidOperationException("member load failed")));
                gridPopulator.Setup(p => p.ClearMembers()).Callback(() =>
                {
                    clearCalls++;
                    membersGrid.Rows.Clear();
                    membersHeader.Text = "Members:";
                });

                var removeButton = membersToolStrip.Items.OfType<ToolStripButton>()
                    .Single(button => button.ToolTipText == "Remove Member");
                removeButton.Enabled = true;
                removeButton.PerformClick();

                StaTestHelper.PumpUntil(() => clearCalls > 0);

                Assert.Equal(0, membersGrid.Rows.Count);
                Assert.Equal("Members:", membersHeader.Text);
                Assert.True(membersGrid.Enabled);
                gridPopulator.Verify(
                    p => p.PopulateMembers(TestGroupSid),
                    Times.AtLeastOnce);
            }
        });
    }

    private static (Form host, GroupsPanel panel, DataGridView groupsGrid, DataGridView membersGrid, Label membersHeader, ToolStrip membersToolStrip) CreatePanel(
        ILocalGroupQueryService groupQuery,
        ILocalGroupMutationService groupMutation,
        IGroupGridPopulator gridPopulator,
        ILoggingService log,
        IMemberPickerDialog memberPicker,
        IGroupMembershipPrompt membershipPrompt)
    {
        var modalCoordinator = Mock.Of<IModalCoordinator>();
        var systemDialogLauncher = Mock.Of<ISystemDialogLauncher>();
        var sidNameCache = Mock.Of<ISidNameCacheService>();
        var membershipOrchestrator = new GroupMembershipOrchestrator(groupMutation, memberPicker, membershipPrompt, log);
        var groupDeletionService = (GroupDeletionService)null!;
        var contextMenuHandler = new GroupActionOrchestrator(
            modalCoordinator,
            groupMutation,
            groupDeletionService,
            null,
            null,
            Mock.Of<IGroupDeletePrompt>(),
            sidNameCache,
            log);
        var refreshController = new GroupRefreshController(gridPopulator, log);
        var descriptionEditor = new GroupDescriptionEditor(groupMutation, log);
        var selectionLoadController = new GroupSelectionLoadController(
            descriptionEditor,
            gridPopulator,
            groupQuery,
            log);

        var panel = new GroupsPanel(
            modalCoordinator,
            gridPopulator,
            membershipOrchestrator,
            contextMenuHandler,
            systemDialogLauncher,
            (GroupSidMigrationLauncher)null!,
            refreshController,
            descriptionEditor,
            selectionLoadController,
            log);

        var host = new Form();
        host.Controls.Add(panel);
        StaTestHelper.CreateControlTree(host);

        var groupsGrid = Descendants(panel).OfType<DataGridView>()
            .Single(g => g.Columns.Cast<DataGridViewColumn>().Any(c => c.Name == "GroupName"));
        var membersGrid = Descendants(panel).OfType<DataGridView>()
            .Single(g => g.Columns.Cast<DataGridViewColumn>().Any(c => c.Name == "MemberName"));
        var membersHeader = Descendants(panel).OfType<Label>().Single(l => l.Text == "Members:");
        var membersToolStrip = Descendants(panel).OfType<ToolStrip>().Single(ts => ts.Items.Count == 2);

        return (host, panel, groupsGrid, membersGrid, membersHeader, membersToolStrip);
    }

    private static IEnumerable<Control> Descendants(Control control)
    {
        yield return control;
        foreach (Control child in control.Controls)
        {
            foreach (var nested in Descendants(child))
                yield return nested;
        }
    }
}
