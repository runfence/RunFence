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

public class GroupSelectionLoadControllerTests
{
    private sealed class TestSelectionLoadView(string? selectedGroupSid, List<bool> loadingStates) : IGroupSelectionLoadView
    {
        public string? SelectedGroupSid { get; set; } = selectedGroupSid;

        public string? GetSelectedGroupSid() => SelectedGroupSid;

        public void SetMembersLoading(bool isMembersLoading)
        {
            loadingStates.Add(isMembersLoading);
        }
    }

    [Fact]
    public void HandleSelectionChangedAsync_LoadsDescriptionAndMembersForSelectedGroup()
    {
        StaTestHelper.RunOnSta(() =>
        {
            const string groupSid = "S-1-5-32-544";

            var mutationService = new Mock<ILocalGroupMutationService>();
            var membershipService = new Mock<ILocalGroupQueryService>();
            membershipService.Setup(s => s.GetGroupDescription(groupSid)).Returns("group description");
            membershipService.Setup(s => s.GetMembersOfGroup(groupSid)).Returns([
                new LocalUserAccount("member1", "S-1-5-21-1"),
                new LocalUserAccount("member2", "S-1-5-21-2")
            ]);

            using var descriptionTextBox = new TextBox();
            using var groupsGrid = new StyledDataGridView();
            groupsGrid.Columns.Add("GroupName", "GroupName");
            groupsGrid.Columns.Add("SID", "SID");
            using var membersGrid = new StyledDataGridView();
            membersGrid.Columns.Add("AccountName", "AccountName");
            membersGrid.Columns.Add("SID", "SID");
            using var membersHeader = new Label();

            var descriptionEditor = new GroupDescriptionEditor(mutationService.Object, Mock.Of<ILoggingService>());
            descriptionEditor.Initialize(descriptionTextBox);

            var populator = new GroupGridPopulator(membershipService.Object, Mock.Of<ILoggingService>(), Mock.Of<ISidNameCacheService>());
            populator.Initialize(groupsGrid, membersGrid, membersHeader);

            var controller = new GroupSelectionLoadController(
                descriptionEditor,
                populator,
                membershipService.Object,
                Mock.Of<ILoggingService>());

            var loadingStates = new List<bool>();
            controller.Initialize(new TestSelectionLoadView(groupSid, loadingStates));

            StaTestHelper.RunAsyncWithMessagePump(() => controller.HandleSelectionChangedAsync(groupSid));

            Assert.Equal("group description", descriptionTextBox.Text);
            Assert.True(descriptionTextBox.Enabled);
            Assert.Equal(2, membersGrid.Rows.Count);
            Assert.Equal([true, false], loadingStates);
        });
    }

    [Fact]
    public void HandleSelectionChangedAsync_WhenSelectionCleared_ClearsMembersAndStopsLoading()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var descriptionEditor = new GroupDescriptionEditor(Mock.Of<ILocalGroupMutationService>(), Mock.Of<ILoggingService>());
            using var descriptionTextBox = new TextBox();
            descriptionEditor.Initialize(descriptionTextBox);

            using var groupsGrid = new StyledDataGridView();
            groupsGrid.Columns.Add("GroupName", "GroupName");
            groupsGrid.Columns.Add("SID", "SID");
            using var membersGrid = new StyledDataGridView();
            membersGrid.Columns.Add("AccountName", "AccountName");
            membersGrid.Columns.Add("SID", "SID");
            membersGrid.Rows.Add("member", "sid");
            using var membersHeader = new Label();

            var populator = new GroupGridPopulator(Mock.Of<ILocalGroupQueryService>(), Mock.Of<ILoggingService>(), Mock.Of<ISidNameCacheService>());
            populator.Initialize(groupsGrid, membersGrid, membersHeader);

            var controller = new GroupSelectionLoadController(
                descriptionEditor,
                populator,
                Mock.Of<ILocalGroupQueryService>(),
                Mock.Of<ILoggingService>());

            var loadingStates = new List<bool>();
            controller.Initialize(new TestSelectionLoadView(null, loadingStates));

            StaTestHelper.RunAsyncWithMessagePump(() => controller.HandleSelectionChangedAsync(null));

            Assert.Empty(membersGrid.Rows);
            Assert.Equal("Members:", membersHeader.Text);
            Assert.Equal([false], loadingStates);
        });
    }

    [Fact]
    public void LoadDescriptionAfterRefreshAsync_WhenSelectionCleared_DoesNotLoadDescription()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var mutationService = new Mock<ILocalGroupMutationService>();
            var membershipService = new Mock<ILocalGroupQueryService>(MockBehavior.Strict);

            using var descriptionTextBox = new TextBox();

            var descriptionEditor = new GroupDescriptionEditor(mutationService.Object, Mock.Of<ILoggingService>());
            descriptionEditor.Initialize(descriptionTextBox);

            var controller = new GroupSelectionLoadController(
                descriptionEditor,
                new GroupGridPopulator(Mock.Of<ILocalGroupQueryService>(), Mock.Of<ILoggingService>(), Mock.Of<ISidNameCacheService>()),
                membershipService.Object,
                Mock.Of<ILoggingService>());
            controller.Initialize(new TestSelectionLoadView(null, []));

            StaTestHelper.RunAsyncWithMessagePump(() => controller.LoadDescriptionAfterRefreshAsync(null));

            Assert.Equal(string.Empty, descriptionTextBox.Text);
            Assert.False(descriptionTextBox.Enabled);
            membershipService.Verify(s => s.GetGroupDescription(It.IsAny<string>()), Times.Never);
        });
    }

    [Fact]
    public void LoadDescriptionAfterRefreshAsync_WhenNotEditing_LoadsDescription()
    {
        StaTestHelper.RunOnSta(() =>
        {
            const string groupSid = "S-1-5-32-544";

            var mutationService = new Mock<ILocalGroupMutationService>();
            var membershipService = new Mock<ILocalGroupQueryService>();
            membershipService.Setup(s => s.GetGroupDescription(groupSid)).Returns("loaded description");

            using var descriptionTextBox = new TextBox();

            var descriptionEditor = new GroupDescriptionEditor(mutationService.Object, Mock.Of<ILoggingService>());
            descriptionEditor.Initialize(descriptionTextBox);

            var controller = new GroupSelectionLoadController(
                descriptionEditor,
                new GroupGridPopulator(Mock.Of<ILocalGroupQueryService>(), Mock.Of<ILoggingService>(), Mock.Of<ISidNameCacheService>()),
                membershipService.Object,
                Mock.Of<ILoggingService>());
            controller.Initialize(new TestSelectionLoadView(groupSid, []));

            StaTestHelper.RunAsyncWithMessagePump(() => controller.LoadDescriptionAfterRefreshAsync(groupSid));

            Assert.Equal("loaded description", descriptionTextBox.Text);
            Assert.True(descriptionTextBox.Enabled);
        });
    }
}
