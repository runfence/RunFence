using Moq;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Account.UI.AppContainer;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.RunAs.UI;
using RunFence.Tests.Helpers;
using RunFence.UI;
using Xunit;

namespace RunFence.Tests;

public class RunAsCredentialListPopulatorTests
{
    private const string SelectedSid = "S-1-5-21-1-2-3-1001";

    private readonly Mock<ISidResolver> _sidResolver = new();
    private readonly Mock<IProfilePathResolver> _profilePathResolver = new();
    private readonly CredentialDisplayItemFactory _credentialDisplayItemFactory;
    private readonly Mock<ILocalUserProvider> _localUserProvider = new();

    public RunAsCredentialListPopulatorTests()
    {
        _sidResolver.Setup(r => r.GetCurrentUserSid()).Returns("S-1-5-21-1-2-3-500");
        _sidResolver.Setup(r => r.TryResolveName(It.IsAny<string>())).Returns<string?>(sid => sid == SelectedSid ? "Selected User" : null);
        _profilePathResolver.Setup(r => r.TryResolveNameFromRegistry(It.IsAny<string>())).Returns((string?)null);
        _localUserProvider.Setup(p => p.GetLocalUserAccounts()).Returns([]);
        _credentialDisplayItemFactory = new CredentialDisplayItemFactory(_sidResolver.Object, _profilePathResolver.Object);
    }

    [Fact]
    public void Repopulate_WhenInitialAccountSidHasNoCredential_IncludesSelectableAccount()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var listBox = new ListBox();
            using var showAll = new CheckBox();
            var populator = CreatePopulator();

            populator.Initialize(
                listBox,
                credentials: [],
                sidNames: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [SelectedSid] = "Selected User"
                },
                showAllAccountsCheckBox: showAll,
                currentUserSid: null,
                initialAccountSid: SelectedSid,
                appContainers: null);

            populator.Repopulate();

            var item = Assert.Single(
                listBox.Items.Cast<RunAsAccountListItem>(),
                listItem => listItem.OptionSource is CredentialRunAsOptionSource source
                            && string.Equals(source.Credential.Sid, SelectedSid, StringComparison.OrdinalIgnoreCase));
            var displayItem = Assert.IsType<CredentialDisplayItem>(item.DisplayItem);
            Assert.Equal(SelectedSid, displayItem.Credential.Sid);
            Assert.False(displayItem.HasStoredCredential);
        });
    }

    [Fact]
    public void Repopulate_WhenInitialAccountSidAlreadyHasCredential_DoesNotDuplicateAccount()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var listBox = new ListBox();
            using var showAll = new CheckBox();
            var populator = CreatePopulator();

            populator.Initialize(
                listBox,
                credentials:
                [
                    new CredentialEntry
                    {
                        Id = Guid.NewGuid(),
                        Sid = SelectedSid
                    }
                ],
                sidNames: null,
                showAllAccountsCheckBox: showAll,
                currentUserSid: null,
                initialAccountSid: SelectedSid,
                appContainers: null);

            populator.Repopulate();

            var item = Assert.Single(
                listBox.Items.Cast<RunAsAccountListItem>(),
                listItem => listItem.OptionSource is CredentialRunAsOptionSource source
                            && string.Equals(source.Credential.Sid, SelectedSid, StringComparison.OrdinalIgnoreCase));
            var displayItem = Assert.IsType<CredentialDisplayItem>(item.DisplayItem);
            Assert.Equal(SelectedSid, displayItem.Credential.Sid);
            Assert.True(displayItem.HasStoredCredential);
        });
    }

    [Fact]
    public void Repopulate_AttachesOptionSourcesToSelectableRows_AndLeavesSeparatorWithoutSource()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var listBox = new ListBox();
            using var showAll = new CheckBox();
            var populator = CreatePopulator();
            var credential = new CredentialEntry { Id = Guid.NewGuid(), Sid = SelectedSid };
            var container = new AppContainerEntry { Name = "rfn_browser", DisplayName = "Browser" };

            populator.Initialize(
                listBox,
                credentials: [credential],
                sidNames: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [SelectedSid] = "Selected User"
                },
                showAllAccountsCheckBox: showAll,
                currentUserSid: null,
                initialAccountSid: null,
                appContainers: [container]);

            populator.Repopulate();

            var items = listBox.Items.Cast<RunAsAccountListItem>().ToList();
            Assert.Contains(items, item => item.OptionSource is CredentialRunAsOptionSource source
                                           && SameCredential(source.Credential, credential));
            Assert.Contains(items, item => item.OptionSource is AppContainerRunAsOptionSource source
                                           && SameContainer(source.Container, container)
                                           && source.ContainerSid == container.Sid);
            Assert.Contains(items, item => item.OptionSource is CreateAccountRunAsOptionSource);
            Assert.Contains(items, item => item.OptionSource is CreateContainerRunAsOptionSource);

            var separator = Assert.Single(items, item => item.IsSeparator);
            Assert.Null(separator.OptionSource);
            Assert.IsType<ContainerSeparatorItem>(separator.DisplayItem);
        });
    }

    [Fact]
    public void Repopulate_WhenCreateContainerWasSelected_RestoresCreateContainerSelection()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var listBox = new ListBox();
            using var showAll = new CheckBox();
            var populator = CreatePopulator();
            var container = new AppContainerEntry { Name = "rfn_browser", DisplayName = "Browser" };

            populator.Initialize(
                listBox,
                credentials: [],
                sidNames: null,
                showAllAccountsCheckBox: showAll,
                currentUserSid: null,
                initialAccountSid: null,
                appContainers: [container]);

            populator.Repopulate();
            listBox.SelectedItem = listBox.Items.Cast<RunAsAccountListItem>()
                .Single(item => item.OptionSource is CreateContainerRunAsOptionSource);

            populator.Repopulate();

            var selected = Assert.IsType<RunAsAccountListItem>(listBox.SelectedItem);
            Assert.IsType<CreateContainerRunAsOptionSource>(selected.OptionSource);
        });
    }

    private RunAsCredentialListPopulator CreatePopulator() => new(
        _credentialDisplayItemFactory,
        _localUserProvider.Object,
        new CredentialFilterHelper(_sidResolver.Object));

    private static bool SameCredential(CredentialEntry left, CredentialEntry right)
        => ReferenceEquals(left, right)
           || (left.Id == right.Id && string.Equals(left.Sid, right.Sid, StringComparison.OrdinalIgnoreCase));

    private static bool SameContainer(AppContainerEntry left, AppContainerEntry right)
        => ReferenceEquals(left, right)
           || (string.Equals(left.Name, right.Name, StringComparison.OrdinalIgnoreCase)
               && string.Equals(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase));
}
