using Moq;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.RunAs.UI;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public class RunAsCredentialListPopulatorTests
{
    private const string SelectedSid = "S-1-5-21-1-2-3-1001";

    private readonly Mock<ISidResolver> _sidResolver = new();
    private readonly Mock<IProfilePathResolver> _profilePathResolver = new();
    private readonly Mock<ILocalUserProvider> _localUserProvider = new();

    public RunAsCredentialListPopulatorTests()
    {
        _sidResolver.Setup(r => r.GetCurrentUserSid()).Returns("S-1-5-21-1-2-3-500");
        _sidResolver.Setup(r => r.TryResolveName(It.IsAny<string>())).Returns<string?>(sid => sid == SelectedSid ? "Selected User" : null);
        _profilePathResolver.Setup(r => r.TryResolveNameFromRegistry(It.IsAny<string>())).Returns((string?)null);
        _localUserProvider.Setup(p => p.GetLocalUserAccounts()).Returns([]);
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

            var item = Assert.Single(listBox.Items.OfType<CredentialDisplayItem>());
            Assert.Equal(SelectedSid, item.Credential.Sid);
            Assert.False(item.HasStoredCredential);
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

            var item = Assert.Single(listBox.Items.OfType<CredentialDisplayItem>());
            Assert.Equal(SelectedSid, item.Credential.Sid);
            Assert.True(item.HasStoredCredential);
        });
    }

    private RunAsCredentialListPopulator CreatePopulator() => new(
        _sidResolver.Object,
        _profilePathResolver.Object,
        _localUserProvider.Object,
        new CredentialFilterHelper(_sidResolver.Object));
}
